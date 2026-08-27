using System.IO;

namespace AndroidTool.Core;

public sealed record ApkInfo(string FileName, string? ApplicationName, string? PackageName, string? VersionName, string? VersionCode, string? MinSdk, string? TargetSdk, string? IconPath, string? LaunchableActivity);

public sealed class ApkInfoParser
{
    public ApkInfo Parse(string fileName, string badgingOutput)
    {
        var packageLine = Line(badgingOutput, "package:");
        var applicationLine = Line(badgingOutput, "application-label:");
        var iconLine = badgingOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.Contains("application-icon-", StringComparison.OrdinalIgnoreCase));
        var launchLine = Line(badgingOutput, "launchable-activity:");
        return new ApkInfo(
            Path.GetFileName(fileName),
            Value(applicationLine, "application-label:"),
            Value(packageLine, "name="),
            Value(packageLine, "versionName="),
            Value(packageLine, "versionCode="),
            Value(packageLine, "sdkVersion:"),
            Value(packageLine, "targetSdkVersion:"),
            Value(iconLine, ":"),
            Value(launchLine, "name="));
    }

    private static string? Line(string content, string prefix) => content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string? Value(string? line, string key)
    {
        if (line is null) return null;
        var start = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += key.Length;
        if (start < line.Length && line[start] == '=') start++;
        if (start < line.Length && line[start] == '\'') start++;
        var end = line.IndexOf('\'', start);
        if (end < 0) end = line.Length;
        return line[start..end].Trim();
    }
}
