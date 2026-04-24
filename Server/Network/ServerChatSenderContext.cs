namespace UltraSim.Server.Network;

public readonly record struct ServerChatSenderContext(
    long SessionId,
    int ConnectionId,
    ulong EntityId,
    string DisplayName,
    int AccessLevel = 0);
