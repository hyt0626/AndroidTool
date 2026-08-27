using AndroidTool.Core;
using AndroidTool.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace AndroidTool.Tests;

public sealed class ApkCardVisualTests
{
    [Fact]
    public async Task SelectedCardTurnsGreenAndCancellationRestoresOriginalColors()
    {
        await RunOnStaAsync(() =>
        {
            var window = new MainWindow();
            var item = CreateItem();
            var card = new Border { DataContext = item, Style = (Style)window.Resources["ApkCard"] };
            FlushBindings();

            Assert.Equal(Colors.White, BrushColor(card.Background));
            Assert.Equal(Color.FromRgb(0xD0, 0xD5, 0xDD), BrushColor(card.BorderBrush));
            Assert.Equal(new Thickness(1), card.BorderThickness);

            item.SetSelected(OperationMode.Install, true);
            FlushBindings();

            Assert.Equal(Color.FromRgb(0xEA, 0xF8, 0xEF), BrushColor(card.Background));
            Assert.Equal(Color.FromRgb(0x12, 0xB7, 0x6A), BrushColor(card.BorderBrush));
            Assert.Equal(new Thickness(2), card.BorderThickness);

            item.SetSelected(OperationMode.Install, false);
            FlushBindings();

            Assert.Equal(Colors.White, BrushColor(card.Background));
            Assert.Equal(Color.FromRgb(0xD0, 0xD5, 0xDD), BrushColor(card.BorderBrush));
            Assert.Equal(new Thickness(1), card.BorderThickness);
        });
    }

    [Fact]
    public async Task SelectionBadgeAndBusyCursorFollowCardState()
    {
        await RunOnStaAsync(() =>
        {
            var window = new MainWindow();
            var item = CreateItem();
            var card = new Border { DataContext = item, Style = (Style)window.Resources["ApkCard"] };
            var badge = new Border { DataContext = item, Style = (Style)window.Resources["SelectedBadge"] };
            FlushBindings();

            Assert.Equal(Visibility.Collapsed, badge.Visibility);
            Assert.Equal(Cursors.Hand, card.Cursor);

            item.SetSelected(OperationMode.Install, true);
            item.State = TaskState.Running;
            FlushBindings();

            Assert.Equal(Visibility.Visible, badge.Visibility);
            Assert.Equal(Cursors.Arrow, card.Cursor);
        });
    }

    [Fact]
    public async Task CardInputPolicyRejectsActionButtonsAndSecondDoubleClick()
    {
        await RunOnStaAsync(() =>
        {
            var content = new Grid();
            var actionButton = new Button();
            content.Children.Add(actionButton);
            var card = new Border { Child = content };

            Assert.True(ApkCardInputPolicy.ShouldToggle(clickCount: 1, content, card));
            Assert.False(ApkCardInputPolicy.ShouldToggle(clickCount: 1, actionButton, card));
            Assert.False(ApkCardInputPolicy.ShouldToggle(clickCount: 2, content, card));
        });
    }

    private static ApkItemViewModel CreateItem() => new(
        new ApkInfo("demo.apk", "示例", "com.demo", "1.0", "1", "23", "34", null, "com.demo.Main"),
        "C:\\apps\\demo.apk");

    private static Color BrushColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

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
