using System.Runtime.InteropServices;
using System.Text;
using RouteAudio.Interop;

namespace RouteAudio.Audio;

/// <summary>设备枚举与属性读取。</summary>
public sealed class DeviceService : IDisposable
{
    private IMMDeviceEnumerator? _enumerator;

    public DeviceService()
    {
        ComThread.EnsureInitialized();
        EnsureEnumerator();
    }

    private IMMDeviceEnumerator EnsureEnumerator()
    {
        ComThread.EnsureInitialized();
        if (_enumerator is not null) return _enumerator;

        var clsid = Guids.CLSID_MMDeviceEnumerator;
        var iid = Guids.IID_IMMDeviceEnumerator;
        var hr = NativeMethods.CoCreateInstance(ref clsid, IntPtr.Zero, ClsCtx.ALL, ref iid, out var obj);
        if (hr < 0) throw new InvalidOperationException($"CoCreateInstance(MMDeviceEnumerator) 0x{hr:X8}");

        _enumerator = (IMMDeviceEnumerator)obj;
        return _enumerator;
    }

    public IReadOnlyList<DeviceEndpointInfo> GetRenderEndpoints(DeviceState state = DeviceState.ACTIVE)
    {
        var enumerator = EnsureEnumerator();
        var hr = enumerator.EnumAudioEndpoints(EDataFlow.eRender, state, out var collection);
        if (hr < 0) throw new InvalidOperationException($"EnumAudioEndpoints 0x{hr:X8}");

        var defaults = new Dictionary<ERole, string?>();
        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
        {
            defaults[role] = TryGetDefaultId(role);
        }

        hr = collection.GetCount(out var count);
        if (hr < 0) throw new InvalidOperationException($"GetCount 0x{hr:X8}");

        var result = new List<DeviceEndpointInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            if (collection.Item(i, out var device) < 0 || device is null) continue;
            result.Add(ReadDevice(device, defaults));
        }

