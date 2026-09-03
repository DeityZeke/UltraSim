using System.Buffers.Binary;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

public sealed partial class WorldPackBuilder
{
    private readonly List<SourceTerrainSurfaceBand> _additionalTerrainBands = [];
    private readonly Dictionary<SurfaceFragmentKey, SourceTerrainSurfaceBand> _additionalTerrainByFragment = [];
    private readonly Dictionary<SpatialChunkKey, int> _additionalTerrainCountsByChunk = [];

    /// <summary>
    /// Adds one canonical chunk fragment of a logical authored terrain shell. The same shell ID
    /// may occur in several chunks, but never more than once in the same chunk. Ordering at rest
    /// is chunk key then shell ID; neither identity implies vertical ordering.
    /// </summary>
    public bool TryAddAdditionalTerrainSurfaceBand(
        SpatialChunkKey key,
        WorldSurfaceId surfaceId,
        TerrainSurfacePatchBounds bounds,
        WorldTerrainSurfaceFaces faces,
        ReadOnlySpan<ulong> surfacePresence,
        ReadOnlySpan<TerrainSurfaceEdgeConnection> edgeConnections,
        ReadOnlySpan<ushort> topMaterials,
        TerrainHeightPlaneBase topHeightBase,
        ReadOnlySpan<TerrainHeightOffset> topHeights,
        ReadOnlySpan<ushort> bottomMaterials,
        TerrainHeightPlaneBase bottomHeightBase,
        ReadOnlySpan<TerrainHeightOffset> bottomHeights,
        ushort sideMaterialId,
        out WorldPackBuildError error)
    {
        if (key.Space != _space)
        {
            error = WorldPackBuildError.SpaceMismatch;
            return false;
        }
        if (!_byKey.ContainsKey(key))
        {
            error = WorldPackBuildError.MissingChunk;
            return false;
        }
        var fragmentKey = new SurfaceFragmentKey(key, surfaceId);
        if (!surfaceId.IsValid || !bounds.IsValid ||
            faces == WorldTerrainSurfaceFaces.None ||
            (faces & ~(WorldTerrainSurfaceFaces.Top | WorldTerrainSurfaceFaces.Bottom)) != 0 ||
            _additionalTerrainByFragment.ContainsKey(fragmentKey))
        {
            error = WorldPackBuildError.InvalidAdditionalTerrainSurface;
            return false;
        }
        if (_additionalTerrainBands.Count >= _limits.MaximumAdditionalTerrainSurfaceFragments)
        {
            error = WorldPackBuildError.CapacityExceeded;
            return false;
        }
        _additionalTerrainCountsByChunk.TryGetValue(key, out int fragmentsInChunk);
        if (fragmentsInChunk >= _limits.MaximumAdditionalTerrainSurfaceFragmentsPerChunk)
        {
            error = WorldPackBuildError.CapacityExceeded;
            return false;
        }
        if (surfacePresence.Length != bounds.PresenceWordCount ||
            !PresenceIsCanonicalAndNonempty(surfacePresence, bounds.CellCount) ||
            edgeConnections.Length != bounds.CellCount ||
            !ConnectionsAreLocallyCanonical(bounds, surfacePresence, edgeConnections))
        {
            error = WorldPackBuildError.InvalidAdditionalTerrainSurface;
            return false;
        }

        bool hasTop = (faces & WorldTerrainSurfaceFaces.Top) != 0;
        bool hasBottom = (faces & WorldTerrainSurfaceFaces.Bottom) != 0;
        if (!ValidateFace(hasTop, bounds, topMaterials, topHeightBase, topHeights) ||
            !ValidateFace(hasBottom, bounds, bottomMaterials, bottomHeightBase, bottomHeights) ||
            hasTop && hasBottom && sideMaterialId == 0 ||
            (!hasTop || !hasBottom) && sideMaterialId != 0)
        {
            error = WorldPackBuildError.InvalidAdditionalTerrainSurface;
            return false;
        }
        if (hasTop && hasBottom)
        {
            for (int vertex = 0; vertex < bounds.VertexCount; vertex++)
            {
                int top = checked(topHeightBase.Millimeters + topHeights[vertex].OffsetMillimeters);
                int bottom = checked(bottomHeightBase.Millimeters + bottomHeights[vertex].OffsetMillimeters);
                if (top <= bottom)
                {
                    error = WorldPackBuildError.InvalidAdditionalTerrainSurface;
                    return false;
                }
            }
        }

        var fragment = new SourceTerrainSurfaceBand(
            key, surfaceId, bounds, faces, surfacePresence.ToArray(), edgeConnections.ToArray(),
            topMaterials.ToArray(),
            topHeightBase, topHeights.ToArray(), bottomMaterials.ToArray(), bottomHeightBase,
            bottomHeights.ToArray(), sideMaterialId);
        _additionalTerrainBands.Add(fragment);
        _additionalTerrainByFragment.Add(fragmentKey, fragment);
        _additionalTerrainCountsByChunk[key] = fragmentsInChunk + 1;
        error = WorldPackBuildError.None;
        return true;
    }

