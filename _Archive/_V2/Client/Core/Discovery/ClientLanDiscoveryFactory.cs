using UltraSim.Discovery;

namespace UltraSim.Client;

public static class ClientLanDiscoveryFactory
{
    public static ServerBrowserProviderDefinition CreateProvider(ClientLanDiscoveryOptions? options = null)
    {
        options ??= new ClientLanDiscoveryOptions();
        return new ServerBrowserProviderDefinition(
            options.ProviderId,
            options.DisplayName,
            ServerDiscoveryProviderKind.Lan,
            new ClientLanDiscoveryQuery(options));
    }
}
