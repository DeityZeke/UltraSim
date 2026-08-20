namespace UltraSim.Discovery;

public sealed record LanDiscoveryResponse(
    string Magic,
    int ProtocolVersion,
    string Nonce,
    ServerDiscoveryRow Row)
{
    public static LanDiscoveryResponse Create(string nonce, ServerDiscoveryRow row)
    {
        return new LanDiscoveryResponse(
            LanDiscoveryDefaults.ResponseMagic,
            LanDiscoveryDefaults.ProtocolVersion,
            nonce,
            row);
    }
}
