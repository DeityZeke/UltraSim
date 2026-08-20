namespace UltraSim.Discovery;

public sealed record ServerDiscoveryRow(
    string ProviderId,
    ServerDiscoveryProviderKind ProviderKind,
    string ServerId,
    string DisplayName,
    string Host,
    int Port,
    string? LeaseId = null,
    ServerListingTrustLevel TrustLevel = ServerListingTrustLevel.Unverified,
    ServerListingHealthState HealthState = ServerListingHealthState.Online,
    ServerListingLifecycleState LifecycleState = ServerListingLifecycleState.Active,
    ServerListingVisibility Visibility = ServerListingVisibility.Public,
    ConnectTargetKind ConnectTargetKind = ConnectTargetKind.Direct,
    string? Region = null,
    string? PrimaryLanguage = null,
    int? CurrentUsers = null,
    int? MaxUsers = null,
    int? PeakUsers = null,
    int? QueueLength = null,
    TimeSpan? Uptime = null,
    bool PasswordRequired = false,
    bool WhitelistOnly = false,
    int? PingMs = null,
    bool IsFavorite = false,
    int VoteCount = 0,
    string? ConnectHint = null,
    string? CompatibilityId = null,
    string? VersionLabel = null,
    IReadOnlyList<string>? Tags = null,
    ServerCapabilityFlags CapabilityFlags = ServerCapabilityFlags.DirectConnect,
    DateTimeOffset? LastSeenUtc = null)
{
    public IReadOnlyList<string> TagsOrEmpty => Tags ?? Array.Empty<string>();
}
