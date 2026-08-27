using AndroidTool.Core;
using AndroidTool.ViewModels;
using Xunit;

namespace AndroidTool.Tests;

public sealed class ApkItemViewModelTests
{
    private static ApkItemViewModel Create() => new(new ApkInfo("demo.apk", "示例", "com.demo", "1.0", "1", "23", "34", null, "com.demo.Main"), "C:\\apps\\demo.apk");

    [Fact]
    public void KeepsSelectionsIndependentAcrossModes()
    {
        var item = Create();
        item.SetSelected(OperationMode.Install, true);
        item.SetSelected(OperationMode.Launch, true);

        Assert.True(item.IsSelected(OperationMode.Install));
        Assert.False(item.IsSelected(OperationMode.Uninstall));
        Assert.True(item.IsSelected(OperationMode.Launch));
    }

    [Fact]
    public void RecordsMultipleObbFilesFromOneDirectory()
    {
        var item = Create();
        item.SetObbFiles(["C:\\obb\\main.1.com.demo.obb", "C:\\obb\\patch.1.com.demo.obb"]);

        Assert.Equal(2, item.ObbFiles.Count);
        Assert.Equal("C:\\obb", item.ObbDirectory);
    }

    [Fact]
    public void SingleLaunchSelectionKeepsOnlyClickedItem()
    {
        var first = Create();
        var second = new ApkItemViewModel(first.Info, "C:\\apps\\second.apk");
        var main = new MainViewModel { LaunchSingleSelect = true };
        main.Items.Add(first); main.Items.Add(second);
        first.SetSelected(OperationMode.Launch, true);
        second.SetSelected(OperationMode.Launch, true);

        main.EnforceLaunchSingleSelection(second);

        Assert.False(first.IsSelected(OperationMode.Launch));
        Assert.True(second.IsSelected(OperationMode.Launch));
    }

    [Fact]
    public void CardToggleSelectsAndCancelsTheCurrentMode()
    {
        var item = Create();
        var main = new MainViewModel();
        main.Items.Add(item);

        Assert.True(main.ToggleCurrentSelection(item));
        Assert.True(item.IsSelected(OperationMode.Install));
        Assert.Equal(1, main.SelectedCount);

        Assert.True(main.ToggleCurrentSelection(item));
        Assert.False(item.IsSelected(OperationMode.Install));
        Assert.Equal(0, main.SelectedCount);
    }

    [Fact]
    public void CardSelectionsRemainIndependentWhenModesChange()
    {
        var item = Create();
        var main = new MainViewModel();
        main.Items.Add(item);

        main.ToggleCurrentSelection(item);
        main.SetMode(OperationMode.Uninstall);

        Assert.False(item.IsCurrentSelected);
        Assert.Equal(0, main.SelectedCount);

        main.ToggleCurrentSelection(item);
        main.SetMode(OperationMode.Install);

        Assert.True(item.IsCurrentSelected);
        Assert.True(item.IsSelected(OperationMode.Uninstall));
        Assert.Equal(1, main.SelectedCount);
    }

    [Fact]
    public void SingleLaunchCardToggleMovesSelectionAndAllowsClearingIt()
    {
        var first = Create();
        var second = new ApkItemViewModel(first.Info, "C:\\apps\\second.apk");
        var main = new MainViewModel { LaunchSingleSelect = true };
        main.Items.Add(first);
        main.Items.Add(second);
        main.SetMode(OperationMode.Launch);

        main.ToggleCurrentSelection(first);
        main.ToggleCurrentSelection(second);

        Assert.False(first.IsSelected(OperationMode.Launch));
        Assert.True(second.IsSelected(OperationMode.Launch));
        Assert.Equal(1, main.SelectedCount);

        main.ToggleCurrentSelection(second);

        Assert.False(second.IsSelected(OperationMode.Launch));
        Assert.Equal(0, main.SelectedCount);
    }

    [Fact]
    public void BusyCardCannotChangeSelection()
    {
        var item = Create();
        var main = new MainViewModel();
        main.Items.Add(item);
        item.State = TaskState.Running;

        Assert.False(main.ToggleCurrentSelection(item));
        Assert.False(item.IsCurrentSelected);
        Assert.Equal(0, main.SelectedCount);
    }

    [Fact]
    public void ClearingCurrentModePreservesOtherModeSelections()
    {
        var item = Create();
        var main = new MainViewModel();
        main.Items.Add(item);
        main.ToggleCurrentSelection(item);
        main.SetMode(OperationMode.Uninstall);
        main.ToggleCurrentSelection(item);
        main.SetMode(OperationMode.Install);

        main.ClearCurrentSelection();

        Assert.False(item.IsSelected(OperationMode.Install));
        Assert.True(item.IsSelected(OperationMode.Uninstall));
        Assert.Equal(0, main.SelectedCount);
    }

    [Fact]
    public void BusyStateChangeNotifiesTheCardStyle()
    {
        var item = Create();
        var changedProperties = new List<string?>();
        item.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        item.State = TaskState.Running;

        Assert.Contains(nameof(ApkItemViewModel.IsBusy), changedProperties);
    }

    [Fact]
    public void CardToggleNotifiesSelectedCountBindings()
    {
        var item = Create();
        var main = new MainViewModel();
        var changedProperties = new List<string?>();
        main.Items.Add(item);
        main.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        main.ToggleCurrentSelection(item);

        Assert.Contains(nameof(MainViewModel.SelectedCount), changedProperties);
        Assert.Contains(nameof(MainViewModel.SelectedCountText), changedProperties);
        Assert.Equal("当前已选 1 个", main.SelectedCountText);
    }
}
