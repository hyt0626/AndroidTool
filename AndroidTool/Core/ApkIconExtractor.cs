using System.IO.Compression;
using System.IO;

namespace AndroidTool.Core;

public sealed class ApkIconExtractor
{
    public async Task<string?> ExtractAsync(string apkPath, string iconEntry, string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(iconEntry) || !File.Exists(apkPath)) return null;
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(apkPath) + ".png");
        using var archive = ZipFile.OpenRead(apkPath);
        var entry = archive.GetEntry(iconEntry);
        if (entry is null) return null;
        await using var source = entry.Open();
        await using var target = File.Create(outputPath);
        await source.CopyToAsync(target, cancellationToken);
        return outputPath;
    }
}
