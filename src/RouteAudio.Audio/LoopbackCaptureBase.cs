using System.Runtime.InteropServices;
using RouteAudio.Interop;

namespace RouteAudio.Audio;

/// <summary>
/// 回环采集基类：负责 IAudioClient 初始化、事件驱动采集循环、统计与 WAV 落盘。
/// 子类只负责「如何拿到 IAudioClient」（进程回环 vs 设备回环）。
/// </summary>
public abstract class LoopbackCaptureBase : IDisposable
{
    protected const uint AudclntStreamflagsLoopback = 0x00020000;
    protected const uint AudclntStreamflagsEventcallback = 0x00040000;
    protected const uint AudclntStreamflagsAutoconvertPcm = 0x80000000;
    protected const uint AudclntStreamflagsSrcDefaultQuality = 0x08000000;
    private const uint AudclntBufferflagsSilent = 0x00000002;

    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;

    private IAudioClient? _client;
    private IAudioCaptureClient? _captureClient;
    private AutoResetEvent? _sampleEvent;
    private Thread? _thread;
    private volatile bool _running;

    private readonly object _statsGate = new();
    private double _sumSquares;
    private long _sampleCount;
    private short _peak;
    private readonly List<short[]> _buffer = [];

    public bool IsRunning => _running;

    public string? EndpointDescription { get; protected set; }

    protected abstract (IAudioClient? Client, string? Error) Activate();

    protected static string DescribeFormat(int sampleRate, int channels, int bits) =>
        $"{sampleRate} Hz / {channels} ch / {bits} bit PCM";

    public static string FormatDescription => DescribeFormat(SampleRate, Channels, BitsPerSample);

    public (bool Ok, string? Error) Start()
    {
        if (_running) return (false, "capture already running");

        var (client, error) = Activate();
        if (client is null) return (false, error);

        _client = client;
        var formatPtr = AllocateFormat();
        try
        {
            int hr = _client.Initialize(
                AudioClientShareMode.Shared,
                AudclntStreamflagsLoopback | AudclntStreamflagsEventcallback |
                AudclntStreamflagsAutoconvertPcm | AudclntStreamflagsSrcDefaultQuality,
                0,
                0,
                formatPtr,
                IntPtr.Zero);

            if (hr < 0) return (false, $"Initialize 0x{hr:X8}");

            hr = _client.GetBufferSize(out _);
            if (hr < 0) return (false, $"GetBufferSize 0x{hr:X8}");

            var captureIid = Guids.IID_IAudioCaptureClient;
            hr = _client.GetService(ref captureIid, out var captureObj);
            if (hr < 0) return (false, $"GetService(IAudioCaptureClient) 0x{hr:X8}");

            _captureClient = (IAudioCaptureClient)captureObj;
            _sampleEvent = new AutoResetEvent(false);

            hr = _client.SetEventHandle(_sampleEvent.SafeWaitHandle.DangerousGetHandle());
            if (hr < 0) return (false, $"SetEventHandle 0x{hr:X8}");

            hr = _client.Start();
            if (hr < 0) return (false, $"Start 0x{hr:X8}");

            _running = true;
            _thread = new Thread(CaptureLoop) { IsBackground = true, Name = $"RouteAudio.{GetType().Name}" };
            _thread.Start();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(formatPtr);
        }
    }

    private static IntPtr AllocateFormat()
    {
        var blockAlign = (ushort)(Channels * BitsPerSample / 8);
        var format = new WaveFormatEx
        {
            wFormatTag = 1,
            nChannels = Channels,
            nSamplesPerSec = SampleRate,
            wBitsPerSample = BitsPerSample,
            nBlockAlign = blockAlign,
            nAvgBytesPerSec = (uint)(SampleRate * blockAlign),
            cbSize = 0,
        };

        var size = Marshal.SizeOf<WaveFormatEx>();
        var ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(format, ptr, false);
        return ptr;
    }

    private void CaptureLoop()
    {
        var eventHandle = _sampleEvent!;
        var capture = _captureClient!;

        while (_running)
        {
            if (!eventHandle.WaitOne(50)) continue;

            while (_running)
            {
                if (capture.GetNextPacketSize(out var framesInPacket) < 0) break;
                if (framesInPacket == 0) break;
                if (capture.GetBuffer(out var data, out var frames, out var flags, out _, out _) < 0) break;
                Append(data, frames, flags);
                _ = capture.ReleaseBuffer(frames);
            }
        }
    }

    private void Append(IntPtr data, uint frames, uint flags)
    {
        var sampleCount = (int)frames * Channels;
        var samples = new short[sampleCount];

        if ((flags & AudclntBufferflagsSilent) != 0)
        {
            Array.Clear(samples);
        }
        else
        {
            Marshal.Copy(data, samples, 0, sampleCount);
        }

        double sum = 0;
        short peak = 0;
        foreach (var s in samples)
        {
            var v = s / 32768.0;
            sum += v * v;
            var abs = s == short.MinValue ? short.MaxValue : Math.Abs(s);
            if (abs > peak) peak = (short)abs;
        }

        lock (_statsGate)
        {
            _sumSquares += sum;
            _sampleCount += sampleCount;
            if (peak > _peak) _peak = peak;
            _buffer.Add(samples);
        }
    }

    public CaptureStatistics TakeStats(bool reset = true)
    {
        lock (_statsGate)
        {
            var frames = _sampleCount / Channels;
            var rms = _sampleCount == 0 ? 0 : Math.Sqrt(_sumSquares / _sampleCount);
            var peak = _peak / 32768.0;
            var stats = new CaptureStatistics(rms, peak, frames / (double)SampleRate, frames);
            if (reset)
            {
                _sumSquares = 0;
                _sampleCount = 0;
                _peak = 0;
            }

            return stats;
        }
    }

    public string? SaveWav(string path)
    {
        lock (_statsGate)
        {
            if (_buffer.Count == 0) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);

            var dataSize = _buffer.Sum(b => b.Length) * 2;
            var blockAlign = Channels * BitsPerSample / 8;

            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataSize);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write((ushort)Channels);
            writer.Write(SampleRate);
            writer.Write(SampleRate * blockAlign);
            writer.Write((ushort)blockAlign);
            writer.Write((ushort)BitsPerSample);
            writer.Write("data"u8.ToArray());
            writer.Write(dataSize);

            foreach (var block in _buffer)
            {
                foreach (var s in block) writer.Write(s);
            }

            _buffer.Clear();
            return path;
        }
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1500);
        _thread = null;

        try
        {
            _client?.Stop();
        }
        catch
        {
            // 设备失效时 Stop 可能失败
        }

        if (_captureClient is not null) Marshal.ReleaseComObject(_captureClient);
        if (_client is not null) Marshal.ReleaseComObject(_client);
        _captureClient = null;
        _client = null;

        _sampleEvent?.Dispose();
        _sampleEvent = null;
    }

    public void Dispose() => Stop();
}

public readonly record struct CaptureStatistics(double Rms, double Peak, double Seconds, long Frames)
{
    public double RmsDb => Rms <= 0 ? -999 : 20 * Math.Log10(Rms);

    public double PeakDb => Peak <= 0 ? -999 : 20 * Math.Log10(Peak);

    public override string ToString() => $"RMS {RmsDb,7:F1} dBFS / Peak {PeakDb,6:F1} dBFS / {Seconds:F2}s";
}
