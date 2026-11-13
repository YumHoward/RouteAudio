using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using RouteAudio.App.ViewModels;
using RouteAudio.Core.Models;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using MessageBox = System.Windows.MessageBox;

namespace RouteAudio.App.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _volumeTimer;
    private DeviceLaneViewModel? _pendingVolumeLane;
    private Point _dragStart;
    private AppItemViewModel? _dragCandidate;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;

        _volumeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _volumeTimer.Tick += OnVolumeTimerTick;

        FitToWorkArea();
    }

    /// <summary>按当前工作区调整初始尺寸并居中，避免小屏/高缩放时超出屏幕。</summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(1160, Math.Max(MinWidth, area.Width - 48));
        Height = Math.Min(748, Math.Max(MinHeight, area.Height - 48));
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    // ---------------- 窗口生命周期 ----------------

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (ViewModel is { MinimizeOnClose: true } && !_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    // ---------------- 顶部按钮 ----------------

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.RefreshAsync();
    }

    private void Rules_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var window = new RulesWindow(vm.Host, vm.Log) { Owner = this };
        window.ShowDialog();
        vm.Refresh(force: true);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var window = new SettingsWindow(vm.Store, vm.Host, vm.Log) { Owner = this };
        window.ShowDialog();
        vm.Refresh(force: true);
    }

    private async void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var count = vm.Lanes.Sum(l => l.Routes.Count) + vm.RoleTargets.Count;
        if (count == 0) return;

        var result = MessageBox.Show(this,
            $"确定清除当前配置档的全部 {count} 条路由吗？\n这不会改变系统默认设备。",
            "清除全部路由", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        await vm.ClearAllAsync();
    }

    private void Search_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (ViewModel is { } vm) vm.FilterText = SearchBox.Text;
    }

    // ---------------- 应用列表拖拽 ----------------

    private void AppsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragCandidate = (e.OriginalSource as FrameworkElement)?.DataContext as AppItemViewModel;
    }

    private void AppsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null) return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var app = _dragCandidate.App;
        _dragCandidate = null;
        DragDrop.DoDragDrop(AppsList, new DataObject(typeof(AppKey), app), DragDropEffects.Copy);
    }

    // ---------------- 设备通道拖放 ----------------

    private void Lane_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceLaneViewModel lane }) return;

        var accepted = e.Data.GetDataPresent(typeof(AppKey));
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        lane.IsDropTarget = accepted;
        e.Handled = true;
    }

    private void Lane_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DeviceLaneViewModel lane }) lane.IsDropTarget = false;
    }

    private async void Lane_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceLaneViewModel lane }) return;
        lane.IsDropTarget = false;

        if (e.Data.GetData(typeof(AppKey)) is not AppKey app) return;
        if (ViewModel is not { } vm) return;

        await vm.RouteAppAsync(app, lane.Device.Id);
    }

    // ---------------- 设备音量 / 静音 ----------------

    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceLaneViewModel lane }) return;
        if (lane.IsUpdatingFromSystem) return;

        // 启动/刷新时绑定会先触发一次 ValueChanged：值与设备当前音量相同则跳过，
        // 避免把用户静音的设备意外解除静音；UI Automation 改值仍会正常生效。
        if (Math.Abs(lane.VolumePercent - lane.Device.Volume * 100) < 0.5) return;

        lane.IsAdjustingVolume = true;
        _pendingVolumeLane = lane;
        _volumeTimer.Stop();
        _volumeTimer.Start();
    }

    private async void OnVolumeTimerTick(object? sender, EventArgs e)
    {
        _volumeTimer.Stop();
        // 稍微延后一点，让连续拖动合并成一次系统调用
        await Task.Delay(80);

        var lane = _pendingVolumeLane;
        if (lane is null || ViewModel is not { } vm) return;

        lane.IsAdjustingVolume = false;
        await vm.SetDeviceVolumeAsync(lane.Device.Id, lane.VolumePercent);
    }

    private async void Mute_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceLaneViewModel lane) return;
        if (ViewModel is not { } vm) return;

        await vm.ToggleDeviceMuteAsync(lane);
    }

    // ---------------- 行内操作 ----------------

    private async void Unroute_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RouteItemViewModel item) return;
        if (ViewModel is not { } vm) return;

        await vm.UnrouteAsync(item.App);
    }

    private async void RenameDevice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceLaneViewModel lane) return;
        if (ViewModel is not { } vm) return;

        var dialog = new InputDialog("重命名设备", "设备别名（留空恢复原名）",
            lane.Device.Alias ?? lane.Device.Name, allowEmpty: true)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true) return;
        await vm.RenameDeviceAsync(lane.Device, dialog.Value);
    }

    private void RefreshLane_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.Refresh(force: true);
    }
}
