using System.Buffers.Binary;
using System.Runtime.InteropServices;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>
/// Stable identity of one immutable authored terrain shell within one exact WorldPack lineage.
/// The same identity may name canonical fragments in several chunks. It is not a dense layer
/// number, ECS identity, renderer handle, fragment slot, or triangle index.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct WorldSurfaceId : IEquatable<WorldSurfaceId>, IComparable<WorldSurfaceId>
{
    public WorldSurfaceId(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(WorldSurfaceId other) => Value.CompareTo(other.Value);
    public bool Equals(WorldSurfaceId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is WorldSurfaceId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Value);
    public override string ToString() => IsValid ? $"WorldSurface({Value})" : "WorldSurface(Invalid)";
    public static bool operator ==(WorldSurfaceId left, WorldSurfaceId right) => left.Equals(right);
    public static bool operator !=(WorldSurfaceId left, WorldSurfaceId right) => !left.Equals(right);
}

[Flags]
public enum WorldTerrainSurfaceFaces : byte
{
    None = 0,
    Top = 1 << 0,
    Bottom = 1 << 1,
}

public enum WorldTerrainSurfaceFace : byte
{
    None = 0,
    Base = 1,
    Top = 2,
    Bottom = 3,
}

[Flags]
public enum TerrainSurfaceEdgeConnection : byte
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3,
}

/// <summary>Bounded chunk-local rectangular cell footprint for one sparse surface band.</summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct TerrainSurfacePatchBounds : IEquatable<TerrainSurfacePatchBounds>
{
    public TerrainSurfacePatchBounds(byte minimumCellX, byte minimumCellZ, byte cellWidth, byte cellDepth)
    {
        if (cellWidth == 0 || cellDepth == 0 ||
            minimumCellX >= TerrainChunkFormat.MaterialCellEdge ||
            minimumCellZ >= TerrainChunkFormat.MaterialCellEdge ||
            minimumCellX + cellWidth > TerrainChunkFormat.MaterialCellEdge ||
            minimumCellZ + cellDepth > TerrainChunkFormat.MaterialCellEdge)
            throw new ArgumentOutOfRangeException(nameof(cellWidth));
        MinimumCellX = minimumCellX;
        MinimumCellZ = minimumCellZ;
        CellWidth = cellWidth;
        CellDepth = cellDepth;
    }

    public byte MinimumCellX { get; }
    public byte MinimumCellZ { get; }
    public byte CellWidth { get; }
    public byte CellDepth { get; }
    public int CellCount => CellWidth * CellDepth;
    public int VertexCount => (CellWidth + 1) * (CellDepth + 1);
    public int PresenceWordCount => (CellCount + 63) / 64;
    public bool IsValid => CellWidth != 0 && CellDepth != 0 &&
        MinimumCellX < TerrainChunkFormat.MaterialCellEdge &&
        MinimumCellZ < TerrainChunkFormat.MaterialCellEdge &&
        MinimumCellX + CellWidth <= TerrainChunkFormat.MaterialCellEdge &&
        MinimumCellZ + CellDepth <= TerrainChunkFormat.MaterialCellEdge;

    public bool Contains(TerrainCellCoord cell) =>
        cell.X >= MinimumCellX && cell.X < MinimumCellX + CellWidth &&
        cell.Z >= MinimumCellZ && cell.Z < MinimumCellZ + CellDepth;

    public bool Equals(TerrainSurfacePatchBounds other) =>
        MinimumCellX == other.MinimumCellX && MinimumCellZ == other.MinimumCellZ &&
        CellWidth == other.CellWidth && CellDepth == other.CellDepth;
    public override bool Equals(object? obj) => obj is TerrainSurfacePatchBounds other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(MinimumCellX, MinimumCellZ, CellWidth, CellDepth);
    public static bool operator ==(TerrainSurfacePatchBounds left, TerrainSurfacePatchBounds right) =>
        left.Equals(right);
    public static bool operator !=(TerrainSurfacePatchBounds left, TerrainSurfacePatchBounds right) =>
        !left.Equals(right);
}

/// <summary>One sampled face in a complete resident X/Z terrain column.</summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly struct WorldTerrainColumnSurface : IEquatable<WorldTerrainColumnSurface>
{
    public WorldTerrainColumnSurface(
        WorldSurfaceId surfaceId,
        WorldTerrainSurfaceFace face,
        int heightMillimeters,
        ushort materialId)
    {
        if (materialId == 0 ||
            (face == WorldTerrainSurfaceFace.Base && surfaceId.IsValid) ||
            (face is WorldTerrainSurfaceFace.Top or WorldTerrainSurfaceFace.Bottom && !surfaceId.IsValid) ||
            face is not (WorldTerrainSurfaceFace.Base or
                WorldTerrainSurfaceFace.Top or WorldTerrainSurfaceFace.Bottom))
            throw new ArgumentOutOfRangeException(nameof(face));
        SurfaceId = surfaceId;
        HeightMillimeters = heightMillimeters;
        MaterialId = materialId;
        Face = face;
    }

    [FieldOffset(0)] public readonly WorldSurfaceId SurfaceId;
    [FieldOffset(8)] public readonly int HeightMillimeters;
    [FieldOffset(12)] public readonly ushort MaterialId;
    [FieldOffset(14)] public readonly WorldTerrainSurfaceFace Face;
    public bool IsValid =>
        (Face is WorldTerrainSurfaceFace.Base or WorldTerrainSurfaceFace.Top or
            WorldTerrainSurfaceFace.Bottom) && MaterialId != 0;
    public bool IsBase => Face == WorldTerrainSurfaceFace.Base && !SurfaceId.IsValid;
    public bool IsSupport => Face is WorldTerrainSurfaceFace.Base or WorldTerrainSurfaceFace.Top;
    public bool IsCeiling => Face == WorldTerrainSurfaceFace.Bottom;
    public bool Equals(WorldTerrainColumnSurface other) =>
        SurfaceId == other.SurfaceId && Face == other.Face &&
        HeightMillimeters == other.HeightMillimeters && MaterialId == other.MaterialId;
    public override bool Equals(object? obj) => obj is WorldTerrainColumnSurface other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(SurfaceId, Face, HeightMillimeters, MaterialId);
    public static bool operator ==(WorldTerrainColumnSurface left, WorldTerrainColumnSurface right) =>
        left.Equals(right);
    public static bool operator !=(WorldTerrainColumnSurface left, WorldTerrainColumnSurface right) =>
        !left.Equals(right);
}

