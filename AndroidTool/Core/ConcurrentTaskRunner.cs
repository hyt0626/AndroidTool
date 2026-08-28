namespace AndroidTool.Core;

public sealed class ConcurrentTaskRunner
{
    private readonly int _maximumConcurrency;

    public ConcurrentTaskRunner(int maximumConcurrency)
    {
        if (maximumConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        _maximumConcurrency = maximumConcurrency;
    }

    public async Task RunAsync(IEnumerable<Func<Task>> jobs, CancellationToken cancellationToken = default)
    {
        using var gate = new SemaphoreSlim(_maximumConcurrency);
        var tasks = jobs.Select(async job =>
        {
            await gate.WaitAsync(cancellationToken);
            try { await job(); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }
}
