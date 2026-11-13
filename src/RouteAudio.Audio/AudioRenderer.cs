using System.Runtime.InteropServices;
using RouteAudio.Interop;

namespace RouteAudio.Audio;

/// <summary>
/// 向指定端点共享模式渲染 16-bit / 48 kHz / 立体声。数据由外部 Push 进来，
/// 欠载时输出静音并计数（绝不重复旧数据，避免爆音）。
/// </summary>
public sealed class AudioRenderer : IDisposable
{
    private const uint AudclntStreamflagsEventcallback = 0x00040000;
    private const uint AudclntStreamflagsAutoconvertPcm = 0x80000000;
    private const uint AudclntStreamflagsSrcDefaultQuality = 0x08000000;

    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;

    private readonly DeviceService _devices;
    private readonly string _endpointId;
    private readonly ShortRingBuffer _ring;
    private IAudioClient? _client;
    private IAudioRenderClient? _renderClient;
    private AutoResetEvent? _sampleEvent;
    private Thread? _thread;
    private short[] _scratch = [];
    private uint _bufferFrames;
    private volatile bool _running;
    private long _framesRendered;
    private long _underruns;
    private long _pushedFrames;

    public AudioRenderer(DeviceService devices, string endpointId, int bufferMilliseconds = 2000)
    {
        _devices = devices;
        _endpointId = endpointId;
        _ring = new ShortRingBuffer(SampleRate * Channels * bufferMilliseconds / 1000);
    }

    public bool IsRunning => _running;

    public uint BufferFrames => _bufferFrames;

    public long FramesRendered => Interlocked.Read(ref _framesRendered);

    public long PushedFrames => Interlocked.Read(ref _pushedFrames);

    public long Underruns => Interlocked.Read(ref _underruns);

    /// <summary>环形缓冲中的音频时长（毫秒）。</summary>
    public double QueueMilliseconds => _ring.Count / (double)(Channels * SampleRate) * 1000;

    public (bool Ok, string? Error) Start()
    {
        if (_running) return (false, "renderer already running");
        ComThread.EnsureInitialized();

        var device = _devices.OpenDevice(_endpointId);
        if (device is null) return (false, $"endpoint not found: {_endpointId}");

        try
        {
            var iid = Guids.IID_IAudioClient;
            var hr = device.Activate(ref iid, ClsCtx.ALL, IntPtr.Zero, out var obj);
            if (hr < 0 || obj is not IAudioClient client) return (false, $"Activate(IAudioClient) 0x{hr:X8}");
            _client = client;

            var formatPtr = AllocateFormat();
            try
            {
                hr = _client.Initialize(
                    AudioClientShareMode.Shared,
                    AudclntStreamflagsEventcallback | AudclntStreamflagsAutoconvertPcm | AudclntStreamflagsSrcDefaultQuality,
                    0,
                    0,
                    formatPtr,
                    IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(formatPtr);
            }

            if (hr < 0) return (false, $"Initialize 0x{hr:X8}");

            hr = _client.GetBufferSize(out _bufferFrames);
            if (hr < 0) return (false, $"GetBufferSize 0x{hr:X8}");

            _scratch = new short[_bufferFrames * Channels];

            var renderIid = Guids.IID_IAudioRenderClient;
            hr = _client.GetService(ref renderIid, out var renderObj);
            if (hr < 0 || renderObj is not IAudioRenderClient render) return (false, $"GetService(IAudioRenderClient) 0x{hr:X8}");
            _renderClient = render;

            _sampleEvent = new AutoResetEvent(false);
            hr = _client.SetEventHandle(_sampleEvent.SafeWaitHandle.DangerousGetHandle());
            if (hr < 0) return (false, $"SetEventHandle 0x{hr:X8}");

            hr = _client.Start();
            if (hr < 0) return (false, $"Start 0x{hr:X8}");

            _running = true;
            _thread = new Thread(RenderLoop) { IsBackground = true, Name = "RouteAudio.Render" };
            _thread.Start();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Marshal.ReleaseComObject(device);
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

    private void RenderLoop()
    {
        var eventHandle = _sampleEvent!;
        var render = _renderClient!;

        while (_running)
        {
            if (!eventHandle.WaitOne(20)) continue;

            if (_client!.GetCurrentPadding(out var padding) < 0) break;

            var framesAvailable = _bufferFrames - padding;
            if (framesAvailable == 0) continue;

            if (render.GetBuffer(framesAvailable, out var dataPtr) < 0) break;

            var samplesNeeded = (int)framesAvailable * Channels;
            var read = _ring.Read(_scratch.AsSpan(0, samplesNeeded));
            if (read < samplesNeeded)
            {
                _scratch.AsSpan(read, samplesNeeded - read).Clear();
                Interlocked.Increment(ref _underruns);
            }

            Marshal.Copy(_scratch, 0, dataPtr, samplesNeeded);
            _ = render.ReleaseBuffer(framesAvailable, 0);
            Interlocked.Add(ref _framesRendered, framesAvailable);
        }
    }

    public void Push(short[] samples)
    {
        if (!_running || samples.Length == 0) return;
        _ring.Write(samples);
        Interlocked.Add(ref _pushedFrames, samples.Length / Channels);
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
            // 设备失效时忽略
        }

        if (_renderClient is not null) Marshal.ReleaseComObject(_renderClient);
        if (_client is not null) Marshal.ReleaseComObject(_client);
        _renderClient = null;
        _client = null;
        _sampleEvent?.Dispose();
        _sampleEvent = null;
    }

    public void Dispose() => Stop();
}
