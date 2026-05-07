using UltraSim.Discovery;

namespace UltraSim.Client;

public sealed class ClientSavedServerList
{
    private readonly Dictionary<string, ClientSavedServerEntry> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ClientSavedServerEntry> _history = new(StringComparer.OrdinalIgnoreCase);

    public ClientSavedServerList(
        IEnumerable<ClientSavedServerEntry>? favorites = null,
        IEnumerable<ClientSavedServerEntry>? history = null)
    {
        if (favorites is not null)
        {
            foreach (ClientSavedServerEntry entry in favorites)
                UpsertFavorite(entry);
        }

        if (history is not null)
        {
            foreach (ClientSavedServerEntry entry in history)
                RecordHistory(entry);
        }
    }

    public IReadOnlyList<ClientSavedServerEntry> Favorites => SortByName(_favorites.Values);
    public IReadOnlyList<ClientSavedServerEntry> History => SortByLastSeen(_history.Values);

    public bool UpsertFavorite(ClientSavedServerEntry entry)
    {
        if (!TryNormalize(entry, out ClientSavedServerEntry normalized))
            return false;

        _favorites[CreateKey(normalized.Host, normalized.Port)] = normalized;
        return true;
    }

    public bool RemoveFavorite(string host, int port)
    {
        if (!TryCreateKey(host, port, out string key))
            return false;

        return _favorites.Remove(key);
    }

    public bool RecordHistory(ClientSavedServerEntry entry)
    {
        if (!TryNormalize(entry, out ClientSavedServerEntry normalized))
            return false;

        _history[CreateKey(normalized.Host, normalized.Port)] = normalized with
        {
            LastSeenUtc = normalized.LastSeenUtc ?? DateTimeOffset.UtcNow,
        };
        return true;
    }

    public void ClearHistory() => _history.Clear();

    public ServerBrowserProviderDefinition CreateFavoritesProvider(
        string providerId = "favorites",
        string displayName = "Favorites")
    {
        return new ServerBrowserProviderDefinition(
            providerId,
            displayName,
            ServerDiscoveryProviderKind.Favorites,
            new InMemoryServerDiscoveryQuery(() => BuildRows(
                Favorites,
                providerId,
                ServerDiscoveryProviderKind.Favorites,
                isFavorite: true)));
    }

    public ServerBrowserProviderDefinition CreateHistoryProvider(
        string providerId = "history",
        string displayName = "History")
    {
        return new ServerBrowserProviderDefinition(
            providerId,
            displayName,
            ServerDiscoveryProviderKind.History,
            new InMemoryServerDiscoveryQuery(() => BuildRows(
                History,
                providerId,
                ServerDiscoveryProviderKind.History,
                isFavorite: false)));
    }

    private static IReadOnlyList<ServerDiscoveryRow> BuildRows(
        IReadOnlyList<ClientSavedServerEntry> entries,
        string providerId,
        ServerDiscoveryProviderKind providerKind,
        bool isFavorite)
    {
        if (entries.Count == 0)
            return Array.Empty<ServerDiscoveryRow>();

        var rows = new List<ServerDiscoveryRow>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            ClientSavedServerEntry entry = entries[i];
            rows.Add(new ServerDiscoveryRow(
                ProviderId: providerId,
                ProviderKind: providerKind,
                ServerId: entry.ServerId,
                DisplayName: entry.DisplayName,
                Host: entry.Host,
                Port: entry.Port,
                HealthState: ServerListingHealthState.Stale,
                Region: entry.Region,
                PrimaryLanguage: entry.PrimaryLanguage,
                IsFavorite: isFavorite,
                CompatibilityId: entry.CompatibilityId,
                Tags: entry.Tags,
                LastSeenUtc: entry.LastSeenUtc));
        }

        return rows;
    }

    private static bool TryNormalize(ClientSavedServerEntry entry, out ClientSavedServerEntry normalized)
    {
        normalized = entry;
        if (!TryCreateKey(entry.Host, entry.Port, out _))
            return false;

        string host = entry.Host.Trim();
        string serverId = string.IsNullOrWhiteSpace(entry.ServerId)
            ? $"{host}:{entry.Port}"
            : entry.ServerId.Trim();
        string displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? $"{host}:{entry.Port}"
            : entry.DisplayName.Trim();

        normalized = entry with
        {
            ServerId = serverId,
            DisplayName = displayName,
            Host = host,
            Tags = entry.TagsOrEmpty.Count == 0 ? null : entry.TagsOrEmpty.ToArray(),
        };
        return true;
    }

    private static bool TryCreateKey(string host, int port, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(host) || port < 0 || port > 65535)
            return false;

        key = CreateKey(host.Trim(), port);
        return true;
    }

    private static string CreateKey(string host, int port)
    {
        return $"{host.Trim().ToUpperInvariant()}:{port}";
    }

    private static IReadOnlyList<ClientSavedServerEntry> SortByName(IEnumerable<ClientSavedServerEntry> entries)
    {
        return entries
            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Port)
            .ToArray();
    }

    private static IReadOnlyList<ClientSavedServerEntry> SortByLastSeen(IEnumerable<ClientSavedServerEntry> entries)
    {
        return entries
            .OrderByDescending(entry => entry.LastSeenUtc ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Port)
            .ToArray();
    }
}
