namespace AndroidTool.Core;

public static class AdbDeviceListParser
{
    public static string? ParseSingleOnlineSerial(string output)
    {
        string? onlineSerial = null;
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 2 || !fields[1].Equals("device", StringComparison.Ordinal)) continue;
            if (onlineSerial is not null) return null;
            onlineSerial = fields[0];
        }
        return onlineSerial;
    }
}
