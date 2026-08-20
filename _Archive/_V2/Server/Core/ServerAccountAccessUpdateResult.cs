namespace UltraSim.Server;

public readonly record struct ServerAccountAccessUpdateResult(
    bool Succeeded,
    string AccountId,
    string Message)
{
    public static ServerAccountAccessUpdateResult Success(string accountId, string message) =>
        new(true, accountId, message);

    public static ServerAccountAccessUpdateResult Reject(string accountId, string message) =>
        new(false, accountId, message);
}
