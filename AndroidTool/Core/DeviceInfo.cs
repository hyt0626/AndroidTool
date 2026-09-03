namespace AndroidTool.Core;

public sealed record DeviceInfo(string Serial, string Brand, string Model, string AndroidVersion, int? BatteryPercent, string? IpAddress = null, string StorageDisplay = "—");

public interface IDeviceInfoSource
{
    Task<string?> GetConnectedSerialAsync(CancellationToken cancellationToken = default);
    Task<DeviceInfo?> ReadDeviceInfoAsync(string serial, CancellationToken cancellationToken = default);
}

public interface IDeviceRefreshController
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task RefreshDeviceAsync(CancellationToken cancellationToken = default);
    Task RefreshDeviceIfChangedAsync(CancellationToken cancellationToken = default);
}

public sealed class DeviceInfoParser
{
    public DeviceInfo Parse(string serial, string brand, string model, string androidVersion, string batteryOutput)
    {
        int? battery = null;
        var line = batteryOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(item => item.Contains("level", StringComparison.OrdinalIgnoreCase));
        if (line is not null)
        {
            var digits = new string(line.SkipWhile(ch => !char.IsDigit(ch)).TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var value)) battery = value;
        }

        return new DeviceInfo(serial.Trim(), brand.Trim(), model.Trim(), androidVersion.Trim(), battery);
    }
}
