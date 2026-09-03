using AndroidTool.Core;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Xunit;

namespace AndroidTool.Tests;

public sealed class ProcessRunnerCancellationTests
{
    [Fact]
    public async Task BufferedProcessIsTerminatedWhenCancelled()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"AndroidTool-{Guid.NewGuid():N}.pid");
        using var cancellation = new CancellationTokenSource();
        int? processId = null;

        try
        {
            var escapedPath = pidFile.Replace("'", "''", StringComparison.Ordinal);
            var command = $"$PID | Set-Content -LiteralPath '{escapedPath}'; Start-Sleep -Seconds 30";
            var runTask = new ProcessRunner().RunAsync(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", command],
                cancellation.Token);

            Assert.True(await WaitUntilAsync(() => File.Exists(pidFile), TimeSpan.FromSeconds(5)));
            processId = int.Parse(await File.ReadAllTextAsync(pidFile), CultureInfo.InvariantCulture);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            Assert.True(await WaitUntilAsync(() => !IsRunning(processId.Value), TimeSpan.FromSeconds(2)));
        }
        finally
        {
            cancellation.Cancel();
            if (processId is int id && IsRunning(id))
            {
                using var process = Process.GetProcessById(id);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            if (File.Exists(pidFile)) File.Delete(pidFile);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
