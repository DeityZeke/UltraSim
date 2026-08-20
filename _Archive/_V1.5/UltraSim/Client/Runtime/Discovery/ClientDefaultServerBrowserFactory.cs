using UltraSim.Discovery;

namespace UltraSim.Client.Runtime.Discovery
{
    public static class ClientDefaultServerBrowserFactory
    {
        public static ClientServerBrowserService CreateDefault(
            out IServerDiscoveryQuery officialQuery,
            out InMemoryServerDiscoveryQuery favoritesQuery,
            out InMemoryServerDiscoveryQuery lanQuery,
            out InMemoryServerDiscoveryQuery manualQuery,
            IServerDiscoveryQuery? officialOverride = null)
        {
            officialQuery = officialOverride ?? new InMemoryServerDiscoveryQuery();
            favoritesQuery = new InMemoryServerDiscoveryQuery();
            lanQuery = new InMemoryServerDiscoveryQuery();
            manualQuery = new InMemoryServerDiscoveryQuery();

            return new ClientServerBrowserService(
                new[]
                {
                    new ServerBrowserProviderDefinition("official", "Official", ServerDiscoveryProviderKind.Official, officialQuery),
                    new ServerBrowserProviderDefinition("favorites", "Favorites", ServerDiscoveryProviderKind.Favorites, favoritesQuery),
                    new ServerBrowserProviderDefinition("lan", "LAN", ServerDiscoveryProviderKind.Lan, lanQuery),
                    new ServerBrowserProviderDefinition("manual", "Manual", ServerDiscoveryProviderKind.Manual, manualQuery),
                });
        }
    }
}
