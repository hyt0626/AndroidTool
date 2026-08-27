using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class ApkInfoParserTests
{
    [Fact]
    public void ParsesCommonAaptBadgingFields()
    {
        const string output = "package: name='com.example.game' versionCode='42' versionName='1.2.3' sdkVersion:'23' targetSdkVersion:'34'\napplication-label:'示例游戏'\napplication-icon-xxx:'res/mipmap/icon.png'\nlaunchable-activity: name='com.example.game.MainActivity'  label='' icon=''";

        var info = new ApkInfoParser().Parse("demo.apk", output);

        Assert.Equal("demo.apk", info.FileName);
        Assert.Equal("com.example.game", info.PackageName);
        Assert.Equal("1.2.3", info.VersionName);
        Assert.Equal("42", info.VersionCode);
        Assert.Equal("示例游戏", info.ApplicationName);
        Assert.Equal("23", info.MinSdk);
        Assert.Equal("34", info.TargetSdk);
        Assert.Equal("com.example.game.MainActivity", info.LaunchableActivity);
    }
}
