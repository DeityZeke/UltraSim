namespace UltraSim.Discovery.Official
{
    public sealed record OfficialRegistryHeartbeatRequest(
        string HeartbeatToken,
        ServerDiscoveryStatusInfo StatusInfo,
        ServerAdvertisedInfo? AdvertisedInfo = null,
        string? OwnershipTokenHashHint = null);
}
