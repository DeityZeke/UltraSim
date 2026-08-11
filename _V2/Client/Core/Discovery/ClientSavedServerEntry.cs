using UltraSim.Discovery;

namespace UltraSim.Client;

public sealed record ClientSavedServerEntry(
    string ServerId,
    string DisplayName,
    string Host,
    int Port,
    string? Region = null,
    string? PrimaryLanguage = null,
    string? CompatibilityId = null,
    IReadOnlyList<string>? Tags = null,
    DateTimeOffset? LastSeenUtc = null)
{
    public IReadOnlyList<string> TagsOrEmpty => Tags ?? Array.Empty<string>();

    public static ClientSavedServerEntry FromRow(ServerDiscoveryRow row, DateTimeOffset? lastSeenUtc = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new ClientSavedServerEntry(
            row.ServerId,
            row.DisplayName,
            row.Host,
            row.Port,
            row.Region,
            row.PrimaryLanguage,
            row.CompatibilityId,
            row.Tags,
            lastSeenUtc ?? row.LastSeenUtc ?? DateTimeOffset.UtcNow);
    }
}
