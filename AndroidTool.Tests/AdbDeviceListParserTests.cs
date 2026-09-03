using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class AdbDeviceListParserTests
{
    [Theory]
    [InlineData("List of devices attached\r\nSERIAL-A\tdevice product:foo model:Pixel\r\n", "SERIAL-A")]
    [InlineData("List of devices attached\nSERIAL-A\toffline\nSERIAL-B\tdevice\n", "SERIAL-B")]
    [InlineData("List of devices attached\nSERIAL-A\tunauthorized\n", null)]
    [InlineData("List of devices attached\nSERIAL-A\tdevice\nSERIAL-B\tdevice\n", null)]
    public void FindsSerialOnlyWhenExactlyOneDeviceIsOnline(string output, string? expected)
    {
        Assert.Equal(expected, AdbDeviceListParser.ParseSingleOnlineSerial(output));
    }
}
