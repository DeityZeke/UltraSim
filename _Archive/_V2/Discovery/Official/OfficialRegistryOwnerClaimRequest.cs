namespace UltraSim.Discovery.Official;

public sealed record OfficialRegistryOwnerClaimRequest(
    string OwnerAccountId,
    string DisplayName,
    string TargetHost,
    int TargetPort,
    string OwnershipToken,
    string? Description = null,
    string? WebsiteUrl = null,
    string? DiscordUrl = null,
    string? RequestedServerId = null);
