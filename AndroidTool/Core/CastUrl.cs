namespace AndroidTool.Core;

public static class CastUrl
{
    public static string Build(string ipAddress) => $"http://{ipAddress}:3342/cast_now";
}