    private static bool ValidateFace(
        bool present,
        TerrainSurfacePatchBounds bounds,
        ReadOnlySpan<ushort> materials,
        TerrainHeightPlaneBase heightBase,
        ReadOnlySpan<TerrainHeightOffset> heights)
    {
        if (!present) return materials.IsEmpty && heights.IsEmpty;
        if (materials.Length != bounds.CellCount || heights.Length != bounds.VertexCount)
            return false;
        for (int index = 0; index < materials.Length; index++)
            if (materials[index] == 0) return false;
        for (int index = 0; index < heights.Length; index++)
        {
            long absolute = (long)heightBase.Millimeters + heights[index].OffsetMillimeters;
            if (absolute is < int.MinValue or > int.MaxValue) return false;
        }
        return true;
    }

    private static bool PresenceIsCanonicalAndNonempty(ReadOnlySpan<ulong> words, int bits)
    {
        bool any = false;
        for (int index = 0; index < words.Length; index++) any |= words[index] != 0;
        if (!any) return false;
        int remainder = bits & 63;
        return remainder == 0 || (words[^1] & ~((1UL << remainder) - 1)) == 0;
    }

    private static bool ConnectionsAreLocallyCanonical(
        TerrainSurfacePatchBounds bounds,
        ReadOnlySpan<ulong> presence,
        ReadOnlySpan<TerrainSurfaceEdgeConnection> connections)
    {
        const TerrainSurfaceEdgeConnection known = TerrainSurfaceEdgeConnection.North |
            TerrainSurfaceEdgeConnection.East | TerrainSurfaceEdgeConnection.South |
            TerrainSurfaceEdgeConnection.West;
        for (int z = 0; z < bounds.CellDepth; z++)
        for (int x = 0; x < bounds.CellWidth; x++)
        {
            int cell = z * bounds.CellWidth + x;
            TerrainSurfaceEdgeConnection value = connections[cell];
            bool present = (presence[cell >> 6] & (1UL << (cell & 63))) != 0;
            if ((value & ~known) != 0 || !present && value != 0) return false;
            if ((value & TerrainSurfaceEdgeConnection.North) != 0 &&
                bounds.MinimumCellZ + z != 0 ||
                (value & TerrainSurfaceEdgeConnection.East) != 0 &&
                bounds.MinimumCellX + x != TerrainChunkFormat.MaterialCellEdge - 1 ||
                (value & TerrainSurfaceEdgeConnection.South) != 0 &&
                bounds.MinimumCellZ + z != TerrainChunkFormat.MaterialCellEdge - 1 ||
                (value & TerrainSurfaceEdgeConnection.West) != 0 &&
                bounds.MinimumCellX + x != 0)
                return false;
        }
        return true;
    }

    internal bool ValidateAdditionalTerrainConnections()
    {
        for (int fragmentIndex = 0; fragmentIndex < _additionalTerrainBands.Count; fragmentIndex++)
        {
            SourceTerrainSurfaceBand fragment = _additionalTerrainBands[fragmentIndex];
            for (int z = 0; z < fragment.Bounds.CellDepth; z++)
            for (int x = 0; x < fragment.Bounds.CellWidth; x++)
            {
                TerrainSurfaceEdgeConnection connections = fragment.EdgeConnections[
                    z * fragment.Bounds.CellWidth + x];
                foreach (TerrainSurfaceEdgeConnection direction in ConnectionDirections)
                {
                    if ((connections & direction) == 0) continue;
                    if (!TryGetNeighbor(fragment.Key, direction, out SpatialChunkKey neighborKey) ||
                        !TryFindFragment(neighborKey, fragment.SurfaceId, out SourceTerrainSurfaceBand neighbor) ||
                        !FragmentsConnect(fragment, x, z, direction, neighbor)) return false;
                }
            }
        }
        return true;
    }

    private bool TryFindFragment(
        SpatialChunkKey key,
        WorldSurfaceId id,
        out SourceTerrainSurfaceBand fragment) =>
        _additionalTerrainByFragment.TryGetValue(new SurfaceFragmentKey(key, id), out fragment!);

