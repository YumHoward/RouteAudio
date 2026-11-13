using RouteAudio.Core.Abstractions;
using RouteAudio.Core.Config;
using RouteAudio.Core.Models;
using RouteAudio.Core.Rules;

namespace RouteAudio.Core.Routing;

/// <summary>
/// 路由协调器：把「配置 + 规则 + 运行状态」对账成「每个应用的期望输出」，并通过后端落地。
/// 采用声明式对账而不是一次性命令，因此可以自愈设备插拔、应用重启、外部改写。
/// </summary>
public sealed class RoutingService : IDisposable
{
    private readonly IDeviceCatalog _deviceCatalog;
    private readonly ISessionCatalog _sessionCatalog;
    private readonly IAppCatalog _appCatalog;
    private readonly IRoutingBackend _backend;
    private readonly ConfigStore _store;
    private readonly IAuditLog _log;

    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<string, RouteStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingCleanup = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IReadOnlyList<DeviceDescriptor> _devices = [];
    private IReadOnlyList<SessionInfo> _sessions = [];

    public RoutingService(
        IDeviceCatalog deviceCatalog,
        ISessionCatalog sessionCatalog,
        IAppCatalog appCatalog,
        IRoutingBackend backend,
        ConfigStore store,
        IAuditLog? log = null)
    {
        _deviceCatalog = deviceCatalog;
        _sessionCatalog = sessionCatalog;
        _appCatalog = appCatalog;
        _backend = backend;
        _store = store;
        _log = log ?? NullAuditLog.Instance;
    }

    public event EventHandler? Changed;

    public ConfigDocument Config => _store.Current;

    public Profile ActiveProfile => Config.ActiveProfile;

    public string ConfigPath => _store.FilePath;

    public string BackendStatus => _backend.StatusText;

    public bool BackendAvailable => _backend.IsAvailable;

    public BackendKind ActiveBackendKind => _backend.Kind;

    public string? LastError { get; private set; }

