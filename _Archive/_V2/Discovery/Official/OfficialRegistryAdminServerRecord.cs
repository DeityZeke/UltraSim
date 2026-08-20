namespace UltraSim.Discovery.Official;

public sealed record OfficialRegistryAdminServerRecord(
    ServerDiscoveryRow Row,
    string RegistrationId,
    string? OwnerAccountId = null,
    DateTimeOffset CreatedAtUtc = default,
    DateTimeOffset LastRegisterUtc = default,
    DateTimeOffset? LastSuccessfulPollUtc = null,
    DateTimeOffset? LastFailedPollUtc = null,
    DateTimeOffset? OwnershipClaimRequestedUtc = null,
    string? OwnershipVerificationError = null,
    DateTimeOffset? VerifiedAtUtc = null);
