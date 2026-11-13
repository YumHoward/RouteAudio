using System.Threading;
using System.Windows;
using RouteAudio.App.Services;
using RouteAudio.App.ViewModels;
using RouteAudio.App.Views;
using RouteAudio.Audio;
using RouteAudio.Core.Config;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RouteAudio.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private AudioHost? _host;
    private TrayService? _tray;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\RouteAudio.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("RouteAudio 已经在运行。", "RouteAudio", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var log = new FileAuditLog();
        log.Info($"==== RouteAudio 启动 pid={Environment.ProcessId} ====");

        var store = new ConfigStore();
        _host = new AudioHost(store, log);

        _viewModel = new MainViewModel(_host, store, log);

        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();

        _tray = new TrayService(window, _viewModel, log);
        _tray.Initialize();

        _ = _viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _host?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
