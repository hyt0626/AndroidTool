namespace AndroidTool.Core;

public sealed record DeviceDetails(string? IpAddress, string StorageDisplay);

public static class DeviceDetailsParser
{
    public static DeviceDetails Parse(string routeOutput, string storageOutput)
    {
        var routeParts = routeOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? ip = null;
        for (var index = 0; index < routeParts.Length - 1; index++)
            if (routeParts[index].Equals("src", StringComparison.OrdinalIgnoreCase)) ip = routeParts[index + 1].Trim();

        var storageLine = storageOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        var storageParts = storageLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var display = storageParts.Length >= 4 ? $"{storageParts[2]} / {storageParts[1]}（可用 {storageParts[3]}）" : "—";
        return new DeviceDetails(ip, display);
    }
}
