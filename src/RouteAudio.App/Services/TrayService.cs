using System.Drawing;
using System.Windows.Forms;
using RouteAudio.App.ViewModels;
using RouteAudio.App.Views;
using RouteAudio.Audio;
using RouteAudio.Core.Abstractions;
using RouteAudio.Core.Config;
using RouteAudio.Core.Models;

namespace RouteAudio.App.Services;

/// <summary>托盘图标与菜单（WinForms NotifyIcon，避免额外依赖）。</summary>
public sealed class TrayService : IDisposable
{
    private readonly MainWindow _window;
    private readonly MainViewModel _viewModel;
    private readonly AudioHost _host;
    private readonly IAuditLog _log;
    private readonly Dictionary<string, RouteState> _lastStates = new(StringComparer.OrdinalIgnoreCase);
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _menu;

    public TrayService(MainWindow window, MainViewModel viewModel, AudioHost host, IAuditLog log)
    {
        _window = window;
        _viewModel = viewModel;
        _host = host;
        _log = log;
        _host.Routing.Changed += OnRoutingChanged;
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application,
            Text = "RouteAudio · 声路由",
            Visible = true,
        };

        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowWindow();
        };

        BuildMenu();
    }

    private void BuildMenu()
    {
        if (_notifyIcon is null) return;

        _menu = new ContextMenuStrip();
        _menu.Items.Add("显示 RouteAudio", null, (_, _) => ShowWindow());
        _menu.Items.Add(new ToolStripSeparator());

        var profilesMenu = new ToolStripMenuItem("配置档");
        foreach (var profile in _viewModel.Profiles.ToList())
        {
            var captured = profile;
            var item = new ToolStripMenuItem(captured.Name)
            {
                Checked = _viewModel.SelectedProfile?.Id == captured.Id,
            };
            item.Click += async (_, _) => await _viewModel.SwitchProfileAsync(captured);
            profilesMenu.DropDownItems.Add(item);
        }

        _menu.Items.Add(profilesMenu);
        _menu.Items.Add("打开配置文件夹", null, (_, _) => _viewModel.OpenConfigFolder());
        _menu.Items.Add("刷新", null, (_, _) => _viewModel.Refresh());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _menu.Opening += (_, _) => BuildMenu();
        _notifyIcon.ContextMenuStrip = _menu;
    }

    private void OnRoutingChanged(object? sender, EventArgs e)
    {
        if (!_viewModel.NotifyOnFailure) return;

        foreach (var status in _host.Routing.GetStatuses())
        {
            _lastStates.TryGetValue(status.App.Id, out var previous);
            var isProblem = status.State is RouteState.Failed or RouteState.Degraded;

            if (isProblem && previous != status.State)
            {
                var text = $"{status.App.DisplayName}：{status.StateText}" +
                           (status.Reason == RouteReason.None ? "" : $"（{status.Reason.ToText()}）");
                ShowToast("RouteAudio 路由提醒", text, ToolTipIcon.Warning);
            }

            _lastStates[status.App.Id] = status.State;
        }
    }

    private void ShowToast(string title, string text, ToolTipIcon icon)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null) return;
        app.Dispatcher.BeginInvoke(() => _notifyIcon?.ShowBalloonTip(4000, title, text, icon));
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == System.Windows.WindowState.Minimized)
        {
            _window.WindowState = System.Windows.WindowState.Normal;
        }

        _window.Activate();
    }

    private void ExitApplication()
    {
        _log.Info("[tray] 用户退出");
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        _window.CloseForReal();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _menu?.Dispose();
    }
}