    private static bool FragmentsConnect(
        SourceTerrainSurfaceBand source,
        int sourceX,
        int sourceZ,
        TerrainSurfaceEdgeConnection direction,
        SourceTerrainSurfaceBand target)
    {
        if (source.Faces != target.Faces) return false;
        int worldCellX = source.Bounds.MinimumCellX + sourceX;
        int worldCellZ = source.Bounds.MinimumCellZ + sourceZ;
        int targetCellX = direction switch
        {
            TerrainSurfaceEdgeConnection.East => 0,
            TerrainSurfaceEdgeConnection.West => TerrainChunkFormat.MaterialCellEdge - 1,
            _ => worldCellX,
        };
        int targetCellZ = direction switch
        {
            TerrainSurfaceEdgeConnection.South => 0,
            TerrainSurfaceEdgeConnection.North => TerrainChunkFormat.MaterialCellEdge - 1,
            _ => worldCellZ,
        };
        int localX = targetCellX - target.Bounds.MinimumCellX;
        int localZ = targetCellZ - target.Bounds.MinimumCellZ;
        if ((uint)localX >= target.Bounds.CellWidth || (uint)localZ >= target.Bounds.CellDepth)
            return false;
        int targetCell = localZ * target.Bounds.CellWidth + localX;
        if ((target.SurfacePresence[targetCell >> 6] & (1UL << (targetCell & 63))) == 0 ||
            (target.EdgeConnections[targetCell] & Opposite(direction)) == 0) return false;
        if ((source.Faces & WorldTerrainSurfaceFaces.Top) != 0 &&
            !EdgeHeightsMatch(source, sourceX, sourceZ, target, localX, localZ, direction, top: true))
            return false;
        return (source.Faces & WorldTerrainSurfaceFaces.Bottom) == 0 ||
            EdgeHeightsMatch(source, sourceX, sourceZ, target, localX, localZ, direction, top: false);
    }

    private static bool EdgeHeightsMatch(
        SourceTerrainSurfaceBand source, int sourceX, int sourceZ,
        SourceTerrainSurfaceBand target, int targetX, int targetZ,
        TerrainSurfaceEdgeConnection direction, bool top)
    {
        GetEdgeVertices(sourceX, sourceZ, direction, out int sx0, out int sz0, out int sx1, out int sz1);
        GetEdgeVertices(targetX, targetZ, Opposite(direction),
            out int tx0, out int tz0, out int tx1, out int tz1);
        return SourceHeight(source, sx0, sz0, top) == SourceHeight(target, tx1, tz1, top) &&
            SourceHeight(source, sx1, sz1, top) == SourceHeight(target, tx0, tz0, top);
    }

    private static int SourceHeight(SourceTerrainSurfaceBand source, int x, int z, bool top)
    {
        TerrainHeightPlaneBase heightBase = top ? source.TopHeightBase : source.BottomHeightBase;
        TerrainHeightOffset[] heights = top ? source.TopHeights : source.BottomHeights;
        int index = z * (source.Bounds.CellWidth + 1) + x;
        return checked(heightBase.Millimeters + heights[index].OffsetMillimeters);
    }

    internal static readonly TerrainSurfaceEdgeConnection[] ConnectionDirections =
    [
        TerrainSurfaceEdgeConnection.North,
        TerrainSurfaceEdgeConnection.East,
        TerrainSurfaceEdgeConnection.South,
        TerrainSurfaceEdgeConnection.West,
    ];

    internal static TerrainSurfaceEdgeConnection Opposite(TerrainSurfaceEdgeConnection direction) =>
        direction switch
        {
            TerrainSurfaceEdgeConnection.North => TerrainSurfaceEdgeConnection.South,
            TerrainSurfaceEdgeConnection.East => TerrainSurfaceEdgeConnection.West,
            TerrainSurfaceEdgeConnection.South => TerrainSurfaceEdgeConnection.North,
            TerrainSurfaceEdgeConnection.West => TerrainSurfaceEdgeConnection.East,
            _ => TerrainSurfaceEdgeConnection.None,
        };

    internal static bool TryGetNeighbor(
        SpatialChunkKey key,
        TerrainSurfaceEdgeConnection direction,
        out SpatialChunkKey neighbor)
    {
        long x = key.Coord.X;
        long z = key.Coord.Z;
        if (direction == TerrainSurfaceEdgeConnection.North) z--;
        else if (direction == TerrainSurfaceEdgeConnection.East) x++;
        else if (direction == TerrainSurfaceEdgeConnection.South) z++;
        else if (direction == TerrainSurfaceEdgeConnection.West) x--;
        if (x is < int.MinValue or > int.MaxValue || z is < int.MinValue or > int.MaxValue)
        {
            neighbor = default;
            return false;
        }
        neighbor = new SpatialChunkKey(key.Space, (int)x, (int)z);
        return true;
    }

    internal static void GetEdgeVertices(
        int x, int z, TerrainSurfaceEdgeConnection direction,
        out int x0, out int z0, out int x1, out int z1)
    {
        switch (direction)
        {
            case TerrainSurfaceEdgeConnection.North:
                x0 = x; z0 = z; x1 = x + 1; z1 = z; break;
            case TerrainSurfaceEdgeConnection.East:
                x0 = x + 1; z0 = z; x1 = x + 1; z1 = z + 1; break;
            case TerrainSurfaceEdgeConnection.South:
                x0 = x + 1; z0 = z + 1; x1 = x; z1 = z + 1; break;
            default:
                x0 = x; z0 = z + 1; x1 = x; z1 = z; break;
        }
    }

    private readonly record struct SurfaceFragmentKey(SpatialChunkKey Key, WorldSurfaceId SurfaceId);

