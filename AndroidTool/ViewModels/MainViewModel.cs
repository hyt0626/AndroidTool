using AndroidTool.Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AndroidTool.ViewModels;

public sealed class MainViewModel : ObservableObject, IDeviceRefreshController
{
    private readonly EmbeddedToolManager _toolManager = new(new ManifestToolSource());
    private readonly ConcurrentTaskRunner _taskRunner = new(3);
    private readonly LogPipeline _logPipeline = new(pendingLines: 10_000, maxLineLength: 16_384, displayLines: 50_000, displayCharacters: 8 * 1024 * 1024);
    private readonly LogSessionGate _logSessionGate = new();
    private readonly SemaphoreSlim _logSessionLock = new(1, 1);
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private ToolPaths? _paths;
    private AdbClient? _adb;
    private IDeviceInfoSource? _deviceInfoSource;
    private bool _hasDeviceSnapshot;
    private string? _deviceSnapshotSerial;
    private long _deviceRefreshGeneration;
    private CancellationTokenSource? _logCancellation;
    private Task<ProcessResult>? _logTask;
    private bool _operationRunning;
    private OperationMode _currentMode;
    private bool _launchSingleSelect;
    private string _statusText = "正在准备内置工具…";
    private string _serial = "—", _brand = "—", _model = "—", _androidVersion = "—", _battery = "—", _ipAddress = "—", _storage = "—";
    private string _taskOutput = "", _logStatus = "未启动", _logBaseStatus = "未启动";

    public MainViewModel()
    {
    }

    public MainViewModel(IDeviceInfoSource deviceInfoSource)
    {
        _deviceInfoSource = deviceInfoSource ?? throw new ArgumentNullException(nameof(deviceInfoSource));
    }

