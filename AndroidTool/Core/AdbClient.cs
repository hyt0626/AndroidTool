namespace AndroidTool.Core;

public sealed class AdbClient : IDeviceInfoSource
{
    private readonly ToolPaths _paths;
    private readonly IProcessRunner _runner;

    public AdbClient(ToolPaths paths, IProcessRunner? runner = null)
    {
        _paths = paths;
        _runner = runner ?? new ProcessRunner();
    }

    public Task<ProcessResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken = default) =>
        _runner.RunAsync(_paths.AdbPath, args, cancellationToken);

    public async Task<bool> HasDeviceAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["get-state"], cancellationToken);
        return result.Succeeded && result.StandardOutput.Contains("device", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<DeviceInfo?> ReadDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        var serial = await GetConnectedSerialAsync(cancellationToken);
        return serial is null ? null : await ReadDeviceInfoAsync(serial, cancellationToken);
    }

    public async Task<string?> GetConnectedSerialAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["devices"], cancellationToken);
        return result.Succeeded ? AdbDeviceListParser.ParseSingleOnlineSerial(result.StandardOutput) : null;
    }

    public async Task<DeviceInfo?> ReadDeviceInfoAsync(string serial, CancellationToken cancellationToken = default)
    {
        var state = await RunForDeviceAsync(serial, ["get-state"], cancellationToken);
        if (!state.Succeeded || !state.StandardOutput.Trim().Equals("device", StringComparison.OrdinalIgnoreCase)) return null;

        var brand = (await RunForDeviceAsync(serial, ["shell", "getprop", "ro.product.brand"], cancellationToken)).StandardOutput;
        var model = (await RunForDeviceAsync(serial, ["shell", "getprop", "ro.product.model"], cancellationToken)).StandardOutput;
        var version = (await RunForDeviceAsync(serial, ["shell", "getprop", "ro.build.version.release"], cancellationToken)).StandardOutput;
        var battery = (await RunForDeviceAsync(serial, ["shell", "dumpsys", "battery"], cancellationToken)).StandardOutput;
        var route = (await RunForDeviceAsync(serial, ["shell", "ip", "route", "get", "1.1.1.1"], cancellationToken)).StandardOutput;
        var storage = (await RunForDeviceAsync(serial, ["shell", "df", "-h", "/data"], cancellationToken)).StandardOutput;
        var details = DeviceDetailsParser.Parse(route, storage);
        return new DeviceInfoParser().Parse(serial, brand, model, version, battery) with { IpAddress = details.IpAddress, StorageDisplay = details.StorageDisplay };
    }

    private Task<ProcessResult> RunForDeviceAsync(string serial, IEnumerable<string> args, CancellationToken cancellationToken) =>
        RunAsync(new[] { "-s", serial }.Concat(args), cancellationToken);

    public Task<ProcessResult> InstallAsync(string apk, CancellationToken token = default) => RunAsync(["install", "-r", apk], token);
    public Task<ProcessResult> UninstallAsync(string packageName, CancellationToken token = default) => RunAsync(["uninstall", packageName], token);
    public Task<ProcessResult> PushAsync(string local, string remote, CancellationToken token = default) => RunAsync(["push", local, remote], token);
    public Task<ProcessResult> PushWithProgressAsync(string local, string remote, Action<int> progress, CancellationToken token = default) =>
        _runner.RunStreamingAsync(_paths.AdbPath, ["push", "-p", local, remote], line =>
        {
            var value = AdbProgressParser.TryParsePercent(line);
            if (value.HasValue) progress(value.Value);
        }, token);
    public static IReadOnlyList<string> BuildLogArguments(bool unityOnly) =>
        unityOnly
            ? ["logcat", "-T", "1", "-v", "time", "-s", "Unity"]
            : ["logcat", "-T", "1", "-v", "time"];

    public Task<ProcessResult> StreamLogAsync(bool unityOnly, Action<string> onLine, CancellationToken token) =>
        _runner.RunStreamingNoCaptureAsync(_paths.AdbPath, BuildLogArguments(unityOnly), onLine, token);
    public Task<ProcessResult> KillServerAsync() => RunAsync(["kill-server"]);
    public Task<ProcessResult> ShellAsync(params string[] args) => RunAsync(["shell", .. args]);
}
