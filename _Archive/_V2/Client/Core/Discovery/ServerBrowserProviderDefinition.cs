using UltraSim.Discovery;

namespace UltraSim.Client;

public sealed class ServerBrowserProviderDefinition
{
    public ServerBrowserProviderDefinition(
        string providerId,
        string displayName,
        ServerDiscoveryProviderKind providerKind,
        IServerDiscoveryQuery query)
    {
        ProviderId = string.IsNullOrWhiteSpace(providerId)
            ? throw new ArgumentException("Provider id is required.", nameof(providerId))
            : providerId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("Display name is required.", nameof(displayName))
            : displayName.Trim();
        ProviderKind = providerKind;
        Query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public string ProviderId { get; }
    public string DisplayName { get; }
    public ServerDiscoveryProviderKind ProviderKind { get; }
    public IServerDiscoveryQuery Query { get; }
}