public enum WorldTerrainColumnQueryStatus : byte
{
    None = 0,
    Complete = 1,
    Unavailable = 2,
    DestinationTooSmall = 3,
}

/// <summary>Borrowed immutable view of every additional terrain band owned by one chunk.</summary>
public readonly ref struct WorldTerrainSurfaceBandChunkView
{
    private readonly ReadOnlySpan<byte> _payload;
    private readonly int _start;

    internal WorldTerrainSurfaceBandChunkView(
        SpatialChunkKey key,
        ReadOnlySpan<byte> payload,
        int start,
        int count)
    {
        Key = key;
        _payload = payload;
        _start = start;
        Count = count;
    }

    public SpatialChunkKey Key { get; }
    public int Count { get; }

    public WorldTerrainSurfaceBandView GetBand(int index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        int descriptorsOffset = BinaryPrimitives.ReadInt32LittleEndian(_payload[20..]);
        ReadOnlySpan<byte> descriptor = _payload.Slice(
            descriptorsOffset + (_start + index) * AdditionalTerrainSurfaceFormat.DescriptorBytes,
            AdditionalTerrainSurfaceFormat.DescriptorBytes);
        return new WorldTerrainSurfaceBandView(Key, _payload, descriptor);
    }

    public bool TryGetBand(WorldSurfaceId surfaceId, out WorldTerrainSurfaceBandView band)
    {
        if (!surfaceId.IsValid)
        {
            band = default;
            return false;
        }
        int low = 0;
        int high = Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            WorldTerrainSurfaceBandView candidate = GetBand(middle);
            int order = candidate.Id.CompareTo(surfaceId);
            if (order == 0)
            {
                band = candidate;
                return true;
            }
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        band = default;
        return false;
    }
}

/// <summary>Borrowed immutable decoded view of one validated sparse terrain band.</summary>
public readonly ref struct WorldTerrainSurfaceBandView
{
    private readonly ReadOnlySpan<byte> _payload;
    private readonly int _presenceOffset;
    private readonly int _connectionsOffset;
    private readonly int _topMaterialsOffset;
    private readonly int _topHeightsOffset;
    private readonly int _bottomMaterialsOffset;
    private readonly int _bottomHeightsOffset;

    internal WorldTerrainSurfaceBandView(
        SpatialChunkKey key,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> descriptor)
    {
        Key = key;
        Id = new WorldSurfaceId(BinaryPrimitives.ReadUInt64LittleEndian(descriptor));
        Bounds = new TerrainSurfacePatchBounds(descriptor[8], descriptor[9], descriptor[10], descriptor[11]);
        Faces = (WorldTerrainSurfaceFaces)descriptor[12];
        SideMaterialId = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[14..]);
        _presenceOffset = BinaryPrimitives.ReadInt32LittleEndian(descriptor[16..]);
        _topMaterialsOffset = BinaryPrimitives.ReadInt32LittleEndian(descriptor[20..]);
        _topHeightsOffset = BinaryPrimitives.ReadInt32LittleEndian(descriptor[24..]);
        _bottomMaterialsOffset = BinaryPrimitives.ReadInt32LittleEndian(descriptor[28..]);
        _bottomHeightsOffset = BinaryPrimitives.ReadInt32LittleEndian(descriptor[32..]);
        _connectionsOffset = BinaryPrimitives.ReadInt32LittleEndian(descriptor[36..]);
        _payload = payload;
    }

    public SpatialChunkKey Key { get; }
    public WorldSurfaceId Id { get; }
    public TerrainSurfacePatchBounds Bounds { get; }
    public WorldTerrainSurfaceFaces Faces { get; }
    public ushort SideMaterialId { get; }
    public bool HasTop => (Faces & WorldTerrainSurfaceFaces.Top) != 0;
    public bool HasBottom => (Faces & WorldTerrainSurfaceFaces.Bottom) != 0;

    public bool IsCellPresent(int localCellX, int localCellZ)
    {
        if (localCellX < 0 || localCellX >= Bounds.CellWidth)
            throw new ArgumentOutOfRangeException(nameof(localCellX));
        if (localCellZ < 0 || localCellZ >= Bounds.CellDepth)
            throw new ArgumentOutOfRangeException(nameof(localCellZ));
        int cell = localCellZ * Bounds.CellWidth + localCellX;
        ulong word = BinaryPrimitives.ReadUInt64LittleEndian(
            _payload[(_presenceOffset + (cell >> 6) * sizeof(ulong))..]);
        return (word & (1UL << (cell & 63))) != 0;
    }

    public TerrainSurfaceEdgeConnection GetEdgeConnections(int localCellX, int localCellZ)
    {
        if (localCellX < 0 || localCellX >= Bounds.CellWidth)
            throw new ArgumentOutOfRangeException(nameof(localCellX));
        if (localCellZ < 0 || localCellZ >= Bounds.CellDepth)
            throw new ArgumentOutOfRangeException(nameof(localCellZ));
        return (TerrainSurfaceEdgeConnection)_payload[
            _connectionsOffset + localCellZ * Bounds.CellWidth + localCellX];
    }

    public ushort GetMaterial(WorldTerrainSurfaceFace face, int localCellX, int localCellZ)
    {
        int offset = face switch
        {
            WorldTerrainSurfaceFace.Top when HasTop => _topMaterialsOffset,
            WorldTerrainSurfaceFace.Bottom when HasBottom => _bottomMaterialsOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };
        if (localCellX < 0 || localCellX >= Bounds.CellWidth)
            throw new ArgumentOutOfRangeException(nameof(localCellX));
        if (localCellZ < 0 || localCellZ >= Bounds.CellDepth)
            throw new ArgumentOutOfRangeException(nameof(localCellZ));
        int cell = localCellZ * Bounds.CellWidth + localCellX;
        return BinaryPrimitives.ReadUInt16LittleEndian(_payload[(offset + cell * sizeof(ushort))..]);
    }

    public int GetHeight(WorldTerrainSurfaceFace face, int localVertexX, int localVertexZ)
    {
        int offset = face switch
        {
            WorldTerrainSurfaceFace.Top when HasTop => _topHeightsOffset,
            WorldTerrainSurfaceFace.Bottom when HasBottom => _bottomHeightsOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };
        int edge = Bounds.CellWidth + 1;
        if (localVertexX < 0 || localVertexX >= edge)
            throw new ArgumentOutOfRangeException(nameof(localVertexX));
        if (localVertexZ < 0 || localVertexZ >= Bounds.CellDepth + 1)
            throw new ArgumentOutOfRangeException(nameof(localVertexZ));
        int vertex = localVertexZ * edge + localVertexX;
        int heightBase = BinaryPrimitives.ReadInt32LittleEndian(_payload[offset..]);
        ushort units = BinaryPrimitives.ReadUInt16LittleEndian(
            _payload[(offset + sizeof(int) + vertex * sizeof(ushort))..]);
        return checked(heightBase + units * TerrainChunkFormat.HeightQuantumMillimeters);
    }
}

