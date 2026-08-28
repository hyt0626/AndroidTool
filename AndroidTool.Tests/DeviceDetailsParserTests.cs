using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class DeviceDetailsParserTests
{
    [Fact]
    public void ParsesIpAndDataStorage()
    {
        var details = DeviceDetailsParser.Parse(
            "1.1.1.1 via 192.168.1.1 dev wlan0 src 192.168.1.8 uid 2000",
            "/dev/block/data 128G 32G 96G 25% /data");

        Assert.Equal("192.168.1.8", details.IpAddress);
        Assert.Equal("32G / 128G（可用 96G）", details.StorageDisplay);
    }
}
