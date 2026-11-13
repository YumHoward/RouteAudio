using System.Runtime.InteropServices;

namespace RouteAudio.Interop;

public enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2,
}

public enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2,
}

[Flags]
public enum DeviceState : uint
{
    ACTIVE = 0x00000001,
    DISABLED = 0x00000002,
    NOTPRESENT = 0x00000004,
    UNPLUGGED = 0x00000008,
    ALL = 0x0000000F,
}

public enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2,
}

[Flags]
public enum ClsCtx : uint
{
    INPROC_SERVER = 0x1,
    INPROC_HANDLER = 0x2,
    LOCAL_SERVER = 0x4,
    REMOTE_SERVER = 0x10,
    ALL = INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER,
}

public enum Stgm : uint
{
    READ = 0x00000000,
    WRITE = 0x00000001,
    READWRITE = 0x00000002,
}

public enum AudioClientShareMode
{
    Shared = 0,
    Exclusive = 1,
}

public static class Guids
{
    public static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    public static readonly Guid IID_IMMNotificationClient = new("7991EEC9-7E89-4D85-8390-6C703CEC60C0");
    public static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    public static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    public static readonly Guid IID_IAudioSessionControl2 = new("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
    public static readonly Guid IID_ISimpleAudioVolume = new("87CE5498-68D6-44E5-9215-6DA47EF883D8");
    public static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid IID_IAudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
    public static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    public static readonly Guid IID_IActivateAudioInterfaceCompletionHandler = new("41D949AB-9862-444A-80F6-C261334DA5EB");
    public static readonly Guid IID_IActivateAudioInterfaceAsyncOperation = new("72A22D78-CDE4-431D-B8CC-843A71199B6D");
    public static readonly Guid IID_IInspectable = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
}

/// <summary>属性键（PKEY）只保留本项目需要的部分。</summary>
public static class PropertyKeys
{
    // {a45c254e-df1c-4efd-8020-67d146a850e0}
    private static readonly Guid DeviceFmtid = new("a45c254e-df1c-4efd-8020-67d146a850e0");

    public static readonly PropertyKey DeviceDesc = new(DeviceFmtid, 2);
    public static readonly PropertyKey DeviceFriendlyName = new(DeviceFmtid, 14);
    public static readonly PropertyKey DeviceEnumeratorName = new(DeviceFmtid, 24);
    public static readonly PropertyKey DeviceContainerId = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    public static readonly PropertyKey DeviceInterfaceFriendlyName = new(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 6);
    public static readonly PropertyKey AudioEndpointFormFactor = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct PropertyKey(Guid fmtid, uint pid)
{
    public readonly Guid FmtId = fmtid;
    public readonly uint Pid = pid;
}

public static class HResults
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_POINTER = unchecked((int)0x80004003);
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int E_INVALIDARG = unchecked((int)0x80070057);
    public const int E_ACCESSDENIED = unchecked((int)0x80070005);
    public const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
    public const int AUDCLNT_E_UNSUPPORTED_FORMAT = unchecked((int)0x88890008);
    public const int AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED = unchecked((int)0x8889000E);
    public const int ERROR_NOT_FOUND = unchecked((int)0x80070490);
    public const int ERROR_PROC_NOT_FOUND = unchecked((int)0x8007007F);

    public static bool Succeeded(int hr) => hr >= 0;
    public static bool Failed(int hr) => hr < 0;

    public static string Describe(int hr) => $"0x{hr:X8}";
}
