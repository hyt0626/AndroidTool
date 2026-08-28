using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class DeviceInfoParserTests
{
    [Fact]
    public void ParsesDevicePropertiesAndBatteryLevel()
    {
        var parser = new DeviceInfoParser();
        var info = parser.Parse(
            serial: "ABC123",
            brand: "Google",
            model: "Pixel 7",
            androidVersion: "14",
            batteryOutput: "  level: 87\n  status: 2");

        Assert.Equal("ABC123", info.Serial);
        Assert.Equal("Google", info.Brand);
        Assert.Equal("Pixel 7", info.Model);
        Assert.Equal("14", info.AndroidVersion);
        Assert.Equal(87, info.BatteryPercent);
    }

    [Fact]
    public void UsesNullBatteryWhenOutputHasNoLevel()
    {
        var info = new DeviceInfoParser().Parse("ABC", "", "", "", "");

        Assert.Null(info.BatteryPercent);
    }
}
