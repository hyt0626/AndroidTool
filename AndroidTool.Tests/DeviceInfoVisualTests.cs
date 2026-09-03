using AndroidTool.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using Xunit;

namespace AndroidTool.Tests;

public sealed class DeviceInfoVisualTests
{
    [Fact]
    public async Task EachDeviceInfoCopyButtonCopiesItsBoundValue()
    {
        await RunOnStaAsync(() =>
        {
            var window = new MainWindow();
            var viewModel = Assert.IsType<MainViewModel>(window.DataContext);
            viewModel.Serial = "SERIAL-001";
            viewModel.Brand = "Brand-002";
            viewModel.Model = "Model-003";
            viewModel.AndroidVersion = "Android-004";
            viewModel.Battery = "55%";
            viewModel.IpAddress = "192.0.2.6";
            viewModel.Storage = "64G / 128G";
            FlushBindings();

            var expectedFields = new[]
            {
                (Label: "序列号", Property: nameof(MainViewModel.Serial), Value: viewModel.Serial),
                (Label: "品牌", Property: nameof(MainViewModel.Brand), Value: viewModel.Brand),
                (Label: "型号", Property: nameof(MainViewModel.Model), Value: viewModel.Model),
                (Label: "Android", Property: nameof(MainViewModel.AndroidVersion), Value: viewModel.AndroidVersion),
                (Label: "电量", Property: nameof(MainViewModel.Battery), Value: viewModel.Battery),
                (Label: "IP 地址", Property: nameof(MainViewModel.IpAddress), Value: viewModel.IpAddress),
                (Label: "内部存储", Property: nameof(MainViewModel.Storage), Value: viewModel.Storage)
            };

            var root = Assert.IsAssignableFrom<DependencyObject>(window.Content);
            var deviceGrid = Assert.Single(
                Descendants<UniformGrid>(root),
                grid => grid.Columns == 7 && grid.Children.OfType<StackPanel>().Count() == 7);
            var originalClipboard = Clipboard.GetDataObject();

            try
            {
                foreach (var field in expectedFields)
                {
                    var panel = Assert.Single(
                        deviceGrid.Children.OfType<StackPanel>(),
                        candidate => candidate.Children.OfType<TextBlock>()
                            .Any(label => label.Text == field.Label));
                    var copyButton = Assert.Single(
                        Descendants<Button>(panel),
                        button => Equals(button.Content, "复制"));
                    var tagBinding = BindingOperations.GetBinding(copyButton, FrameworkElement.TagProperty);

                    Assert.NotNull(tagBinding);
                    Assert.Equal(field.Property, tagBinding.Path.Path);
                    Assert.Equal(field.Value, copyButton.Tag);

                    Clipboard.SetText("unchanged");
                    copyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(field.Value, Clipboard.GetText());
                }
            }
            finally
            {
                if (originalClipboard is null) Clipboard.Clear();
                else Clipboard.SetDataObject(originalClipboard, true);
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void FlushBindings() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

    private static async Task RunOnStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
