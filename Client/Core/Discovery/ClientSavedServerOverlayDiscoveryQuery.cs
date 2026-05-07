using UltraSim.Discovery;

namespace UltraSim.Client;

public sealed class ClientSavedServerOverlayDiscoveryQuery : IServerDiscoveryQuery
{
    private readonly ClientSavedServerList _savedServers;
    private readonly IServerDiscoveryQuery _inner;

    public ClientSavedServerOverlayDiscoveryQuery(ClientSavedServerList savedServers, IServerDiscoveryQuery inner)
    {
        _savedServers = savedServers ?? throw new ArgumentNullException(nameof(savedServers));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async ValueTask<IReadOnlyList<ServerDiscoveryRow>> QueryAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ServerDiscoveryRow> rows = await _inner.QueryAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
            return rows;

        ServerDiscoveryRow[] overlaid = new ServerDiscoveryRow[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            ServerDiscoveryRow row = rows[i];
            overlaid[i] = row.IsFavorite || !_savedServers.IsFavorite(row.Host, row.Port)
                ? row
                : row with { IsFavorite = true };
        }

        return overlaid;
    }
}
