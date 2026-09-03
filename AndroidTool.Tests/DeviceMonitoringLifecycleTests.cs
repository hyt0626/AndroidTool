using AndroidTool.Core;
using AndroidTool.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace AndroidTool.Tests;

public sealed class DeviceMonitoringLifecycleTests
{
    [Fact]
    public Task ClosingDuringInitializationCancelsAndWaitsForDeviceWork() =>
        RunStaScenarioAsync(RunWindowScenario);

    [Fact]
    public Task StoppingDeviceMonitoringPreventsOverlapAndCancelsActiveCheck() =>
        RunStaScenarioAsync(RunAutomaticCheckScenario);

    [Fact]
    public Task ClosingDuringManualRefreshCancelsAndWaitsForDeviceWork() =>
        RunStaScenarioAsync(RunManualRefreshScenario);

    private static async Task RunStaScenarioAsync(Action<TaskCompletionSource> scenario)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcherReady = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcherReady.TrySetResult(dispatcher);
            try
            {
                scenario(completion);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
                TryShutdown(dispatcher);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Dispatcher? scenarioDispatcher = null;
        try
        {
            scenarioDispatcher = await dispatcherReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            if (scenarioDispatcher is not null) TryShutdown(scenarioDispatcher);
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA test dispatcher did not stop.");
        }
    }

    private static void RunWindowScenario(TaskCompletionSource completion)
    {
        var controller = new BlockingDeviceRefreshController();
        var window = CreateHiddenWindow(controller);
        window.Closed += (_, _) => CompleteScenario(completion, window.Dispatcher, () =>
        {
            Assert.True(controller.InitializationCancelled);
            Assert.True(controller.InitializationExited);
        });
        RunDriver(completion, window.Dispatcher, async () =>
        {
            await controller.InitializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await window.Dispatcher.InvokeAsync(window.Close);
        });

        window.Show();
        Dispatcher.Run();
    }

    private static void RunAutomaticCheckScenario(TaskCompletionSource completion)
    {
        var controller = new BlockingAutomaticRefreshController();
        var window = CreateHiddenWindow(controller, TimeSpan.FromMilliseconds(20));
        window.Closed += (_, _) => CompleteScenario(completion, window.Dispatcher, () =>
        {
            Assert.Equal(1, controller.CheckCount);
            Assert.True(controller.CheckCancelled);
            Assert.True(controller.CheckExited);
        });
        RunDriver(completion, window.Dispatcher, async () =>
        {
            await controller.CheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            var stopTask = await window.Dispatcher.InvokeAsync(window.StopDeviceMonitoringAsync);
            await stopTask;
            await window.Dispatcher.InvokeAsync(window.Close);
        });

        window.Show();
        Dispatcher.Run();
    }

    private static void RunManualRefreshScenario(TaskCompletionSource completion)
    {
        var controller = new BlockingManualRefreshController();
        var window = CreateHiddenWindow(controller, TimeSpan.FromMinutes(1));
        window.Loaded += (_, _) =>
        {
            var refreshButton = Assert.Single(
                Descendants<Button>(window),
                button => Equals(button.Content, "刷新设备"));
            refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        window.Closed += (_, _) => CompleteScenario(completion, window.Dispatcher, () =>
        {
            Assert.Equal(1, controller.RefreshCount);
            Assert.True(controller.RefreshCancelled);
            Assert.True(controller.RefreshExited);
        });
        RunDriver(completion, window.Dispatcher, async () =>
        {
            await controller.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            await window.Dispatcher.InvokeAsync(window.Close);
        });

        window.Show();
        Dispatcher.Run();
    }

    private static MainWindow CreateHiddenWindow(
        IDeviceRefreshController controller,
        TimeSpan? monitorInterval = null) =>
        new(new MainViewModel(), controller, monitorInterval)
        {
            Width = 1,
            Height = 1,
            Left = -10_000,
            Top = -10_000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowState = WindowState.Minimized
        };

    private static void RunDriver(
        TaskCompletionSource completion,
        Dispatcher dispatcher,
        Func<Task> driver)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await driver();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
                TryShutdown(dispatcher);
            }
        });
    }

    private static void CompleteScenario(
        TaskCompletionSource completion,
        Dispatcher dispatcher,
        Action assertions)
    {
        try
        {
            assertions();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            TryShutdown(dispatcher);
        }
    }

    private static void TryShutdown(Dispatcher dispatcher)
    {
        if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class BlockingDeviceRefreshController : IDeviceRefreshController
    {
        public TaskCompletionSource InitializationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool InitializationCancelled { get; private set; }
        public bool InitializationExited { get; private set; }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                InitializationCancelled = true;
                throw;
            }
            finally
            {
                InitializationExited = true;
            }
        }

        public Task RefreshDeviceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshDeviceIfChangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BlockingAutomaticRefreshController : IDeviceRefreshController
    {
        private int _checkCount;

        public TaskCompletionSource CheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CheckCount => Volatile.Read(ref _checkCount);
        public bool CheckCancelled { get; private set; }
        public bool CheckExited { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshDeviceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task RefreshDeviceIfChangedAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _checkCount);
            CheckStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CheckCancelled = true;
                throw;
            }
            finally
            {
                CheckExited = true;
            }
        }
    }

    private sealed class BlockingManualRefreshController : IDeviceRefreshController
    {
        private int _refreshCount;

        public TaskCompletionSource RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RefreshCount => Volatile.Read(ref _refreshCount);
        public bool RefreshCancelled { get; private set; }
        public bool RefreshExited { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshDeviceIfChangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task RefreshDeviceAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _refreshCount);
            RefreshStarted.TrySetResult();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                RefreshCancelled = true;
                throw;
            }
            finally
            {
                RefreshExited = true;
            }
        }
    }
}
