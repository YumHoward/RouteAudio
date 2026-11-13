using System.Collections.ObjectModel;
using System.IO;
using RouteAudio.Audio;
using RouteAudio.Core.Abstractions;
using RouteAudio.Core.Config;
using RouteAudio.Core.Models;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;

namespace RouteAudio.App.ViewModels;

public sealed class AppItemViewModel : ObservableObject
{
    public AppItemViewModel(AppRuntimeView view)
    {
        View = view;
        Name = string.IsNullOrWhiteSpace(view.App.ProductName) ? view.App.ProcessName : view.App.ProductName!;
        Initial = Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
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

    public bool IsError => View.Status?.State is RouteState.Failed or RouteState.Degraded;

    public bool IsPending => View.Status?.State is RouteState.Pending or RouteState.AppliedPendingSession;

    public Brush StateBrush => IsError
        ? System.Windows.Media.Brushes.OrangeRed
        : IsPending
            ? System.Windows.Media.Brushes.Goldenrod
            : HasStatus
                ? System.Windows.Media.Brushes.SeaGreen
                : System.Windows.Media.Brushes.Gray;
}

public sealed class RouteItemViewModel
{
    public RouteItemViewModel(RouteEntry entry, RouteStatus? status)
    {
        Entry = entry;
        Status = status;
        AppName = string.IsNullOrWhiteSpace(entry.App.ProductName) ? entry.App.ProcessName : entry.App.ProductName!;
    }

    public RouteEntry Entry { get; }

    public RouteStatus? Status { get; }

    public AppKey App => Entry.App;

    public string AppName { get; }

    public string StateText => Status?.StateText ?? "等待应用出声";

    public string ReasonText => Status is { Reason: not RouteReason.None } s ? s.Reason.ToText() : string.Empty;

    public bool IsError => Status?.State is RouteState.Failed or RouteState.Degraded;

    public bool IsPending => Status?.State is RouteState.Pending or RouteState.AppliedPendingSession;

    public Brush StateBrush => IsError
        ? System.Windows.Media.Brushes.OrangeRed
        : IsPending
            ? System.Windows.Media.Brushes.Goldenrod
            : System.Windows.Media.Brushes.SeaGreen;
}

public sealed class DeviceLaneViewModel
{
    public DeviceLaneViewModel(DeviceDescriptor device)
    {
        Device = device;
        Routes = [];
    }

    public DeviceDescriptor Device { get; }

    public string Title => Device.DisplayName;

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (Device.IsDefaultConsole) parts.Add("默认");
            if (Device.IsDefaultCommunications) parts.Add("通信默认");
            if (Device.IsVirtual) parts.Add("虚拟设备");
            if (!string.IsNullOrWhiteSpace(Device.EnumeratorName)) parts.Add(Device.EnumeratorName!);
            return string.Join(" · ", parts);
        }
    }

    public bool IsDefault => Device.IsDefaultConsole;

    public bool IsVirtual => Device.IsVirtual;

    public ObservableCollection<RouteItemViewModel> Routes { get; }

    public bool HasRoutes => Routes.Count > 0;
}

public sealed class MainViewModel : ObservableObject
{
    private readonly AudioHost _host;
    private readonly ConfigStore _store;
    private readonly IAuditLog _log;
    private bool _syncingProfile;
    private Profile? _selectedProfile;
    private string _statusText = "正在初始化…";
    private string _backendText = "";
    private string _lastError = "";
    private bool _isBusy;

    public MainViewModel(AudioHost host, ConfigStore store, IAuditLog log)
    {
        _host = host;
        _store = store;
        _log = log;

        RefreshCommand = RelayCommand.Create(() => _ = RefreshAsync());
        ClearAllCommand = RelayCommand.Create(() => _ = ClearAllAsync());
        SettingsCommand = RelayCommand.Create(() => OpenSettings());
        OpenConfigFolderCommand = RelayCommand.Create(OpenConfigFolder);

        _host.Routing.Changed += (_, _) => DispatchRefresh();
    }

    public ObservableCollection<AppItemViewModel> RunningApps { get; } = [];

    public ObservableCollection<DeviceLaneViewModel> Lanes { get; } = [];

    public ObservableCollection<RouteItemViewModel> RoleTargets { get; } = [];