/// <summary>Deterministic complete-column queries over base plus sparse additional surfaces.</summary>
public static class WorldTerrainColumnQueries
{
    /// <summary>
    /// Default admitted maximum: one BaseTerrain face plus Top/Bottom from every additional
    /// fragment allowed in one chunk. Consumers may use this as fixed fail-closed scratch.
    /// </summary>
    public const int DefaultMaximumColumnSurfaceCount = 1 + (64 * 2);

    public static WorldTerrainColumnQueryStatus TryCopyTerrainColumnSurfaces(
        this BoundWorldPack world,
        in WorldPosition location,
        Span<WorldTerrainColumnSurface> destination,
        out int count,
        out int required) =>
        Query(new TerrainColumnSource(world), location, destination, out count, out required);

    public static WorldTerrainColumnQueryStatus TryCopyTerrainColumnSurfaces(
        this WorldPreparedView world,
        in WorldPosition location,
        Span<WorldTerrainColumnSurface> destination,
        out int count,
        out int required) =>
        Query(new TerrainColumnSource(world), location, destination, out count, out required);

    public static WorldTerrainColumnQueryStatus TryCopyTerrainColumnSurfaces(
        this PreparedWorldComposition world,
        in WorldPosition location,
        Span<WorldTerrainColumnSurface> destination,
        out int count,
        out int required) =>
        Query(new TerrainColumnSource(world), location, destination, out count, out required);

    public static WorldTerrainColumnQueryStatus TryFindHighestSupportAtOrBelow(
        this BoundWorldPack world,
        in WorldPosition location,
        int maximumHeightMillimeters,
        out WorldTerrainColumnSurface support) =>
        Find(new TerrainColumnSource(world), location, maximumHeightMillimeters, support: true, out support);

    public static WorldTerrainColumnQueryStatus TryFindHighestSupportAtOrBelow(
        this WorldPreparedView world,
        in WorldPosition location,
        int maximumHeightMillimeters,
        out WorldTerrainColumnSurface support) =>
        Find(new TerrainColumnSource(world), location, maximumHeightMillimeters, support: true, out support);

    public static WorldTerrainColumnQueryStatus TryFindHighestSupportAtOrBelow(
        this PreparedWorldComposition world,
        in WorldPosition location,
        int maximumHeightMillimeters,
        out WorldTerrainColumnSurface support) =>
        Find(new TerrainColumnSource(world), location, maximumHeightMillimeters, support: true, out support);

    /// <summary>
    /// Finds the support face nearest one requested standing elevation. Distance is physical;
    /// identity is used only to make an exact-height tie canonical. An equally distant lower
    /// support wins over an upper support so placement does not rise on an ambiguous tie.
    /// </summary>
    public static WorldTerrainColumnQueryStatus TryFindNearestSupport(
        this BoundWorldPack world,
        in WorldPosition location,
        int requestedHeightMillimeters,
        out WorldTerrainColumnSurface support) =>
        FindNearestSupport(
            new TerrainColumnSource(world), location, requestedHeightMillimeters, out support);

    public static WorldTerrainColumnQueryStatus TryFindNearestSupport(
        this WorldPreparedView world,
        in WorldPosition location,
        int requestedHeightMillimeters,
        out WorldTerrainColumnSurface support) =>
        FindNearestSupport(
            new TerrainColumnSource(world), location, requestedHeightMillimeters, out support);

    public static WorldTerrainColumnQueryStatus TryFindNearestSupport(
        this PreparedWorldComposition world,
        in WorldPosition location,
        int requestedHeightMillimeters,
        out WorldTerrainColumnSurface support) =>
        FindNearestSupport(
            new TerrainColumnSource(world), location, requestedHeightMillimeters, out support);

