using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class ConcurrentTaskRunnerTests
{
    [Fact]
    public async Task NeverExceedsConfiguredConcurrency()
    {
        var active = 0;
        var maximum = 0;
        var runner = new ConcurrentTaskRunner(3);
        var jobs = Enumerable.Range(0, 9).Select(_ => (Func<Task>)(async () =>
        {
            var now = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, now);
            await Task.Delay(30);
            Interlocked.Decrement(ref active);
        }));

        await runner.RunAsync(jobs);

        Assert.Equal(3, maximum);
    }
}
