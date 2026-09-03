using AndroidTool.Core;
using AndroidTool.ViewModels;
using Xunit;

namespace AndroidTool.Tests;

public sealed class DeviceInfoRefreshTests
{
    [Fact]
    public async Task SwitchingConnectedDeviceRefreshesAllDisplayedInformation()
    {
        var source = new FakeDeviceInfoSource
        {
            ConnectedSerial = "DEVICE-A"
        };
        source.SetInfo(new DeviceInfo("DEVICE-A", "Brand A", "Model A", "13", 40, "192.0.2.10", "10 GB / 20 GB"));
        source.SetInfo(new DeviceInfo("DEVICE-B", "Brand B", "Model B", "14", 85, "192.0.2.20", "30 GB / 40 GB"));
        var viewModel = new MainViewModel(source);

        await viewModel.RefreshDeviceAsync();
        source.ConnectedSerial = "DEVICE-B";
        await viewModel.RefreshDeviceIfChangedAsync();

        Assert.Equal("设备已连接", viewModel.StatusText);
        Assert.Equal("DEVICE-B", viewModel.Serial);
        Assert.Equal("Brand B", viewModel.Brand);
        Assert.Equal("Model B", viewModel.Model);
        Assert.Equal("14", viewModel.AndroidVersion);
        Assert.Equal("85%", viewModel.Battery);
        Assert.Equal("192.0.2.20", viewModel.IpAddress);
        Assert.Equal("30 GB / 40 GB", viewModel.Storage);
    }

    [Fact]
    public async Task UnchangedDeviceDoesNotReloadFullInformation()
    {
        var source = new FakeDeviceInfoSource
        {
            ConnectedSerial = "DEVICE-A"
        };
        source.SetInfo(new DeviceInfo("DEVICE-A", "Brand A", "Model A", "13", 40));
        var viewModel = new MainViewModel(source);

        await viewModel.RefreshDeviceAsync();
        await viewModel.RefreshDeviceIfChangedAsync();

        Assert.Equal(1, source.InfoReadCount);
    }

    [Fact]
    public async Task DisconnectingDeviceClearsDisplayedInformation()
    {
        var source = new FakeDeviceInfoSource
        {
            ConnectedSerial = "DEVICE-A"
        };
        source.SetInfo(new DeviceInfo("DEVICE-A", "Brand A", "Model A", "13", 40, "192.0.2.10", "10 GB / 20 GB"));
        var viewModel = new MainViewModel(source);
        await viewModel.RefreshDeviceAsync();

        source.ConnectedSerial = null;
        await viewModel.RefreshDeviceIfChangedAsync();

        Assert.Equal("未检测到设备", viewModel.StatusText);
        Assert.All(
            new[] { viewModel.Serial, viewModel.Brand, viewModel.Model, viewModel.AndroidVersion, viewModel.Battery, viewModel.IpAddress, viewModel.Storage },
            value => Assert.Equal("—", value));
    }

    [Fact]
    public async Task SlowerResultFromPreviousDeviceCannotOverwriteNewDevice()
    {
        var source = new FakeDeviceInfoSource
        {
            ConnectedSerial = "DEVICE-A"
        };
        source.SetInfo(new DeviceInfo("DEVICE-A", "Brand A", "Model A", "13", 40));
        source.SetInfo(new DeviceInfo("DEVICE-B", "Brand B", "Model B", "14", 60));
        source.SetInfo(new DeviceInfo("DEVICE-C", "Brand C", "Model C", "15", 80));
        var viewModel = new MainViewModel(source);
        await viewModel.RefreshDeviceAsync();
        var blockedRead = source.BlockRead("DEVICE-B");

        source.ConnectedSerial = "DEVICE-B";
        var previousRefresh = viewModel.RefreshDeviceIfChangedAsync();
        await blockedRead.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        source.ConnectedSerial = "DEVICE-C";
        await viewModel.RefreshDeviceIfChangedAsync();
        blockedRead.Completion.SetResult(source.GetInfo("DEVICE-B"));
        await previousRefresh;

        Assert.Equal("DEVICE-C", viewModel.Serial);
        Assert.Equal("Brand C", viewModel.Brand);
        Assert.Equal("Model C", viewModel.Model);
    }

    [Fact]
    public async Task DeviceChangingDuringReadDoesNotApplyOutdatedInformation()
    {
        var source = new FakeDeviceInfoSource
        {
            ConnectedSerial = "DEVICE-A"
        };
        source.SetInfo(new DeviceInfo("DEVICE-A", "Brand A", "Model A", "13", 40));
        source.SetInfo(new DeviceInfo("DEVICE-B", "Brand B", "Model B", "14", 60));
        source.SetInfo(new DeviceInfo("DEVICE-C", "Brand C", "Model C", "15", 80));
        var viewModel = new MainViewModel(source);
        await viewModel.RefreshDeviceAsync();
        var blockedRead = source.BlockRead("DEVICE-B");

        source.ConnectedSerial = "DEVICE-B";
        var refresh = viewModel.RefreshDeviceIfChangedAsync();
        await blockedRead.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.ConnectedSerial = "DEVICE-C";
        blockedRead.Completion.SetResult(source.GetInfo("DEVICE-B"));
        await refresh;

        Assert.Equal("DEVICE-A", viewModel.Serial);

        await viewModel.RefreshDeviceIfChangedAsync();
        Assert.Equal("DEVICE-C", viewModel.Serial);
    }

    [Fact]
    public async Task CancelledRefreshDoesNotStartDeviceRead()
    {
        var source = new FakeDeviceInfoSource
        {
            ConnectedSerial = "DEVICE-A"
        };
        source.SetInfo(new DeviceInfo("DEVICE-A", "Brand A", "Model A", "13", 40));
        var viewModel = new MainViewModel(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => viewModel.RefreshDeviceAsync(cancellation.Token));

        Assert.Equal(0, source.InfoReadCount);
        Assert.Equal("—", viewModel.Serial);
    }

    private sealed class FakeDeviceInfoSource : IDeviceInfoSource
    {
        private readonly Dictionary<string, DeviceInfo> _infoBySerial = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingRead> _pendingReads = new(StringComparer.Ordinal);

        public string? ConnectedSerial { get; set; }
        public int InfoReadCount { get; private set; }

        public void SetInfo(DeviceInfo info) => _infoBySerial[info.Serial] = info;
        public DeviceInfo GetInfo(string serial) => _infoBySerial[serial];

        public PendingRead BlockRead(string serial)
        {
            var pending = new PendingRead();
            _pendingReads[serial] = pending;
            return pending;
        }

        public Task<string?> GetConnectedSerialAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectedSerial);

        public async Task<DeviceInfo?> ReadDeviceInfoAsync(string serial, CancellationToken cancellationToken = default)
        {
            InfoReadCount++;
            if (_pendingReads.TryGetValue(serial, out var pending))
            {
                pending.Started.TrySetResult();
                return await pending.Completion.Task.WaitAsync(cancellationToken);
            }
            return _infoBySerial.GetValueOrDefault(serial);
        }

        public sealed class PendingRead
        {
            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<DeviceInfo?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
