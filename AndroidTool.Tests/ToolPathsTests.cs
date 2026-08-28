using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class ToolPathsTests
{
    [Fact]
    public void FindsBundledToolsRelativeToApplicationDirectory()
    {
        var root = Path.Combine("C:\\portable", "AndroidTool");

        var paths = ToolPaths.FromRoot(root);

        Assert.Equal(Path.Combine(root, "adb", "adb.exe"), paths.AdbPath);
        Assert.Equal(Path.Combine(root, "aapt", "aapt.exe"), paths.AaptPath);
    }

    [Fact]
    public void ReportsMissingBundledToolNames()
    {
        var paths = ToolPaths.FromRoot("C:\\portable\\AndroidTool");

        Assert.Contains(paths.RequiredFiles, file => file.EndsWith("adb.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths.RequiredFiles, file => file.EndsWith("aapt.exe", StringComparison.OrdinalIgnoreCase));
    }
}
