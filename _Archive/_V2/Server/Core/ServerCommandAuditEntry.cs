namespace UltraSim.Server;

public sealed record ServerCommandAuditEntry(
    DateTimeOffset TimestampUtc,
    ServerCommandSource Source,
    ServerCommandDispatchStatus Status,
    string CommandName,
    string PermissionKey,
    int AccessLevel,
    long SessionId,
    int ConnectionId,
    ulong EntityId,
    string SenderName,
    string TargetContext,
    string RawText,
    string Error);
