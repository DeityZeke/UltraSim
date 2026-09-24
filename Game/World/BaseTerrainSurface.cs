using System.Runtime.InteropServices;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>Failure classifications for one complete fixed-grid base-terrain mesh build.</summary>
public enum TerrainBaseMeshBuildError
{
    None = 0,
    DestinationTooSmall,
    OverlappingDestinations,
    MissingChunk,
}

/// <summary>Exact bounded output description for one base-heightfield mesh payload.</summary>
public readonly record struct TerrainBaseMeshSummary(
    int VertexCount,
    int TriangleCount,
    int IndexCount,
    int MinimumHeightMillimeters,
    int MaximumHeightMillimeters);

/// <summary>
/// Engine-neutral deterministic base-terrain vertex. Position is chunk-local X/Z plus absolute Y.
/// Normal and tangent are exact unnormalized integer directions; presentation may normalize them.
/// Canonical triangle indices satisfy cross(v1-v0, v2-v0) toward the semantic front/outward side
/// in UltraSim coordinates, and emitted normals occupy that same semantic hemisphere. Engine
/// adapters own any coordinate, handedness, winding, normal, or tangent conversion they require.
/// This is an internal repository mesh-preparation payload, not a persistence, pack, Network,
/// Game.Contracts, mod, or long-term presentation ABI.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 36)]
public readonly struct TerrainBaseMeshVertex : IEquatable<TerrainBaseMeshVertex>
{
    public TerrainBaseMeshVertex(
        int localXMillimeters,
        int absoluteYMillimeters,
        int localZMillimeters,
        int normalX,
        int normalY,
        int normalZ,
        int tangentX,
        int tangentY,
        int tangentZ)
    {
        LocalXMillimeters = localXMillimeters;
        AbsoluteYMillimeters = absoluteYMillimeters;
        LocalZMillimeters = localZMillimeters;
        NormalX = normalX;
        NormalY = normalY;
        NormalZ = normalZ;
        TangentX = tangentX;
        TangentY = tangentY;
        TangentZ = tangentZ;
    }

    public int LocalXMillimeters { get; }
    public int AbsoluteYMillimeters { get; }
    public int LocalZMillimeters { get; }
    public int NormalX { get; }
    public int NormalY { get; }
    public int NormalZ { get; }
    public int TangentX { get; }
    public int TangentY { get; }
    public int TangentZ { get; }

    public bool Equals(TerrainBaseMeshVertex other) =>
        LocalXMillimeters == other.LocalXMillimeters &&
        AbsoluteYMillimeters == other.AbsoluteYMillimeters &&
        LocalZMillimeters == other.LocalZMillimeters &&
        NormalX == other.NormalX && NormalY == other.NormalY && NormalZ == other.NormalZ &&
        TangentX == other.TangentX && TangentY == other.TangentY && TangentZ == other.TangentZ;
    public override bool Equals(object? obj) => obj is TerrainBaseMeshVertex other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        HashCode.Combine(LocalXMillimeters, AbsoluteYMillimeters, LocalZMillimeters),
        HashCode.Combine(NormalX, NormalY, NormalZ),
        HashCode.Combine(TangentX, TangentY, TangentZ));
    public static bool operator ==(TerrainBaseMeshVertex left, TerrainBaseMeshVertex right) => left.Equals(right);
    public static bool operator !=(TerrainBaseMeshVertex left, TerrainBaseMeshVertex right) => !left.Equals(right);
}

/// <summary>Immutable-pack base-heightfield queries and deterministic fixed-grid mesh preparation.</summary>
public static class BaseTerrainSurface
{
    private const int NorthWest = 0;
    private const int NorthEast = 1;
    private const int SouthWest = 2;
    private const int SouthEast = 3;

