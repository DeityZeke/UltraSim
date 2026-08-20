namespace UltraSim.Server;

public readonly record struct ServerAccountSummarySnapshot(
    string AccountId,
    string DisplayName,
    bool IsOnline,
    bool IsBanned,
    string? Detail = null,
    int AccessLevel = 0,
    IReadOnlyList<string>? AccessRuleKeys = null,
    IReadOnlyList<string>? DirectPermissionGrants = null,
    IReadOnlyList<string>? DirectPermissionDenies = null,
    DateTimeOffset? CreatedUtc = null,
    DateTimeOffset? LastLoginUtc = null,
    IReadOnlyList<string>? KnownRemoteAddresses = null,
    TimeSpan TotalPlayTime = default)
{
    public IReadOnlyList<string> AccessRules => AccessRuleKeys ?? Array.Empty<string>();

    public IReadOnlyList<string> DirectGrants => DirectPermissionGrants ?? Array.Empty<string>();

    public IReadOnlyList<string> DirectDenies => DirectPermissionDenies ?? Array.Empty<string>();

    public IReadOnlyList<string> KnownRemotes => KnownRemoteAddresses ?? Array.Empty<string>();
}
