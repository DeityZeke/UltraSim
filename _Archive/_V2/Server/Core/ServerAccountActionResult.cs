namespace UltraSim.Server;

public readonly record struct ServerAccountActionResult(
    bool Succeeded,
    string AccountId,
    string Message)
{
    public static ServerAccountActionResult Success(string accountId, string message) =>
        new(true, accountId, message);

    public static ServerAccountActionResult Reject(string accountId, string message) =>
        new(false, accountId, message);
}
