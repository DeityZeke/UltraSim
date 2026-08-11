namespace UltraSim.Network;

public sealed class ChunkAoiPolicy
{
    private readonly float[] _ringRadiiMeters;
    private readonly float[] _ringRadiiSquared;

    public ChunkAoiPolicy(float chunkSizeMeters, params float[] ringRadiiMeters)
    {
        if (chunkSizeMeters <= 0f || float.IsNaN(chunkSizeMeters) || float.IsInfinity(chunkSizeMeters))
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));
        ArgumentNullException.ThrowIfNull(ringRadiiMeters);
        if (ringRadiiMeters.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(ringRadiiMeters));

        _ringRadiiMeters = new float[ringRadiiMeters.Length];
        _ringRadiiSquared = new float[ringRadiiMeters.Length];
        float previous = 0f;
        for (int i = 0; i < ringRadiiMeters.Length; i++)
        {
            float radius = ringRadiiMeters[i];
            if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException(nameof(ringRadiiMeters));
            if (i > 0 && radius <= previous)
                throw new ArgumentException("Ring radii must be strictly increasing.", nameof(ringRadiiMeters));

            _ringRadiiMeters[i] = radius;
            _ringRadiiSquared[i] = radius * radius;
            previous = radius;
        }

        ChunkSizeMeters = chunkSizeMeters;
    }

    public float ChunkSizeMeters { get; }

    public int RingCount => _ringRadiiMeters.Length;

    public int CollectKeys(in WorldPosition2 center, ICollection<SubscriptionKey> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return CollectCore(
            center,
            addScope: static (ICollection<SubscriptionKey> target, int ringIndex, in ChunkCoord chunk) =>
            {
                target.Add(ChunkSubscriptionKey.Create(ringIndex, chunk));
            },
            destination);
    }

    public int CollectScopes(in WorldPosition2 center, ICollection<ChunkSubscriptionScope> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return CollectCore(
            center,
            addScope: static (ICollection<ChunkSubscriptionScope> target, int ringIndex, in ChunkCoord chunk) =>
            {
                target.Add(new ChunkSubscriptionScope(
                    ringIndex,
                    chunk,
                    ChunkSubscriptionKey.Create(ringIndex, chunk)));
            },
            destination);
    }

    private int CollectCore<TDestination>(
        in WorldPosition2 center,
        AddScope<TDestination> addScope,
        TDestination destination)
    {
        float outerRadius = _ringRadiiMeters[^1];
        int minX = checked((int)MathF.Floor((center.X - outerRadius) / ChunkSizeMeters));
        int maxX = checked((int)MathF.Floor((center.X + outerRadius) / ChunkSizeMeters));
        int minY = checked((int)MathF.Floor((center.Y - outerRadius) / ChunkSizeMeters));
        int maxY = checked((int)MathF.Floor((center.Y + outerRadius) / ChunkSizeMeters));

        int count = 0;
        for (int chunkY = minY; chunkY <= maxY; chunkY++)
        {
            for (int chunkX = minX; chunkX <= maxX; chunkX++)
            {
                var chunk = new ChunkCoord(chunkX, chunkY);
                int ringIndex = GetRingIndexForChunk(center, chunk);
                if (ringIndex < 0)
                    continue;

                addScope(destination, ringIndex, chunk);
                count++;
            }
        }

        return count;
    }

    private int GetRingIndexForChunk(in WorldPosition2 center, in ChunkCoord chunk)
    {
        float minX = chunk.X * ChunkSizeMeters;
        float minY = chunk.Y * ChunkSizeMeters;
        float maxX = minX + ChunkSizeMeters;
        float maxY = minY + ChunkSizeMeters;

        float dx = 0f;
        if (center.X < minX)
            dx = minX - center.X;
        else if (center.X > maxX)
            dx = center.X - maxX;

        float dy = 0f;
        if (center.Y < minY)
            dy = minY - center.Y;
        else if (center.Y > maxY)
            dy = center.Y - maxY;

        float distanceSquared = dx * dx + dy * dy;
        for (int i = 0; i < _ringRadiiSquared.Length; i++)
        {
            if (distanceSquared <= _ringRadiiSquared[i])
                return i;
        }

        return -1;
    }

    private delegate void AddScope<TDestination>(TDestination destination, int ringIndex, in ChunkCoord chunk);
}
