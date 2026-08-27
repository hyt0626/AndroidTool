using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class EmbeddedToolManagerTests
{
    [Fact]
    public async Task ExtractsActualManifestResources()
    {
        var root = Path.Combine(Path.GetTempPath(), "AndroidToolManifestTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = await new EmbeddedToolManager(new ManifestToolSource(), root).EnsureExtractedAsync();
            Assert.True(paths.IsComplete());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExtractsAndRepairsEmbeddedToolFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "AndroidToolTests", Guid.NewGuid().ToString("N"));
        try
        {
            var source = new DictionaryToolSource(new Dictionary<string, byte[]>
            {
                ["adb/adb.exe"] = [1, 2, 3],
                ["aapt/aapt.exe"] = [4, 5]
            });
            var manager = new EmbeddedToolManager(source, root);

            var paths = await manager.EnsureExtractedAsync();
            await File.WriteAllBytesAsync(paths.AdbPath, [9]);
            paths = await manager.EnsureExtractedAsync();

            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(paths.AdbPath));
            Assert.Equal([4, 5], await File.ReadAllBytesAsync(paths.AaptPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
