using System.Runtime.InteropServices;
using RouteAudio.Interop;

namespace RouteAudio.Audio;

/// <summary>跨所有渲染端点枚举音频会话。</summary>
public sealed class SessionService : IDisposable
{
    private readonly DeviceService _devices;

    public SessionService(DeviceService devices) => _devices = devices;

    public IReadOnlyList<AudioSessionInfo> GetAllSessions()
    {
        ComThread.EnsureInitialized();
        var result = new List<AudioSessionInfo>();
        foreach (var endpoint in _devices.GetRenderEndpoints())
        {
            result.AddRange(GetSessionsForEndpoint(endpoint));
        }

        return result;
    }

    public IReadOnlyList<AudioSessionInfo> GetSessionsForEndpoint(DeviceEndpointInfo endpoint)
    {
        var list = new List<AudioSessionInfo>();
        var device = _devices.OpenDevice(endpoint.Id);
        if (device is null) return list;

        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            var iid = Guids.IID_IAudioSessionManager2;
            var hr = device.Activate(ref iid, ClsCtx.ALL, IntPtr.Zero, out var obj);
            if (hr < 0 || obj is not IAudioSessionManager2 m) return list;
            manager = m;

            hr = manager.GetSessionEnumerator(out var sessionEnum);
            if (hr < 0) return list;
            sessions = sessionEnum;

            if (sessions.GetCount(out var count) < 0) return list;

            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var control) < 0 || control is null) continue;
                var info = ReadSession(control, endpoint);
                if (info is not null) list.Add(info);
                Marshal.ReleaseComObject(control);
            }
        }
        finally
        {
            if (sessions is not null) Marshal.ReleaseComObject(sessions);
            if (manager is not null) Marshal.ReleaseComObject(manager);
            Marshal.ReleaseComObject(device);
        }

        return list;
    }

    public IReadOnlyList<AudioSessionInfo> GetSessionsForProcess(uint pid) =>
        GetAllSessions().Where(s => s.ProcessId == pid).ToList();

    /// <summary>对某进程在所有端点上的会话设置静音。返回实际生效的会话数。</summary>
    public int SetMuteForProcess(uint pid, bool muted)
    {
        ComThread.EnsureInitialized();
        var applied = 0;
        foreach (var endpoint in _devices.GetRenderEndpoints())
        {
            var device = _devices.OpenDevice(endpoint.Id);
            if (device is null) continue;

            IAudioSessionManager2? manager = null;
            IAudioSessionEnumerator? sessions = null;
            try
            {
                var iid = Guids.IID_IAudioSessionManager2;
                if (device.Activate(ref iid, ClsCtx.ALL, IntPtr.Zero, out var obj) < 0 ||
                    obj is not IAudioSessionManager2 m) continue;
                manager = m;

                if (manager.GetSessionEnumerator(out var sessionEnum) < 0) continue;
                sessions = sessionEnum;
                if (sessions.GetCount(out var count) < 0) continue;

                for (var i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var control) < 0 || control is null) continue;
                    try
                    {
                        if (control is IAudioSessionControl2 c2 && c2.GetProcessId(out var sessionPid) >= 0 && sessionPid == pid
                            && control is ISimpleAudioVolume volume)
                        {
                            var context = Guid.Empty;
                            if (volume.SetMute(muted, ref context) >= 0) applied++;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(control);
                    }
                }
            }
            finally
            {
                if (sessions is not null) Marshal.ReleaseComObject(sessions);
                if (manager is not null) Marshal.ReleaseComObject(manager);
                Marshal.ReleaseComObject(device);
            }
        }

        return applied;
    }

    /// <summary>对某进程在所有端点上的会话设置音量。返回实际生效的会话数。</summary>
    public int SetVolumeForProcess(uint pid, float level)
    {
        ComThread.EnsureInitialized();
        var applied = 0;
        foreach (var endpoint in _devices.GetRenderEndpoints())
        {
            var device = _devices.OpenDevice(endpoint.Id);
            if (device is null) continue;

            IAudioSessionManager2? manager = null;
            IAudioSessionEnumerator? sessions = null;
            try
            {
                var iid = Guids.IID_IAudioSessionManager2;
                if (device.Activate(ref iid, ClsCtx.ALL, IntPtr.Zero, out var obj) < 0 ||
                    obj is not IAudioSessionManager2 m) continue;
                manager = m;

                if (manager.GetSessionEnumerator(out var sessionEnum) < 0) continue;
                sessions = sessionEnum;
                if (sessions.GetCount(out var count) < 0) continue;

                for (var i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var control) < 0 || control is null) continue;
                    try
                    {
                        if (control is IAudioSessionControl2 c2 && c2.GetProcessId(out var sessionPid) >= 0 && sessionPid == pid
                            && control is ISimpleAudioVolume volume)
                        {
                            var context = Guid.Empty;
                            if (volume.SetMasterVolume(Math.Clamp(level, 0f, 1f), ref context) >= 0) applied++;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(control);
                    }
                }
            }
            finally
            {
                if (sessions is not null) Marshal.ReleaseComObject(sessions);
                if (manager is not null) Marshal.ReleaseComObject(manager);
                Marshal.ReleaseComObject(device);
            }
        }

        return applied;
    }

    private static AudioSessionInfo? ReadSession(IAudioSessionControl control, DeviceEndpointInfo endpoint)
    {
        if (control is not IAudioSessionControl2 control2) return null;

        if (control2.GetProcessId(out var pid) < 0) pid = 0;

        string? sessionId = null, instanceId = null, displayName = null;
        if (control2.GetSessionIdentifier(out var sid) >= 0) sessionId = sid;
        if (control2.GetSessionInstanceIdentifier(out var iidStr) >= 0) instanceId = iidStr;
        if (control.GetDisplayName(out var dn) >= 0) displayName = dn;

        var state = AudioSessionState.Inactive;
        _ = control.GetState(out state);

        var isSystemSounds = control2.IsSystemSoundsSession() == 0;

        var volume = 0f;
        var muted = false;
        if (control is ISimpleAudioVolume simple)
        {
            _ = simple.GetMasterVolume(out volume);
            _ = simple.GetMute(out muted);
        }

        var (procName, procPath) = ProcessInfoHelper.GetProcessInfo(pid);

        return new AudioSessionInfo
        {
            EndpointId = endpoint.Id,
            EndpointName = endpoint.FriendlyName,
            ProcessId = pid,
            ProcessName = procName,
            ProcessPath = procPath,
            SessionIdentifier = sessionId,
            InstanceIdentifier = instanceId,
            DisplayName = displayName,
            State = state,
            IsSystemSounds = isSystemSounds,
            Volume = volume,
            Muted = muted,
        };
    }

    public void Dispose()
    {
    }
}
