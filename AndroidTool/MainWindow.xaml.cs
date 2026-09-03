using AndroidTool.Core;
using AndroidTool.ViewModels;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AndroidTool;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IDeviceRefreshController _deviceRefreshController;
    private readonly DispatcherTimer _logUiTimer;
    private readonly DispatcherTimer _deviceMonitorTimer;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Task _initializationTask = Task.CompletedTask;
    private Task _deviceCheckTask = Task.CompletedTask;
    private Task _manualDeviceRefreshTask = Task.CompletedTask;
    private bool _closeInProgress;
    private bool _allowClose;

    public MainWindow() : this(new MainViewModel())
    {
    }

    internal MainWindow(
        MainViewModel viewModel,
        IDeviceRefreshController? deviceRefreshController = null,
        TimeSpan? deviceMonitorInterval = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _deviceRefreshController = deviceRefreshController ?? viewModel;
        InitializeComponent();
        DataContext = _viewModel;
        _logUiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _logUiTimer.Tick += (_, _) => FlushLogOutput();
        _deviceMonitorTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = deviceMonitorInterval ?? TimeSpan.FromSeconds(2) };
        _deviceMonitorTimer.Tick += DeviceMonitorTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_lifetimeCancellation.IsCancellationRequested || !_initializationTask.IsCompleted) return;
        _initializationTask = InitializeWindowAsync(_lifetimeCancellation.Token);
    }

    private async Task InitializeWindowAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _deviceRefreshController.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _logUiTimer.Start();
            _deviceMonitorTimer.Start();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化失败：{ex.Message}", "AndroidTool", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closeInProgress) return;
        _closeInProgress = true;
        _logUiTimer.Stop();
        try
        {
            await StopDeviceMonitoringAsync();
            await _viewModel.StopLogAsync();
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            _allowClose = true;
            _ = Dispatcher.BeginInvoke(Close);
        }
    }

    internal async Task StopDeviceMonitoringAsync()
    {
        _deviceMonitorTimer.Stop();
        if (!_lifetimeCancellation.IsCancellationRequested) _lifetimeCancellation.Cancel();
        await Task.WhenAll(_initializationTask, _deviceCheckTask, _manualDeviceRefreshTask);
    }

    private void DeviceMonitorTimer_Tick(object? sender, EventArgs e)
    {
        if (_lifetimeCancellation.IsCancellationRequested || !_deviceCheckTask.IsCompleted) return;
        _deviceCheckTask = CheckForDeviceChangeAsync(_lifetimeCancellation.Token);
    }

    private async Task CheckForDeviceChangeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _deviceRefreshController.RefreshDeviceIfChangedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"自动检测设备失败：{ex}");
        }
    }

    private void RefreshDevice_Click(object sender, RoutedEventArgs e)
    {
        if (_lifetimeCancellation.IsCancellationRequested || !_manualDeviceRefreshTask.IsCompleted) return;
        _manualDeviceRefreshTask = RefreshDeviceSafelyAsync(_lifetimeCancellation.Token);
    }

    private async Task RefreshDeviceSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _deviceRefreshController.RefreshDeviceAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show($"刷新设备失败：{ex.Message}", "AndroidTool", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private async void AddApk_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "APK 文件|*.apk", Multiselect = true }; if (dialog.ShowDialog() == true) await _viewModel.AddApksAsync(dialog.FileNames); }
    private void InstallMode_Checked(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel) _viewModel.SetMode(OperationMode.Install); }
    private void UninstallMode_Checked(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel) _viewModel.SetMode(OperationMode.Uninstall); }
    private void LaunchMode_Checked(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel) _viewModel.SetMode(OperationMode.Launch); }
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => _viewModel.ClearCurrentSelection();
    private void ApkCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border card || card.Tag is not ApkItemViewModel item || e.ChangedButton != MouseButton.Left) return;
        if (!ApkCardInputPolicy.ShouldToggle(e.ClickCount, e.OriginalSource as DependencyObject, card)) return;
        card.Focus();
        _viewModel.ToggleCurrentSelection(item);
        e.Handled = true;
    }
    private void ApkCard_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Border card || card.Tag is not ApkItemViewModel item || e.Key is not (Key.Space or Key.Enter)) return;
        if (!ApkCardInputPolicy.ShouldToggle(1, e.OriginalSource as DependencyObject, card)) return;
        _viewModel.ToggleCurrentSelection(item);
        e.Handled = true;
    }
    private async void StartOperation_Click(object sender, RoutedEventArgs e) { try { await _viewModel.RunSelectedAsync(); } catch (Exception ex) { MessageBox.Show(ex.Message); } }
    private void RemoveApk_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is ApkItemViewModel item) _viewModel.Remove(item); }
    private void SelectObb_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is not ApkItemViewModel item || item.IsBusy) return; var dialog = new OpenFileDialog { Filter = "OBB 文件|*.obb", Multiselect = true }; if (dialog.ShowDialog() != true) return; try { item.SetObbFiles(dialog.FileNames); } catch (ArgumentException ex) { MessageBox.Show(ex.Message, "OBB 选择", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    private void Copy_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is string value) Clipboard.SetText(value); }
    private async void AndroidLog_Click(object sender, RoutedEventArgs e) => await StartLogSafelyAsync(false);
    private async void UnityLog_Click(object sender, RoutedEventArgs e) => await StartLogSafelyAsync(true);
    private async void StopLog_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.StopLogAsync(); }
        catch (Exception ex) { MessageBox.Show($"停止日志失败：{ex.Message}", "实时日志", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void ClearLog_Click(object sender, RoutedEventArgs e) { _viewModel.ClearLog(); LogOutputBox.Clear(); }
    private async void ExportLog_Click(object sender, RoutedEventArgs e) { FlushLogOutput(int.MaxValue); var dialog = new SaveFileDialog { Filter = "日志文件|*.log|文本文件|*.txt", FileName = $"AndroidLog_{DateTime.Now:yyyyMMdd_HHmmss}.log" }; if (dialog.ShowDialog() == true) await _viewModel.ExportLogAsync(dialog.FileName); }
    private void OpenCast_Click(object sender, RoutedEventArgs e) { var address = _viewModel.CastAddress; if (address is null) { MessageBox.Show("当前没有可用的设备 IP 地址。"); return; } Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
    private void OpenRuntime_Click(object sender, RoutedEventArgs e) => _viewModel.OpenRuntimeDirectory();
    private void ClearOldRuntime_Click(object sender, RoutedEventArgs e) { _viewModel.ClearOldRuntimeVersions(); MessageBox.Show("旧版本运行文件已清理。"); }
    private async void ClearAllRuntime_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("将停止 ADB 服务、删除全部运行文件并退出。是否继续？", "清理运行文件", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await StopDeviceMonitoringAsync(); await _viewModel.StopLogAsync(); await _viewModel.KillAdbAsync();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidTool", "Runtime");
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception ex) { MessageBox.Show($"清理失败：{ex.Message}"); return; }
        Application.Current.Shutdown();
    }

    private async Task StartLogSafelyAsync(bool unityOnly)
    {
        try
        {
            await _viewModel.StartLogAsync(unityOnly);
            LogOutputBox.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动日志失败：{ex.Message}", "实时日志", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FlushLogOutput(int maxLines = 1_000)
    {
        var batch = _viewModel.DrainLogUiBatch(maxLines);
        if (batch.ReplacementText is not null)
        {
            LogOutputBox.Text = batch.ReplacementText.Length == 0
                ? string.Empty
                : batch.ReplacementText + Environment.NewLine;
        }
        else if (batch.Lines.Count > 0)
        {
            LogOutputBox.AppendText(string.Join(Environment.NewLine, batch.Lines) + Environment.NewLine);
        }

        if (batch.Lines.Count > 0 && AutoScrollCheck.IsChecked == true) LogOutputBox.ScrollToEnd();
    }
}
