namespace UltraSim.Discovery
{
    public sealed record ServerDiscoveryPollStatusResponse(
        string? ServerId,
        ServerDiscoveryStatusInfo StatusInfo,
        string? OwnershipTokenHash = null);
}
