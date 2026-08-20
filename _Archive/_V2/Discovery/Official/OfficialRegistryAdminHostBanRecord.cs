namespace UltraSim.Discovery.Official;

public sealed record OfficialRegistryAdminHostBanRecord(
    string Host,
    string? Reason = null,
    DateTimeOffset IssuedAtUtc = default,
    DateTimeOffset? LastAppliedAtUtc = null);
