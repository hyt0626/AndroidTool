using AndroidTool.Core;
using Xunit;

namespace AndroidTool.Tests;

public sealed class CastUrlTests
{
    [Fact]
    public void BuildsCastNowAddress()
    {
        Assert.Equal("http://192.168.1.8:3342/cast_now", CastUrl.Build("192.168.1.8"));
    }
}
