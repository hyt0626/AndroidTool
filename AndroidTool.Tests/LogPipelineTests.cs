using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class LogPipelineTests
{
    [Fact]
    public void PendingQueueStaysBoundedDuringLargeBurst()
    {
        var queue = new BoundedLogQueue(maxLines: 10_000, maxLineLength: 16_384);

        for (var index = 0; index < 100_000; index++) queue.Enqueue($"line-{index}");
        var batch = queue.Drain(1_000);

        Assert.Equal(9_000, queue.Count);
        Assert.Equal(1_000, batch.Lines.Count);
        Assert.Equal(90_000, batch.TotalDroppedLines);
        Assert.Equal("line-90000", batch.Lines[0]);
    }

    [Fact]
    public void PendingQueueAlsoStaysBoundedByTotalCharacters()
    {
        var queue = new BoundedLogQueue(maxLines: 100, maxLineLength: 100, maxCharacters: 20);

        for (var index = 0; index < 10; index++) queue.Enqueue(new string((char)('a' + index), 10));
        Assert.Equal(2, queue.Count);
        Assert.True(queue.CharacterCount <= 20);
        var batch = queue.Drain(100);

        Assert.Equal(2, batch.Lines.Count);
        Assert.Equal(8, batch.TotalDroppedLines);
    }

    [Fact]
    public async Task PendingQueueRemainsConsistentWithConcurrentProducers()
    {
        var queue = new BoundedLogQueue(maxLines: 10_000, maxLineLength: 1_000);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(producer => Task.Run(() =>
        {
            for (var index = 0; index < 25_000; index++) queue.Enqueue($"{producer}:{index}");
        })));

        var batch = queue.Drain(10_000);
        Assert.Equal(10_000, batch.Lines.Count);
        Assert.Equal(90_000, batch.TotalDroppedLines);
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.CharacterCount);
    }

    [Fact]
    public void QueueTruncatesAbnormallyLongLineAndRemovesNullCharacters()
    {
        var queue = new BoundedLogQueue(maxLines: 10, maxLineLength: 8);
        queue.Enqueue("abc\0defghijklmnop");

        var line = queue.Drain(1).Lines.Single();

        Assert.DoesNotContain('\0', line);
        Assert.EndsWith("…", line);
        Assert.True(line.Length <= 9);
    }

    [Fact]
    public void DisplayBufferKeepsLineAndCharacterLimits()
    {
        var display = new BoundedDisplayBuffer(maxLines: 5, maxCharacters: 20);
        display.AddRange(["1111", "2222", "3333", "4444", "5555", "6666"]);

        Assert.True(display.LineCount <= 5);
        Assert.True(display.CharacterCount <= 20);
        Assert.DoesNotContain("1111", display.Text);
        Assert.Contains("6666", display.Text);
    }

    [Fact]
    public void DisplayBufferTrimsToLowWatermarkInsteadOfOnEveryFollowingLine()
    {
        var display = new BoundedDisplayBuffer(maxLines: 10, maxCharacters: 1_000);
        display.AddRange(Enumerable.Range(0, 11).Select(index => $"line-{index}"));

        Assert.Equal(8, display.LineCount);
        Assert.False(display.AddRange(["next-line"]));
        Assert.Equal(9, display.LineCount);
    }

    [Fact]
    public void SessionGateRejectsLinesFromPreviousSession()
    {
        var gate = new LogSessionGate();
        var oldSession = gate.Begin();
        var currentSession = gate.Begin();

        Assert.False(gate.IsCurrent(oldSession));
        Assert.True(gate.IsCurrent(currentSession));
    }

    [Fact]
    public async Task NoCaptureStreamingDeliversLinesWithoutRetainingStandardOutput()
    {
        var lines = 0;
        var result = await new ProcessRunner().RunStreamingNoCaptureAsync(
            "cmd.exe",
            ["/d", "/c", "for /L %i in (1,1,2000) do @echo line"],
            _ => Interlocked.Increment(ref lines));

        Assert.True(result.Succeeded);
        Assert.Equal(2_000, lines);
        Assert.Empty(result.StandardOutput);
        Assert.True(result.StandardError.Length < 32_768);
    }

    [Fact]
    public void UiBatchUsesIncrementalLinesUntilDisplayHistoryIsTrimmed()
    {
        var pipeline = new LogPipeline(pendingLines: 10, maxLineLength: 100, displayLines: 3, displayCharacters: 1_000);
        pipeline.Enqueue("one");
        pipeline.Enqueue("two");

        var first = pipeline.DrainUiBatch(10);

        Assert.Equal(["one", "two"], first.Lines);
        Assert.Null(first.ReplacementText);

        pipeline.Enqueue("three");
        pipeline.Enqueue("four");
        var second = pipeline.DrainUiBatch(10);

        Assert.Equal(["three", "four"], second.Lines);
        Assert.Equal(string.Join(Environment.NewLine, new[] { "three", "four" }), second.ReplacementText);
    }

    [Fact]
    public async Task NoCaptureStreamingStopsPromptlyWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await new ProcessRunner().RunStreamingNoCaptureAsync(
            "cmd.exe",
            ["/d", "/c", "ping -t 127.0.0.1"],
            _ => { },
            cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task NoCaptureStreamingDoesNotPostLineContinuationsToCallingContext()
    {
        var previous = SynchronizationContext.Current;
        var context = new CountingSynchronizationContext();
        Task<ProcessResult> task;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            task = new ProcessRunner().RunStreamingNoCaptureAsync(
                "cmd.exe",
                ["/d", "/c", "echo first & ping -n 2 127.0.0.1 >nul & echo second"],
                _ => { });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        Assert.Equal(0, context.PostCount);
    }

    [Theory]
    [InlineData(false, "logcat", "-T", "1", "-v", "time")]
    [InlineData(true, "logcat", "-T", "1", "-v", "time", "-s", "Unity")]
    public void LogcatStartsNearCurrentTime(bool unityOnly, params string[] expected)
    {
        Assert.Equal(expected, AdbClient.BuildLogArguments(unityOnly));
    }


    private sealed class CountingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }
}
