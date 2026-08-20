namespace UltraSim.Server.Scripting;

public sealed class UltraSimScriptCommandContext
{
    private readonly Action<string>? _reply;

    public UltraSimScriptCommandContext(
        string rawText,
        string rawArguments,
        IReadOnlyList<string> arguments,
        int accessLevel,
        long sessionId,
        int connectionId,
        string senderName,
        Action<string>? reply)
    {
        RawText = rawText ?? string.Empty;
        RawArguments = rawArguments ?? string.Empty;
        Arguments = arguments ?? Array.Empty<string>();
        AccessLevel = accessLevel;
        SessionId = sessionId;
        ConnectionId = connectionId;
        SenderName = senderName ?? string.Empty;
        _reply = reply;
    }

    public string RawText { get; }
    public string RawArguments { get; }
    public IReadOnlyList<string> Arguments { get; }
    public int AccessLevel { get; }
    public long SessionId { get; }
    public int ConnectionId { get; }
    public string SenderName { get; }

    public void Reply(string message)
    {
        _reply?.Invoke(message);
    }
}
