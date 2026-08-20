namespace UltraSim.Discovery;

public interface IServerDiscoveryQuery
{
    ValueTask<IReadOnlyList<ServerDiscoveryRow>> QueryAsync(CancellationToken cancellationToken = default);
}
