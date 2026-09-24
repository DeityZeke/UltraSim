using UltraSim.Spatial;

namespace UltraSim.Game.World;

public enum WorldStaticQueryKind : byte
{
    FrozenStatic = 1,
    MultiFragment = 2,
}

public enum WorldStaticQueryError
{
    None = 0,
    InvalidBounds,
    CandidateLimitExceeded,
    DestinationTooSmall,
}

/// <summary>One immutable static-geometry overlap. Only fields matching <see cref="Kind"/> are valid.</summary>
public readonly record struct WorldStaticQueryHit(
    WorldStaticQueryKind Kind,
    WorldStaticId StaticId,
    MultiInstanceId MultiId,
    MultiPieceId MultiPieceId,
    WorldStaticDefinitionId DefinitionId,
    WorldPosition Position,
    WorldYaw Orientation,
    ushort Variant,
    WorldStaticFlags Flags,
    WorldStaticBounds Bounds)
{
    internal static WorldStaticQueryHit From(in WorldFrozenStaticRecord record) => new(
        WorldStaticQueryKind.FrozenStatic, record.Id, default, default, record.DefinitionId,
        record.Position, record.Orientation, record.Variant, record.Flags, record.Bounds);

    internal static WorldStaticQueryHit From(in WorldMultiFragmentRecord record) => new(
        WorldStaticQueryKind.MultiFragment, default, record.MultiId, record.PieceId, record.DefinitionId,
        record.Position, record.Orientation, record.Variant, record.Flags, record.Bounds);
}

