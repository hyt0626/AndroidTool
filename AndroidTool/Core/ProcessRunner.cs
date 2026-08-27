using System.Diagnostics;
using System.IO;

namespace AndroidTool.Core;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public sealed class ProcessRunner
{
    private const int StreamingErrorLimit = 16 * 1024;

    public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    public async Task<ProcessResult> RunStreamingAsync(string fileName, IEnumerable<string> arguments, Action<string>? onLine, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();
        process.Start();

        async Task ReadAsync(StreamReader reader, System.Text.StringBuilder target)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                target.AppendLine(line);
                onLine?.Invoke(line);
            }
        }

        try
        {
            await Task.WhenAll(ReadAsync(process.StandardOutput, output), ReadAsync(process.StandardError, error), process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
        }
        return new ProcessResult(process.HasExited ? process.ExitCode : -1, output.ToString(), error.ToString());
    }

    public async Task<ProcessResult> RunStreamingNoCaptureAsync(string fileName, IEnumerable<string> arguments, Action<string>? onLine, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var boundedError = new System.Text.StringBuilder();
        process.Start();

        async Task ReadOutputAsync()
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                onLine?.Invoke(line);
            }
        }

        async Task ReadErrorAsync()
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                AppendBoundedLine(boundedError, line, StreamingErrorLimit);
                onLine?.Invoke(line);
            }
        }

        var outputReader = ReadOutputAsync();
        var errorReader = ReadErrorAsync();
        using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));

        try
        {
            await Task.WhenAll(outputReader, errorReader, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await WaitForExitAfterCancellationAsync(process).ConfigureAwait(false);
            await IgnoreCancellationAsync(outputReader).ConfigureAwait(false);
            await IgnoreCancellationAsync(errorReader).ConfigureAwait(false);
        }
        catch
        {
            TryKill(process);
            await WaitForExitAfterCancellationAsync(process).ConfigureAwait(false);
            throw;
        }

        return new ProcessResult(process.HasExited ? process.ExitCode : -1, string.Empty, boundedError.ToString());
    }

    private static void AppendBoundedLine(System.Text.StringBuilder target, string line, int limit)
    {
        target.AppendLine(line);
        if (target.Length <= limit) return;
        target.Remove(0, target.Length - limit);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task WaitForExitAfterCancellationAsync(Process process)
    {
        try
        {
            if (!process.HasExited) await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
