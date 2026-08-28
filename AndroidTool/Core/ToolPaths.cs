using System.IO;

namespace AndroidTool.Core;

public sealed class ToolPaths
{
    private ToolPaths(string root)
    {
        Root = root;
        AdbPath = Path.Combine(root, "adb", "adb.exe");
        AaptPath = Path.Combine(root, "aapt", "aapt.exe");
        RequiredFiles = [AdbPath, AaptPath, Path.Combine(root, "adb", "AdbWinApi.dll"), Path.Combine(root, "adb", "AdbWinUsbApi.dll")];
    }

    public string Root { get; }
    public string AdbPath { get; }
    public string AaptPath { get; }
    public IReadOnlyList<string> RequiredFiles { get; }

    public static ToolPaths FromRoot(string root) => new(Path.GetFullPath(root));

    public bool IsComplete() => RequiredFiles.All(File.Exists);

    public IReadOnlyList<string> MissingFiles() => RequiredFiles.Where(file => !File.Exists(file)).ToArray();
}