    internal sealed record SourceTerrainSurfaceBand(
        SpatialChunkKey Key,
        WorldSurfaceId SurfaceId,
        TerrainSurfacePatchBounds Bounds,
        WorldTerrainSurfaceFaces Faces,
        ulong[] SurfacePresence,
        TerrainSurfaceEdgeConnection[] EdgeConnections,
        ushort[] TopMaterials,
        TerrainHeightPlaneBase TopHeightBase,
        TerrainHeightOffset[] TopHeights,
        ushort[] BottomMaterials,
        TerrainHeightPlaneBase BottomHeightBase,
        TerrainHeightOffset[] BottomHeights,
        ushort SideMaterialId);
}

internal static class AdditionalTerrainSurfaceCodec
{
    internal static bool TryMeasure(
        int chunkCount,
        IReadOnlyList<WorldPackBuilder.SourceTerrainSurfaceBand> fragments,
        out int length)
    {
        if (fragments.Count == 0)
        {
            length = 0;
            return true;
        }
        try
        {
            int value = checked(AdditionalTerrainSurfaceFormat.HeaderBytes +
                chunkCount * AdditionalTerrainSurfaceFormat.RangeBytes +
                fragments.Count * AdditionalTerrainSurfaceFormat.DescriptorBytes);
            for (int index = 0; index < fragments.Count; index++)
            {
                WorldPackBuilder.SourceTerrainSurfaceBand fragment = fragments[index];
                value = checked(value + fragment.Bounds.PresenceWordCount * sizeof(ulong));
                value = checked(value + fragment.Bounds.CellCount);
                if ((fragment.Faces & WorldTerrainSurfaceFaces.Top) != 0)
                    value = checked(value + fragment.Bounds.CellCount * sizeof(ushort) + sizeof(int) +
                        fragment.Bounds.VertexCount * sizeof(ushort));
                if ((fragment.Faces & WorldTerrainSurfaceFaces.Bottom) != 0)
                    value = checked(value + fragment.Bounds.CellCount * sizeof(ushort) + sizeof(int) +
                        fragment.Bounds.VertexCount * sizeof(ushort));
            }
            length = value;
            return true;
        }
        catch (OverflowException)
        {
            length = 0;
            return false;
        }
    }

