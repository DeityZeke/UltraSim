using UltraSim.Discovery;
using UltraSim.Discovery.Official;

namespace UltraSim.Server.Discovery;

public sealed record ServerDiscoveryPublicationPlan(
    Uri RegistryUri,
    DiscoveryPublishMode PublishMode,
    IServerDiscoveryPublisher Publisher,
    ServerDiscoveryAdvertisedSettings AdvertisedSettings,
    ServerDiscoveryPublisherOptions PublisherOptions);
