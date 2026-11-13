using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using RouteAudio.Audio;
using RouteAudio.Core.Abstractions;
using RouteAudio.Core.Config;
using RouteAudio.Core.Models;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace RouteAudio.App.ViewModels;

internal static class StatusVisuals
{
    public static readonly Brush Success = new SolidColorBrush(Color.FromRgb(0x15, 0xA0, 0x5C));
    public static readonly Brush SuccessSoft = new SolidColorBrush(Color.FromRgb(0xE7, 0xF7, 0xEE));
    public static readonly Brush Warning = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
    public static readonly Brush WarningSoft = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xE2));
    public static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    public static readonly Brush DangerSoft = new SolidColorBrush(Color.FromRgb(0xFD, 0xEC, 0xEC));
    public static readonly Brush Neutral = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
    public static readonly Brush NeutralSoft = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF5));

    static StatusVisuals()
    {
        foreach (var brush in new[] { Success, SuccessSoft, Warning, WarningSoft, Danger, DangerSoft, Neutral, NeutralSoft })
        {
            brush.Freeze();
        }
    }

    public static (Brush Foreground, Brush Background) For(RouteState state) => state switch
    {
        RouteState.ActivePolicy or RouteState.ActiveStreaming => (Success, SuccessSoft),
        RouteState.Pending or RouteState.AppliedPendingSession => (Warning, WarningSoft),
        RouteState.Failed or RouteState.Degraded => (Danger, DangerSoft),
        _ => (Neutral, NeutralSoft),
    };
}

public sealed class AppItemViewModel
{
    public AppItemViewModel(AppRuntimeView view)
    {
        View = view;
        Name = string.IsNullOrWhiteSpace(view.App.ProductName) ? view.App.ProcessName : view.App.ProductName!;
        Initial = Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
        var (foreground, background) = StatusVisuals.For(view.Status?.State ?? RouteState.NotConfigured);
        StatusForeground = foreground;
        StatusBackground = background;
    }

    public AppRuntimeView View { get; }

    public AppKey App => View.App;

    public uint ProcessId => View.ProcessId;

    public string Name { get; }

    public string Initial { get; }

    public string ProcessName => View.App.ProcessName;

    public string CurrentDeviceName => View.CurrentDeviceName ?? "无输出";

    public bool HasStatus => View.Status is not null && View.Status.State != RouteState.NotConfigured;

    public string StateText => View.Status?.StateText ?? "未设置";

    public string ReasonText => View.Status is { Reason: not RouteReason.None } s ? s.Reason.ToText() : string.Empty;

    public string DesiredDeviceName => View.Status?.DesiredDeviceName ?? string.Empty;

    public bool IsConfigured => HasStatus || !string.IsNullOrEmpty(DesiredDeviceName);

    public Brush StatusForeground { get; }

    public Brush StatusBackground { get; }

    public string RouteHint => HasStatus && !string.IsNullOrEmpty(DesiredDeviceName)
        ? $"→ {DesiredDeviceName}"
        : "拖到右侧设备即可路由";
}

public sealed class RouteItemViewModel
{
    public RouteItemViewModel(RouteEntry entry, RouteStatus? status)
    {
        Entry = entry;
        Status = status;
        AppName = string.IsNullOrWhiteSpace(entry.App.ProductName) ? entry.App.ProcessName : entry.App.ProductName!;
        var (foreground, background) = StatusVisuals.For(status?.State ?? RouteState.Pending);
        StatusForeground = foreground;
        StatusBackground = background;
    }

    public RouteEntry Entry { get; }

    public RouteStatus? Status { get; }

    public AppKey App => Entry.App;

    public string AppName { get; }

    public string StateText => Status?.StateText ?? "等待应用出声";

    public string ReasonText => Status is { Reason: not RouteReason.None } s ? s.Reason.ToText() : string.Empty;

    public bool IsError => Status?.State is RouteState.Failed or RouteState.Degraded;

    public Brush StatusForeground { get; }

    public Brush StatusBackground { get; }
}

public sealed class DeviceLaneViewModel : ObservableObject
{
    private DeviceDescriptor _device;
    private double _volumePercent;
    private bool _isMuted;
    private bool _isDropTarget;

    public DeviceLaneViewModel(DeviceDescriptor device)
    {
        _device = device;
        _volumePercent = device.Volume * 100;
        _isMuted = device.Muted;
        Routes = [];
    }

    public DeviceDescriptor Device => _device;

    public string Title => _device.DisplayName;