    /// <summary>
    /// Resolves resident base-surface topology without conflating intentional void with missing
    /// or unprepared world data. The supplied Y coordinate is ignored.
    /// </summary>
    public static BaseTerrainSurfaceSample ProbeBaseTerrainSurface(
        this BoundWorldPack world,
        in WorldPosition location)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Probe(new TerrainMeshSource(world), location);
    }

    /// <summary>Resolves current prepared base-surface topology and durable terrain replacement.</summary>
    public static BaseTerrainSurfaceSample ProbeBaseTerrainSurface(
        this WorldPreparedView world,
        in WorldPosition location)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Probe(new TerrainMeshSource(world), location);
    }

    public static BaseTerrainSurfaceSample ProbeBaseTerrainSurface(
        this PreparedWorldComposition world,
        in WorldPosition location)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Probe(new TerrainMeshSource(world), location);
    }

    /// <summary>
    /// Samples the canonical triangle plane beneath the supplied X/Z. The location's Y is ignored.
    /// </summary>
    public static bool TrySampleBaseTerrainHeight(
        this BoundWorldPack world,
        in WorldPosition location,
        out int heightMillimeters)
    {
        BaseTerrainSurfaceSample sample = world.ProbeBaseTerrainSurface(location);
        heightMillimeters = sample.HeightMillimeters;
        return sample.IsPresent;
    }

    /// <summary>Samples the current prepared base plus durable replacement terrain.</summary>
    public static bool TrySampleBaseTerrainHeight(
        this WorldPreparedView world,
        in WorldPosition location,
        out int heightMillimeters)
    {
        BaseTerrainSurfaceSample sample = world.ProbeBaseTerrainSurface(location);
        heightMillimeters = sample.HeightMillimeters;
        return sample.IsPresent;
    }

    public static bool TrySampleBaseTerrainHeight(
        this PreparedWorldComposition world,
        in WorldPosition location,
        out int heightMillimeters)
    {
        BaseTerrainSurfaceSample sample = world.ProbeBaseTerrainSurface(location);
        heightMillimeters = sample.HeightMillimeters;
        return sample.IsPresent;
    }

    internal static int SampleHeight(in WorldTerrainChunkView terrain, in WorldPosition location)
    {

        TerrainCellCoord cell = TerrainCellCoord.FromLocal(location.Local);
        int localX = location.Local.XMillimeters - cell.X * GameWorldSpatialProfile.TerrainCellEdgeUnits;
        int localZ = location.Local.ZMillimeters - cell.Z * GameWorldSpatialProfile.TerrainCellEdgeUnits;
        GetCellHeights(terrain, cell.X, cell.Z, out int northWest, out int northEast,
            out int southWest, out int southEast);
        long globalCellX = checked((long)location.Chunk.X * TerrainChunkFormat.MaterialCellEdge + cell.X);
        long globalCellZ = checked((long)location.Chunk.Z * TerrainChunkFormat.MaterialCellEdge + cell.Z);
        long numerator = IsEvenParity(globalCellX, globalCellZ)
            ? SampleEvenNumerator(localX, localZ, northWest, northEast, southWest, southEast)
            : SampleOddNumerator(localX, localZ, northWest, northEast, southWest, southEast);
        return checked((int)DivideNearestAwayFromZero(
            numerator, GameWorldSpatialProfile.TerrainCellEdgeUnits));
    }

    /// <summary>Resolves the bound numeric material of the base terrain cell beneath X/Z.</summary>
    public static bool TryResolveBaseTerrainMaterial(
        this BoundWorldPack world,
        in WorldPosition location,
        out ushort materialId)
    {
        BaseTerrainSurfaceSample sample = world.ProbeBaseTerrainSurface(location);
        materialId = sample.MaterialId;
        return sample.IsPresent;
    }

    /// <summary>Resolves current prepared base plus durable replacement terrain material.</summary>
    public static bool TryResolveBaseTerrainMaterial(
        this WorldPreparedView world,
        in WorldPosition location,
        out ushort materialId)
    {
        BaseTerrainSurfaceSample sample = world.ProbeBaseTerrainSurface(location);
        materialId = sample.MaterialId;
        return sample.IsPresent;
    }

    public static bool TryResolveBaseTerrainMaterial(
        this PreparedWorldComposition world,
        in WorldPosition location,
        out ushort materialId)
    {
        BaseTerrainSurfaceSample sample = world.ProbeBaseTerrainSurface(location);
        materialId = sample.MaterialId;
        return sample.IsPresent;
    }

    /// <summary>
    /// Emits one complete 33x33 fixed-grid base mesh. Border frames resolve a bounded immutable
    /// neighbor halo around the root chunk. All destinations are preflighted before writes. Triangle
    /// materials are parallel to the two triangles emitted for each terrain cell.
    /// </summary>
    public static bool TryBuildBaseTerrainMesh(
        this BoundWorldPack world,
        SpatialChunkKey key,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        Span<ushort> triangleMaterials,
        out TerrainBaseMeshSummary summary,
        out TerrainBaseMeshBuildError error)
    {
        ArgumentNullException.ThrowIfNull(world);
        return TryBuildBaseTerrainMesh(new TerrainMeshSource(world), key, vertices, indices,
            triangleMaterials, out summary, out error);
    }

    /// <summary>
    /// Emits one complete prepared base-terrain mesh, including durable terrain replacements and
    /// the bounded prepared neighbor halo needed for exact border frames.
    /// </summary>
    public static bool TryBuildBaseTerrainMesh(
        this WorldPreparedView world,
        SpatialChunkKey key,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        Span<ushort> triangleMaterials,
        out TerrainBaseMeshSummary summary,
        out TerrainBaseMeshBuildError error)
    {
        ArgumentNullException.ThrowIfNull(world);
        return TryBuildBaseTerrainMesh(new TerrainMeshSource(world), key, vertices, indices,
            triangleMaterials, out summary, out error);
    }

    public static bool TryBuildBaseTerrainMesh(
        this PreparedWorldComposition world,
        SpatialChunkKey key,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        Span<ushort> triangleMaterials,
        out TerrainBaseMeshSummary summary,
        out TerrainBaseMeshBuildError error)
    {
        ArgumentNullException.ThrowIfNull(world);
        return TryBuildBaseTerrainMesh(new TerrainMeshSource(world), key, vertices, indices,
            triangleMaterials, out summary, out error);
    }

    private static bool TryBuildBaseTerrainMesh(
        in TerrainMeshSource world,
        SpatialChunkKey key,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        Span<ushort> triangleMaterials,
        out TerrainBaseMeshSummary summary,
        out TerrainBaseMeshBuildError error)
    {
        summary = default;
        if (!world.TryGetBaseTerrain(key, out WorldTerrainChunkView terrain))
        {
            error = TerrainBaseMeshBuildError.MissingChunk;
            return false;
        }
        int presentCells = terrain.PresentSurfaceCount;
        int requiredTriangles = checked(presentCells * 2);
        int requiredIndices = checked(requiredTriangles * 3);
        if (vertices.Length < TerrainChunkFormat.HeightVertexCount ||
            indices.Length < requiredIndices ||
            triangleMaterials.Length < requiredTriangles)
        {
            error = TerrainBaseMeshBuildError.DestinationTooSmall;
            return false;
        }
        Span<byte> vertexBytes = MemoryMarshal.AsBytes(vertices[..TerrainChunkFormat.HeightVertexCount]);
        Span<byte> indexBytes = MemoryMarshal.AsBytes(indices[..requiredIndices]);
        Span<byte> materialBytes = MemoryMarshal.AsBytes(triangleMaterials[..requiredTriangles]);
        if (vertexBytes.Overlaps(indexBytes) || vertexBytes.Overlaps(materialBytes) ||
            indexBytes.Overlaps(materialBytes))
        {
            error = TerrainBaseMeshBuildError.OverlappingDestinations;
            return false;
        }
        int minimumHeight = int.MaxValue;
        int maximumHeight = int.MinValue;
        for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
        {
            for (byte x = 0; x < TerrainChunkFormat.HeightVertexEdge; x++)
            {
                int vertexIndex = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(x, z));
                int height = terrain.GetAbsoluteHeightMillimeters(vertexIndex);
                TerrainVertexFrame frame = CalculateVertexFrame(world, key, x, z);
                vertices[vertexIndex] = new TerrainBaseMeshVertex(
                    x * GameWorldSpatialProfile.TerrainCellEdgeUnits,
                    height,
                    z * GameWorldSpatialProfile.TerrainCellEdgeUnits,
                    frame.NormalX,
                    frame.NormalY,
                    frame.NormalZ,
                    frame.NormalY,
                    checked(-frame.NormalX),
                    0);
                if (IsReferencedByPresentCell(terrain, x, z))
                {
                    minimumHeight = Math.Min(minimumHeight, height);
                    maximumHeight = Math.Max(maximumHeight, height);
                }
            }
        }

        if (presentCells == 0)
        {
            minimumHeight = 0;
            maximumHeight = 0;
        }

        int triangle = 0;
        int indexOffset = 0;
        for (byte z = 0; z < TerrainChunkFormat.MaterialCellEdge; z++)
        {
            for (byte x = 0; x < TerrainChunkFormat.MaterialCellEdge; x++)
            {
                int materialIndex = TerrainChunkFormat.GetMaterialIndex(new TerrainCellCoord(x, z));
                if (!terrain.IsSurfacePresent(materialIndex)) continue;
                int northWest = z * TerrainChunkFormat.HeightVertexEdge + x;
                int northEast = northWest + 1;
                int southWest = northWest + TerrainChunkFormat.HeightVertexEdge;
                int southEast = southWest + 1;
                long globalCellX = checked((long)key.Coord.X * TerrainChunkFormat.MaterialCellEdge + x);
                long globalCellZ = checked((long)key.Coord.Z * TerrainChunkFormat.MaterialCellEdge + z);
                if (IsEvenParity(globalCellX, globalCellZ))
                {
                    WriteTriangle(indices, ref indexOffset, northWest, southWest, southEast);
                    WriteTriangle(indices, ref indexOffset, northWest, southEast, northEast);
                }
                else
                {
                    WriteTriangle(indices, ref indexOffset, northWest, southWest, northEast);
                    WriteTriangle(indices, ref indexOffset, northEast, southWest, southEast);
                }
                ushort material = terrain.GetMaterial(materialIndex);
                triangleMaterials[triangle++] = material;
                triangleMaterials[triangle++] = material;
            }
        }

        summary = new TerrainBaseMeshSummary(
            TerrainChunkFormat.HeightVertexCount,
            requiredTriangles,
            requiredIndices,
            minimumHeight,
            maximumHeight);
        error = TerrainBaseMeshBuildError.None;
        return true;
    }

    private static long SampleEvenNumerator(
        int x,
        int z,
        int northWest,
        int northEast,
        int southWest,
        int southEast)
    {
        int edge = GameWorldSpatialProfile.TerrainCellEdgeUnits;
        return z <= x
            ? checked((long)northWest * edge + (long)(northEast - northWest) * x +
                (long)(southEast - northEast) * z)
            : checked((long)northWest * edge + (long)(southEast - southWest) * x +
                (long)(southWest - northWest) * z);
    }

    private static long SampleOddNumerator(
        int x,
        int z,
        int northWest,
        int northEast,
        int southWest,
        int southEast)
    {
        int edge = GameWorldSpatialProfile.TerrainCellEdgeUnits;
        return x + z <= edge
            ? checked((long)northWest * edge + (long)(northEast - northWest) * x +
                (long)(southWest - northWest) * z)
            : checked((long)southEast * edge + (long)(southWest - southEast) * (edge - x) +
                (long)(northEast - southEast) * (edge - z));
    }

    private static long DivideNearestAwayFromZero(long numerator, int positiveDivisor)
    {
        long quotient = numerator / positiveDivisor;
        long remainder = numerator % positiveDivisor;
        if (remainder == 0) return quotient;
        long magnitude = remainder < 0 ? -remainder : remainder;
        if (magnitude * 2 >= positiveDivisor) quotient += numerator < 0 ? -1 : 1;
        return quotient;
    }

    private static TerrainVertexFrame CalculateVertexFrame(
        in TerrainMeshSource world,
        SpatialChunkKey key,
        byte localVertexX,
        byte localVertexZ)
    {
        long globalVertexX = checked((long)key.Coord.X * TerrainChunkFormat.MaterialCellEdge + localVertexX);
        long globalVertexZ = checked((long)key.Coord.Z * TerrainChunkFormat.MaterialCellEdge + localVertexZ);
        long normalX = 0;
        long normalY = 0;
        long normalZ = 0;
        for (long cellZ = globalVertexZ - 1; cellZ <= globalVertexZ; cellZ++)
        {
            for (long cellX = globalVertexX - 1; cellX <= globalVertexX; cellX++)
            {
                if (!TryGetGlobalCell(world, cellX, cellZ, out WorldTerrainChunkView cellTerrain,
                        out byte localCellX, out byte localCellZ))
                    continue;
                if (!cellTerrain.IsSurfacePresent(TerrainChunkFormat.GetMaterialIndex(
                        new TerrainCellCoord(localCellX, localCellZ))))
                    continue;
                GetCellHeights(cellTerrain, localCellX, localCellZ, out int northWest,
                    out int northEast, out int southWest, out int southEast);
                int targetCorner = checked((int)(globalVertexX - cellX)) switch
                {
                    0 when globalVertexZ - cellZ == 0 => NorthWest,
                    1 when globalVertexZ - cellZ == 0 => NorthEast,
                    0 when globalVertexZ - cellZ == 1 => SouthWest,
                    1 when globalVertexZ - cellZ == 1 => SouthEast,
                    _ => throw new InvalidOperationException("Global terrain vertex did not map to a cell corner."),
                };
                if (IsEvenParity(cellX, cellZ))
                {
                    AccumulateTriangle(targetCorner, NorthWest, SouthWest, SouthEast,
                        northWest, northEast, southWest, southEast, ref normalX, ref normalY, ref normalZ);
                    AccumulateTriangle(targetCorner, NorthWest, SouthEast, NorthEast,
                        northWest, northEast, southWest, southEast, ref normalX, ref normalY, ref normalZ);
                }
                else
                {
                    AccumulateTriangle(targetCorner, NorthWest, SouthWest, NorthEast,
                        northWest, northEast, southWest, southEast, ref normalX, ref normalY, ref normalZ);
                    AccumulateTriangle(targetCorner, NorthEast, SouthWest, SouthEast,
                        northWest, northEast, southWest, southEast, ref normalX, ref normalY, ref normalZ);
                }
            }
        }
        if (normalY == 0) return new TerrainVertexFrame(0, 1, 0);
        if (normalY < 0)
            throw new InvalidOperationException("A validated base-terrain vertex had inverted triangle support.");
        return new TerrainVertexFrame(checked((int)normalX), checked((int)normalY), checked((int)normalZ));
    }

    private static bool TryGetGlobalCell(
        in TerrainMeshSource world,
        long globalCellX,
        long globalCellZ,
        out WorldTerrainChunkView terrain,
        out byte localCellX,
        out byte localCellZ)
    {
        long chunkX = FloorDivide(globalCellX, TerrainChunkFormat.MaterialCellEdge);
        long chunkZ = FloorDivide(globalCellZ, TerrainChunkFormat.MaterialCellEdge);
        if (chunkX is < int.MinValue or > int.MaxValue || chunkZ is < int.MinValue or > int.MaxValue)
        {
            terrain = default;
            localCellX = 0;
            localCellZ = 0;
            return false;
        }
        localCellX = checked((byte)(globalCellX - chunkX * TerrainChunkFormat.MaterialCellEdge));
        localCellZ = checked((byte)(globalCellZ - chunkZ * TerrainChunkFormat.MaterialCellEdge));
        return world.TryGetBaseTerrain(new SpatialChunkKey(world.Space, checked((int)chunkX), checked((int)chunkZ)),
            out terrain);
    }

    private readonly struct TerrainMeshSource
    {
        private readonly BoundWorldPack? _baseWorld;
        private readonly WorldPreparedView? _preparedWorld;
        private readonly PreparedWorldComposition? _composition;

        internal TerrainMeshSource(BoundWorldPack world)
        {
            _baseWorld = world;
            _preparedWorld = null;
            _composition = null;
            Space = world.Space;
        }

        internal TerrainMeshSource(WorldPreparedView world)
        {
            _baseWorld = null;
            _preparedWorld = world;
            _composition = null;
            Space = world.Space;
        }

        internal TerrainMeshSource(PreparedWorldComposition world)
        {
            _baseWorld = null;
            _preparedWorld = null;
            _composition = world;
            Space = world.Space;
        }

        internal SpatialSpaceId Space { get; }

        internal bool TryGetBaseTerrain(SpatialChunkKey key, out WorldTerrainChunkView terrain) =>
            _composition is not null
                ? _composition.TryGetBaseTerrain(key, out terrain)
                : _preparedWorld is not null
                    ? _preparedWorld.TryGetBaseTerrain(key, out terrain)
                    : _baseWorld!.TryGetBaseTerrain(key, out terrain);
    }

    private static long FloorDivide(long value, int positiveDivisor)
    {
        long quotient = value / positiveDivisor;
        if (value % positiveDivisor < 0) quotient--;
        return quotient;
    }

    private static bool IsEvenParity(long globalCellX, long globalCellZ) =>
        ((globalCellX ^ globalCellZ) & 1) == 0;

    private static bool IsReferencedByPresentCell(
        in WorldTerrainChunkView terrain,
        byte vertexX,
        byte vertexZ)
    {
        int minimumX = Math.Max(0, (int)vertexX - 1);
        int maximumX = Math.Min(TerrainChunkFormat.MaterialCellEdge - 1, (int)vertexX);
        int minimumZ = Math.Max(0, (int)vertexZ - 1);
        int maximumZ = Math.Min(TerrainChunkFormat.MaterialCellEdge - 1, (int)vertexZ);
        for (int z = minimumZ; z <= maximumZ; z++)
        {
            for (int x = minimumX; x <= maximumX; x++)
            {
                if (terrain.IsSurfacePresent(TerrainChunkFormat.GetMaterialIndex(
                        new TerrainCellCoord(checked((byte)x), checked((byte)z)))))
                    return true;
            }
        }
        return false;
    }

    private static BaseTerrainSurfaceSample Probe(
        in TerrainMeshSource world,
        in WorldPosition location)
    {
        if (!world.TryGetBaseTerrain(location.ToChunkKey(world.Space), out WorldTerrainChunkView terrain))
            return BaseTerrainSurfaceSample.Unavailable;
        TerrainCellCoord cell = TerrainCellCoord.FromLocal(location.Local);
        int materialIndex = TerrainChunkFormat.GetMaterialIndex(cell);
        if (!terrain.IsSurfacePresent(materialIndex)) return BaseTerrainSurfaceSample.Absent;
        return new BaseTerrainSurfaceSample(
            BaseTerrainSurfaceStatus.Present,
            SampleHeight(terrain, location),
            terrain.GetMaterial(materialIndex));
    }

    private static void GetCellHeights(
        WorldTerrainChunkView terrain,
        byte localCellX,
        byte localCellZ,
        out int northWest,
        out int northEast,
        out int southWest,
        out int southEast)
    {
        byte east = checked((byte)(localCellX + 1));
        byte south = checked((byte)(localCellZ + 1));
        northWest = terrain.GetAbsoluteHeightMillimeters(TerrainChunkFormat.GetHeightIndex(
            new TerrainVertexCoord(localCellX, localCellZ)));
        northEast = terrain.GetAbsoluteHeightMillimeters(TerrainChunkFormat.GetHeightIndex(
            new TerrainVertexCoord(east, localCellZ)));
        southWest = terrain.GetAbsoluteHeightMillimeters(TerrainChunkFormat.GetHeightIndex(
            new TerrainVertexCoord(localCellX, south)));
        southEast = terrain.GetAbsoluteHeightMillimeters(TerrainChunkFormat.GetHeightIndex(
            new TerrainVertexCoord(east, south)));
    }

    private static void AccumulateTriangle(
        int targetCorner,
        int first,
        int second,
        int third,
        int northWest,
        int northEast,
        int southWest,
        int southEast,
        ref long normalX,
        ref long normalY,
        ref long normalZ)
    {
        if (targetCorner != first && targetCorner != second && targetCorner != third) return;
        GetCorner(first, northWest, northEast, southWest, southEast, out int firstX, out int firstY,
            out int firstZ);
        GetCorner(second, northWest, northEast, southWest, southEast, out int secondX, out int secondY,
            out int secondZ);
        GetCorner(third, northWest, northEast, southWest, southEast, out int thirdX, out int thirdY,
            out int thirdZ);
        long bax = secondX - firstX;
        long bay = (long)secondY - firstY;
        long baz = secondZ - firstZ;
        long cax = thirdX - firstX;
        long cay = (long)thirdY - firstY;
        long caz = thirdZ - firstZ;
        normalX = checked(normalX + bay * caz - baz * cay);
        normalY = checked(normalY + baz * cax - bax * caz);
        normalZ = checked(normalZ + bax * cay - bay * cax);
    }

    private static void GetCorner(
        int corner,
        int northWest,
        int northEast,
        int southWest,
        int southEast,
        out int x,
        out int y,
        out int z)
    {
        int edge = GameWorldSpatialProfile.TerrainCellEdgeUnits;
        switch (corner)
        {
            case NorthWest:
                x = 0; y = northWest; z = 0; return;
            case NorthEast:
                x = edge; y = northEast; z = 0; return;
            case SouthWest:
                x = 0; y = southWest; z = edge; return;
            case SouthEast:
                x = edge; y = southEast; z = edge; return;
            default:
                throw new ArgumentOutOfRangeException(nameof(corner));
        }
    }

    private static void WriteTriangle(Span<ushort> indices, ref int offset, int first, int second, int third)
    {
        indices[offset++] = checked((ushort)first);
        indices[offset++] = checked((ushort)second);
        indices[offset++] = checked((ushort)third);
    }

    private readonly record struct TerrainVertexFrame(int NormalX, int NormalY, int NormalZ);
}
