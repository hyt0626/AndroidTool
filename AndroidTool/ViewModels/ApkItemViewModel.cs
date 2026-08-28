using AndroidTool.Core;
using System.IO;

namespace AndroidTool.ViewModels;

public sealed class ApkItemViewModel : ObservableObject
{
    private bool _installSelected;
    private bool _uninstallSelected;
    private bool _launchSelected;
    private TaskState _state;
    private string _status = "就绪";
    private string? _error;
    private double _progress;
    private bool _isIndeterminate;
    private OperationMode _currentMode;

    public ApkItemViewModel(ApkInfo info, string fullPath)
    {
        Info = info;
        FullPath = Path.GetFullPath(fullPath);
    }

    public ApkInfo Info { get; }
    public string FullPath { get; }
    public string DisplayName => Info.ApplicationName ?? Info.FileName;
    public string FileName => Info.FileName;
    public string ApplicationName => Info.ApplicationName ?? "—";
    public string PackageName => Info.PackageName ?? "—";
    public string VersionName => Info.VersionName ?? "—";
    public string VersionCode => Info.VersionCode ?? "—";
    public string MinSdk => Info.MinSdk ?? "—";
    public string TargetSdk => Info.TargetSdk ?? "—";
    public List<string> ObbFiles { get; } = [];
    public string? ObbDirectory { get; private set; }
    public string ObbSummary => ObbFiles.Count == 0 ? "未选择 OBB" : $"{ObbDirectory}（{ObbFiles.Count} 个文件）";
    public TaskState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value)) RaisePropertyChanged(nameof(IsBusy));
        }
    }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }
    public bool IsIndeterminate { get => _isIndeterminate; set => SetProperty(ref _isIndeterminate, value); }
    public bool IsBusy => State is TaskState.Waiting or TaskState.Running or TaskState.CopyingObb;
    public OperationMode CurrentMode
    {
        get => _currentMode;
        set { if (SetProperty(ref _currentMode, value)) RaisePropertyChanged(nameof(IsCurrentSelected)); }
    }
    public bool IsCurrentSelected
    {
        get => IsSelected(CurrentMode);
        set { SetSelected(CurrentMode, value); RaisePropertyChanged(); }
    }

    public bool IsSelected(OperationMode mode) => mode switch
    {
        OperationMode.Install => _installSelected,
        OperationMode.Uninstall => _uninstallSelected,
        _ => _launchSelected
    };

    public void SetSelected(OperationMode mode, bool value)
    {
        switch (mode)
        {
            case OperationMode.Install: _installSelected = value; break;
            case OperationMode.Uninstall: _uninstallSelected = value; break;
            case OperationMode.Launch: _launchSelected = value; break;
        }
        RaisePropertyChanged(nameof(IsSelected));
        RaisePropertyChanged(nameof(IsCurrentSelected));
    }

    public void SetObbFiles(IEnumerable<string> files)
    {
        var selected = files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected.Length > 1 && selected.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            throw new ArgumentException("OBB 文件必须位于同一目录");
        ObbFiles.Clear();
        ObbFiles.AddRange(selected);
        ObbDirectory = selected.Length == 0 ? null : Path.GetDirectoryName(selected[0]);
        RaisePropertyChanged(nameof(ObbDirectory));
        RaisePropertyChanged(nameof(ObbSummary));
    }
}
