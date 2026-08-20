using UltraSim.Discovery;

namespace UltraSim.Client.Runtime.Discovery
{
    public readonly record struct ServerBrowserProviderSnapshot(
        string ProviderId,
        string DisplayName,
        ServerDiscoveryProviderKind ProviderKind,
        bool IsSelected);
}
