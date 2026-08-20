using System;
using UltraSim.Discovery;

namespace UltraSim.Client.Runtime.Discovery
{
    public sealed class ServerBrowserProviderDefinition
    {
        public ServerBrowserProviderDefinition(
            string providerId,
            string displayName,
            ServerDiscoveryProviderKind providerKind,
            IServerDiscoveryQuery query)
        {
            ProviderId = string.IsNullOrWhiteSpace(providerId)
                ? throw new ArgumentException("ProviderId is required.", nameof(providerId))
                : providerId;
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? throw new ArgumentException("DisplayName is required.", nameof(displayName))
                : displayName;
            ProviderKind = providerKind;
            Query = query ?? throw new ArgumentNullException(nameof(query));
        }

        public string ProviderId { get; }
        public string DisplayName { get; }
        public ServerDiscoveryProviderKind ProviderKind { get; }
        public IServerDiscoveryQuery Query { get; }
    }
}
