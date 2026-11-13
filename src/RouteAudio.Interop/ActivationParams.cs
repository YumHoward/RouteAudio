using System.Runtime.InteropServices;

namespace RouteAudio.Interop;

public enum AudioClientActivationType
{
    Default = 0,
    ProcessLoopback = 1,
}

public enum ProcessLoopbackMode
{
    IncludeTargetProcessTree = 0,
    ExcludeTargetProcessTree = 1,
}

[StructLayout(LayoutKind.Sequential)]
public struct AudioClientProcessLoopbackParams
{
    public uint TargetProcessId;
    public int ProcessLoopbackMode;
}

[StructLayout(LayoutKind.Sequential)]
public struct AudioClientActivationParams
{
    public int ActivationType;
    public AudioClientProcessLoopbackParams ProcessLoopbackParams;
}

/// <summary>
/// 激活参数（VT_BLOB）的构造与生命周期管理。</summary>
public sealed class ActivationParamsBlob : IDisposable
{
    public const string ProcessLoopbackDeviceInterface = @"VAD\Process_Loopback";

    private readonly IntPtr _blob;
    private readonly int _blobSize;
    private readonly IntPtr _propVariant;

    private ActivationParamsBlob(IntPtr blob, int blobSize, IntPtr propVariant)
    {
        _blob = blob;
        _blobSize = blobSize;
        _propVariant = propVariant;
    }

    public IntPtr PropVariantPointer => _propVariant;

    public static ActivationParamsBlob CreateProcessLoopback(uint targetProcessId, bool includeProcessTree = true)
    {
        var parameters = new AudioClientActivationParams
        {
            ActivationType = (int)AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParams
            {
                TargetProcessId = targetProcessId,
                ProcessLoopbackMode = (int)(includeProcessTree
                    ? ProcessLoopbackMode.IncludeTargetProcessTree
                    : ProcessLoopbackMode.ExcludeTargetProcessTree),
            },
        };

        var blobSize = Marshal.SizeOf<AudioClientActivationParams>();
        var blob = Marshal.AllocHGlobal(blobSize);
        Marshal.StructureToPtr(parameters, blob, false);

        var propVariant = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        var pv = PropVariant.FromBlob(blob, (uint)blobSize);
        Marshal.StructureToPtr(pv, propVariant, false);

        return new ActivationParamsBlob(blob, blobSize, propVariant);
    }

    public void Dispose()
    {
        if (_propVariant != IntPtr.Zero) Marshal.FreeHGlobal(_propVariant);
        if (_blob != IntPtr.Zero) Marshal.FreeHGlobal(_blob);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ActivateAudioInterfaceAsync 的完成回调。必须作为 COM 对象暴露给系统。
/// </summary>
[ComVisible(true)]
public sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler
{
    private readonly ManualResetEventSlim _completed = new(false);
    private readonly object _gate = new();

    public object? ActivatedInterface { get; private set; }
    public int ResultHResult { get; private set; } = HResults.E_FAIL;
    public Exception? Failure { get; private set; }

    public int ActivateCompleted(IntPtr activateOperation)
    {
        try
        {
            if (activateOperation == IntPtr.Zero)
            {
                ResultHResult = HResults.E_POINTER;
                return HResults.S_OK;
            }

            var operation = (IActivateAudioInterfaceAsyncOperation)Marshal.GetObjectForIUnknown(activateOperation);
            try
            {
                var hr = operation.GetActivateResult(out var activateResult, out var activated);
                ResultHResult = hr >= 0 ? activateResult : hr;
                if (ResultHResult >= 0) ActivatedInterface = activated;
            }
            finally
            {
                Marshal.ReleaseComObject(operation);
            }
        }
        catch (Exception ex)
        {
            Failure = ex;
            ResultHResult = HResults.E_FAIL;
        }
        finally
        {
            lock (_gate) _completed.Set();
        }

        return HResults.S_OK;
    }

    public bool WaitForCompletion(TimeSpan timeout) => _completed.Wait(timeout);
}
