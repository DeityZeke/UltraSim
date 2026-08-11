namespace UltraSim.Server.Network;

public readonly record struct ServerChatSenderContext(
    long SessionId,
    int ConnectionId,
    ulong EntityId,
    string DisplayName,
    int AccessLevel = 0,
    IReadOnlyList<string>? AccessRuleKeys = null,
    IReadOnlyList<string>? DirectPermissionGrants = null,
    IReadOnlyList<string>? DirectPermissionDenies = null);
