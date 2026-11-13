using System.Runtime.InteropServices;
using RouteAudio.Interop;

namespace RouteAudio.Audio;

/// <summary>COM 公寓初始化。整个进程只初始化一次，退出时反初始化。</summary>
public sealed class ComApartment : IDisposable
{
    private readonly bool _owns;

    private ComApartment(bool owns) => _owns = owns;

    public static ComApartment InitializeMultithreaded()
    {
        var hr = NativeMethods.CoInitializeEx(IntPtr.Zero, 0x0 /* COINIT_MULTITHREADED */);
        if (hr is not (0 or 1)) // S_OK / S_FALSE
        {
            throw Marshal.GetExceptionForHR(hr)!;
        }

        return new ComApartment(hr == 0);
    }

    public void Dispose()
    {
        if (_owns) NativeMethods.CoUninitialize();
    }
}
