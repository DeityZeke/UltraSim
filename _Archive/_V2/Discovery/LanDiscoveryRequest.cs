namespace UltraSim.Discovery;

public sealed record LanDiscoveryRequest(
    string Magic,
    int ProtocolVersion,
    string Nonce,
    string? CompatibilityId = null)
{
    public static LanDiscoveryRequest Create(string nonce, string? compatibilityId = null)
    {
        return new LanDiscoveryRequest(
            LanDiscoveryDefaults.RequestMagic,
            LanDiscoveryDefaults.ProtocolVersion,
            nonce,
            compatibilityId);
    }
}
