using System.Reflection;
using System.Security.Cryptography;
using System.IO;

namespace AndroidTool.Core;

public interface IEmbeddedToolSource
{
    IReadOnlyCollection<string> Names { get; }
    Task<byte[]> ReadAsync(string name, CancellationToken cancellationToken = default);
}

public sealed class DictionaryToolSource(IReadOnlyDictionary<string, byte[]> files) : IEmbeddedToolSource
{
    public IReadOnlyCollection<string> Names => files.Keys.ToArray();
    public Task<byte[]> ReadAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(files[name]);
}

public sealed class ManifestToolSource : IEmbeddedToolSource
{
    private static readonly string[] ToolNames = ["adb/adb.exe", "adb/AdbWinApi.dll", "adb/AdbWinUsbApi.dll", "aapt/aapt.exe"];
    private readonly Assembly _assembly = typeof(ManifestToolSource).Assembly;

    public IReadOnlyCollection<string> Names => ToolNames;

    public async Task<byte[]> ReadAsync(string name, CancellationToken cancellationToken = default)
    {
        var resourceName = "Tools." + name.Replace('/', '.');
        await using var stream = _assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"缺少内置资源：{resourceName}");
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }
}

public sealed class EmbeddedToolManager
{
    private static readonly SemaphoreSlim ExtractionGate = new(1, 1);
    private readonly IEmbeddedToolSource _source;
    private readonly string _cacheRoot;

    public EmbeddedToolManager(IEmbeddedToolSource source, string? cacheRoot = null)
    {
        _source = source;
        _cacheRoot = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidTool", "Runtime");
    }

    public string CacheRoot => _cacheRoot;

    public async Task<ToolPaths> EnsureExtractedAsync(CancellationToken cancellationToken = default)
    {
        await ExtractionGate.WaitAsync(cancellationToken);
        try
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in _source.Names) files[name] = await _source.ReadAsync(name, cancellationToken);
            var version = ComputeVersion(files);
            var versionRoot = Path.Combine(_cacheRoot, version);
            foreach (var (name, bytes) in files)
            {
                var destination = Path.Combine(versionRoot, name.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(destination) && await HasExpectedHashAsync(destination, bytes, cancellationToken)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                File.Move(temporary, destination, true);
            }
            return ToolPaths.FromRoot(versionRoot);
        }
        finally { ExtractionGate.Release(); }
    }

    public void ClearOldVersions(string currentRoot)
    {
        if (!Directory.Exists(_cacheRoot)) return;
        foreach (var directory in Directory.GetDirectories(_cacheRoot))
            if (!Path.GetFullPath(directory).Equals(Path.GetFullPath(currentRoot), StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory, true);
    }

    private static string ComputeVersion(IReadOnlyDictionary<string, byte[]> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in files.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(item.Key));
            hash.AppendData(item.Value);
        }
        return Convert.ToHexString(hash.GetHashAndReset())[..12].ToLowerInvariant();
    }

    private static async Task<bool> HasExpectedHashAsync(string path, byte[] expected, CancellationToken cancellationToken)
    {
        var actual = await File.ReadAllBytesAsync(path, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(actual), SHA256.HashData(expected));
    }
}
