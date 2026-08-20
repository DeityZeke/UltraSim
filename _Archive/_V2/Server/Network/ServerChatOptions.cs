namespace UltraSim.Server.Network;

public sealed record ServerChatOptions(
    int MaxTextLength = 256,
    long MinTicksBetweenMessages = 8,
    bool EnableCommands = true)
{
    public static ServerChatOptions Default { get; } = new();
}