    internal static bool TryCompile(
        ReadOnlySpan<SpatialChunkKey> orderedKeys,
        IReadOnlyList<WorldPackBuilder.SourceTerrainSurfaceBand> fragments,
        out byte[]? payload)
    {
        payload = null;
        if (fragments.Count == 0) return true;
        if (!TryMeasure(orderedKeys.Length, fragments, out int length)) return false;
        WorldPackBuilder.SourceTerrainSurfaceBand[] ordered = fragments.ToArray();
        Array.Sort(ordered, static (left, right) =>
        {
            int chunk = left.Key.CompareTo(right.Key);
            return chunk != 0 ? chunk : left.SurfaceId.CompareTo(right.SurfaceId);
        });
        var bytes = new byte[length];
        int rangesOffset = AdditionalTerrainSurfaceFormat.HeaderBytes;
        int descriptorsOffset = checked(rangesOffset + orderedKeys.Length * AdditionalTerrainSurfaceFormat.RangeBytes);
        int dataOffset = checked(descriptorsOffset + ordered.Length * AdditionalTerrainSurfaceFormat.DescriptorBytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes, orderedKeys.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), ordered.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), AdditionalTerrainSurfaceFormat.RangeBytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), AdditionalTerrainSurfaceFormat.DescriptorBytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), rangesOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), descriptorsOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), dataOffset);

        int fragmentIndex = 0;
        int cursor = dataOffset;
        for (int chunk = 0; chunk < orderedKeys.Length; chunk++)
        {
            int start = fragmentIndex;
            while (fragmentIndex < ordered.Length && ordered[fragmentIndex].Key == orderedKeys[chunk])
            {
                WriteFragment(bytes, descriptorsOffset, fragmentIndex, ordered[fragmentIndex], ref cursor);
                fragmentIndex++;
            }
            Span<byte> range = bytes.AsSpan(rangesOffset + chunk * AdditionalTerrainSurfaceFormat.RangeBytes);
            BinaryPrimitives.WriteInt32LittleEndian(range, start);
            BinaryPrimitives.WriteInt32LittleEndian(range[4..], fragmentIndex - start);
        }
        if (fragmentIndex != ordered.Length || cursor != bytes.Length)
            throw new InvalidOperationException("Additional terrain compilation diverged from canonical measurement.");
        payload = bytes;
        return true;
    }

    private static void WriteFragment(
        Span<byte> bytes,
        int descriptorsOffset,
        int index,
        WorldPackBuilder.SourceTerrainSurfaceBand fragment,
        ref int cursor)
    {
        Span<byte> descriptor = bytes.Slice(
            descriptorsOffset + index * AdditionalTerrainSurfaceFormat.DescriptorBytes,
            AdditionalTerrainSurfaceFormat.DescriptorBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(descriptor, fragment.SurfaceId.Value);
        descriptor[8] = fragment.Bounds.MinimumCellX;
        descriptor[9] = fragment.Bounds.MinimumCellZ;
        descriptor[10] = fragment.Bounds.CellWidth;
        descriptor[11] = fragment.Bounds.CellDepth;
        descriptor[12] = (byte)fragment.Faces;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[14..], fragment.SideMaterialId);
        BinaryPrimitives.WriteInt32LittleEndian(descriptor[16..], cursor);
        for (int word = 0; word < fragment.SurfacePresence.Length; word++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[cursor..], fragment.SurfacePresence[word]);
            cursor += sizeof(ulong);
        }
        BinaryPrimitives.WriteInt32LittleEndian(descriptor[36..], cursor);
        for (int cell = 0; cell < fragment.EdgeConnections.Length; cell++)
            bytes[cursor++] = (byte)fragment.EdgeConnections[cell];
        WriteFace(bytes, descriptor, fragment.Faces, WorldTerrainSurfaceFaces.Top,
            fragment.TopMaterials, fragment.TopHeightBase, fragment.TopHeights, 20, 24, ref cursor);
        WriteFace(bytes, descriptor, fragment.Faces, WorldTerrainSurfaceFaces.Bottom,
            fragment.BottomMaterials, fragment.BottomHeightBase, fragment.BottomHeights, 28, 32, ref cursor);
        BinaryPrimitives.WriteInt32LittleEndian(descriptor[40..], cursor);
    }

    private static void WriteFace(
        Span<byte> bytes,
        Span<byte> descriptor,
        WorldTerrainSurfaceFaces faces,
        WorldTerrainSurfaceFaces face,
        ushort[] materials,
        TerrainHeightPlaneBase heightBase,
        TerrainHeightOffset[] heights,
        int materialField,
        int heightField,
        ref int cursor)
    {
        if ((faces & face) == 0) return;
        BinaryPrimitives.WriteInt32LittleEndian(descriptor[materialField..], cursor);
        for (int material = 0; material < materials.Length; material++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[cursor..], materials[material]);
            cursor += sizeof(ushort);
        }
        BinaryPrimitives.WriteInt32LittleEndian(descriptor[heightField..], cursor);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[cursor..], heightBase.Millimeters);
        cursor += sizeof(int);
        for (int height = 0; height < heights.Length; height++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[cursor..], heights[height].Units);
            cursor += sizeof(ushort);
        }
    }

    internal static bool TryValidateSection(
        ReadOnlySpan<byte> pack,
        WorldPackMetadata metadata,
        WorldPackSectionMetadata section,
        WorldPackLimits limits)
    {
        try
        {
            return TryValidateSectionCore(pack, metadata, section, limits);
        }
        catch (OverflowException) { return false; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool TryValidateSectionCore(
        ReadOnlySpan<byte> pack,
        WorldPackMetadata metadata,
        WorldPackSectionMetadata section,
        WorldPackLimits limits)
    {
        if (section.Count > (ulong)limits.MaximumAdditionalTerrainSurfaceFragments ||
            section.Length < AdditionalTerrainSurfaceFormat.HeaderBytes) return false;
        ReadOnlySpan<byte> payload = pack.Slice(section.Offset, section.Length);
        int chunkCount = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int fragmentCount = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        int rangesOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[16..]);
        int descriptorsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[20..]);
        int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[24..]);
        if (chunkCount != metadata.ChunkCount || fragmentCount <= 0 ||
            (ulong)fragmentCount != section.Count ||
            BinaryPrimitives.ReadInt32LittleEndian(payload[8..]) != AdditionalTerrainSurfaceFormat.RangeBytes ||
            BinaryPrimitives.ReadInt32LittleEndian(payload[12..]) != AdditionalTerrainSurfaceFormat.DescriptorBytes ||
            rangesOffset != AdditionalTerrainSurfaceFormat.HeaderBytes ||
            descriptorsOffset != rangesOffset + chunkCount * AdditionalTerrainSurfaceFormat.RangeBytes ||
            dataOffset != descriptorsOffset + fragmentCount * AdditionalTerrainSurfaceFormat.DescriptorBytes ||
            BinaryPrimitives.ReadInt32LittleEndian(payload[28..]) != 0 || dataOffset > payload.Length)
            return false;

        int expectedStart = 0;
        int expectedData = dataOffset;
        for (int chunk = 0; chunk < chunkCount; chunk++)
        {
            ReadOnlySpan<byte> range = payload.Slice(
                rangesOffset + chunk * AdditionalTerrainSurfaceFormat.RangeBytes,
                AdditionalTerrainSurfaceFormat.RangeBytes);
            int start = BinaryPrimitives.ReadInt32LittleEndian(range);
            int count = BinaryPrimitives.ReadInt32LittleEndian(range[4..]);
            if (start != expectedStart || count < 0 ||
                count > limits.MaximumAdditionalTerrainSurfaceFragmentsPerChunk ||
                start > fragmentCount - count) return false;
            ulong priorId = 0;
            for (int local = 0; local < count; local++)
            {
                ReadOnlySpan<byte> descriptor = payload.Slice(
                    descriptorsOffset + (start + local) * AdditionalTerrainSurfaceFormat.DescriptorBytes,
                    AdditionalTerrainSurfaceFormat.DescriptorBytes);
                ulong id = BinaryPrimitives.ReadUInt64LittleEndian(descriptor);
                if (id == 0 || id <= priorId || descriptor[13] != 0 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(descriptor[44..]) != 0) return false;
                priorId = id;
                TerrainSurfacePatchBounds bounds;
                try { bounds = new(descriptor[8], descriptor[9], descriptor[10], descriptor[11]); }
                catch (ArgumentOutOfRangeException) { return false; }
                var faces = (WorldTerrainSurfaceFaces)descriptor[12];
                if (faces == WorldTerrainSurfaceFaces.None ||
                    (faces & ~(WorldTerrainSurfaceFaces.Top | WorldTerrainSurfaceFaces.Bottom)) != 0 ||
                    BinaryPrimitives.ReadInt32LittleEndian(descriptor[16..]) != expectedData)
                    return false;
                int presenceBytes = bounds.PresenceWordCount * sizeof(ulong);
                if (expectedData > payload.Length - presenceBytes) return false;
                ReadOnlySpan<byte> presence = payload.Slice(expectedData, presenceBytes);
                if (!ValidatePresence(presence, bounds.CellCount)) return false;
                expectedData += presenceBytes;
                if (BinaryPrimitives.ReadInt32LittleEndian(descriptor[36..]) != expectedData ||
                    expectedData > payload.Length - bounds.CellCount ||
                    !ValidateLocalConnections(bounds, presence,
                        payload.Slice(expectedData, bounds.CellCount))) return false;
                expectedData += bounds.CellCount;
                bool top = (faces & WorldTerrainSurfaceFaces.Top) != 0;
                bool bottom = (faces & WorldTerrainSurfaceFaces.Bottom) != 0;
                ushort sideMaterial = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[14..]);
                if (top && bottom ? sideMaterial == 0 : sideMaterial != 0) return false;
                if (!ValidateFace(payload, descriptor, bounds, top, 20, 24, ref expectedData,
                        out int topHeightOffset) ||
                    !ValidateFace(payload, descriptor, bounds, bottom, 28, 32, ref expectedData,
                        out int bottomHeightOffset)) return false;
                if (top && bottom)
                {
                    for (int vertex = 0; vertex < bounds.VertexCount; vertex++)
                        if (ReadHeight(payload, topHeightOffset, vertex) <=
                            ReadHeight(payload, bottomHeightOffset, vertex)) return false;
                }
                if (BinaryPrimitives.ReadInt32LittleEndian(descriptor[40..]) != expectedData)
                    return false;
            }
            expectedStart += count;
        }
        return expectedStart == fragmentCount && expectedData == payload.Length &&
            ValidatePackedConnections(pack, metadata, payload, rangesOffset);
    }

    private static bool ValidatePresence(ReadOnlySpan<byte> bytes, int bits)
    {
        bool any = false;
        int words = (bits + 63) / 64;
        for (int word = 0; word < words; word++)
            any |= BinaryPrimitives.ReadUInt64LittleEndian(bytes[(word * sizeof(ulong))..]) != 0;
        if (!any) return false;
        int remainder = bits & 63;
        return remainder == 0 ||
            (BinaryPrimitives.ReadUInt64LittleEndian(bytes[((words - 1) * sizeof(ulong))..]) &
                ~((1UL << remainder) - 1)) == 0;
    }

    private static bool ValidateLocalConnections(
        TerrainSurfacePatchBounds bounds,
        ReadOnlySpan<byte> presence,
        ReadOnlySpan<byte> connections)
    {
        const byte known = (byte)(TerrainSurfaceEdgeConnection.North |
            TerrainSurfaceEdgeConnection.East | TerrainSurfaceEdgeConnection.South |
            TerrainSurfaceEdgeConnection.West);
        for (int z = 0; z < bounds.CellDepth; z++)
        for (int x = 0; x < bounds.CellWidth; x++)
        {
            int cell = z * bounds.CellWidth + x;
            byte value = connections[cell];
            bool present = (BinaryPrimitives.ReadUInt64LittleEndian(
                presence[((cell >> 6) * sizeof(ulong))..]) & (1UL << (cell & 63))) != 0;
            if ((value & ~known) != 0 || !present && value != 0) return false;
            if ((value & (byte)TerrainSurfaceEdgeConnection.North) != 0 &&
                bounds.MinimumCellZ + z != 0 ||
                (value & (byte)TerrainSurfaceEdgeConnection.East) != 0 &&
                bounds.MinimumCellX + x != TerrainChunkFormat.MaterialCellEdge - 1 ||
                (value & (byte)TerrainSurfaceEdgeConnection.South) != 0 &&
                bounds.MinimumCellZ + z != TerrainChunkFormat.MaterialCellEdge - 1 ||
                (value & (byte)TerrainSurfaceEdgeConnection.West) != 0 &&
                bounds.MinimumCellX + x != 0) return false;
        }
        return true;
    }

    private static bool ValidatePackedConnections(
        ReadOnlySpan<byte> pack,
        WorldPackMetadata metadata,
        ReadOnlySpan<byte> payload,
        int rangesOffset)
    {
        int descriptorsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[20..]);
        for (int chunk = 0; chunk < metadata.ChunkCount; chunk++)
        {
            SpatialChunkKey chunkKey = WorldPackCodec.ReadChunkKey(pack, metadata.ChunkDirectoryOffset, chunk);
            ReadOnlySpan<byte> range = payload.Slice(
                rangesOffset + chunk * AdditionalTerrainSurfaceFormat.RangeBytes,
                AdditionalTerrainSurfaceFormat.RangeBytes);
            int start = BinaryPrimitives.ReadInt32LittleEndian(range);
            int count = BinaryPrimitives.ReadInt32LittleEndian(range[4..]);
            for (int local = 0; local < count; local++)
            {
                ReadOnlySpan<byte> descriptor = payload.Slice(
                    descriptorsOffset + (start + local) * AdditionalTerrainSurfaceFormat.DescriptorBytes,
                    AdditionalTerrainSurfaceFormat.DescriptorBytes);
                var bounds = new TerrainSurfacePatchBounds(
                    descriptor[8], descriptor[9], descriptor[10], descriptor[11]);
                int connectionsOffset = BinaryPrimitives.ReadInt32LittleEndian(descriptor[36..]);
                for (int z = 0; z < bounds.CellDepth; z++)
                for (int x = 0; x < bounds.CellWidth; x++)
                {
                    var connections = (TerrainSurfaceEdgeConnection)payload[
                        connectionsOffset + z * bounds.CellWidth + x];
                    foreach (TerrainSurfaceEdgeConnection direction in
                        WorldPackBuilder.ConnectionDirections)
                    {
                        if ((connections & direction) == 0) continue;
                        if (!ValidatePackedConnection(pack, metadata, payload, rangesOffset,
                                chunkKey, descriptor, bounds, x, z, direction)) return false;
                    }
                }
            }
        }
        return true;
    }

    private static bool ValidatePackedConnection(
        ReadOnlySpan<byte> pack,
        WorldPackMetadata metadata,
        ReadOnlySpan<byte> payload,
        int rangesOffset,
        SpatialChunkKey sourceKey,
        ReadOnlySpan<byte> sourceDescriptor,
        TerrainSurfacePatchBounds sourceBounds,
        int sourceX,
        int sourceZ,
        TerrainSurfaceEdgeConnection direction)
    {
        if (!WorldPackBuilder.TryGetNeighbor(sourceKey, direction, out SpatialChunkKey neighborKey))
            return false;
        int neighborChunk = WorldPackCodec.FindChunkIndex(pack, metadata, neighborKey);
        if (neighborChunk < 0) return false;
        ReadOnlySpan<byte> range = payload.Slice(
            rangesOffset + neighborChunk * AdditionalTerrainSurfaceFormat.RangeBytes,
            AdditionalTerrainSurfaceFormat.RangeBytes);
        int start = BinaryPrimitives.ReadInt32LittleEndian(range);
        int count = BinaryPrimitives.ReadInt32LittleEndian(range[4..]);
        int descriptorsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[20..]);
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(sourceDescriptor);
        ReadOnlySpan<byte> targetDescriptor = default;
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> candidate = payload.Slice(
                descriptorsOffset + (start + index) * AdditionalTerrainSurfaceFormat.DescriptorBytes,
                AdditionalTerrainSurfaceFormat.DescriptorBytes);
            ulong candidateId = BinaryPrimitives.ReadUInt64LittleEndian(candidate);
            if (candidateId == id) { targetDescriptor = candidate; break; }
            if (candidateId > id) break;
        }
        if (targetDescriptor.IsEmpty || sourceDescriptor[12] != targetDescriptor[12]) return false;
        var targetBounds = new TerrainSurfacePatchBounds(
            targetDescriptor[8], targetDescriptor[9], targetDescriptor[10], targetDescriptor[11]);
        int worldCellX = sourceBounds.MinimumCellX + sourceX;
        int worldCellZ = sourceBounds.MinimumCellZ + sourceZ;
        int targetCellX = direction switch
        {
            TerrainSurfaceEdgeConnection.East => 0,
            TerrainSurfaceEdgeConnection.West => TerrainChunkFormat.MaterialCellEdge - 1,
            _ => worldCellX,
        };
        int targetCellZ = direction switch
        {
            TerrainSurfaceEdgeConnection.South => 0,
            TerrainSurfaceEdgeConnection.North => TerrainChunkFormat.MaterialCellEdge - 1,
            _ => worldCellZ,
        };
        int localX = targetCellX - targetBounds.MinimumCellX;
        int localZ = targetCellZ - targetBounds.MinimumCellZ;
        if ((uint)localX >= targetBounds.CellWidth || (uint)localZ >= targetBounds.CellDepth)
            return false;
        int targetCell = localZ * targetBounds.CellWidth + localX;
        int targetPresence = BinaryPrimitives.ReadInt32LittleEndian(targetDescriptor[16..]);
        if ((BinaryPrimitives.ReadUInt64LittleEndian(
                payload[(targetPresence + (targetCell >> 6) * sizeof(ulong))..]) &
                (1UL << (targetCell & 63))) == 0) return false;
        int targetConnections = BinaryPrimitives.ReadInt32LittleEndian(targetDescriptor[36..]);
        if ((((TerrainSurfaceEdgeConnection)payload[targetConnections + targetCell]) &
            WorldPackBuilder.Opposite(direction)) == 0) return false;
        var faces = (WorldTerrainSurfaceFaces)sourceDescriptor[12];
        if ((faces & WorldTerrainSurfaceFaces.Top) != 0 &&
            !PackedEdgeHeightsMatch(payload, sourceDescriptor, sourceBounds, sourceX, sourceZ,
                targetDescriptor, targetBounds, localX, localZ, direction, top: true)) return false;
        return (faces & WorldTerrainSurfaceFaces.Bottom) == 0 ||
            PackedEdgeHeightsMatch(payload, sourceDescriptor, sourceBounds, sourceX, sourceZ,
                targetDescriptor, targetBounds, localX, localZ, direction, top: false);
    }

    private static bool PackedEdgeHeightsMatch(
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> sourceDescriptor, TerrainSurfacePatchBounds sourceBounds,
        int sourceX, int sourceZ,
        ReadOnlySpan<byte> targetDescriptor, TerrainSurfacePatchBounds targetBounds,
        int targetX, int targetZ,
        TerrainSurfaceEdgeConnection direction, bool top)
    {
        WorldPackBuilder.GetEdgeVertices(sourceX, sourceZ, direction,
            out int sx0, out int sz0, out int sx1, out int sz1);
        WorldPackBuilder.GetEdgeVertices(targetX, targetZ, WorldPackBuilder.Opposite(direction),
            out int tx0, out int tz0, out int tx1, out int tz1);
        int sourceOffset = BinaryPrimitives.ReadInt32LittleEndian(sourceDescriptor[(top ? 24 : 32)..]);
        int targetOffset = BinaryPrimitives.ReadInt32LittleEndian(targetDescriptor[(top ? 24 : 32)..]);
        return ReadHeight(payload, sourceOffset, sz0 * (sourceBounds.CellWidth + 1) + sx0) ==
                ReadHeight(payload, targetOffset, tz1 * (targetBounds.CellWidth + 1) + tx1) &&
            ReadHeight(payload, sourceOffset, sz1 * (sourceBounds.CellWidth + 1) + sx1) ==
                ReadHeight(payload, targetOffset, tz0 * (targetBounds.CellWidth + 1) + tx0);
    }

    private static bool ValidateFace(
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> descriptor,
        TerrainSurfacePatchBounds bounds,
        bool present,
        int materialField,
        int heightField,
        ref int expectedData,
        out int heightOffset)
    {
        int materialOffset = BinaryPrimitives.ReadInt32LittleEndian(descriptor[materialField..]);
        heightOffset = BinaryPrimitives.ReadInt32LittleEndian(descriptor[heightField..]);
        if (!present) return materialOffset == 0 && heightOffset == 0;
        int materialBytes = bounds.CellCount * sizeof(ushort);
        if (materialOffset != expectedData || expectedData > payload.Length - materialBytes) return false;
        for (int material = 0; material < bounds.CellCount; material++)
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload[(expectedData + material * sizeof(ushort))..]) == 0)
                return false;
        expectedData += materialBytes;
        int heightBytes = sizeof(int) + bounds.VertexCount * sizeof(ushort);
        if (heightOffset != expectedData || expectedData > payload.Length - heightBytes ||
            !TerrainHeightPlaneBase.TryCreate(BinaryPrimitives.ReadInt32LittleEndian(payload[heightOffset..]), out _))
            return false;
        int heightBase = BinaryPrimitives.ReadInt32LittleEndian(payload[heightOffset..]);
        for (int vertex = 0; vertex < bounds.VertexCount; vertex++)
        {
            ushort units = BinaryPrimitives.ReadUInt16LittleEndian(
                payload[(heightOffset + sizeof(int) + vertex * sizeof(ushort))..]);
            long absolute = (long)heightBase + units * TerrainChunkFormat.HeightQuantumMillimeters;
            if (absolute is < int.MinValue or > int.MaxValue) return false;
        }
        expectedData += heightBytes;
        return true;
    }

    private static int ReadHeight(ReadOnlySpan<byte> payload, int offset, int vertex) => checked(
        BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]) +
        BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + sizeof(int) + vertex * sizeof(ushort))..]) *
        TerrainChunkFormat.HeightQuantumMillimeters);
}
