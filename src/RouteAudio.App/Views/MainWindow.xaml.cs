using System.Windows;
using System.Windows.Input;
using RouteAudio.App.ViewModels;
using RouteAudio.Core.Models;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace RouteAudio.App.Views;

public partial class MainWindow : Window
{
    private Point _dragStart;
    private AppItemViewModel? _dragCandidate;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel { MinimizeOnClose: true } && !_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private bool _allowClose;

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

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

    private void Lane_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(AppKey)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Lane_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DeviceLaneViewModel lane }) return;
        if (e.Data.GetData(typeof(AppKey)) is not AppKey app) return;
        if (DataContext is not MainViewModel viewModel) return;

        await viewModel.RouteAppAsync(app, lane.Device.Id);
    }

    private async void Unroute_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RouteItemViewModel item) return;
        if (DataContext is not MainViewModel viewModel) return;

        await viewModel.UnrouteAsync(item.App);
    }
}