    public string Subtitle
    {
        get
        {
            if (_device.IsVirtual) return "虚拟音频设备";
            return _device.EnumeratorName switch
            {
                "USB" => "USB 音频设备",
                "BTHENUM" => "蓝牙音频设备",
                "HDAUDIO" => "板载音频设备",
                "SWD" => "软件音频设备",
                "ROOT" => "虚拟音频设备",
                _ => "音频输出设备",
            };
        }
    }

    public string IconGlyph
    {
        get
        {
            var name = _device.Name;
            if (name.Contains("耳机", StringComparison.Ordinal) || name.Contains("headphone", StringComparison.OrdinalIgnoreCase))
                return "\uE7F6";
            if (name.Contains("HDMI", StringComparison.OrdinalIgnoreCase) || name.Contains("TV", StringComparison.OrdinalIgnoreCase))
                return "\uE7F4";
            return "\uE767";
        }
    }

    public bool IsDefault => _device.IsDefaultConsole;

    public bool IsVirtual => _device.IsVirtual;

    public bool IsCommunicationsDefault => _device.IsDefaultCommunications;

    public ObservableCollection<RouteItemViewModel> Routes { get; }

    public bool HasRoutes => Routes.Count > 0;

    public bool IsEmpty => Routes.Count == 0;

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            if (!SetProperty(ref _volumePercent, value)) return;
            OnPropertyChanged(nameof(VolumeText));
            OnPropertyChanged(nameof(IsAudible));
        }
    }

    public string VolumeText => $"{Math.Round(_volumePercent):0}%";

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (!SetProperty(ref _isMuted, value)) return;
            OnPropertyChanged(nameof(MuteGlyph));
            OnPropertyChanged(nameof(IsAudible));
        }
    }

    public string MuteGlyph => _isMuted ? "\uE74F" : "\uE767";

    public bool IsAudible => !_isMuted && _volumePercent > 0.5;

    public string MuteTooltip => _isMuted ? "取消静音" : "静音此设备";

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }

    /// <summary>用户正在拖动音量时，来自系统的刷新不应覆盖滑块。</summary>
    public bool IsAdjustingVolume { get; set; }

    /// <summary>正在把系统音量写入滑块（用于避免触发用户的音量事件）。</summary>
    public bool IsUpdatingFromSystem { get; private set; }

    public void ApplyDevice(DeviceDescriptor device)
    {
        _device = device;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IconGlyph));
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(IsVirtual));
        OnPropertyChanged(nameof(IsCommunicationsDefault));

        if (!IsAdjustingVolume)
        {
            IsUpdatingFromSystem = true;
            VolumePercent = device.Volume * 100;
            IsUpdatingFromSystem = false;
        }

        IsMuted = device.Muted;
    }

    public void SyncRoutes(IReadOnlyList<RouteEntry> entries, IReadOnlyDictionary<string, RouteStatus> statuses)
    {
        var desired = entries
            .Where(e => string.Equals(e.Target.DeviceId, _device.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.App.ProcessName, StringComparer.CurrentCulture)
            .ToList();

        var same = desired.Count == Routes.Count &&
                   desired.Select(e => e.App.Id).SequenceEqual(Routes.Select(r => r.App.Id)) &&
                   desired.Select(e => statuses.GetValueOrDefault(e.App.Id)?.StateText ?? "").SequenceEqual(Routes.Select(r => r.StateText));

        if (same) return;

        Routes.Clear();
        foreach (var entry in desired)
        {
            Routes.Add(new RouteItemViewModel(entry, statuses.GetValueOrDefault(entry.App.Id)));
        }

        OnPropertyChanged(nameof(HasRoutes));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly AudioHost _host;
    private readonly ConfigStore _store;
    private readonly IAuditLog _log;
    private readonly List<AppRuntimeView> _allApps = [];

    private bool _syncingProfile;
    private Profile? _selectedProfile;
    private string _statusText = "正在初始化…";
    private string _backendText = string.Empty;
    private string _lastError = string.Empty;
    private string _filterText = string.Empty;
    private string _emptyStateText = string.Empty;
    private string _lastSignature = string.Empty;
    private bool _isEmptyApps;
    private int _healthyCount;
    private int _pendingCount;
    private int _failedCount;

    public MainViewModel(AudioHost host, ConfigStore store, IAuditLog log)
    {
        _host = host;
        _store = store;
        _log = log;

        RefreshCommand = RelayCommand.Create(() => _ = RefreshAsync());

        _host.Routing.Changed += (_, _) => DispatchRefresh();
        _host.DevicesChanged += (_, _) => _ = RefreshAsync();
    }

    public AudioHost Host => _host;

    public ConfigStore Store => _store;

    public IAuditLog Log => _log;

    public ObservableCollection<AppItemViewModel> Apps { get; } = [];

    public ObservableCollection<DeviceLaneViewModel> Lanes { get; } = [];

    public ObservableCollection<RouteItemViewModel> RoleTargets { get; } = [];

    public ObservableCollection<Profile> Profiles { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value) || value is null || _syncingProfile) return;
            _ = SwitchProfileAsync(value);
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetProperty(ref _filterText, value)) return;
            ApplyFilter();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string BackendText
    {
        get => _backendText;
        private set => SetProperty(ref _backendText, value);
    }

    public string LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    public string EmptyStateText
    {
        get => _emptyStateText;
        private set => SetProperty(ref _emptyStateText, value);
    }

    public int HealthyCount
    {
        get => _healthyCount;
        private set => SetProperty(ref _healthyCount, value);
    }

    public int PendingCount
    {
        get => _pendingCount;
        private set => SetProperty(ref _pendingCount, value);
    }

    public int FailedCount
    {
        get => _failedCount;
        private set => SetProperty(ref _failedCount, value);
    }

    public bool HasRoleTargets => RoleTargets.Count > 0;

    public bool IsEmptyApps
    {
        get => _isEmptyApps;
        private set => SetProperty(ref _isEmptyApps, value);
    }

    public bool HasVirtualCable => _host.HasVirtualCable;

    public string VirtualCableHint => HasVirtualCable
        ? "检测到虚拟音频设备：可用于兼容模式"
        : "未检测到虚拟音频设备：独占模式应用无法搬移";

    public bool MinimizeOnClose => _store.Current.Settings.MinimizeToTray;

    public bool NotifyOnFailure => _store.Current.Settings.NotifyOnFailure;

    public string ConfigPath => _store.FilePath;

    public async Task InitializeAsync()
    {
        _log.Info("[ui] 初始化");
        await _host.Routing.ReconcileAsync();
        _host.Routing.Start();
        Refresh(force: true);
    }

    public async Task RefreshAsync()
    {
        await _host.Routing.ReconcileAsync();
        Refresh(force: true);
    }

    private void DispatchRefresh()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Refresh();
        else dispatcher.BeginInvoke(new Action(() => Refresh()));
    }

    public void Refresh(bool force = false)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => Refresh(force)));
            return;
        }

        var statuses = _host.Routing.GetStatuses().ToDictionary(s => s.App.Id, StringComparer.OrdinalIgnoreCase);
        var entries = _host.Routing.ActiveProfile.Entries;
        var devices = FilterDevices(_host.Routing.Devices, entries);
        var runtimeApps = _host.Routing.GetRunningApps();

        var signature = BuildSignature(devices, statuses, entries, runtimeApps);
        if (!force && signature == _lastSignature) return;
        _lastSignature = signature;

        // 设备通道：复用已有实例，避免拖动音量时被刷新打断
        var existing = Lanes.ToDictionary(l => l.Device.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<DeviceLaneViewModel>(devices.Count);
        foreach (var device in devices)
        {
            if (!existing.TryGetValue(device.Id, out var lane))
            {
                lane = new DeviceLaneViewModel(device);
            }

            lane.ApplyDevice(device);
            lane.SyncRoutes(entries, statuses);
            ordered.Add(lane);
        }

        if (!Lanes.SequenceEqual(ordered))
        {
            Lanes.Clear();
            foreach (var lane in ordered) Lanes.Add(lane);
        }

        // 按角色路由（没有指定具体设备的条目）
        var roleEntries = entries.Where(e => string.IsNullOrEmpty(e.Target.DeviceId)).ToList();
        if (RoleTargets.Count != roleEntries.Count ||
            !roleEntries.Select(e => e.App.Id).SequenceEqual(RoleTargets.Select(r => r.App.Id)))
        {
            RoleTargets.Clear();
            foreach (var entry in roleEntries) RoleTargets.Add(new RouteItemViewModel(entry, statuses.GetValueOrDefault(entry.App.Id)));
            OnPropertyChanged(nameof(HasRoleTargets));
        }

        _allApps.Clear();
        _allApps.AddRange(runtimeApps);
        ApplyFilter();

        Profiles.Clear();
        foreach (var profile in _store.Current.Profiles) Profiles.Add(profile);

        _syncingProfile = true;
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _store.Current.ActiveProfileId) ?? Profiles.FirstOrDefault();
        _syncingProfile = false;

        BackendText = _host.Routing.BackendStatus;
        LastError = _host.Routing.LastError ?? string.Empty;

        HealthyCount = statuses.Values.Count(s => s.State is RouteState.ActivePolicy or RouteState.ActiveStreaming);
        PendingCount = statuses.Values.Count(s => s.State is RouteState.Pending or RouteState.AppliedPendingSession);
        FailedCount = statuses.Values.Count(s => s.State is RouteState.Failed or RouteState.Degraded);
        StatusText = $"{entries.Count} 条路由";

        OnPropertyChanged(nameof(HasVirtualCable));
        OnPropertyChanged(nameof(VirtualCableHint));
    }

    /// <summary>
    /// 隐藏「没有路由的虚拟设备」。有路由的虚拟设备必须保留，否则用户会看不到/取消不了已配置的路由。
    /// </summary>
    private List<DeviceDescriptor> FilterDevices(IReadOnlyList<DeviceDescriptor> devices, IReadOnlyList<RouteEntry> entries)
    {
        if (!_store.Current.Settings.HideVirtualDevices) return devices.ToList();

        var routedIds = entries
            .Select(e => e.Target.DeviceId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return devices.Where(d => !d.IsVirtual || routedIds.Contains(d.Id)).ToList();
    }

    private void ApplyFilter()
    {
        var filter = _filterText.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allApps
            : _allApps.Where(a =>
                a.App.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                a.App.ProcessName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();

        Apps.Clear();
        foreach (var app in filtered) Apps.Add(new AppItemViewModel(app));

        EmptyStateText = _allApps.Count == 0
            ? "现在没有程序在播放声音\n播放音乐或启动游戏后会出现在这里"
            : "没有匹配的应用";
    }

    private static string BuildSignature(
        IReadOnlyList<DeviceDescriptor> devices,
        IReadOnlyDictionary<string, RouteStatus> statuses,
        IReadOnlyList<RouteEntry> entries,
        IReadOnlyList<AppRuntimeView> apps)
    {
        var parts = new List<string>(devices.Count + apps.Count + entries.Count + 4)
        {
            "p:" + entries.Count,
            "v:" + devices.Count,
        };

        foreach (var device in devices)
        {
            parts.Add($"d:{device.Id}:{(int)(device.Volume * 100)}:{device.Muted}:{device.IsDefaultConsole}:{device.Alias}");
        }

        foreach (var entry in entries)
        {
            parts.Add($"e:{entry.App.Id}:{entry.Target.DeviceId}:{entry.Target.Role}");
        }

        foreach (var app in apps)
        {
            parts.Add($"a:{app.ProcessId}:{app.CurrentDeviceId}:{app.Status?.State}:{app.Status?.Reason}");
        }

        return string.Join("|", parts);
    }

    public async Task RouteAppAsync(AppKey app, string deviceId, AudioRole role = AudioRole.Console)
    {
        _log.Info($"[ui] 路由 {app.ProcessName} → {deviceId}");
        await _host.Routing.RouteAsync(app, deviceId, role);
        Refresh(force: true);
    }

    public async Task UnrouteAsync(AppKey app)
    {
        await _host.Routing.UnrouteAsync(app);
        Refresh(force: true);
    }

    public async Task ClearAllAsync()
    {
        await _host.Routing.ClearAllRoutesAsync();
        Refresh(force: true);
    }

    public async Task SwitchProfileAsync(Profile profile)
    {
        await _host.Routing.SwitchProfileAsync(profile.Id);
        Refresh(force: true);
    }

    public async Task RenameDeviceAsync(DeviceDescriptor device, string? alias)
    {
        await _host.Routing.RenameDeviceAsync(device.Id, alias);
        Refresh(force: true);
    }

    /// <summary>用户调整设备音量（0..100）。</summary>
    public async Task SetDeviceVolumeAsync(string deviceId, double percent)
    {
        var lane = Lanes.FirstOrDefault(l => string.Equals(l.Device.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (lane is not null && lane.IsMuted)
        {
            lane.IsMuted = false;
            await _host.Routing.SetDeviceMuteAsync(deviceId, false);
        }

        await _host.Routing.SetDeviceVolumeAsync(deviceId, Math.Clamp(percent, 0, 100) / 100.0);
    }

    public async Task ToggleDeviceMuteAsync(DeviceLaneViewModel lane)
    {
        var target = !lane.IsMuted;
        lane.IsMuted = target;
        await _host.Routing.SetDeviceMuteAsync(lane.Device.Id, target);
        Refresh(force: true);
    }

    public void OpenConfigFolder()
    {
        var directory = Path.GetDirectoryName(_store.FilePath);
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{directory}\"")
        {
            UseShellExecute = true,
        });
    }
}
