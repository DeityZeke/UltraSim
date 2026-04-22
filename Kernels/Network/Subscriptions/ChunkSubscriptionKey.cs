namespace UltraSim.Network;

public static class ChunkSubscriptionKey
{
    public static SubscriptionKey Create(int ringIndex, in ChunkCoord chunk)
    {
        if (ringIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(ringIndex));

        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offset;
        hash ^= (uint)ringIndex;
        hash *= prime;
        hash ^= (uint)chunk.X;
        hash *= prime;
        hash ^= (uint)chunk.Y;
        hash *= prime;

        if (hash == 0)
            hash = 1;

        return new SubscriptionKey(hash);
    }
}
