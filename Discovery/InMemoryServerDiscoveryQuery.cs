namespace UltraSim.Discovery;

public sealed class InMemoryServerDiscoveryQuery : IServerDiscoveryQuery
{
    private readonly Func<IReadOnlyList<ServerDiscoveryRow>> _snapshotFactory;

    public InMemoryServerDiscoveryQuery(IReadOnlyList<ServerDiscoveryRow> rows)
        : this(() => rows)
    {
    }

    public InMemoryServerDiscoveryQuery(Func<IReadOnlyList<ServerDiscoveryRow>> snapshotFactory)
    {
        _snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
    }

    public ValueTask<IReadOnlyList<ServerDiscoveryRow>> QueryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_snapshotFactory());
    }
}
