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
    private readonly MainViewModel _viewModel = new();
    private readonly DispatcherTimer _logUiTimer;
    private bool _closeInProgress;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _logUiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _logUiTimer.Tick += (_, _) => FlushLogOutput();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
            _logUiTimer.Start();
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
            await _viewModel.StopLogAsync();
        }
        finally
        {
            _allowClose = true;
            _ = Dispatcher.BeginInvoke(Close);
        }
    }
    private async void RefreshDevice_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshDeviceAsync();
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
        await _viewModel.StopLogAsync(); await _viewModel.KillAdbAsync();
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
