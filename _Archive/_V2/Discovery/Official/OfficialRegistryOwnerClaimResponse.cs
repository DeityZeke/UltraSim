namespace UltraSim.Discovery.Official;

public sealed record OfficialRegistryOwnerClaimResponse(
    OfficialRegistryOwnerClaimState State,
    string? ServerId,
    string? RegistrationId,
    string? Message,
    ServerListingTrustLevel? TrustLevel = null,
    string? ConnectHint = null);
