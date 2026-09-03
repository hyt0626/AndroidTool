using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class AdbClientDeviceTargetTests
{
    [Fact]
    public async Task ConnectedSerialComesFromExactlyOneOnlineDevice()
    {
        var runner = new RecordingProcessRunner
        {
            DevicesOutput = "List of devices attached\nDEVICE-A\toffline\nDEVICE-B\tdevice model:Pixel\n"
        };
        var client = new AdbClient(ToolPaths.FromRoot("C:\\tools"), runner);

        var serial = await client.GetConnectedSerialAsync();

        Assert.Equal("DEVICE-B", serial);
        Assert.Single(runner.Commands);
        Assert.Equal(new[] { "devices" }, runner.Commands[0]);
    }

    [Fact]
    public async Task DeviceInfoCommandsTargetDetectedSerial()
    {
        var runner = new RecordingProcessRunner();
        var client = new AdbClient(ToolPaths.FromRoot("C:\\tools"), runner);

        var info = await client.ReadDeviceInfoAsync("DEVICE-B");

        Assert.NotNull(info);
        Assert.NotEmpty(runner.Commands);
        Assert.All(runner.Commands, command => Assert.Equal(new[] { "-s", "DEVICE-B" }, command.Take(2)));
    }

    [Fact]
    public async Task DefaultDeviceInfoReadTargetsTheDetectedSerial()
    {
        var runner = new RecordingProcessRunner
        {
            DevicesOutput = "List of devices attached\nDEVICE-B\tdevice model:Pixel\n"
        };
        var client = new AdbClient(ToolPaths.FromRoot("C:\\tools"), runner);

        var info = await client.ReadDeviceInfoAsync();

        Assert.NotNull(info);
        Assert.Equal("DEVICE-B", info.Serial);
        Assert.Equal(new[] { "devices" }, runner.Commands[0]);
        Assert.All(runner.Commands.Skip(1), command => Assert.Equal(new[] { "-s", "DEVICE-B" }, command.Take(2)));
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<string[]> Commands { get; } = [];
        public string DevicesOutput { get; init; } = "List of devices attached\n";

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken = default)
        {
            var command = arguments.ToArray();
            Commands.Add(command);
            var output = command.SequenceEqual(new[] { "devices" }) ? DevicesOutput : command[^1] switch
            {
                "get-state" => "device\n",
                "ro.product.brand" => "Brand B\n",
                "ro.product.model" => "Model B\n",
                "ro.build.version.release" => "14\n",
                "battery" => "level: 85\n",
                "1.1.1.1" => "1.1.1.1 via 192.0.2.1 dev wlan0 src 192.0.2.20\n",
                "/data" => "Filesystem Size Used Avail Use% Mounted on\n/data 40G 10G 30G 25% /data\n",
                _ => string.Empty
            };
            return Task.FromResult(new ProcessResult(0, output, string.Empty));
        }

        public Task<ProcessResult> RunStreamingAsync(string fileName, IEnumerable<string> arguments, Action<string>? onLine, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProcessResult> RunStreamingNoCaptureAsync(string fileName, IEnumerable<string> arguments, Action<string>? onLine, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