        return result;
    }

    public string? TryGetDefaultId(ERole role)
    {
        try
        {
            var hr = EnsureEnumerator().GetDefaultAudioEndpoint(EDataFlow.eRender, role, out var device);
            if (hr < 0 || device is null) return null;
            var idHr = device.GetId(out var id);
            return idHr >= 0 ? id : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public DeviceEndpointInfo? TryGetDevice(string endpointId)
    {
        var hr = EnsureEnumerator().GetDevice(endpointId, out var device);
        if (hr < 0 || device is null) return null;
        var defaults = new Dictionary<ERole, string?>
        {
            [ERole.eConsole] = TryGetDefaultId(ERole.eConsole),
            [ERole.eMultimedia] = TryGetDefaultId(ERole.eMultimedia),
            [ERole.eCommunications] = TryGetDefaultId(ERole.eCommunications),
        };
        return ReadDevice(device, defaults);
    }

    internal IMMDevice? OpenDevice(string endpointId)
    {
        var hr = EnsureEnumerator().GetDevice(endpointId, out var device);
        return hr >= 0 ? device : null;
    }

    /// <summary>注册设备事件回调。返回是否成功。</summary>
    public bool RegisterNotificationClient(IMMNotificationClient client)
    {
        ComThread.EnsureInitialized();
        return EnsureEnumerator().RegisterEndpointNotificationCallback(client) >= 0;
    }

    public void UnregisterNotificationClient(IMMNotificationClient client)
    {
        ComThread.EnsureInitialized();
        _ = EnsureEnumerator().UnregisterEndpointNotificationCallback(client);
    }

    /// <summary>读取端点主音量与静音状态（诊断/展示用）。</summary>
    public bool TryGetEndpointVolume(string endpointId, out bool muted, out float volume, out int hresult)
    {
        ComThread.EnsureInitialized();
        muted = false;
        volume = 1f;
        hresult = 0;

        var device = OpenDevice(endpointId);
        if (device is null)
        {
            hresult = unchecked((int)0x80070490);
            return false;
        }

        try
        {
            var iid = Guids.IID_IAudioEndpointVolume;
            var hr = device.Activate(ref iid, ClsCtx.ALL, IntPtr.Zero, out var obj);
            if (hr < 0)
            {
                hresult = hr;
                return false;
            }

            if (obj is not IAudioEndpointVolume endpointVolume)
            {
                hresult = unchecked((int)0x80004002);
                return false;
            }

            try
            {
                _ = endpointVolume.GetMute(out muted);
                _ = endpointVolume.GetMasterVolumeLevelScalar(out volume);
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(endpointVolume);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private static DeviceEndpointInfo ReadDevice(IMMDevice device, IReadOnlyDictionary<ERole, string?> defaults)
    {
        var id = string.Empty;
        if (device.GetId(out var gotId) >= 0) id = gotId;

        string? friendly = null, desc = null, iface = null, enumeratorName = null;
        Guid? containerId = null;
        uint formFactor = 0;

        if (device.OpenPropertyStore(Stgm.READ, out var store) >= 0 && store is not null)
        {
            friendly = ReadString(store, PropertyKeys.DeviceFriendlyName);
            desc = ReadString(store, PropertyKeys.DeviceDesc);
            iface = ReadString(store, PropertyKeys.DeviceInterfaceFriendlyName);
            enumeratorName = ReadString(store, PropertyKeys.DeviceEnumeratorName);
            formFactor = ReadUInt32(store, PropertyKeys.AudioEndpointFormFactor) ?? 0;
            containerId = ReadGuid(store, PropertyKeys.DeviceContainerId);

            if (string.IsNullOrWhiteSpace(friendly)) friendly = desc;
            if (string.IsNullOrWhiteSpace(friendly)) friendly = iface;
            Marshal.ReleaseComObject(store);
        }

        var state = DeviceState.ACTIVE;
        if (device.GetState(out var st) >= 0) state = st;

        return new DeviceEndpointInfo
        {
            Id = id,
            FriendlyName = string.IsNullOrWhiteSpace(friendly) ? "(未命名设备)" : friendly!,
            Description = desc,
            InterfaceName = iface,
            EnumeratorName = enumeratorName,
            FormFactor = formFactor,
            ContainerId = containerId,
            State = state,
            IsDefaultConsole = id.Length > 0 && id == defaults[ERole.eConsole],
            IsDefaultMultimedia = id.Length > 0 && id == defaults[ERole.eMultimedia],
            IsDefaultCommunications = id.Length > 0 && id == defaults[ERole.eCommunications],
        };
    }

    private static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var pv) < 0) return null;
        try
        {
            return pv.AsString();
        }
        finally
        {
            pv.Clear();
        }
    }

    private static uint? ReadUInt32(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var pv) < 0) return null;
        try
        {
            return pv.AsUInt32();
        }
        finally
        {
            pv.Clear();
        }
    }

    private static Guid? ReadGuid(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var pv) < 0) return null;
        try
        {
            return pv.VarType == 72 /* VT_CLSID */ && pv.PointerValue != IntPtr.Zero
                ? Marshal.PtrToStructure<Guid>(pv.PointerValue)
                : null;
        }
        finally
        {
            pv.Clear();
        }
    }

    public void Dispose()
    {
        if (_enumerator is not null)
        {
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }
}

public static class ProcessInfoHelper
{
    public static (string? Name, string? Path) GetProcessInfo(uint pid)
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        var handle = NativeMethods.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return (SafeGetName(pid), null);
        try
        {
            var sb = new StringBuilder(1024);
            uint size = 1024;
            if (NativeMethods.QueryFullProcessImageNameW(handle, 0, sb, ref size))
            {
                var path = sb.ToString(0, (int)size);
                return (System.IO.Path.GetFileNameWithoutExtension(path), path);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }

        return (SafeGetName(pid), null);
    }

    private static string? SafeGetName(uint pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
