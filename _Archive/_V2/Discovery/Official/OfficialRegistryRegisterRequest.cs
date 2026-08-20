using System;

namespace UltraSim.Discovery.Official
{
    public sealed record OfficialRegistryRegisterRequest(
        DiscoveryPublishMode PublishMode,
        ServerAdvertisedInfo AdvertisedInfo,
        ServerDiscoveryStatusInfo StatusInfo,
        string? PublishToken = null,
        string? OwnershipTokenHashHint = null);
}
