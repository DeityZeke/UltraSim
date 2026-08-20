namespace UltraSim.Server;

public readonly record struct ServerCommandRequest(
    ServerCommandSource Source,
    string RawText,
    int AccessLevel = 0,
    long SessionId = 0,
    int ConnectionId = 0,
    ulong EntityId = 0,
    string SenderName = "",
    string TargetContext = "",
    IReadOnlyList<string>? AccessRuleKeys = null,
    IReadOnlyList<string>? DirectPermissionGrants = null,
    IReadOnlyList<string>? DirectPermissionDenies = null,
    Action<string>? Reply = null);
