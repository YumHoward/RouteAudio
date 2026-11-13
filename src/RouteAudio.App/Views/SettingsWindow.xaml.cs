using System.IO;
using System.Windows;
using RouteAudio.App.Services;
using RouteAudio.Audio;
using RouteAudio.Core.Abstractions;
using RouteAudio.Core.Config;

namespace RouteAudio.App.Views;

public partial class SettingsWindow : Window
{
    private readonly ConfigStore _store;
    private readonly AudioHost _host;
    private readonly IAuditLog _log;

    public SettingsWindow(ConfigStore store, AudioHost host, IAuditLog log)
    {
        InitializeComponent();
        _store = store;
        _host = host;
        _log = log;

        var settings = store.Current.Settings;
        StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
        MinimizeToTrayCheck.IsChecked = settings.MinimizeToTray;
        NotifyCheck.IsChecked = settings.NotifyOnFailure;
        ReconcileBox.Text = settings.ReconcileSeconds.ToString();
        ConfigPathText.Text = store.FilePath;
        BackendText.Text = host.Routing.BackendStatus;
        VirtualCableText.Text = host.HasVirtualCable
            ? "检测到虚拟音频设备（可用于兼容模式搬移）"
            : "未检测到虚拟音频设备（独占模式应用无法搬移）";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = _store.Current.Settings;
        settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        settings.NotifyOnFailure = NotifyCheck.IsChecked == true;
        settings.ReconcileSeconds = int.TryParse(ReconcileBox.Text.Trim(), out var seconds)
            ? Math.Clamp(seconds, 1, 60)
            : 3;

        _store.Save();

        try
        {
            AutostartService.SetEnabled(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            _log.Warn($"[settings] 设置开机启动失败: {ex.Message}");
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
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
