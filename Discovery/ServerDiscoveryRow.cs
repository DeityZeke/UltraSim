namespace UltraSim.Discovery;

public sealed record ServerDiscoveryRow(
    string ProviderId,
    ServerDiscoveryProviderKind ProviderKind,
    string ServerId,
    string DisplayName,
    string Host,
    int Port,
    ServerListingTrustLevel TrustLevel = ServerListingTrustLevel.Unverified,
    ServerListingHealthState HealthState = ServerListingHealthState.Online,
    ConnectTargetKind ConnectTargetKind = ConnectTargetKind.Direct,
    string? Region = null,
    string? PrimaryLanguage = null,
    int? CurrentUsers = null,
    int? MaxUsers = null,
    int? PingMs = null,
    bool IsFavorite = false,
    string? ConnectHint = null,
    string? CompatibilityId = null,
    string? VersionLabel = null,
    IReadOnlyList<string>? Tags = null,
    ServerCapabilityFlags CapabilityFlags = ServerCapabilityFlags.DirectConnect,
    DateTimeOffset? LastSeenUtc = null)
{
    public IReadOnlyList<string> TagsOrEmpty => Tags ?? Array.Empty<string>();
}