    public static WorldTerrainColumnQueryStatus TryFindLowestCeilingAtOrAbove(
        this BoundWorldPack world,
        in WorldPosition location,
        int minimumHeightMillimeters,
        out WorldTerrainColumnSurface ceiling) =>
        Find(new TerrainColumnSource(world), location, minimumHeightMillimeters, support: false, out ceiling);

    public static WorldTerrainColumnQueryStatus TryFindLowestCeilingAtOrAbove(
        this WorldPreparedView world,
        in WorldPosition location,
        int minimumHeightMillimeters,
        out WorldTerrainColumnSurface ceiling) =>
        Find(new TerrainColumnSource(world), location, minimumHeightMillimeters, support: false, out ceiling);

    public static WorldTerrainColumnQueryStatus TryFindLowestCeilingAtOrAbove(
        this PreparedWorldComposition world,
        in WorldPosition location,
        int minimumHeightMillimeters,
        out WorldTerrainColumnSurface ceiling) =>
        Find(new TerrainColumnSource(world), location, minimumHeightMillimeters, support: false, out ceiling);

    private static WorldTerrainColumnQueryStatus Query(
        in TerrainColumnSource world,
        in WorldPosition location,
        Span<WorldTerrainColumnSurface> destination,
        out int count,
        out int required)
    {
        count = 0;
        required = 0;
        if (!world.TryGetBaseTerrain(location.ToChunkKey(world.Space), out WorldTerrainChunkView terrain))
            return WorldTerrainColumnQueryStatus.Unavailable;
        TerrainCellCoord cell = TerrainCellCoord.FromLocal(location.Local);
        int materialIndex = TerrainChunkFormat.GetMaterialIndex(cell);
        bool basePresent = terrain.IsSurfacePresent(materialIndex);
        WorldTerrainSurfaceBandChunkView bands = default;
        world.TryGetAdditionalTerrainSurfaceBands(terrain.Key, out bands);
        required = basePresent ? 1 : 0;
        for (int bandIndex = 0; bandIndex < bands.Count; bandIndex++)
        {
            WorldTerrainSurfaceBandView band = bands.GetBand(bandIndex);
            if (!band.Bounds.Contains(cell)) continue;
            int localX = cell.X - band.Bounds.MinimumCellX;
            int localZ = cell.Z - band.Bounds.MinimumCellZ;
            if (!band.IsCellPresent(localX, localZ)) continue;
            if (band.HasTop) required++;
            if (band.HasBottom) required++;
        }
        if (destination.Length < required) return WorldTerrainColumnQueryStatus.DestinationTooSmall;

        if (basePresent)
        {
            destination[count++] = new WorldTerrainColumnSurface(
                default, WorldTerrainSurfaceFace.Base,
                BaseTerrainSurface.SampleHeight(terrain, location), terrain.GetMaterial(materialIndex));
        }
        for (int bandIndex = 0; bandIndex < bands.Count; bandIndex++)
        {
            WorldTerrainSurfaceBandView band = bands.GetBand(bandIndex);
            if (!band.Bounds.Contains(cell)) continue;
            int localX = cell.X - band.Bounds.MinimumCellX;
            int localZ = cell.Z - band.Bounds.MinimumCellZ;
            if (!band.IsCellPresent(localX, localZ)) continue;
            if (band.HasTop) destination[count++] = Sample(band, WorldTerrainSurfaceFace.Top, location);
            if (band.HasBottom) destination[count++] = Sample(band, WorldTerrainSurfaceFace.Bottom, location);
        }
        SortHighestFirst(destination[..count]);
        return WorldTerrainColumnQueryStatus.Complete;
    }

    private static WorldTerrainColumnQueryStatus Find(
        in TerrainColumnSource world,
        in WorldPosition location,
        int threshold,
        bool support,
        out WorldTerrainColumnSurface result)
    {
        result = default;
        if (!world.TryGetBaseTerrain(location.ToChunkKey(world.Space), out WorldTerrainChunkView terrain))
            return WorldTerrainColumnQueryStatus.Unavailable;
        bool found = false;
        TerrainCellCoord cell = TerrainCellCoord.FromLocal(location.Local);
        int materialIndex = TerrainChunkFormat.GetMaterialIndex(cell);
        if (support && terrain.IsSurfacePresent(materialIndex))
        {
            int height = BaseTerrainSurface.SampleHeight(terrain, location);
            if (height <= threshold)
            {
                result = new WorldTerrainColumnSurface(
                    default, WorldTerrainSurfaceFace.Base, height, terrain.GetMaterial(materialIndex));
                found = true;
            }
        }

        if (world.TryGetAdditionalTerrainSurfaceBands(terrain.Key, out WorldTerrainSurfaceBandChunkView bands))
        {
            for (int index = 0; index < bands.Count; index++)
            {
                WorldTerrainSurfaceBandView band = bands.GetBand(index);
                if (!band.Bounds.Contains(cell)) continue;
                int localX = cell.X - band.Bounds.MinimumCellX;
                int localZ = cell.Z - band.Bounds.MinimumCellZ;
                if (!band.IsCellPresent(localX, localZ)) continue;
                WorldTerrainSurfaceFace face = support
                    ? WorldTerrainSurfaceFace.Top
                    : WorldTerrainSurfaceFace.Bottom;
                if (support ? !band.HasTop : !band.HasBottom) continue;
                WorldTerrainColumnSurface candidate = Sample(band, face, location);
                if (support && candidate.HeightMillimeters > threshold ||
                    !support && candidate.HeightMillimeters < threshold)
                    continue;
                bool isBetter = !found || (support
                    ? candidate.HeightMillimeters > result.HeightMillimeters ||
                        candidate.HeightMillimeters == result.HeightMillimeters &&
                        CompareIdentity(candidate, result) < 0
                    : candidate.HeightMillimeters < result.HeightMillimeters ||
                        candidate.HeightMillimeters == result.HeightMillimeters &&
                        CompareIdentity(candidate, result) < 0);
                if (isBetter)
                {
                    result = candidate;
                    found = true;
                }
            }
        }
        return WorldTerrainColumnQueryStatus.Complete;
    }

