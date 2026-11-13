using System.Runtime.InteropServices;
using RouteAudio.Interop;

namespace RouteAudio.Audio;

/// <summary>
/// 用 WinMM waveOut 循环播放正弦音，用于 PoC 验证与手动测试。
/// 走系统音频引擎，因此可被「按应用策略」路由。
/// </summary>
public sealed class TonePlayer : IDisposable
{
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint WhdrDone = 0x00000001;
    private const uint WhdrPrepared = 0x00000002;
    private const int BufferCount = 4;

    private IntPtr _hWaveOut;
    private IntPtr[] _headers = [];
    private IntPtr[] _buffers = [];
    private WaveFormatEx _format;
    private Thread? _pump;
    private volatile bool _running;
    private int _framesPerBuffer;
    private double _phase;
    private double _freq = 1000;
    private float _amplitude = 0.2f;

    public bool IsRunning => _running;

    public void Start(uint deviceId = WaveMapper, int sampleRate = 44100, int milliseconds = 120, double frequency = 1000)
    {
        if (_running) throw new InvalidOperationException("tone already running");

        _freq = frequency;
        _format = new WaveFormatEx
        {
            wFormatTag = 1, // PCM
            nChannels = 1,
            nSamplesPerSec = (uint)sampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = (uint)(sampleRate * 2),
            cbSize = 0,
        };

        _framesPerBuffer = sampleRate * milliseconds / 1000;
        var bytesPerBuffer = _framesPerBuffer * 2;

        var hr = NativeMethods.waveOutOpen(out _hWaveOut, deviceId, ref _format, IntPtr.Zero, IntPtr.Zero, 0);
        if (hr < 0) throw new InvalidOperationException($"waveOutOpen failed 0x{hr:X8} (device={deviceId})");

        _headers = new IntPtr[BufferCount];
        _buffers = new IntPtr[BufferCount];
        var headerSize = (uint)Marshal.SizeOf<WaveHdr>();

        for (var i = 0; i < BufferCount; i++)
        {
            _buffers[i] = Marshal.AllocHGlobal(bytesPerBuffer);
            FillBuffer(_buffers[i], _framesPerBuffer);

            var header = Marshal.AllocHGlobal((int)headerSize);
            var hdr = new WaveHdr
            {
                lpData = _buffers[i],
                dwBufferLength = (uint)bytesPerBuffer,
                dwFlags = 0,
                dwLoops = 0,
            };
            Marshal.StructureToPtr(hdr, header, false);
            _headers[i] = header;

            hr = NativeMethods.waveOutPrepareHeader(_hWaveOut, header, headerSize);
            if (hr < 0) throw new InvalidOperationException($"waveOutPrepareHeader failed 0x{hr:X8}");

            hr = NativeMethods.waveOutWrite(_hWaveOut, header, headerSize);
            if (hr < 0) throw new InvalidOperationException($"waveOutWrite failed 0x{hr:X8}");
        }

        _running = true;
        _pump = new Thread(PumpLoop) { IsBackground = true, Name = "RouteAudio.TonePump" };
        _pump.Start();
    }

    private void PumpLoop()
    {
        var headerSize = (uint)Marshal.SizeOf<WaveHdr>();
        while (_running)
        {
            for (var i = 0; i < BufferCount; i++)
            {
                var hdr = Marshal.PtrToStructure<WaveHdr>(_headers[i]);
                if ((hdr.dwFlags & WhdrDone) == 0) continue;

                FillBuffer(_buffers[i], _framesPerBuffer);
                hdr.dwFlags &= ~WhdrDone;
                Marshal.StructureToPtr(hdr, _headers[i], false);
                _ = NativeMethods.waveOutWrite(_hWaveOut, _headers[i], headerSize);
            }

            Thread.Sleep(10);
        }
    }

    private void FillBuffer(IntPtr buffer, int frames)
    {
        var samples = new short[frames];
        var step = 2 * Math.PI * _freq / _format.nSamplesPerSec;
        for (var i = 0; i < frames; i++)
        {
            samples[i] = (short)(Math.Sin(_phase) * _amplitude * short.MaxValue);
            _phase += step;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
        }

        Marshal.Copy(samples, 0, buffer, frames);
    }

    public void Stop()
    {
        _running = false;
        try
        {
            if (_hWaveOut != IntPtr.Zero)
            {
                _ = NativeMethods.waveOutReset(_hWaveOut);
                Thread.Sleep(30);
                var headerSize = (uint)Marshal.SizeOf<WaveHdr>();
                foreach (var header in _headers)
                {
                    if (header != IntPtr.Zero) _ = NativeMethods.waveOutUnprepareHeader(_hWaveOut, header, headerSize);
                }
            }
        }
        finally
        {
            foreach (var header in _headers)
            {
                if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
            }

            foreach (var buffer in _buffers)
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }

            _headers = [];
            _buffers = [];

            if (_hWaveOut != IntPtr.Zero)
            {
                _ = NativeMethods.waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
            }
        }
    }

    public void Dispose() => Stop();
}
