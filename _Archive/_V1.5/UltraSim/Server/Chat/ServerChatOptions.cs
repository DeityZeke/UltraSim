namespace UltraSim.Server.Chat
{
    public sealed record ServerChatOptions(
        long MinTicksBetweenMessages = 8,
        bool EnableCommands = true,
        bool EnableDenseLocalChatRecipientCap = true,
        int DenseLocalChatCapStartRecipients = 24,
        int DenseLocalChatCapMaxRecipients = 8,
        bool EnableDenseLocalChatRateCap = true,
        int DenseLocalChatRateCapStartRecipients = 24,
        int DenseLocalChatRateCapMinTicks = 60,
        bool EnableBatching = true)
    {
        public static ServerChatOptions Default { get; } = new();
    }
}