    public ObservableCollection<Profile> Profiles { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ClearAllCommand { get; }

    public RelayCommand SettingsCommand { get; }

    public RelayCommand OpenConfigFolderCommand { get; }

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value) || value is null || _syncingProfile) return;
            _ = SwitchProfileAsync(value);
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

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string ConfigPath => _store.FilePath;

    public bool MinimizeOnClose => _store.Current.Settings.MinimizeToTray;

    public bool HasRoleTargets => RoleTargets.Count > 0;

    public bool HasVirtualCable => _host.HasVirtualCable;

    public string VirtualCableHint => HasVirtualCable
        ? "检测到虚拟音频设备：可用「兼容模式」搬移独占类应用"
        : "未检测到虚拟音频设备：部分应用（独占模式）无法搬移";

    public async Task InitializeAsync()
    {
        _log.Info("[ui] 初始化");
        await _host.Routing.ReconcileAsync();
        _host.Routing.Start();
        Refresh();
    }

    public async Task RefreshAsync()
    {
        await _host.Routing.ReconcileAsync();
        Refresh();
    }

    private void DispatchRefresh()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Refresh();
        else dispatcher.BeginInvoke(Refresh);
    }

    public void Refresh()
    {
        var devices = _host.Routing.Devices;
        var statuses = _host.Routing.GetStatuses().ToDictionary(s => s.App.Id, StringComparer.OrdinalIgnoreCase);
        var entries = _host.Routing.ActiveProfile.Entries;

        Lanes.Clear();
        foreach (var device in devices)
        {
            var lane = new DeviceLaneViewModel(device);
            foreach (var entry in entries.Where(e => string.Equals(e.Target.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase)))
            {
                lane.Routes.Add(new RouteItemViewModel(entry, statuses.GetValueOrDefault(entry.App.Id)));
            }

            Lanes.Add(lane);
        }

        RoleTargets.Clear();
        foreach (var entry in entries.Where(e => string.IsNullOrEmpty(e.Target.DeviceId)))
        {
            RoleTargets.Add(new RouteItemViewModel(entry, statuses.GetValueOrDefault(entry.App.Id)));
        }

        RunningApps.Clear();
        foreach (var app in _host.Routing.GetRunningApps())
        {
            RunningApps.Add(new AppItemViewModel(app));
        }

        Profiles.Clear();
        foreach (var profile in _store.Current.Profiles) Profiles.Add(profile);

        _syncingProfile = true;
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _store.Current.ActiveProfileId) ?? Profiles.FirstOrDefault();
        _syncingProfile = false;

        BackendText = _host.Routing.BackendStatus;
        LastError = _host.Routing.LastError ?? string.Empty;

        var healthy = statuses.Values.Count(s => s.State is RouteState.ActivePolicy or RouteState.ActiveStreaming);
        var pending = statuses.Values.Count(s => s.State is RouteState.Pending or RouteState.AppliedPendingSession);
        var failed = statuses.Values.Count(s => s.State is RouteState.Failed or RouteState.Degraded);
        StatusText = $"{entries.Count} 条路由 · 已生效 {healthy} · 等待 {pending} · 异常 {failed}";

        OnPropertyChanged(nameof(HasVirtualCable));
        OnPropertyChanged(nameof(VirtualCableHint));
        OnPropertyChanged(nameof(HasRoleTargets));
    }

    public async Task RouteAppAsync(AppKey app, string deviceId, AudioRole role = AudioRole.Console)
    {
        IsBusy = true;
        try
        {
            _log.Info($"[ui] 路由 {app.ProcessName} → {deviceId}");
            await _host.Routing.RouteAsync(app, deviceId, role);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    public async Task UnrouteAsync(AppKey app)
    {
        IsBusy = true;
        try
        {
            await _host.Routing.UnrouteAsync(app);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    public async Task ClearAllAsync()
    {
        IsBusy = true;
        try
        {
            await _host.Routing.ClearAllRoutesAsync();
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    public async Task SwitchProfileAsync(Profile profile)
    {
        await _host.Routing.SwitchProfileAsync(profile.Id);
        Refresh();
    }

    public async Task RenameDeviceAsync(DeviceDescriptor device, string? alias)
    {
        await _host.Routing.RenameDeviceAsync(device.Id, alias);
        Refresh();
    }

    public void OpenConfigFolder()
    {
        var directory = Path.GetDirectoryName(_store.FilePath);
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
    }

    private void OpenSettings()
    {
        var window = new Views.SettingsWindow(_store, _host, _log) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        Refresh();
    }
}
