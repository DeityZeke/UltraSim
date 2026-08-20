namespace UltraSim.Server.Network;

public readonly record struct ServerChatMetricsSnapshot(
    long ReceivedMessages,
    long PublishedMessages,
    long CommandDispatches,
    long RateLimitedMessages,
    long RejectedMessages);