    public ObservableCollection<ApkItemViewModel> Items { get; } = [];
    public OperationMode CurrentMode
    {
        get => _currentMode;
        private set
        {
            if (!SetProperty(ref _currentMode, value)) return;
            foreach (var item in Items) item.CurrentMode = value;
            RaisePropertyChanged(nameof(ModeTitle));
            RaiseSelectionSummary();
        }
    }
    public string ModeTitle => CurrentMode switch { OperationMode.Install => "安装", OperationMode.Uninstall => "卸载", _ => "启动" };
    public bool LaunchSingleSelect
    {
        get => _launchSingleSelect;
        set
        {
            if (!SetProperty(ref _launchSingleSelect, value)) return;
            if (value) EnforceSingleLaunchSelection();
            RaiseSelectionSummary();
        }
    }
    public int SelectedCount => Items.Count(item => item.IsSelected(CurrentMode));
    public string SelectedCountText => $"当前已选 {SelectedCount} 个";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string Serial { get => _serial; set => SetProperty(ref _serial, value); }
    public string Brand { get => _brand; set => SetProperty(ref _brand, value); }
    public string Model { get => _model; set => SetProperty(ref _model, value); }
    public string AndroidVersion { get => _androidVersion; set => SetProperty(ref _androidVersion, value); }
    public string Battery { get => _battery; set => SetProperty(ref _battery, value); }
    public string IpAddress { get => _ipAddress; set => SetProperty(ref _ipAddress, value); }
    public string Storage { get => _storage; set => SetProperty(ref _storage, value); }
    public string TaskOutput { get => _taskOutput; set => SetProperty(ref _taskOutput, value); }
    public string LogStatus { get => _logStatus; set => SetProperty(ref _logStatus, value); }
    public string RuntimeDirectory => _paths?.Root ?? _toolManager.CacheRoot;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _paths = await _toolManager.EnsureExtractedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _adb = new AdbClient(_paths);
        _deviceInfoSource ??= _adb;
        RaisePropertyChanged(nameof(RuntimeDirectory));
        await RefreshDeviceAsync(cancellationToken);
    }

    public async Task RefreshDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (_deviceInfoSource is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        var serial = await _deviceInfoSource.GetConnectedSerialAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var generation = Interlocked.Increment(ref _deviceRefreshGeneration);
        await RefreshDeviceCoreAsync(serial, generation, cancellationToken);
    }

    public async Task RefreshDeviceIfChangedAsync(CancellationToken cancellationToken = default)
    {
        if (_deviceInfoSource is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        var serial = await _deviceInfoSource.GetConnectedSerialAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_hasDeviceSnapshot && string.Equals(serial, _deviceSnapshotSerial, StringComparison.Ordinal)) return;
        var generation = Interlocked.Increment(ref _deviceRefreshGeneration);
        await RefreshDeviceCoreAsync(serial, generation, cancellationToken);
    }

    private async Task RefreshDeviceCoreAsync(string? serial, long generation, CancellationToken cancellationToken)
    {
        var info = serial is null ? null : await _deviceInfoSource!.ReadDeviceInfoAsync(serial, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != Volatile.Read(ref _deviceRefreshGeneration)) return;

        var connectedSerial = await _deviceInfoSource!.GetConnectedSerialAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != Volatile.Read(ref _deviceRefreshGeneration) ||
            !string.Equals(serial, connectedSerial, StringComparison.Ordinal)) return;

        _hasDeviceSnapshot = true;
        _deviceSnapshotSerial = info?.Serial;
        if (info is null) { StatusText = "未检测到设备"; Serial = Brand = Model = AndroidVersion = Battery = IpAddress = Storage = "—"; return; }
        StatusText = "设备已连接"; Serial = info.Serial; Brand = info.Brand; Model = info.Model; AndroidVersion = info.AndroidVersion;
        Battery = info.BatteryPercent is null ? "—" : $"{info.BatteryPercent}%"; IpAddress = info.IpAddress ?? "—"; Storage = info.StorageDisplay;
    }

    public async Task AddApksAsync(IEnumerable<string> paths)
    {
        EnsureReady();
        foreach (var file in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Items.Any(item => item.FullPath.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;
            var result = await new ProcessRunner().RunAsync(_paths!.AaptPath, ["dump", "badging", file]);
            if (!result.Succeeded) { AppendTask($"读取 {Path.GetFileName(file)} 失败：{AdbErrorTranslator.ToUserMessage(result.StandardError)}"); continue; }
            var item = new ApkItemViewModel(new ApkInfoParser().Parse(file, result.StandardOutput), file) { CurrentMode = CurrentMode };
            Items.Add(item);
        }
    }

    public void SetMode(OperationMode mode) => CurrentMode = mode;
    public bool ToggleCurrentSelection(ApkItemViewModel item)
    {
        if (!Items.Contains(item) || item.IsBusy) return false;

        var select = !item.IsSelected(CurrentMode);
        if (CurrentMode == OperationMode.Launch && LaunchSingleSelect && select)
        {
            foreach (var other in Items)
                if (!ReferenceEquals(other, item)) other.SetSelected(OperationMode.Launch, false);
        }

        item.SetSelected(CurrentMode, select);
        RaiseSelectionSummary();
        return true;
    }
    public void ClearCurrentSelection()
    {
        foreach (var item in Items) item.SetSelected(CurrentMode, false);
        RaiseSelectionSummary();
    }
    public void Remove(ApkItemViewModel item)
    {
        if (!item.IsBusy && Items.Remove(item)) RaiseSelectionSummary();
    }

    public async Task RunSelectedAsync()
    {
        EnsureReady();
        if (_operationRunning) { AppendTask("已有任务正在执行，请等待完成。"); return; }
        var mode = CurrentMode;
        var selected = Items.Where(item => item.IsSelected(mode) && !item.IsBusy).ToArray();
        if (selected.Length == 0) { AppendTask($"没有勾选要{ModeTitle}的 APK。"); return; }
        _operationRunning = true;
        try
        {
            if (mode == OperationMode.Launch)
            {
                foreach (var item in selected) { await ExecuteLaunchAsync(item); await Task.Delay(1000); }
                return;
            }
            foreach (var item in selected) { item.State = TaskState.Waiting; item.Status = "等待"; item.Error = null; }
            await _taskRunner.RunAsync(selected.Select(item => (Func<Task>)(() => mode == OperationMode.Install ? ExecuteInstallAsync(item) : ExecuteUninstallAsync(item))));
        }
        finally { _operationRunning = false; }
    }

    private async Task ExecuteInstallAsync(ApkItemViewModel item)
    {
        Begin(item, "安装");
        var result = await _adb!.InstallAsync(item.FullPath);
        if (!result.Succeeded) { Fail(item, "安装", result); return; }
        foreach (var obb in item.ObbFiles)
        {
            item.State = TaskState.CopyingObb; item.Status = $"复制 {Path.GetFileName(obb)}"; item.IsIndeterminate = false; item.Progress = 0;
            await _adb.ShellAsync("mkdir", "-p", $"/sdcard/Android/obb/{item.Info.PackageName}");
            result = await _adb.PushWithProgressAsync(obb, $"/sdcard/Android/obb/{item.Info.PackageName}/", value => Post(() => item.Progress = value));
            if (!result.Succeeded) { Fail(item, "复制 OBB", result); return; }
        }
        Succeed(item, "安装");
    }

    private async Task ExecuteUninstallAsync(ApkItemViewModel item)
    {
        Begin(item, "卸载");
        if (string.IsNullOrWhiteSpace(item.Info.PackageName)) { Fail(item, "卸载", "无法读取包名"); return; }
        var result = await _adb!.UninstallAsync(item.Info.PackageName);
        if (!result.Succeeded) { Fail(item, "卸载", result); return; }
        Succeed(item, "卸载");
    }

    private async Task ExecuteLaunchAsync(ApkItemViewModel item)
    {
        Begin(item, "启动");
        if (string.IsNullOrWhiteSpace(item.Info.PackageName) || string.IsNullOrWhiteSpace(item.Info.LaunchableActivity)) { Fail(item, "启动", "APK 没有可识别的启动 Activity"); return; }
        var result = await _adb!.ShellAsync("am", "start", "-n", $"{item.Info.PackageName}/{item.Info.LaunchableActivity}");
        if (!result.Succeeded) { Fail(item, "启动", result); return; }
        Succeed(item, "启动");
    }

    public async Task StartLogAsync(bool unityOnly)
    {
        EnsureReady();
        await _logSessionLock.WaitAsync();
        try
        {
            await StopLogCoreAsync(updateStatus: false);
            _logPipeline.Clear();

            var session = _logSessionGate.Begin();
            _logBaseStatus = unityOnly ? "Unity 实时日志" : "Android 实时日志";
            LogStatus = _logBaseStatus;
            _logCancellation = new CancellationTokenSource();
            _logTask = _adb!.StreamLogAsync(
                unityOnly,
                line =>
                {
                    if (_logSessionGate.IsCurrent(session)) _logPipeline.Enqueue(line);
                },
                _logCancellation.Token);
            _ = ObserveLogCompletionAsync(session, _logTask);
        }
        finally
        {
            _logSessionLock.Release();
        }
    }

    public async Task StopLogAsync()
    {
        await _logSessionLock.WaitAsync();
        try
        {
            await StopLogCoreAsync(updateStatus: true);
        }
        finally
        {
            _logSessionLock.Release();
        }
    }

    public LogUiBatch DrainLogUiBatch(int maxLines = 1_000)
    {
        var batch = _logPipeline.DrainUiBatch(maxLines);
        if (batch.TotalDroppedLines > 0)
            LogStatus = $"{_logBaseStatus}（界面繁忙，已丢弃 {batch.TotalDroppedLines:N0} 行）";
        return batch;
    }

    public void ClearLog()
    {
        _logPipeline.Clear();
        LogStatus = _logBaseStatus;
    }
    public async Task ExportLogAsync(string path) => await File.WriteAllTextAsync(path, _logPipeline.Snapshot, System.Text.Encoding.UTF8);
    public string? CastAddress => IpAddress == "—" ? null : CastUrl.Build(IpAddress);
    public void OpenRuntimeDirectory() { Directory.CreateDirectory(_toolManager.CacheRoot); Process.Start(new ProcessStartInfo("explorer.exe", _toolManager.CacheRoot) { UseShellExecute = true }); }
    public void ClearOldRuntimeVersions() { if (_paths is not null) _toolManager.ClearOldVersions(_paths.Root); }
    public async Task KillAdbAsync() { if (_adb is not null) await _adb.KillServerAsync(); }

    private async Task StopLogCoreAsync(bool updateStatus)
    {
        _logSessionGate.Invalidate();
        var cancellation = _logCancellation;
        var task = _logTask;
        _logCancellation = null;
        _logTask = null;

        if (cancellation is not null)
        {
            cancellation.Cancel();
            try
            {
                if (task is not null) await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (updateStatus)
                {
                    _logBaseStatus = $"停止失败：{ex.Message}";
                    LogStatus = _logBaseStatus;
                    return;
                }
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        if (updateStatus)
        {
            _logBaseStatus = "已停止";
            LogStatus = _logBaseStatus;
        }
    }

    private async Task ObserveLogCompletionAsync(long session, Task<ProcessResult> task)
    {
        try
        {
            var result = await task.ConfigureAwait(false);
            if (!_logSessionGate.IsCurrent(session)) return;
            var status = result.Succeeded
                ? "日志流已结束"
                : $"日志中断：{AdbErrorTranslator.ToUserMessage(result.StandardError)}";
            Post(() =>
            {
                if (!_logSessionGate.IsCurrent(session)) return;
                _logBaseStatus = status;
                LogStatus = status;
            });
        }
        catch (Exception ex)
        {
            if (!_logSessionGate.IsCurrent(session)) return;
            Post(() =>
            {
                if (!_logSessionGate.IsCurrent(session)) return;
                _logBaseStatus = $"日志启动失败：{ex.Message}";
                LogStatus = _logBaseStatus;
            });
        }
    }

    private void EnforceSingleLaunchSelection()
    {
        var keep = Items.FirstOrDefault(item => item.IsSelected(OperationMode.Launch));
        foreach (var item in Items) item.SetSelected(OperationMode.Launch, ReferenceEquals(item, keep));
        RaiseSelectionSummary();
    }
    public void EnforceLaunchSingleSelection(ApkItemViewModel selected)
    {
        if (!LaunchSingleSelect || !selected.IsSelected(OperationMode.Launch)) return;
        foreach (var item in Items)
            if (!ReferenceEquals(item, selected)) item.SetSelected(OperationMode.Launch, false);
        RaiseSelectionSummary();
    }
    private void RaiseSelectionSummary()
    {
        RaisePropertyChanged(nameof(SelectedCount));
        RaisePropertyChanged(nameof(SelectedCountText));
    }
    private void Begin(ApkItemViewModel item, string operation) { item.State = TaskState.Running; item.Status = $"{operation}中"; item.Error = null; item.IsIndeterminate = true; item.Progress = 0; AppendTask($"开始{operation}：{item.DisplayName}"); }
    private void Succeed(ApkItemViewModel item, string operation) { item.State = TaskState.Succeeded; item.Status = $"{operation}成功"; item.IsIndeterminate = false; item.Progress = 100; AppendTask($"{operation}成功：{item.DisplayName}"); }
    private void Fail(ApkItemViewModel item, string operation, ProcessResult result) => Fail(item, operation, AdbErrorTranslator.ToUserMessage(result.StandardError + result.StandardOutput));
    private void Fail(ApkItemViewModel item, string operation, string reason) { item.State = TaskState.Failed; item.Status = $"{operation}失败"; item.Error = reason; item.IsIndeterminate = false; AppendTask($"{operation}失败：{item.DisplayName}；原因：{reason}"); }
    private void AppendTask(string message) => TaskOutput += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
    private void EnsureReady() { if (_adb is null || _paths is null) throw new InvalidOperationException("内置工具尚未准备完成"); }
    private void Post(Action action) { if (_context is null) action(); else _context.Post(_ => action(), null); }
}