    private static WorldTerrainColumnQueryStatus FindNearestSupport(
        in TerrainColumnSource world,
        in WorldPosition location,
        int requestedHeightMillimeters,
        out WorldTerrainColumnSurface result)
    {
        result = default;
        if (!world.TryGetBaseTerrain(location.ToChunkKey(world.Space), out WorldTerrainChunkView terrain))
            return WorldTerrainColumnQueryStatus.Unavailable;

        bool found = false;
        TerrainCellCoord cell = TerrainCellCoord.FromLocal(location.Local);
        int materialIndex = TerrainChunkFormat.GetMaterialIndex(cell);
        if (terrain.IsSurfacePresent(materialIndex))
        {
            result = new WorldTerrainColumnSurface(
                default,
                WorldTerrainSurfaceFace.Base,
                BaseTerrainSurface.SampleHeight(terrain, location),
                terrain.GetMaterial(materialIndex));
            found = true;
        }

        if (world.TryGetAdditionalTerrainSurfaceBands(
                terrain.Key, out WorldTerrainSurfaceBandChunkView bands))
        {
            for (int index = 0; index < bands.Count; index++)
            {
                WorldTerrainSurfaceBandView band = bands.GetBand(index);
                if (!band.HasTop || !band.Bounds.Contains(cell)) continue;
                int localX = cell.X - band.Bounds.MinimumCellX;
                int localZ = cell.Z - band.Bounds.MinimumCellZ;
                if (!band.IsCellPresent(localX, localZ)) continue;
                WorldTerrainColumnSurface candidate =
                    Sample(band, WorldTerrainSurfaceFace.Top, location);
                if (!found || IsNearerSupport(candidate, result, requestedHeightMillimeters))
                {
                    result = candidate;
                    found = true;
                }
            }
        }
        return WorldTerrainColumnQueryStatus.Complete;
    }

    private static bool IsNearerSupport(
        in WorldTerrainColumnSurface candidate,
        in WorldTerrainColumnSurface current,
        int requestedHeightMillimeters)
    {
        long candidateDelta = (long)candidate.HeightMillimeters - requestedHeightMillimeters;
        long currentDelta = (long)current.HeightMillimeters - requestedHeightMillimeters;
        ulong candidateDistance = (ulong)Math.Abs(candidateDelta);
        ulong currentDistance = (ulong)Math.Abs(currentDelta);
        if (candidateDistance != currentDistance) return candidateDistance < currentDistance;
        bool candidateBelow = candidateDelta <= 0;
        bool currentBelow = currentDelta <= 0;
        if (candidateBelow != currentBelow) return candidateBelow;
        return CompareIdentity(candidate, current) < 0;
    }

    internal static WorldTerrainColumnSurface Sample(
        in WorldTerrainSurfaceBandView band,
        WorldTerrainSurfaceFace face,
        in WorldPosition location)
    {
        TerrainCellCoord cell = TerrainCellCoord.FromLocal(location.Local);
        int localCellX = cell.X - band.Bounds.MinimumCellX;
        int localCellZ = cell.Z - band.Bounds.MinimumCellZ;
        int localOriginX = cell.X * GameWorldSpatialProfile.TerrainCellEdgeUnits;
        int localOriginZ = cell.Z * GameWorldSpatialProfile.TerrainCellEdgeUnits;
        int x = location.Local.XMillimeters - localOriginX;
        int z = location.Local.ZMillimeters - localOriginZ;
        int northWest = band.GetHeight(face, localCellX, localCellZ);
        int northEast = band.GetHeight(face, localCellX + 1, localCellZ);
        int southWest = band.GetHeight(face, localCellX, localCellZ + 1);
        int southEast = band.GetHeight(face, localCellX + 1, localCellZ + 1);
        long globalCellX = checked((long)band.Key.Coord.X * TerrainChunkFormat.MaterialCellEdge + cell.X);
        long globalCellZ = checked((long)band.Key.Coord.Z * TerrainChunkFormat.MaterialCellEdge + cell.Z);
        int height = SampleTriangleHeight(
            x, z, northWest, northEast, southWest, southEast,
            ((globalCellX ^ globalCellZ) & 1) == 0);
        return new WorldTerrainColumnSurface(
            band.Id, face, height, band.GetMaterial(face, localCellX, localCellZ));
    }

    private static int SampleTriangleHeight(
        int x,
        int z,
        int northWest,
        int northEast,
        int southWest,
        int southEast,
        bool even)
    {
        int edge = GameWorldSpatialProfile.TerrainCellEdgeUnits;
        long numerator = even
            ? z <= x
                ? checked((long)northWest * edge + (long)(northEast - northWest) * x +
                    (long)(southEast - northEast) * z)
                : checked((long)northWest * edge + (long)(southEast - southWest) * x +
                    (long)(southWest - northWest) * z)
            : x + z <= edge
                ? checked((long)northWest * edge + (long)(northEast - northWest) * x +
                    (long)(southWest - northWest) * z)
                : checked((long)southEast * edge + (long)(southWest - southEast) * (edge - x) +
                    (long)(northEast - southEast) * (edge - z));
        long quotient = numerator / edge;
        long remainder = numerator % edge;
        if (remainder != 0 && (remainder < 0 ? -remainder : remainder) * 2 >= edge)
            quotient += numerator > 0 ? 1 : -1;
        return checked((int)quotient);
    }

