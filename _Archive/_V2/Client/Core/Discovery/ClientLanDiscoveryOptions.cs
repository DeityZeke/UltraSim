using System.Net;
using UltraSim.Discovery;

namespace UltraSim.Client;

public sealed class ClientLanDiscoveryOptions
{
    public ClientLanDiscoveryOptions(
        IReadOnlyList<IPEndPoint>? queryEndpoints = null,
        TimeSpan timeout = default,
        int maxResponses = 64,
        IPEndPoint? listenEndPoint = null,
        string providerId = "lan",
        string displayName = "LAN",
        string? compatibilityId = null)
    {
        QueryEndpoints = queryEndpoints ?? [new IPEndPoint(IPAddress.Broadcast, LanDiscoveryDefaults.Port)];
        Timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(750) : timeout;
        MaxResponses = Math.Clamp(maxResponses, 1, 512);
        ListenEndPoint = listenEndPoint ?? new IPEndPoint(IPAddress.Any, 0);
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? "lan" : providerId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "LAN" : displayName.Trim();
        CompatibilityId = string.IsNullOrWhiteSpace(compatibilityId) ? null : compatibilityId.Trim();
    }

    public IReadOnlyList<IPEndPoint> QueryEndpoints { get; }
    public TimeSpan Timeout { get; }
    public int MaxResponses { get; }
    public IPEndPoint ListenEndPoint { get; }
    public string ProviderId { get; }
    public string DisplayName { get; }
    public string? CompatibilityId { get; }
}
