using System.Runtime.InteropServices;

namespace RouteAudio.Interop;

internal static class NativeMethods
{
    private const string Combase = "combase.dll";
    private const string Ole32 = "ole32.dll";
    private const string Mmdevapi = "Mmdevapi.dll";
    private const string Winmm = "winmm.dll";
    private const string Kernel32 = "kernel32.dll";

    // ---------- COM / WinRT ----------

    [DllImport(Ole32, ExactSpelling = true, PreserveSig = true)]
    internal static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport(Ole32, ExactSpelling = true, PreserveSig = true)]
    internal static extern int CoUninitialize();

    [DllImport(Ole32, ExactSpelling = true, PreserveSig = true)]
    internal static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, ClsCtx dwClsContext, ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

    [DllImport(Combase, ExactSpelling = true, PreserveSig = true)]
    internal static extern int RoInitialize(uint initType);

    [DllImport(Combase, ExactSpelling = true, PreserveSig = true)]
    internal static extern int RoUninitialize();

    [DllImport(Combase, ExactSpelling = true, PreserveSig = true)]
    internal static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport(Combase, ExactSpelling = true, PreserveSig = true)]
    internal static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString, uint length, out IntPtr hstring);

    [DllImport(Combase, ExactSpelling = true, PreserveSig = true)]
    internal static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport(Combase, ExactSpelling = true)]
    internal static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [DllImport(Ole32, ExactSpelling = true, PreserveSig = true)]
    internal static extern int PropVariantClear(ref PropVariant pvar);

    [DllImport(Ole32, ExactSpelling = true)]
    internal static extern void CoTaskMemFree(IntPtr ptr);

    // ---------- 音频激活（进程回环 / 设备回环） ----------

    [DllImport(Mmdevapi, ExactSpelling = true, PreserveSig = true)]
    internal static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        IntPtr completionHandler,
        out IntPtr activationOperation);

    // ---------- WinMM（PoC 测试音） ----------

    [DllImport(Winmm, ExactSpelling = true, PreserveSig = true)]
    internal static extern int waveOutOpen(out IntPtr hWaveOut, uint uDeviceID, ref WaveFormatEx lpFormat,
        IntPtr dwCallback, IntPtr dwInstance, uint dwFlags);

    [DllImport(Winmm, ExactSpelling = true, PreserveSig = true)]
    internal static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveHdr, uint cbWaveHdr);

    [DllImport(Winmm, ExactSpelling = true, PreserveSig = true)]
    internal static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveHdr, uint cbWaveHdr);

    [DllImport(Winmm, ExactSpelling = true, PreserveSig = true)]
    internal static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveHdr, uint cbWaveHdr);

    [DllImport(Winmm, ExactSpelling = true, PreserveSig = true)]
    internal static extern int waveOutReset(IntPtr hWaveOut);

    [DllImport(Winmm, ExactSpelling = true, PreserveSig = true)]
    internal static extern int waveOutClose(IntPtr hWaveOut);

    [DllImport(Winmm, ExactSpelling = true, PreserveSig = true)]
    internal static extern int waveOutGetNumDevs();

    // ---------- 进程 ----------

    [DllImport(Kernel32, ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport(Kernel32, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport(Kernel32, ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags,
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder lpExeName, ref uint lpdwSize);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormatEx
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WaveHdr
{
    public IntPtr lpData;
    public uint dwBufferLength;
    public uint dwBytesRecorded;
    public IntPtr dwUser;
    public uint dwFlags;
    public uint dwLoops;
    public IntPtr lpNext;
    public IntPtr reserved;
}
