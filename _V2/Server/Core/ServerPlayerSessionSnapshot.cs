namespace UltraSim.Server;

public readonly record struct ServerPlayerSessionSnapshot(
    string SessionId,
    string PlayerName,
    string AccountId,
    string SessionState,
    string? Detail = null,
    string AccountName = "",
    string RemoteAddress = "",
    bool IsOnline = true,
    DateTimeOffset? ConnectedAtUtc = null,
    TimeSpan CurrentUptime = default,
    TimeSpan TotalPlayTime = default,
    int AccessLevel = 0,
    IReadOnlyList<string>? AccessRuleKeys = null,
    IReadOnlyList<string>? DirectPermissionGrants = null,
    IReadOnlyList<string>? DirectPermissionDenies = null)
{
    public IReadOnlyList<string> AccessRules => AccessRuleKeys ?? Array.Empty<string>();

    public IReadOnlyList<string> DirectGrants => DirectPermissionGrants ?? Array.Empty<string>();

    public IReadOnlyList<string> DirectDenies => DirectPermissionDenies ?? Array.Empty<string>();
}
