using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class RuntimeHelpersTests
{
    [Fact]
    public void BoundedLogKeepsNewestLines()
    {
        var buffer = new BoundedLineBuffer(3);
        buffer.Add("one"); buffer.Add("two"); buffer.Add("three"); buffer.Add("four");

        Assert.Equal(string.Join(Environment.NewLine, "two", "three", "four"), buffer.Text);
    }

    [Theory]
    [InlineData("[ 42%] /sdcard/file", 42)]
    [InlineData("random output", null)]
    public void ParsesAdbPushProgress(string line, int? expected)
    {
        Assert.Equal(expected, AdbProgressParser.TryParsePercent(line));
    }

    [Theory]
    [InlineData("INSTALL_FAILED_VERSION_DOWNGRADE", "版本降级")]
    [InlineData("device unauthorized", "设备未授权")]
    public void ConvertsCommonAdbErrors(string raw, string expectedPart)
    {
        Assert.Contains(expectedPart, AdbErrorTranslator.ToUserMessage(raw));
    }
}
