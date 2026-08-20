namespace UltraSim.Discovery.Official
{
    public sealed record OfficialRegistryMetadataUpdateRequest(
        string HeartbeatToken,
        ServerAdvertisedInfo AdvertisedInfo);
}
