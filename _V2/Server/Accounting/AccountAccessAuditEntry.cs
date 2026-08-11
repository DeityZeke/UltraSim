namespace UltraSim.Server.Accounting;

public sealed record AccountAccessAuditEntry(
    DateTimeOffset TimestampUtc,
    ulong? ActorAccountId,
    ulong TargetAccountId,
    string TargetUsername,
    AccountAccessAuditAction Action,
    IReadOnlyList<string> AccessRuleKeys,
    IReadOnlyList<string> DirectPermissionGrants,
    IReadOnlyList<string> DirectPermissionDenies,
    string Reason);