/// <summary>Bounded exact queries over one immutable pack's frozen-static and static-Multi geometry.</summary>
public static class WorldStaticQueries
{
    /// <summary>Global bounds query routed by each candidate chunk's structural Pack owner.</summary>
    public static bool TryQueryStaticBounds(
        this PreparedWorldComposition world,
        in WorldStaticBounds bounds,
        int maximumCandidateChunks,
        Span<WorldStaticQueryHit> destination,
        out int count,
        out int required,
        out WorldStaticQueryError error)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidateChunks);
        count = 0;
        required = 0;
        if (!bounds.IsValid)
        {
            error = WorldStaticQueryError.InvalidBounds;
            return false;
        }
        if (!TryGetCandidateRange(bounds, maximumCandidateChunks,
                out int minimumX, out int minimumZ, out int maximumX, out int maximumZ))
        {
            error = WorldStaticQueryError.CandidateLimitExceeded;
            return false;
        }
        required = CountMatches(world, bounds, minimumX, minimumZ, maximumX, maximumZ);
        if (destination.Length < required)
        {
            error = WorldStaticQueryError.DestinationTooSmall;
            return false;
        }
        int written = 0;
        for (long x = minimumX; x <= maximumX; x++)
        {
            for (long z = minimumZ; z <= maximumZ; z++)
            {
                var key = new SpatialChunkKey(world.Space, checked((int)x), checked((int)z));
                if (world.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics))
                {
                    for (int index = 0; index < statics.Count; index++)
                    {
                        WorldFrozenStaticRecord record = statics.GetRecord(index);
                        if (record.Bounds.Intersects(bounds)) destination[written++] = WorldStaticQueryHit.From(record);
                    }
                }
                if (world.TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments))
                {
                    for (int index = 0; index < fragments.Count; index++)
                    {
                        WorldMultiFragmentRecord record = fragments.GetRecord(index);
                        if (record.Bounds.Intersects(bounds)) destination[written++] = WorldStaticQueryHit.From(record);
                    }
                }
            }
        }
        count = written;
        error = WorldStaticQueryError.None;
        return true;
    }

    public static bool TryQueryStaticBounds(
        this WorldPreparedView world,
        in WorldStaticBounds bounds,
        int maximumCandidateChunks,
        Span<WorldStaticQueryHit> destination,
        out int count,
        out int required,
        out WorldStaticQueryError error)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidateChunks);
        count = 0;
        required = 0;
        if (!bounds.IsValid)
        {
            error = WorldStaticQueryError.InvalidBounds;
            return false;
        }
        if (!TryGetCandidateRange(bounds, maximumCandidateChunks,
                out int minimumX, out int minimumZ, out int maximumX, out int maximumZ))
        {
            error = WorldStaticQueryError.CandidateLimitExceeded;
            return false;
        }
        required = CountMatches(world, bounds, minimumX, minimumZ, maximumX, maximumZ);
        if (destination.Length < required)
        {
            error = WorldStaticQueryError.DestinationTooSmall;
            return false;
        }
        int written = 0;
        for (long x = minimumX; x <= maximumX; x++)
        {
            for (long z = minimumZ; z <= maximumZ; z++)
            {
                var key = new SpatialChunkKey(world.Space, checked((int)x), checked((int)z));
                if (world.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics))
                {
                    for (int index = 0; index < statics.Count; index++)
                    {
                        WorldFrozenStaticRecord record = statics.GetRecord(index);
                        if (record.Bounds.Intersects(bounds)) destination[written++] = WorldStaticQueryHit.From(record);
                    }
                }
                if (world.TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments))
                {
                    for (int index = 0; index < fragments.Count; index++)
                    {
                        WorldMultiFragmentRecord record = fragments.GetRecord(index);
                        if (record.Bounds.Intersects(bounds)) destination[written++] = WorldStaticQueryHit.From(record);
                    }
                }
            }
        }
        count = written;
        error = WorldStaticQueryError.None;
        return true;
    }

    public static bool TryQueryStaticBounds(
        this WorldPack pack,
        in WorldStaticBounds bounds,
        int maximumCandidateChunks,
        Span<WorldStaticQueryHit> destination,
        out int count,
        out int required,
        out WorldStaticQueryError error)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidateChunks);
        count = 0;
        required = 0;
        if (!bounds.IsValid)
        {
            error = WorldStaticQueryError.InvalidBounds;
            return false;
        }
        if (pack.FrozenStaticCount == 0 && pack.MultiFragmentCount == 0)
        {
            error = WorldStaticQueryError.None;
            return true;
        }

        if (!TryGetCandidateRange(bounds, maximumCandidateChunks,
                out int minimumX, out int minimumZ, out int maximumX, out int maximumZ))
        {
            error = WorldStaticQueryError.CandidateLimitExceeded;
            return false;
        }

        required = CountMatches(pack, bounds, minimumX, minimumZ, maximumX, maximumZ);
        if (destination.Length < required)
        {
            error = WorldStaticQueryError.DestinationTooSmall;
            return false;
        }

        int written = 0;
        for (long x = minimumX; x <= maximumX; x++)
        {
            for (long z = minimumZ; z <= maximumZ; z++)
            {
                var key = new SpatialChunkKey(pack.Space, checked((int)x), checked((int)z));
                if (pack.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics))
                {
                    for (int index = 0; index < statics.Count; index++)
                    {
                        WorldFrozenStaticRecord record = statics.GetRecord(index);
                        if (record.Bounds.Intersects(bounds)) destination[written++] = WorldStaticQueryHit.From(record);
                    }
                }
                if (pack.TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments))
                {
                    for (int index = 0; index < fragments.Count; index++)
                    {
                        WorldMultiFragmentRecord record = fragments.GetRecord(index);
                        if (record.Bounds.Intersects(bounds)) destination[written++] = WorldStaticQueryHit.From(record);
                    }
                }
            }
        }
        count = written;
        error = WorldStaticQueryError.None;
        return true;
    }

    public static bool TryQueryStaticBounds(
        this BoundWorldPack pack,
        in WorldStaticBounds bounds,
        int maximumCandidateChunks,
        Span<WorldStaticQueryHit> destination,
        out int count,
        out int required,
        out WorldStaticQueryError error)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return pack.Pack.TryQueryStaticBounds(bounds, maximumCandidateChunks, destination,
            out count, out required, out error);
    }

    private static int CountMatches(
        PreparedWorldComposition world,
        in WorldStaticBounds bounds,
        int minimumX,
        int minimumZ,
        int maximumX,
        int maximumZ)
    {
        int count = 0;
        for (long x = minimumX; x <= maximumX; x++)
        {
            for (long z = minimumZ; z <= maximumZ; z++)
            {
                var key = new SpatialChunkKey(world.Space, checked((int)x), checked((int)z));
                if (world.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics))
                    for (int index = 0; index < statics.Count; index++)
                        if (statics.GetRecord(index).Bounds.Intersects(bounds)) count = checked(count + 1);
                if (world.TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments))
                    for (int index = 0; index < fragments.Count; index++)
                        if (fragments.GetRecord(index).Bounds.Intersects(bounds)) count = checked(count + 1);
            }
        }
        return count;
    }

    private static int CountMatches(
        WorldPack pack,
        in WorldStaticBounds bounds,
        int minimumX,
        int minimumZ,
        int maximumX,
        int maximumZ)
    {
        int count = 0;
        for (long x = minimumX; x <= maximumX; x++)
        {
            for (long z = minimumZ; z <= maximumZ; z++)
            {
                var key = new SpatialChunkKey(pack.Space, checked((int)x), checked((int)z));
                if (pack.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics))
                {
                    for (int index = 0; index < statics.Count; index++)
                        if (statics.GetRecord(index).Bounds.Intersects(bounds)) count = checked(count + 1);
                }
                if (pack.TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments))
                {
                    for (int index = 0; index < fragments.Count; index++)
                        if (fragments.GetRecord(index).Bounds.Intersects(bounds)) count = checked(count + 1);
                }
            }
        }
        return count;
    }

    private static int CountMatches(
        WorldPreparedView world,
        in WorldStaticBounds bounds,
        int minimumX,
        int minimumZ,
        int maximumX,
        int maximumZ)
    {
        int count = 0;
        for (long x = minimumX; x <= maximumX; x++)
        {
            for (long z = minimumZ; z <= maximumZ; z++)
            {
                var key = new SpatialChunkKey(world.Space, checked((int)x), checked((int)z));
                if (world.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics))
                    for (int index = 0; index < statics.Count; index++)
                        if (statics.GetRecord(index).Bounds.Intersects(bounds)) count = checked(count + 1);
                if (world.TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments))
                    for (int index = 0; index < fragments.Count; index++)
                        if (fragments.GetRecord(index).Bounds.Intersects(bounds)) count = checked(count + 1);
            }
        }
        return count;
    }

    private static bool TryGetCandidateRange(
        in WorldStaticBounds bounds,
        int maximumCandidateChunks,
        out int minimumX,
        out int minimumZ,
        out int maximumX,
        out int maximumZ)
    {
        if (!GameWorldSpatialProfile.ChunkGeometry.TryNormalize(
            bounds.MinimumXMillimeters, bounds.MinimumZMillimeters,
            out SpatialChunkCoord minimum, out _, out _) ||
            !GameWorldSpatialProfile.ChunkGeometry.TryNormalize(
            bounds.MaximumXExclusiveMillimeters - 1, bounds.MaximumZExclusiveMillimeters - 1,
            out SpatialChunkCoord maximum, out _, out _))
            throw new InvalidOperationException("Validated world bounds did not normalize to the game grid.");
        minimumX = minimum.X == int.MinValue ? int.MinValue : minimum.X - 1;
        minimumZ = minimum.Z == int.MinValue ? int.MinValue : minimum.Z - 1;
        maximumX = maximum.X == int.MaxValue ? int.MaxValue : maximum.X + 1;
        maximumZ = maximum.Z == int.MaxValue ? int.MaxValue : maximum.Z + 1;
        long width = (long)maximumX - minimumX + 1;
        long depth = (long)maximumZ - minimumZ + 1;
        return width <= maximumCandidateChunks && depth <= maximumCandidateChunks / width;
    }
}
