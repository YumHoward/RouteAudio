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

        if (e.Args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            RunSmokeTest(log, store);
            return;
        }

        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();

        if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            window.Activate();
        }
        else
        {
            window.Hide();
        }

        _tray = new TrayService(window, _viewModel, _host, log);
        _tray.Initialize();

        _ = _viewModel.InitializeAsync();
    }

    /// <summary>无人值守冒烟测试：构造全部窗口，验证 XAML 与绑定可加载。</summary>
    private void RunSmokeTest(FileAuditLog log, ConfigStore store)
    {
        try
        {
            var config = new InputDialog("冒烟", "输入", "值");
            var main = new MainWindow { DataContext = _viewModel };
            var rules = new RulesWindow(_host!, log);
            var editor = new RuleEditorWindow(_host!.Routing.Devices);

            config.Close();
            main.CloseForReal();
            rules.Close();
            editor.Close();

            log.Info("SMOKE OK: InputDialog / MainWindow / RulesWindow / RuleEditorWindow 构造成功");
            Console.WriteLine("SMOKE OK");
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            log.Error("SMOKE FAIL", ex);
            Console.WriteLine($"SMOKE FAIL: {ex}");
            Environment.ExitCode = 1;
        }
        finally
        {
            Shutdown(Environment.ExitCode);
        }
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
