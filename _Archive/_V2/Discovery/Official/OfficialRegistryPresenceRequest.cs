namespace UltraSim.Discovery.Official
{
    public sealed record OfficialRegistryPresenceRequest(
        string HeartbeatToken,
        OfficialRegistryPresenceSignalKind Signal,
        ServerDiscoveryStatusInfo? StatusInfo = null,
        ServerAdvertisedInfo? AdvertisedInfo = null,
        string? OwnershipTokenHashHint = null);
}
