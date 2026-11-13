using RouteAudio.Interop;

namespace RouteAudio.Audio;

/// <summary>
/// 每个线程使用 COM 前必须初始化公寓。WPF UI 线程是 STA，线程池线程需要显式初始化 MTA。
/// 重复初始化返回 S_FALSE / RPC_E_CHANGED_MODE，均视为可用。
/// </summary>
internal static class ComThread
{
    private const int RpcEChangedMode = unchecked((int)0x80010106);

    [ThreadStatic]
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;

        var hr = NativeMethods.CoInitializeEx(IntPtr.Zero, 0x0 /* COINIT_MULTITHREADED */);
        if (hr is 0 or 1 /* S_OK / S_FALSE */ || hr == RpcEChangedMode)
        {
            _initialized = true;
        }
    }
}