    public IReadOnlyList<DeviceDescriptor> Devices
    {
        get
        {
            lock (_stateGate) return _devices;
        }
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (!await _reconcileGate.WaitAsync(0, cancellationToken)) return;

        try
        {
            var devices = LoadDevices();
            var sessions = LoadSessions();
            var running = BuildRunningApps(sessions);

            lock (_stateGate)
            {
                _devices = devices;
                _sessions = sessions;
            }

            if (!_backend.IsAvailable)
            {
                foreach (var entry in ActiveProfile.Entries.Where(e => e.Enabled))
                {
                    SetStatus(BuildStatus(entry.App, RouteState.Failed, RouteReason.PolicyApiUnavailable, null, null));
                }

                LastError = "路由后端不可用";
                RaiseChanged();
                return;
            }

            var desired = BuildDesiredRoutes(devices, running);
            var desiredIds = new HashSet<string>(desired.Select(d => d.App.Id), StringComparer.OrdinalIgnoreCase);

            foreach (var route in desired)
            {
                await ApplyAsync(route, running, cancellationToken).ConfigureAwait(false);
            }

            foreach (var appId in _pendingCleanup.ToList())
            {
                if (running.TryGetValue(appId, out var ra))
                {
                    await _backend.RemoveAsync(ra.ProcessId, ra.App, cancellationToken).ConfigureAwait(false);
                    _pendingCleanup.Remove(appId);
                }
            }

            lock (_stateGate)
            {
                foreach (var key in _statuses.Keys.Where(k => !desiredIds.Contains(k)).ToList())
                {
                    _statuses.Remove(key);
                }
            }

            LastError = null;
            _log.Info($"[reconcile] devices={devices.Count} sessions={sessions.Count} running={running.Count} desired={desired.Count} statuses={_statuses.Count}");
            RaiseChanged();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _log.Error("对账失败", ex);
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    private async Task ApplyAsync(DesiredRoute route, IReadOnlyDictionary<string, RunningApp> running, CancellationToken cancellationToken)
    {
        if (!running.TryGetValue(route.App.Id, out var runningApp) || runningApp.Sessions.Count == 0)
        {
            SetStatus(BuildStatus(route.App, RouteState.Pending, RouteReason.NotRunning, route.Device, null));
            return;
        }

        var activeSessions = runningApp.Sessions.Where(s => s.IsActive).ToList();
        if (activeSessions.Count == 0)
        {
            SetStatus(BuildStatus(route.App, RouteState.Pending, RouteReason.NoAudioSession, route.Device, null));
            return;
        }

        if (route.Degraded)
        {
            var result = await _backend.ApplyAsync(
                new RouteRequest(route.App, runningApp.ProcessId, route.Device.Id, route.Role, route.Mode),
                cancellationToken).ConfigureAwait(false);

            SetStatus(result.Ok
                ? BuildStatus(route.App, RouteState.Degraded, RouteReason.EndpointNotFound, route.Device,
                    activeSessions[0].EndpointId, activeSessions[0].EndpointName, "目标设备不可用，已回退到默认设备")
                : BuildStatus(route.App, RouteState.Failed, result.Reason, route.Device, null, result.Message));

            return;
        }

        var apply = await _backend.ApplyAsync(
            new RouteRequest(route.App, runningApp.ProcessId, route.Device.Id, route.Role, route.Mode),
            cancellationToken).ConfigureAwait(false);

        if (!apply.Ok)
        {
            _log.Warn($"[apply] {route.App.ProcessName} → {route.Device.DisplayName} 失败: {apply.Reason} {apply.Message}");
            SetStatus(BuildStatus(route.App, RouteState.Failed, apply.Reason, route.Device, null, apply.Message));
            return;
        }

        var onTarget = activeSessions.FirstOrDefault(s => s.EndpointId == route.Device.Id);
        var state = onTarget is not null
            ? apply.Backend == BackendKind.Policy ? RouteState.ActivePolicy : RouteState.ActiveStreaming
            : RouteState.AppliedPendingSession;


        SetStatus(BuildStatus(
            route.App,
            state,
            RouteReason.None,
            route.Device,
            onTarget?.EndpointId ?? activeSessions[0].EndpointId,
            onTarget?.EndpointName ?? activeSessions[0].EndpointName));
    }

    private IReadOnlyList<DesiredRoute> BuildDesiredRoutes(
        IReadOnlyList<DeviceDescriptor> devices,
        IReadOnlyDictionary<string, RunningApp> running)
    {
        var result = new List<DesiredRoute>();
        var explicitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ActiveProfile.Entries.Where(e => e.Enabled))
        {
            explicitIds.Add(entry.App.Id);
            var (device, degraded) = ResolveTarget(entry.Target, devices);
            if (device is null) continue;
            result.Add(new DesiredRoute(entry.App, device, entry.Target.Role, entry.Mode, degraded));
        }

        foreach (var app in running.Values)
        {
            if (explicitIds.Contains(app.App.Id)) continue;

            var match = RuleEvaluator.Evaluate(ActiveProfile.Rules, new AppMatchContext(app.App));
            if (match is null) continue;

            foreach (var action in match.Actions)
            {
                switch (action)
                {
                    case RouteToDeviceAction toDevice:
                    {
                        var (device, degraded) = ResolveTarget(RouteTarget.ToDevice(toDevice.DeviceId, toDevice.Role), devices);
                        if (device is not null) result.Add(new DesiredRoute(app.App, device, toDevice.Role, RouteMode.Auto, degraded));
                        break;
                    }

                    case RouteToRoleAction toRole:
                    {
                        var (device, degraded) = ResolveTarget(RouteTarget.ToRole(toRole.Role), devices);
                        if (device is not null) result.Add(new DesiredRoute(app.App, device, toRole.Role, RouteMode.Auto, degraded));
                        break;
                    }

                    case FollowDefaultAction:
                    {
                        var (device, degraded) = ResolveTarget(RouteTarget.ToRole(AudioRole.Console), devices);
                        if (device is not null) result.Add(new DesiredRoute(app.App, device, AudioRole.Console, RouteMode.Auto, degraded));
                        break;
                    }
                }

                if (result.Count > 0 && result[^1].App.Id == app.App.Id) break;
            }
        }

        return result;
    }

    private (DeviceDescriptor? Device, bool Degraded) ResolveTarget(RouteTarget target, IReadOnlyList<DeviceDescriptor> devices)
    {
        if (!target.IsRoleTarget)
        {
            var explicitDevice = devices.FirstOrDefault(d => string.Equals(d.Id, target.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (explicitDevice is not null) return (explicitDevice, false);

            var fallback = DefaultForRole(target.Role, devices);
            return (fallback, fallback is not null);
        }

        return (DefaultForRole(target.Role, devices), false);
    }

    private static DeviceDescriptor? DefaultForRole(AudioRole role, IReadOnlyList<DeviceDescriptor> devices) => role switch
    {
        AudioRole.Communications => devices.FirstOrDefault(d => d.IsDefaultCommunications)
                                    ?? devices.FirstOrDefault(d => d.IsDefaultConsole)
                                    ?? devices.FirstOrDefault(),
        AudioRole.Multimedia => devices.FirstOrDefault(d => d.IsDefaultMultimedia)
                                ?? devices.FirstOrDefault(d => d.IsDefaultConsole)
                                ?? devices.FirstOrDefault(),
        _ => devices.FirstOrDefault(d => d.IsDefaultConsole) ?? devices.FirstOrDefault(),
    };

    private Dictionary<string, RunningApp> BuildRunningApps(IReadOnlyList<SessionInfo> sessions)
    {
        var result = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        var ownPid = (uint)Environment.ProcessId;
        var resolved = new Dictionary<uint, AppKey>();

        foreach (var session in sessions)
        {
            if (session.IsSystemSounds || session.ProcessId == ownPid) continue;

            if (!resolved.TryGetValue(session.ProcessId, out var app))
            {
                app = _appCatalog.Resolve(session.ProcessId);
                resolved[session.ProcessId] = app;
            }

            if (!result.TryGetValue(app.Id, out var entry))
            {
                result[app.Id] = entry = new RunningApp(app, session.ProcessId);
            }

            entry.Sessions.Add(session);
        }

        return result;
    }

    private IReadOnlyList<DeviceDescriptor> LoadDevices()
    {
        try
        {
            return _deviceCatalog.GetDevices()
                .Select(dev => dev with
                {
                    Alias = Config.DeviceAliases.TryGetValue(dev.Id, out var alias) && !string.IsNullOrWhiteSpace(alias) ? alias : null,
                })
                .OrderByDescending(d => d.IsDefaultAny)
                .ThenBy(d => d.Name, StringComparer.CurrentCulture)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Error("枚举设备失败", ex);
            return [];
        }
    }

    private IReadOnlyList<SessionInfo> LoadSessions()
    {
        try
        {
            return _sessionCatalog.GetSessions();
        }
        catch (Exception ex)
        {
            _log.Error("枚举会话失败", ex);
            return [];
        }
    }

    // ---------------- 对外操作 ----------------

    public async Task<RouteStatus> RouteAsync(AppKey app, string deviceId, AudioRole role = AudioRole.Console)
    {
        var entry = ActiveProfile.GetOrCreateEntry(app);
        entry.Target = RouteTarget.ToDevice(deviceId, role);
        entry.Enabled = true;
        entry.Mode = RouteMode.Auto;
        _pendingCleanup.Remove(app.Id);
        _store.Save();
        await ReconcileAsync().ConfigureAwait(false);
        return GetStatus(app);
    }

    public async Task UnrouteAsync(AppKey app)
    {
        var entry = ActiveProfile.FindEntry(app);
        if (entry is not null)
        {
            ActiveProfile.Entries.Remove(entry);
            _store.Save();
        }

        _pendingCleanup.Add(app.Id);
        await ReconcileAsync().ConfigureAwait(false);
    }

    public async Task SwitchProfileAsync(string profileId)
    {
        if (Config.Profiles.All(p => p.Id != profileId)) return;
        if (Config.ActiveProfileId == profileId) return;

        foreach (var entry in Config.ActiveProfile.Entries) _pendingCleanup.Add(entry.App.Id);

        Config.ActiveProfileId = profileId;
        _store.Save();
        await ReconcileAsync().ConfigureAwait(false);
    }

    /// <summary>UI 直接修改配置（规则/条目）后调用：保存并立即对账。</summary>
    public async Task ApplyConfigChangesAsync()
    {
        _store.Save();
        await ReconcileAsync().ConfigureAwait(false);
    }

    /// <summary>设置设备端点音量（0..1）。</summary>
    public async Task<bool> SetDeviceVolumeAsync(string deviceId, double volume)
    {
        var ok = _deviceCatalog.SetVolume(deviceId, volume);
        if (!ok) return false;

        RefreshDeviceList();
        await Task.CompletedTask;
        return true;
    }

    /// <summary>设置设备静音。</summary>
    public async Task<bool> SetDeviceMuteAsync(string deviceId, bool muted)
    {
        var ok = _deviceCatalog.SetMute(deviceId, muted);
        if (!ok) return false;

        RefreshDeviceList();
        await Task.CompletedTask;
        return true;
    }

    private void RefreshDeviceList()
    {
        var devices = LoadDevices();
        lock (_stateGate)
        {
            _devices = devices;
        }

        RaiseChanged();
    }

    public async Task<Profile> CreateProfileAsync(string name)
    {
        var profile = new Profile
        {
            Id = "p" + Guid.NewGuid().ToString("n")[..8],
            Name = string.IsNullOrWhiteSpace(name) ? "新配置档" : name.Trim(),
        };

        foreach (var entry in Config.ActiveProfile.Entries) _pendingCleanup.Add(entry.App.Id);

        Config.Profiles.Add(profile);
        Config.ActiveProfileId = profile.Id;
        _store.Save();
        await ReconcileAsync().ConfigureAwait(false);
        return profile;
    }

    public async Task<Profile?> DuplicateProfileAsync(Profile source, string name)
    {
        if (Config.Profiles.All(p => p.Id != source.Id)) return null;

        foreach (var entry in Config.ActiveProfile.Entries) _pendingCleanup.Add(entry.App.Id);

        var copy = source.Clone(
            "p" + Guid.NewGuid().ToString("n")[..8],
            string.IsNullOrWhiteSpace(name) ? source.Name + " 副本" : name.Trim());

        Config.Profiles.Add(copy);
        Config.ActiveProfileId = copy.Id;
        _store.Save();
        await ReconcileAsync().ConfigureAwait(false);
        return copy;
    }

    public async Task RenameProfileAsync(Profile profile, string name)
    {
        var target = Config.Profiles.FirstOrDefault(p => p.Id == profile.Id);
        if (target is null || string.IsNullOrWhiteSpace(name)) return;
        target.Name = name.Trim();
        _store.Save();
        await ReconcileAsync().ConfigureAwait(false);
    }

    public async Task<bool> DeleteProfileAsync(Profile profile)
    {
        if (Config.Profiles.Count <= 1) return false;

        var removed = Config.Profiles.RemoveAll(p => p.Id == profile.Id) > 0;
        if (!removed) return false;

        foreach (var entry in profile.Entries) _pendingCleanup.Add(entry.App.Id);
        if (Config.ActiveProfileId == profile.Id) Config.ActiveProfileId = Config.Profiles[0].Id;

        _store.Save();
        await ReconcileAsync().ConfigureAwait(false);
        return true;
    }

    public async Task RenameDeviceAsync(string deviceId, string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) Config.DeviceAliases.Remove(deviceId);
        else Config.DeviceAliases[deviceId] = alias.Trim();
        _store.Save();
        await ReconcileAsync().ConfigureAwait(false);
    }

    public async Task ClearAllRoutesAsync()
    {
        var entries = ActiveProfile.Entries.ToList();
        ActiveProfile.Entries.Clear();
        foreach (var entry in entries) _pendingCleanup.Add(entry.App.Id);
        _store.Save();
        await ReconcileAsync().ConfigureAwait(false);
    }

    public RouteStatus GetStatus(AppKey app)
    {
        lock (_stateGate)
        {
            return _statuses.TryGetValue(app.Id, out var status)
                ? status
                : RouteStatus.For(app, RouteState.NotConfigured);
        }
    }

    public IReadOnlyList<AppRuntimeView> GetRunningApps()
    {
        IReadOnlyList<SessionInfo> sessions;
        lock (_stateGate) sessions = _sessions;

        var result = new List<AppRuntimeView>();
        var ownPid = (uint)Environment.ProcessId;

        var entries = sessions
            .Where(s => !s.IsSystemSounds && s.ProcessId != ownPid)
            .Select(s => (Session: s, App: _appCatalog.Resolve(s.ProcessId)))
            .ToList();

        foreach (var group in entries.GroupBy(e => e.App.Id, StringComparer.OrdinalIgnoreCase))
        {
            var app = group.First().App;
            var activeSession = group.Select(e => e.Session).FirstOrDefault(s => s.IsActive) ?? group.First().Session;

            lock (_stateGate)
            {
                _statuses.TryGetValue(app.Id, out var status);
                result.Add(new AppRuntimeView
                {
                    App = app,
                    ProcessId = group.First().Session.ProcessId,
                    CurrentDeviceId = activeSession.EndpointId,
                    CurrentDeviceName = activeSession.EndpointName,
                    HasAudio = group.Any(e => e.Session.IsActive),
                    Volume = activeSession.Volume,
                    Muted = activeSession.Muted,
                    Status = status,
                });
            }
        }

        return result.OrderBy(v => v.App.ProcessName, StringComparer.CurrentCulture).ToList();
    }

    public IReadOnlyList<RouteStatus> GetStatuses()
    {
        lock (_stateGate) return _statuses.Values.ToList();
    }

    private void SetStatus(RouteStatus status)
    {
        RouteStatus? previous;
        lock (_stateGate)
        {
            _statuses.TryGetValue(status.App.Id, out previous);
            _statuses[status.App.Id] = status;
        }

        if (previous is null || previous.State != status.State || previous.Reason != status.Reason)
        {
            _log.Info($"[route] {status.App.ProcessName}: {previous?.State.ToString() ?? "None"} → {status.State}" +
                      $"{(status.Reason == RouteReason.None ? "" : $" ({status.Reason.ToText()})")}");
        }
    }

    private static RouteStatus BuildStatus(
        AppKey app,
        RouteState state,
        RouteReason reason,
        DeviceDescriptor? desired,
        string? actualDeviceId,
        string? actualDeviceName = null,
        string? message = null)
    {
        return new RouteStatus
        {
            App = app,
            State = state,
            Reason = reason,
            DesiredDeviceId = desired?.Id,
            DesiredDeviceName = desired?.DisplayName,
            ActualDeviceId = actualDeviceId,
            ActualDeviceName = actualDeviceName,
            Backend = BackendKind.Policy,
            Message = message,
            Timestamp = DateTimeOffset.Now,
        };
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _log.Error("状态通知失败", ex);
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var seconds = Math.Clamp(Config.Settings.ReconcileSeconds, 1, 60);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
                await ReconcileAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error("对账循环异常", ex);
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // 忽略关闭期间的异常
        }

        _cts?.Dispose();
        _reconcileGate.Dispose();
    }

    private sealed class RunningApp(AppKey app, uint processId)
    {
        public AppKey App { get; } = app;

        public uint ProcessId { get; } = processId;

        public List<SessionInfo> Sessions { get; } = [];
    }

    private sealed record DesiredRoute(AppKey App, DeviceDescriptor Device, AudioRole Role, RouteMode Mode, bool Degraded);
}
