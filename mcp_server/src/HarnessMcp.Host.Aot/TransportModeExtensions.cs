using HarnessMcp.Contracts;

namespace HarnessMcp.Host.Aot;

public static class TransportModeExtensions
{
    public static bool IsHttp(this ServerConfig server) =>
        server.TransportMode == TransportMode.Http;

    public static bool IsStdio(this ServerConfig server) =>
        server.TransportMode == TransportMode.Stdio;
}
