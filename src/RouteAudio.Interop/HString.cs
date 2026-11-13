using System.Runtime.InteropServices;

namespace RouteAudio.Interop;

/// <summary>HSTRING 拥有者，确保正确释放。</summary>
public sealed class HString : IDisposable
{
    private IntPtr _handle;

    private HString(IntPtr handle) => _handle = handle;

    public IntPtr DangerousGetHandle() => _handle;

    public static HString FromString(string value)
    {
        var hr = NativeMethods.WindowsCreateString(value, (uint)value.Length, out var handle);
        if (hr < 0) throw Marshal.GetExceptionForHR(hr)!;
        return new HString(handle);
    }

    public static string? ToString(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        var buffer = NativeMethods.WindowsGetStringRawBuffer(handle, out var len);
        if (buffer == IntPtr.Zero) return null;
        return len == 0 ? string.Empty : Marshal.PtrToStringUni(buffer, (int)len);
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.WindowsDeleteString(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