    private static void SortHighestFirst(Span<WorldTerrainColumnSurface> surfaces)
    {
        for (int index = 1; index < surfaces.Length; index++)
        {
            WorldTerrainColumnSurface value = surfaces[index];
            int insertion = index;
            while (insertion > 0 && Compare(value, surfaces[insertion - 1]) < 0)
            {
                surfaces[insertion] = surfaces[insertion - 1];
                insertion--;
            }
            surfaces[insertion] = value;
        }
    }

    private static int Compare(in WorldTerrainColumnSurface left, in WorldTerrainColumnSurface right)
    {
        int height = right.HeightMillimeters.CompareTo(left.HeightMillimeters);
        return height != 0 ? height : CompareIdentity(left, right);
    }

    private static int CompareIdentity(in WorldTerrainColumnSurface left, in WorldTerrainColumnSurface right)
    {
        int identity = left.SurfaceId.Value.CompareTo(right.SurfaceId.Value);
        return identity != 0 ? identity : left.Face.CompareTo(right.Face);
    }

    private readonly ref struct TerrainColumnSource
    {
        private readonly BoundWorldPack? _baseWorld;
        private readonly WorldPreparedView? _preparedWorld;
        private readonly PreparedWorldComposition? _composition;

        internal TerrainColumnSource(BoundWorldPack world)
        {
            _baseWorld = world ?? throw new ArgumentNullException(nameof(world));
            _preparedWorld = null;
            _composition = null;
            Space = world.Space;
        }

        internal TerrainColumnSource(WorldPreparedView world)
        {
            _baseWorld = null;
            _preparedWorld = world ?? throw new ArgumentNullException(nameof(world));
            _composition = null;
            Space = world.Space;
        }

        internal TerrainColumnSource(PreparedWorldComposition world)
        {
            _baseWorld = null;
            _preparedWorld = null;
            _composition = world ?? throw new ArgumentNullException(nameof(world));
            Space = world.Space;
        }

        internal SpatialSpaceId Space { get; }
        internal bool TryGetBaseTerrain(SpatialChunkKey key, out WorldTerrainChunkView terrain) =>
            _composition is not null
                ? _composition.TryGetBaseTerrain(key, out terrain)
                : _preparedWorld is not null
                    ? _preparedWorld.TryGetBaseTerrain(key, out terrain)
                    : _baseWorld!.TryGetBaseTerrain(key, out terrain);
        internal bool TryGetAdditionalTerrainSurfaceBands(
            SpatialChunkKey key,
            out WorldTerrainSurfaceBandChunkView bands) =>
            _composition is not null
                ? _composition.TryGetAdditionalTerrainSurfaceBands(key, out bands)
                : _preparedWorld is not null
                    ? _preparedWorld.TryGetAdditionalTerrainSurfaceBands(key, out bands)
                    : _baseWorld!.TryGetAdditionalTerrainSurfaceBands(key, out bands);
    }
}

public enum TerrainSurfaceBandMeshBuildError : byte
{
    None = 0,
    DestinationTooSmall,
    OverlappingDestinations,
}

public readonly record struct TerrainSurfaceBandMeshSummary(
    WorldSurfaceId SurfaceId,
    int VertexCount,
    int TriangleCount,
    int IndexCount,
    int TopTriangleCount,
    int BottomTriangleCount,
    int BoundaryTriangleCount,
    int MinimumHeightMillimeters,
    int MaximumHeightMillimeters);

/// <summary>
/// Engine-neutral, deterministic mesh materialization for one sparse band. Triangles own their
/// vertices so top, bottom, and closed boundary faces retain exact hard semantic normals.
/// </summary>
public static class TerrainSurfaceBandMesh
{
    public const int MaximumBoundaryEdgeCount = TerrainChunkFormat.MaterialCellCount * 4;
    public const int MaximumTriangleCount =
        TerrainChunkFormat.MaterialCellCount * 4 + MaximumBoundaryEdgeCount * 2;
    public const int MaximumVertexCount = MaximumTriangleCount * 3;
    public const int MaximumIndexCount = MaximumVertexCount;

