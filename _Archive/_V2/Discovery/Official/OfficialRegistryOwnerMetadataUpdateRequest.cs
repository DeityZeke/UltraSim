namespace UltraSim.Discovery.Official;

public sealed record OfficialRegistryOwnerMetadataUpdateRequest(
    string OwnerAccountId,
    string DisplayName,
    string TargetHost,
    int TargetPort,
    string? Description = null,
    string? WebsiteUrl = null,
    string? DiscordUrl = null);
