namespace UltraSim.Network;

public readonly record struct ChunkCoord(int X, int Y)
{
    public static ChunkCoord FromWorldPosition(in WorldPosition2 position, float chunkSizeMeters)
    {
        if (chunkSizeMeters <= 0f || float.IsNaN(chunkSizeMeters) || float.IsInfinity(chunkSizeMeters))
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));

        return new ChunkCoord(
            FloorDiv(position.X, chunkSizeMeters),
            FloorDiv(position.Y, chunkSizeMeters));
    }

    private static int FloorDiv(float value, float divisor) =>
        checked((int)MathF.Floor(value / divisor));
}