    public static bool TryBuild(
        in WorldTerrainSurfaceBandView band,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        Span<ushort> triangleMaterials,
        out TerrainSurfaceBandMeshSummary summary,
        out TerrainSurfaceBandMeshBuildError error)
    {
        int cells = 0;
        int boundaryEdges = 0;
        for (int z = 0; z < band.Bounds.CellDepth; z++)
        {
            for (int x = 0; x < band.Bounds.CellWidth; x++)
            {
                if (!band.IsCellPresent(x, z)) continue;
                cells++;
                if (band.HasTop && band.HasBottom)
                {
                    TerrainSurfaceEdgeConnection connected = band.GetEdgeConnections(x, z);
                    if ((z == 0 || !band.IsCellPresent(x, z - 1)) &&
                        (connected & TerrainSurfaceEdgeConnection.North) == 0) boundaryEdges++;
                    if ((x == band.Bounds.CellWidth - 1 || !band.IsCellPresent(x + 1, z)) &&
                        (connected & TerrainSurfaceEdgeConnection.East) == 0) boundaryEdges++;
                    if ((z == band.Bounds.CellDepth - 1 || !band.IsCellPresent(x, z + 1)) &&
                        (connected & TerrainSurfaceEdgeConnection.South) == 0) boundaryEdges++;
                    if ((x == 0 || !band.IsCellPresent(x - 1, z)) &&
                        (connected & TerrainSurfaceEdgeConnection.West) == 0) boundaryEdges++;
                }
            }
        }
        int topTriangles = band.HasTop ? checked(cells * 2) : 0;
        int bottomTriangles = band.HasBottom ? checked(cells * 2) : 0;
        int boundaryTriangles = checked(boundaryEdges * 2);
        int triangles = checked(topTriangles + bottomTriangles + boundaryTriangles);
        int vertexCount = checked(triangles * 3);
        if (vertices.Length < vertexCount || indices.Length < vertexCount ||
            triangleMaterials.Length < triangles)
        {
            summary = default;
            error = TerrainSurfaceBandMeshBuildError.DestinationTooSmall;
            return false;
        }
        Span<byte> vertexBytes = MemoryMarshal.AsBytes(vertices[..vertexCount]);
        Span<byte> indexBytes = MemoryMarshal.AsBytes(indices[..vertexCount]);
        Span<byte> materialBytes = MemoryMarshal.AsBytes(triangleMaterials[..triangles]);
        if (vertexBytes.Overlaps(indexBytes) || vertexBytes.Overlaps(materialBytes) ||
            indexBytes.Overlaps(materialBytes))
        {
            summary = default;
            error = TerrainSurfaceBandMeshBuildError.OverlappingDestinations;
            return false;
        }

        int vertex = 0;
        int triangle = 0;
        int minimum = int.MaxValue;
        int maximum = int.MinValue;
        for (int z = 0; z < band.Bounds.CellDepth; z++)
        {
            for (int x = 0; x < band.Bounds.CellWidth; x++)
            {
                if (!band.IsCellPresent(x, z)) continue;
                bool even = ((((long)band.Key.Coord.X * TerrainChunkFormat.MaterialCellEdge +
                    band.Bounds.MinimumCellX + x) ^
                    ((long)band.Key.Coord.Z * TerrainChunkFormat.MaterialCellEdge +
                    band.Bounds.MinimumCellZ + z)) & 1) == 0;
                if (band.HasTop)
                    WriteCellFace(band, WorldTerrainSurfaceFace.Top, x, z, even,
                        vertices, indices, triangleMaterials, ref vertex, ref triangle,
                        ref minimum, ref maximum);
                if (band.HasBottom)
                    WriteCellFace(band, WorldTerrainSurfaceFace.Bottom, x, z, even,
                        vertices, indices, triangleMaterials, ref vertex, ref triangle,
                        ref minimum, ref maximum);
                if (band.HasTop && band.HasBottom)
                {
                    TerrainSurfaceEdgeConnection connected = band.GetEdgeConnections(x, z);
                    if ((z == 0 || !band.IsCellPresent(x, z - 1)) &&
                        (connected & TerrainSurfaceEdgeConnection.North) == 0)
                        WriteBoundary(band, x, z, Boundary.North, vertices, indices,
                            triangleMaterials, ref vertex, ref triangle, ref minimum, ref maximum);
                    if ((x == band.Bounds.CellWidth - 1 || !band.IsCellPresent(x + 1, z)) &&
                        (connected & TerrainSurfaceEdgeConnection.East) == 0)
                        WriteBoundary(band, x, z, Boundary.East, vertices, indices,
                            triangleMaterials, ref vertex, ref triangle, ref minimum, ref maximum);
                    if ((z == band.Bounds.CellDepth - 1 || !band.IsCellPresent(x, z + 1)) &&
                        (connected & TerrainSurfaceEdgeConnection.South) == 0)
                        WriteBoundary(band, x, z, Boundary.South, vertices, indices,
                            triangleMaterials, ref vertex, ref triangle, ref minimum, ref maximum);
                    if ((x == 0 || !band.IsCellPresent(x - 1, z)) &&
                        (connected & TerrainSurfaceEdgeConnection.West) == 0)
                        WriteBoundary(band, x, z, Boundary.West, vertices, indices,
                            triangleMaterials, ref vertex, ref triangle, ref minimum, ref maximum);
                }
            }
        }
        if (vertex != vertexCount || triangle != triangles)
            throw new InvalidOperationException("Additional terrain mesh measurement diverged from materialization.");
        summary = new TerrainSurfaceBandMeshSummary(
            band.Id, vertexCount, triangles, vertexCount, topTriangles, bottomTriangles,
            boundaryTriangles, minimum, maximum);
        error = TerrainSurfaceBandMeshBuildError.None;
        return true;
    }

    private static void WriteCellFace(
        in WorldTerrainSurfaceBandView band,
        WorldTerrainSurfaceFace face,
        int x,
        int z,
        bool even,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        Span<ushort> materials,
        ref int vertex,
        ref int triangle,
        ref int minimum,
        ref int maximum)
    {
        Point nw = PointAt(band, face, x, z);
        Point ne = PointAt(band, face, x + 1, z);
        Point sw = PointAt(band, face, x, z + 1);
        Point se = PointAt(band, face, x + 1, z + 1);
        ushort material = band.GetMaterial(face, x, z);
        bool bottom = face == WorldTerrainSurfaceFace.Bottom;
        if (even)
        {
            WriteTriangle(nw, bottom ? se : sw, bottom ? sw : se, material,
                vertices, indices, materials, ref vertex, ref triangle, ref minimum, ref maximum);
            WriteTriangle(nw, bottom ? ne : se, bottom ? se : ne, material,
                vertices, indices, materials, ref vertex, ref triangle, ref minimum, ref maximum);
        }
        else
        {
            WriteTriangle(nw, bottom ? ne : sw, bottom ? sw : ne, material,
                vertices, indices, materials, ref vertex, ref triangle, ref minimum, ref maximum);
            WriteTriangle(ne, bottom ? se : sw, bottom ? sw : se, material,
                vertices, indices, materials, ref vertex, ref triangle, ref minimum, ref maximum);
        }
    }

