using System.Runtime.InteropServices;
using RouteAudio.Interop;

namespace RouteAudio.Audio;

/// <summary>采集指定进程（树）正在渲染的音频。不经过物理设备，因此不受其默认端点影响。</summary>
public sealed class ProcessLoopbackCapture : LoopbackCaptureBase
{
    private readonly uint _targetProcessId;
    private readonly bool _includeProcessTree;

    public ProcessLoopbackCapture(uint targetProcessId, bool includeProcessTree = true)
    {
        _targetProcessId = targetProcessId;
        _includeProcessTree = includeProcessTree;
        EndpointDescription = $"进程回环 pid={targetProcessId}";
    }

    protected override (IAudioClient? Client, string? Error) Activate()
    {
        ComThread.EnsureInitialized();
        using var blob = ActivationParamsBlob.CreateProcessLoopback(_targetProcessId, _includeProcessTree);
        using var handler = new ActivationCompletionHandler();
        var handlerPtr = Marshal.GetComInterfaceForObject(handler, typeof(IActivateAudioInterfaceCompletionHandler));

        try
        {
            var iid = Guids.IID_IAudioClient;
            var hr = NativeMethods.ActivateAudioInterfaceAsync(
                ActivationParamsBlob.ProcessLoopbackDeviceInterface,
                ref iid,
                blob.PropVariantPointer,
                handlerPtr,
                out var operationPtr);

            if (hr < 0) return (null, $"ActivateAudioInterfaceAsync 0x{hr:X8}");
            if (operationPtr != IntPtr.Zero) Marshal.Release(operationPtr);

            if (!handler.WaitForCompletion(TimeSpan.FromSeconds(10)))
                return (null, "ActivateAudioInterfaceAsync timeout");

            if (handler.ResultHResult < 0)
                return (null, $"activation failed 0x{handler.ResultHResult:X8}");

            return handler.ActivatedInterface is IAudioClient client
                ? (client, null)
                : (null, "activated object is not IAudioClient");
        }
        catch (Exception ex)
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Marshal.Release(handlerPtr);
        }
    }
}

/// <summary>采集某个渲染端点上的全部混音输出（设备回环）。虚拟声卡场景下的「总线采集」。</summary>
public sealed class DeviceLoopbackCapture : LoopbackCaptureBase
{
    private readonly DeviceService _devices;
    private readonly string _endpointId;

    public DeviceLoopbackCapture(DeviceService devices, string endpointId)
    {
        _devices = devices;
        _endpointId = endpointId;
        EndpointDescription = endpointId;
    }

    protected override (IAudioClient? Client, string? Error) Activate()
    {
        ComThread.EnsureInitialized();
        var device = _devices.OpenDevice(_endpointId);
        if (device is null) return (null, $"endpoint not found: {_endpointId}");

        try
        {
            var iid = Guids.IID_IAudioClient;
            var hr = device.Activate(ref iid, ClsCtx.ALL, IntPtr.Zero, out var obj);
            if (hr < 0) return (null, $"Activate(IAudioClient) 0x{hr:X8}");
            return obj is IAudioClient client ? (client, null) : (null, "not IAudioClient");
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }
}
