using System.Runtime.InteropServices;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public sealed class ServerEcsChunkAoiScopeClassifier
{
    private readonly ChunkAoiPolicy _policy;

    public ServerEcsChunkAoiScopeClassifier(ChunkAoiPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public ChunkAoiPolicy Policy => _policy;

    public int CollectBroadcastKeys(in WorldPosition2 position, ICollection<SubscriptionKey> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        ChunkCoord chunk = GetChunk(position);
        for (int ringIndex = 0; ringIndex < _policy.RingCount; ringIndex++)
            destination.Add(ChunkSubscriptionKey.Create(ringIndex, chunk));

        return _policy.RingCount;
    }

    public int CollectBroadcastScopes(in WorldPosition2 position, ICollection<ChunkSubscriptionScope> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        ChunkCoord chunk = GetChunk(position);
        for (int ringIndex = 0; ringIndex < _policy.RingCount; ringIndex++)
        {
            destination.Add(new ChunkSubscriptionScope(
                ringIndex,
                chunk,
                ChunkSubscriptionKey.Create(ringIndex, chunk)));
        }

        return _policy.RingCount;
    }

    public int CollectBroadcastKeys(in WorldPosition2 position, Span<SubscriptionKey> destination)
    {
        if (destination.Length < _policy.RingCount)
            throw new ArgumentException("Destination is too small for the configured ring count.", nameof(destination));

        ChunkCoord chunk = GetChunk(position);
        for (int ringIndex = 0; ringIndex < _policy.RingCount; ringIndex++)
            destination[ringIndex] = ChunkSubscriptionKey.Create(ringIndex, chunk);

        return _policy.RingCount;
    }

    public ChunkCoord GetChunk(in WorldPosition2 position)
    {
        int chunkX = checked((int)MathF.Floor(position.X / _policy.ChunkSizeMeters));
        int chunkY = checked((int)MathF.Floor(position.Y / _policy.ChunkSizeMeters));
        return new ChunkCoord(chunkX, chunkY);
    }
}
