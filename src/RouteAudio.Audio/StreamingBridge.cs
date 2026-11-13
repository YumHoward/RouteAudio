namespace RouteAudio.Audio;

/// <summary>
/// 把「采集端」与「渲染端」连成一条流式管线：采集包写入渲染环形缓冲。
/// 用于镜像（原设备继续出声，同时复制到第二设备）与虚拟声卡桥接。
/// </summary>
public sealed class StreamingBridge : IDisposable
{
    private readonly LoopbackCaptureBase _capture;
    private readonly AudioRenderer _renderer;
    private bool _started;

    public StreamingBridge(LoopbackCaptureBase capture, AudioRenderer renderer)
    {
        _capture = capture;
        _renderer = renderer;
    }

    public LoopbackCaptureBase Capture => _capture;

    public AudioRenderer Renderer => _renderer;

    public (bool Ok, string? Error) Start()
    {
        if (_started) return (false, "bridge already started");

        var (renderOk, renderError) = _renderer.Start();
        if (!renderOk) return (false, $"渲染端启动失败: {renderError}");

        _capture.CollectSamples = false;
        _capture.SamplesAvailable += OnSamples;

        var (captureOk, captureError) = _capture.Start();
        if (!captureOk)
        {
            _capture.SamplesAvailable -= OnSamples;
            _renderer.Stop();
            return (false, $"采集端启动失败: {captureError}");
        }

        _started = true;
        return (true, null);
    }

    private void OnSamples(short[] samples) => _renderer.Push(samples);

    /// <summary>管线估算延迟（毫秒，不含 DAC/扬声器）：采集缓冲一半 + 队列 + 渲染缓冲一半。</summary>
    public double EstimatedLatencyMilliseconds =>
        (_capture.BufferFrames / 2.0 / AudioRenderer.SampleRate * 1000) +
        _renderer.QueueMilliseconds +
        (_renderer.BufferFrames / 2.0 / AudioRenderer.SampleRate * 1000);

    public void Stop()
    {
        if (!_started) return;

        _capture.SamplesAvailable -= OnSamples;
        _capture.Stop();
        _renderer.Stop();
        _started = false;
    }

    public void Dispose() => Stop();
}
