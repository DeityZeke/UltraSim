using UltraSim.Discovery;

namespace UltraSim.Client;

public readonly record struct ServerBrowserProviderSnapshot(
    string ProviderId,
    string DisplayName,
    ServerDiscoveryProviderKind ProviderKind,
    bool IsSelected);