    private static void WriteBoundary(
        in WorldTerrainSurfaceBandView band,
        int x,
        int z,
        Boundary boundary,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        Span<ushort> materials,
        ref int vertex,
        ref int triangle,
        ref int minimum,
        ref int maximum)
    {
        Point firstTop;
        Point secondTop;
        Point firstBottom;
        Point secondBottom;
        switch (boundary)
        {
            case Boundary.North:
                firstTop = PointAt(band, WorldTerrainSurfaceFace.Top, x, z);
                secondTop = PointAt(band, WorldTerrainSurfaceFace.Top, x + 1, z);
                firstBottom = PointAt(band, WorldTerrainSurfaceFace.Bottom, x, z);
                secondBottom = PointAt(band, WorldTerrainSurfaceFace.Bottom, x + 1, z);
                break;
            case Boundary.East:
                firstTop = PointAt(band, WorldTerrainSurfaceFace.Top, x + 1, z);
                secondTop = PointAt(band, WorldTerrainSurfaceFace.Top, x + 1, z + 1);
                firstBottom = PointAt(band, WorldTerrainSurfaceFace.Bottom, x + 1, z);
                secondBottom = PointAt(band, WorldTerrainSurfaceFace.Bottom, x + 1, z + 1);
                break;
            case Boundary.South:
                firstTop = PointAt(band, WorldTerrainSurfaceFace.Top, x + 1, z + 1);
                secondTop = PointAt(band, WorldTerrainSurfaceFace.Top, x, z + 1);
                firstBottom = PointAt(band, WorldTerrainSurfaceFace.Bottom, x + 1, z + 1);
                secondBottom = PointAt(band, WorldTerrainSurfaceFace.Bottom, x, z + 1);
                break;
            default:
                firstTop = PointAt(band, WorldTerrainSurfaceFace.Top, x, z + 1);
                secondTop = PointAt(band, WorldTerrainSurfaceFace.Top, x, z);
                firstBottom = PointAt(band, WorldTerrainSurfaceFace.Bottom, x, z + 1);
                secondBottom = PointAt(band, WorldTerrainSurfaceFace.Bottom, x, z);
                break;
        }
        WriteTriangle(firstTop, secondTop, firstBottom, band.SideMaterialId,
            vertices, indices, materials, ref vertex, ref triangle, ref minimum, ref maximum);
        WriteTriangle(secondTop, secondBottom, firstBottom, band.SideMaterialId,
            vertices, indices, materials, ref vertex, ref triangle, ref minimum, ref maximum);
    }

    private static Point PointAt(
        in WorldTerrainSurfaceBandView band,
        WorldTerrainSurfaceFace face,
        int vertexX,
        int vertexZ) =>
        new(
            (band.Bounds.MinimumCellX + vertexX) * GameWorldSpatialProfile.TerrainCellEdgeUnits,
            band.GetHeight(face, vertexX, vertexZ),
            (band.Bounds.MinimumCellZ + vertexZ) * GameWorldSpatialProfile.TerrainCellEdgeUnits);

    private static void WriteTriangle(
        Point first,
        Point second,
        Point third,
        ushort material,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        Span<ushort> materials,
        ref int vertex,
        ref int triangle,
        ref int minimum,
        ref int maximum)
    {
        long ax = (long)second.X - first.X;
        long ay = (long)second.Y - first.Y;
        long az = (long)second.Z - first.Z;
        long bx = (long)third.X - first.X;
        long by = (long)third.Y - first.Y;
        long bz = (long)third.Z - first.Z;
        ReduceDirection(
            checked(ay * bz - az * by),
            checked(az * bx - ax * bz),
            checked(ax * by - ay * bx),
            out int normalX, out int normalY, out int normalZ);
        ReduceDirection(ax, ay, az, out int tangentX, out int tangentY, out int tangentZ);
        WriteOneVertex(first, normalX, normalY, normalZ, tangentX, tangentY, tangentZ,
            vertices, indices, ref vertex, ref minimum, ref maximum);
        WriteOneVertex(second, normalX, normalY, normalZ, tangentX, tangentY, tangentZ,
            vertices, indices, ref vertex, ref minimum, ref maximum);
        WriteOneVertex(third, normalX, normalY, normalZ, tangentX, tangentY, tangentZ,
            vertices, indices, ref vertex, ref minimum, ref maximum);
        materials[triangle++] = material;
    }

    private static void WriteOneVertex(
        Point point,
        int normalX,
        int normalY,
        int normalZ,
        int tangentX,
        int tangentY,
        int tangentZ,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        ref int vertex,
        ref int minimum,
        ref int maximum)
    {
        if (vertex > ushort.MaxValue)
            throw new InvalidOperationException("Additional terrain vertex identity exceeded UInt16.");
        indices[vertex] = checked((ushort)vertex);
        vertices[vertex++] = new TerrainBaseMeshVertex(
            point.X, point.Y, point.Z, normalX, normalY, normalZ,
            tangentX, tangentY, tangentZ);
        minimum = Math.Min(minimum, point.Y);
        maximum = Math.Max(maximum, point.Y);
    }

    private static void ReduceDirection(long x, long y, long z, out int rx, out int ry, out int rz)
    {
        long divisor = GreatestCommonDivisor(
            GreatestCommonDivisor(Math.Abs(x), Math.Abs(y)), Math.Abs(z));
        if (divisor == 0) throw new InvalidOperationException("A validated surface emitted a degenerate triangle.");
        rx = checked((int)(x / divisor));
        ry = checked((int)(y / divisor));
        rz = checked((int)(z / divisor));
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0)
        {
            long remainder = left % right;
            left = right;
            right = remainder;
        }
        return left;
    }

    private enum Boundary : byte { North, East, South, West }
    private readonly record struct Point(int X, int Y, int Z);
}

internal static class AdditionalTerrainSurfaceFormat
{
    internal const int HeaderBytes = 32;
    internal const int RangeBytes = 8;
    internal const int DescriptorBytes = 48;
}
