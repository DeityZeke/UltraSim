namespace UltraSim.Server;

public readonly record struct ServerPlayerSessionActionResult(
    bool Succeeded,
    string SessionId,
    string Message)
{
    public static ServerPlayerSessionActionResult Success(string sessionId, string message) =>
        new(true, sessionId, message);

    public static ServerPlayerSessionActionResult Reject(string sessionId, string message) =>
        new(false, sessionId, message);
}
