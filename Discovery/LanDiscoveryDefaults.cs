namespace UltraSim.Discovery;

public static class LanDiscoveryDefaults
{
    public const int ProtocolVersion = 1;
    public const int Port = 42043;
    public const string RequestMagic = "UltraSim.LanDiscovery.Query";
    public const string ResponseMagic = "UltraSim.LanDiscovery.Response";
    public const int MaxDatagramBytes = 32 * 1024;
}
