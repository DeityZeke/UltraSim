using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>Frozen binary geometry for the sectioned WorldPack v1 container.</summary>
public static class WorldPackFormat
{
    public const ulong Magic = 0x314B_4341_5057_5355UL; // USWPACK1 in little-endian byte order.
    public const uint ContainerVersion = 1;
    public const int HeaderBytes = 160;
    public const int ChunkDirectoryRowBytes = 48;
    public const int SectionDirectoryRowBytes = 48;
    public const int PackFingerprintOffset = 128;
    public const int PackFingerprintBytes = 32;
    public const uint TerrainMaterialsSchemaVersion = 1;
    public const uint TerrainHeightsSchemaVersion = 1;
    public const uint FrozenStaticsSchemaVersion = 1;
    public const uint MultiFragmentsSchemaVersion = 1;
    public const uint TerrainPresenceSchemaVersion = 1;
    public const uint AdditionalTerrainSurfacesSchemaVersion = 1;
}

/// <summary>Known canonical WorldPack section identities.</summary>
public enum WorldPackSectionKind : uint
{
    TerrainMaterials = 1,
    TerrainHeights = 2,
    FrozenStatics = 3,
    MultiFragments = 4,
    TerrainPresence = 5,
    AdditionalTerrainSurfaces = 6,
}

/// <summary>Cold content/schema identity supplied by game composition when opening a pack.</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct WorldContentFingerprint : IEquatable<WorldContentFingerprint>
{
    public ulong Low { get; }
    public ulong High { get; }
    public bool IsValid => Low != 0 || High != 0;

    public WorldContentFingerprint(ulong low, ulong high)
    {
        if (low == 0 && high == 0) throw new ArgumentOutOfRangeException(nameof(low));
        Low = low;
        High = high;
    }

    public bool Equals(WorldContentFingerprint other) => Low == other.Low && High == other.High;
    public override bool Equals(object? obj) => obj is WorldContentFingerprint other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Low, High);
    public override string ToString() => $"{High:X16}{Low:X16}";
    public static bool operator ==(WorldContentFingerprint left, WorldContentFingerprint right) => left.Equals(right);
    public static bool operator !=(WorldContentFingerprint left, WorldContentFingerprint right) => !left.Equals(right);
}

/// <summary>SHA-256 identity of one complete canonical WorldPack artifact.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public readonly struct WorldPackFingerprint : IEquatable<WorldPackFingerprint>
{
    public ulong Part0 { get; }
    public ulong Part1 { get; }
    public ulong Part2 { get; }
    public ulong Part3 { get; }
    public bool IsValid => Part0 != 0 || Part1 != 0 || Part2 != 0 || Part3 != 0;

    internal WorldPackFingerprint(ulong part0, ulong part1, ulong part2, ulong part3)
    {
        Part0 = part0;
        Part1 = part1;
        Part2 = part2;
        Part3 = part3;
    }

    internal static WorldPackFingerprint Read(ReadOnlySpan<byte> source) => new(
        BinaryPrimitives.ReadUInt64LittleEndian(source),
        BinaryPrimitives.ReadUInt64LittleEndian(source[8..]),
        BinaryPrimitives.ReadUInt64LittleEndian(source[16..]),
        BinaryPrimitives.ReadUInt64LittleEndian(source[24..]));

    /// <summary>Parses the canonical 64-character uppercase fingerprint used by deployment data.</summary>
    public static bool TryParse(string? value, out WorldPackFingerprint fingerprint)
    {
        fingerprint = default;
        if (value is null || value.Length != 64 ||
            value.Any(static character => character is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
            return false;
        byte[] bytes;
        try { bytes = Convert.FromHexString(value); }
        catch (FormatException) { return false; }
        fingerprint = new WorldPackFingerprint(
            BinaryPrimitives.ReadUInt64BigEndian(bytes[24..]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[16..24]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[8..16]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]));
        return fingerprint.IsValid && string.Equals(value, fingerprint.ToString(), StringComparison.Ordinal);
    }

    internal void Write(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, Part0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], Part1);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], Part2);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], Part3);
    }

    public bool Equals(WorldPackFingerprint other) =>
        Part0 == other.Part0 && Part1 == other.Part1 && Part2 == other.Part2 && Part3 == other.Part3;
    public override bool Equals(object? obj) => obj is WorldPackFingerprint other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Part0, Part1, Part2, Part3);
    public override string ToString() => $"{Part3:X16}{Part2:X16}{Part1:X16}{Part0:X16}";
    public static bool operator ==(WorldPackFingerprint left, WorldPackFingerprint right) => left.Equals(right);
    public static bool operator !=(WorldPackFingerprint left, WorldPackFingerprint right) => !left.Equals(right);
}

/// <summary>Explicit cold admission bounds applied before proportional WorldPack allocation.</summary>
public readonly record struct WorldPackLimits
{
    public static WorldPackLimits Default { get; } = new(
        512 * 1024 * 1024,
        WorldPackAddressing.MaximumChunksPerWorldPack,
        32,
        maximumFrozenStatics: 1_000_000,
        maximumMultiInstances: 262_144,
        maximumMultiFragments: 2_000_000,
        maximumAdditionalTerrainSurfaceFragments: 65_536,
        maximumAdditionalTerrainSurfaceFragmentsPerChunk: 64);

    public WorldPackLimits(
        int maximumPackBytes,
        int maximumChunks,
        int maximumSections,
        int maximumFrozenStatics = 1_000_000,
        int maximumMultiInstances = 262_144,
        int maximumMultiFragments = 2_000_000,
        int maximumAdditionalTerrainSurfaceFragments = 65_536,
        int maximumAdditionalTerrainSurfaceFragmentsPerChunk = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPackBytes, WorldPackFormat.HeaderBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumChunks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumChunks, WorldPackAddressing.MaximumChunksPerWorldPack);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumSections, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumFrozenStatics);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumMultiInstances);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumMultiFragments);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAdditionalTerrainSurfaceFragments);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAdditionalTerrainSurfaceFragmentsPerChunk);
        MaximumPackBytes = maximumPackBytes;
        MaximumChunks = maximumChunks;
        MaximumSections = maximumSections;
        MaximumFrozenStatics = maximumFrozenStatics;
        MaximumMultiInstances = maximumMultiInstances;
        MaximumMultiFragments = maximumMultiFragments;
        MaximumAdditionalTerrainSurfaceFragments = maximumAdditionalTerrainSurfaceFragments;
        MaximumAdditionalTerrainSurfaceFragmentsPerChunk = maximumAdditionalTerrainSurfaceFragmentsPerChunk;
    }

    public int MaximumPackBytes { get; }
    public int MaximumChunks { get; }
    public int MaximumSections { get; }
    public int MaximumFrozenStatics { get; }
    public int MaximumMultiInstances { get; }
    public int MaximumMultiFragments { get; }
    public int MaximumAdditionalTerrainSurfaceFragments { get; }
    public int MaximumAdditionalTerrainSurfaceFragmentsPerChunk { get; }
    internal bool IsValid => MaximumPackBytes >= WorldPackFormat.HeaderBytes &&
        MaximumChunks is > 0 and <= WorldPackAddressing.MaximumChunksPerWorldPack &&
        MaximumSections >= 2 && MaximumFrozenStatics >= 0 && MaximumMultiInstances >= 0 &&
        MaximumMultiFragments >= 0 && MaximumAdditionalTerrainSurfaceFragments >= 0 &&
        MaximumAdditionalTerrainSurfaceFragmentsPerChunk >= 0;
}

public enum WorldPackBuildError
{
    None = 0,
    CapacityExceeded,
    SpaceMismatch,
    DuplicateChunk,
    MalformedMaterialPlane,
    InvalidMaterialId,
    MalformedHeightPlane,
    InvalidHeightPlane,
    MalformedTerrainPresence,
    InvalidAdditionalTerrainSurface,
    SharedEdgeMismatch,
    DuplicateStaticId,
    InvalidStaticPlacement,
    DuplicateMultiId,
    InvalidMulti,
    MissingChunk,
    SizeOverflow,
    ChunkOutsidePackWindow,
}

/// <summary>Result of checking an opened v1 artifact against externally declared Pack ownership.</summary>
public enum WorldPackSpatialValidationError
{
    None = 0,
    ChunkOutsidePackWindow,
}

public enum WorldPackOpenError
{
    None = 0,
    Truncated,
    InvalidMagic,
    UnsupportedVersion,
    CapacityExceeded,
    LengthMismatch,
    InvalidHeader,
    ContentMismatch,
    HashMismatch,
    InvalidSectionDirectory,
    MissingRequiredSection,
    UnsupportedRequiredSection,
    InvalidChunkDirectory,
    InvalidTerrainPayload,
    InvalidFrozenStaticPayload,
    InvalidMultiPayload,
    InvalidAdditionalTerrainSurfacePayload,
    SharedEdgeMismatch,
    IoFailure,
}

public enum WorldPackBindingError
{
    None = 0,
    ContentMismatch,
    UnknownTerrainMaterial,
    UnknownStaticDefinition,
}

/// <summary>Cold composition-owned binding from pack numeric IDs to the admitted game content domain.</summary>
public sealed class WorldContentBinding
{
    private readonly ushort[] _terrainMaterials;
    private readonly uint[] _staticDefinitions;

    public WorldContentBinding(
        WorldContentFingerprint fingerprint,
        ReadOnlySpan<ushort> terrainMaterialIds)
        : this(fingerprint, terrainMaterialIds, ReadOnlySpan<uint>.Empty) { }

    public WorldContentBinding(
        WorldContentFingerprint fingerprint,
        ReadOnlySpan<ushort> terrainMaterialIds,
        ReadOnlySpan<uint> staticDefinitionIds)
    {
        if (!fingerprint.IsValid) throw new ArgumentOutOfRangeException(nameof(fingerprint));
        if (terrainMaterialIds.IsEmpty) throw new ArgumentException("At least one terrain material is required.",
            nameof(terrainMaterialIds));
        _terrainMaterials = terrainMaterialIds.ToArray();
        Array.Sort(_terrainMaterials);
        for (int index = 0; index < _terrainMaterials.Length; index++)
        {
            if (_terrainMaterials[index] == 0 ||
                index != 0 && _terrainMaterials[index] == _terrainMaterials[index - 1])
                throw new ArgumentException("Terrain material identities must be nonzero and unique.",
                    nameof(terrainMaterialIds));
        }
        _staticDefinitions = staticDefinitionIds.ToArray();
        Array.Sort(_staticDefinitions);
        for (int index = 0; index < _staticDefinitions.Length; index++)
        {
            if (_staticDefinitions[index] == 0 ||
                index != 0 && _staticDefinitions[index] == _staticDefinitions[index - 1])
                throw new ArgumentException("Static definition identities must be nonzero and unique.",
                    nameof(staticDefinitionIds));
        }
        Fingerprint = fingerprint;
    }

    public WorldContentFingerprint Fingerprint { get; }
    public int TerrainMaterialCount => _terrainMaterials.Length;
    public int StaticDefinitionCount => _staticDefinitions.Length;

    public bool ContainsTerrainMaterial(ushort materialId) =>
        Array.BinarySearch(_terrainMaterials, materialId) >= 0;

    public bool ContainsStaticDefinition(WorldStaticDefinitionId definitionId) =>
        definitionId.IsValid && Array.BinarySearch(_staticDefinitions, definitionId.Value) >= 0;
}

/// <summary>Cold deterministic compiler for sectioned WorldPack v1 artifacts.</summary>
public sealed partial class WorldPackBuilder
{
    private readonly SpatialSpaceId _space;
    private readonly WorldContentFingerprint _content;
    private readonly WorldPackLimits _limits;
    private readonly WorldPackCoord? _declaredPackCoord;
    private readonly List<SourceChunk> _chunks;
    private readonly Dictionary<SpatialChunkKey, SourceChunk> _byKey;
    private bool _hasAbsentBaseSurface;

    public WorldPackBuilder(
        SpatialSpaceId space,
        WorldContentFingerprint content,
        WorldPackLimits limits)
        : this(space, content, limits, null) { }

    /// <summary>Authors one declared structural shard; its coordinate is not encoded in WorldPack/v1.</summary>
    public WorldPackBuilder(
        SpatialSpaceId space,
        WorldContentFingerprint content,
        WorldPackLimits limits,
        WorldPackCoord declaredPackCoord)
        : this(space, content, limits, (WorldPackCoord?)declaredPackCoord) { }

    private WorldPackBuilder(
        SpatialSpaceId space,
        WorldContentFingerprint content,
        WorldPackLimits limits,
        WorldPackCoord? declaredPackCoord)
    {
        if (!space.IsValid) throw new ArgumentOutOfRangeException(nameof(space));
        if (!content.IsValid) throw new ArgumentOutOfRangeException(nameof(content));
        if (!limits.IsValid) throw new ArgumentOutOfRangeException(nameof(limits));
        _space = space;
        _content = content;
        _limits = limits;
        _declaredPackCoord = declaredPackCoord;
        _chunks = new List<SourceChunk>(Math.Min(limits.MaximumChunks, 256));
        _byKey = new Dictionary<SpatialChunkKey, SourceChunk>();
    }

    public int Count => _chunks.Count;

    public bool TryAddBaseTerrain(
        SpatialChunkKey key,
        ReadOnlySpan<ushort> materials,
        TerrainHeightPlaneBase heightBase,
        ReadOnlySpan<TerrainHeightOffset> heightOffsets,
        out WorldPackBuildError error)
    {
        Span<ulong> presence = stackalloc ulong[TerrainChunkFormat.SurfacePresenceWordCount];
        TerrainSurfacePresence.FillPresent(presence);
        return TryAddBaseTerrain(key, materials, heightBase, heightOffsets, presence, out error);
    }

    /// <summary>
    /// Adds one complete base-heightfield chunk plus its independent one-bit-per-cell topology.
    /// Clear presence bits represent intentional resident void; material and height storage remain
    /// complete canonical source data but are not effective surface truth for those cells.
    /// </summary>
    public bool TryAddBaseTerrain(
        SpatialChunkKey key,
        ReadOnlySpan<ushort> materials,
        TerrainHeightPlaneBase heightBase,
        ReadOnlySpan<TerrainHeightOffset> heightOffsets,
        ReadOnlySpan<ulong> surfacePresence,
        out WorldPackBuildError error)
    {
        if (key.Space != _space)
        {
            error = WorldPackBuildError.SpaceMismatch;
            return false;
        }
        if (_declaredPackCoord is WorldPackCoord declared &&
            !WorldPackAddressing.Contains(declared, key.Coord))
        {
            error = WorldPackBuildError.ChunkOutsidePackWindow;
            return false;
        }
        if (_chunks.Count >= _limits.MaximumChunks)
        {
            error = WorldPackBuildError.CapacityExceeded;
            return false;
        }
        if (_byKey.ContainsKey(key))
        {
            error = WorldPackBuildError.DuplicateChunk;
            return false;
        }
        if (materials.Length != TerrainChunkFormat.MaterialCellCount)
        {
            error = WorldPackBuildError.MalformedMaterialPlane;
            return false;
        }
        for (int index = 0; index < materials.Length; index++)
        {
            if (materials[index] == 0)
            {
                error = WorldPackBuildError.InvalidMaterialId;
                return false;
            }
        }
        if (heightOffsets.Length != TerrainChunkFormat.HeightVertexCount)
        {
            error = WorldPackBuildError.MalformedHeightPlane;
            return false;
        }
        for (int index = 0; index < heightOffsets.Length; index++)
        {
            long absolute = (long)heightBase.Millimeters + heightOffsets[index].OffsetMillimeters;
            if (absolute is < int.MinValue or > int.MaxValue)
            {
                error = WorldPackBuildError.InvalidHeightPlane;
                return false;
            }
        }

        if (surfacePresence.Length != TerrainChunkFormat.SurfacePresenceWordCount)
        {
            error = WorldPackBuildError.MalformedTerrainPresence;
            return false;
        }

        var chunk = new SourceChunk(key, materials.ToArray(), heightBase, heightOffsets.ToArray(),
            surfacePresence.ToArray());
        _chunks.Add(chunk);
        _byKey.Add(key, chunk);
        _hasAbsentBaseSurface |= TerrainSurfacePresence.HasAnyAbsent(surfacePresence);
        error = WorldPackBuildError.None;
        return true;
    }

    public bool TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError error)
    {
        artifact = null;
        if (_chunks.Count == 0)
        {
            error = WorldPackBuildError.CapacityExceeded;
            return false;
        }
        if (!ValidateSharedEdges())
        {
            error = WorldPackBuildError.SharedEdgeMismatch;
            return false;
        }
        if (!ValidateAdditionalTerrainConnections())
        {
            error = WorldPackBuildError.InvalidAdditionalTerrainSurface;
            return false;
        }

        SourceChunk[] ordered = _chunks.ToArray();
        Array.Sort(ordered, static (left, right) => left.Key.CompareTo(right.Key));
        int sectionCount = 2 + (_frozenStatics.Count == 0 ? 0 : 1) +
            (_staticMultis.Count == 0 ? 0 : 1) + (_hasAbsentBaseSurface ? 1 : 0) +
            (_additionalTerrainBands.Count == 0 ? 0 : 1);
        if (sectionCount > _limits.MaximumSections)
        {
            error = WorldPackBuildError.CapacityExceeded;
            return false;
        }
        int chunkDirectoryLength;
        int sectionDirectoryLength;
        int materialLength;
        int heightLength;
        int materialOffset;
        int heightOffset;
        int staticOffset;
        int multiOffset;
        int presenceOffset;
        int presenceLength;
        int additionalTerrainOffset;
        int totalLength;
        if (!FrozenWorldStructureCodec.TryMeasure(ordered.Length, _frozenStatics.Count,
                _staticMultis.Count, _multiFragmentCount, out int measuredStaticLength,
                out int measuredMultiLength))
        {
            error = WorldPackBuildError.SizeOverflow;
            return false;
        }
        if (!AdditionalTerrainSurfaceCodec.TryMeasure(ordered.Length, _additionalTerrainBands,
                out int measuredAdditionalTerrainLength))
        {
            error = WorldPackBuildError.SizeOverflow;
            return false;
        }
        try
        {
            chunkDirectoryLength = checked(ordered.Length * WorldPackFormat.ChunkDirectoryRowBytes);
            sectionDirectoryLength = checked(sectionCount * WorldPackFormat.SectionDirectoryRowBytes);
            materialLength = checked(ordered.Length * TerrainChunkFormat.MaterialPlaneBytes);
            heightLength = checked(ordered.Length * TerrainChunkFormat.HeightPlaneBytes);
            materialOffset = checked(WorldPackFormat.HeaderBytes + chunkDirectoryLength + sectionDirectoryLength);
            heightOffset = checked(materialOffset + materialLength);
            staticOffset = checked(heightOffset + heightLength);
            multiOffset = checked(staticOffset + measuredStaticLength);
            presenceOffset = checked(multiOffset + measuredMultiLength);
            presenceLength = _hasAbsentBaseSurface
                ? checked(ordered.Length * TerrainChunkFormat.SurfacePresencePlaneBytes)
                : 0;
            additionalTerrainOffset = checked(presenceOffset + presenceLength);
            totalLength = checked(additionalTerrainOffset + measuredAdditionalTerrainLength);
        }
        catch (OverflowException)
        {
            error = WorldPackBuildError.SizeOverflow;
            return false;
        }
        if (totalLength > _limits.MaximumPackBytes)
        {
            error = WorldPackBuildError.CapacityExceeded;
            return false;
        }

        var orderedKeys = new SpatialChunkKey[ordered.Length];
        for (int index = 0; index < ordered.Length; index++) orderedKeys[index] = ordered[index].Key;
        if (!FrozenWorldStructureCodec.TryCompile(orderedKeys, _frozenStatics, _staticMultis, _limits,
                out byte[]? staticPayload, out byte[]? multiPayload, out error))
            return false;
        if ((staticPayload?.Length ?? 0) != measuredStaticLength ||
            (multiPayload?.Length ?? 0) != measuredMultiLength)
            throw new InvalidOperationException("Frozen structure measurement diverged from compilation.");
        if (!AdditionalTerrainSurfaceCodec.TryCompile(orderedKeys, _additionalTerrainBands,
                out byte[]? additionalTerrainPayload) ||
            (additionalTerrainPayload?.Length ?? 0) != measuredAdditionalTerrainLength)
            throw new InvalidOperationException("Additional terrain measurement diverged from compilation.");

        var bytes = new byte[totalLength];
        WriteHeader(bytes, ordered.Length, chunkDirectoryLength, sectionDirectoryLength,
            sectionCount, materialOffset);
        WritePayloadAndChunkDirectory(bytes, ordered, materialOffset, heightOffset);
        if (staticPayload is not null) staticPayload.CopyTo(bytes.AsSpan(staticOffset));
        if (multiPayload is not null) multiPayload.CopyTo(bytes.AsSpan(multiOffset));
        if (_hasAbsentBaseSurface) WritePresencePayload(bytes, ordered, presenceOffset);
        if (additionalTerrainPayload is not null)
            additionalTerrainPayload.CopyTo(bytes.AsSpan(additionalTerrainOffset));
        WriteSectionDirectory(bytes, ordered.Length, chunkDirectoryLength, materialOffset,
            materialLength, heightOffset, heightLength, staticOffset, staticPayload,
            _frozenStatics.Count, multiOffset, multiPayload, _multiFragmentCount,
            presenceOffset, presenceLength, additionalTerrainOffset, additionalTerrainPayload,
            _additionalTerrainBands.Count);
        WorldPackFingerprint fingerprint = WorldPackCodec.ComputePackFingerprint(bytes);
        fingerprint.Write(bytes.AsSpan(WorldPackFormat.PackFingerprintOffset,
            WorldPackFormat.PackFingerprintBytes));
        artifact = new WorldPackArtifact(bytes, fingerprint, _content, _limits);
        error = WorldPackBuildError.None;
        return true;
    }

    private bool ValidateSharedEdges()
    {
        foreach (SourceChunk chunk in _chunks)
        {
            if (chunk.Key.Coord.X != int.MaxValue)
            {
                var rightKey = new SpatialChunkKey(_space, chunk.Key.Coord.X + 1, chunk.Key.Coord.Z);
                if (_byKey.TryGetValue(rightKey, out SourceChunk? right) &&
                    !EdgesMatch(chunk, right, vertical: true))
                    return false;
            }
            if (chunk.Key.Coord.Z != int.MaxValue)
            {
                var bottomKey = new SpatialChunkKey(_space, chunk.Key.Coord.X, chunk.Key.Coord.Z + 1);
                if (_byKey.TryGetValue(bottomKey, out SourceChunk? bottom) &&
                    !EdgesMatch(chunk, bottom, vertical: false))
                    return false;
            }
        }
        return true;
    }

    private static bool EdgesMatch(SourceChunk first, SourceChunk second, bool vertical)
    {
        for (byte vertex = 0; vertex < TerrainChunkFormat.HeightVertexEdge; vertex++)
        {
            TerrainVertexCoord firstCoord = vertical
                ? new TerrainVertexCoord(32, vertex)
                : new TerrainVertexCoord(vertex, 32);
            TerrainVertexCoord secondCoord = vertical
                ? new TerrainVertexCoord(0, vertex)
                : new TerrainVertexCoord(vertex, 0);
            if (Absolute(first, TerrainChunkFormat.GetHeightIndex(firstCoord)) !=
                Absolute(second, TerrainChunkFormat.GetHeightIndex(secondCoord)))
                return false;
        }
        return true;
    }

    private static int Absolute(SourceChunk chunk, int index) => checked(
        chunk.HeightBase.Millimeters + chunk.HeightOffsets[index].OffsetMillimeters);

    private void WriteHeader(
        Span<byte> bytes,
        int chunkCount,
        int chunkDirectoryLength,
        int sectionDirectoryLength,
        int sectionCount,
        int payloadOffset)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, WorldPackFormat.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], WorldPackFormat.ContainerVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], WorldPackFormat.HeaderBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], checked((ulong)bytes.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], _space.Value);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[32..], GameWorldSpatialProfile.WorldChunkEdgeUnits);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[36..], GameWorldSpatialProfile.TerrainCellEdgeUnits);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[40..], TerrainChunkFormat.MaterialCellEdge);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[44..], TerrainChunkFormat.HeightVertexEdge);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[48..], _content.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[56..], _content.High);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[64..], chunkCount);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[68..], sectionCount);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[72..], WorldPackFormat.HeaderBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[80..], checked((ulong)chunkDirectoryLength));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[88..],
            checked((ulong)(WorldPackFormat.HeaderBytes + chunkDirectoryLength)));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[96..], checked((ulong)sectionDirectoryLength));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[104..], checked((ulong)payloadOffset));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[112..], checked((ulong)(bytes.Length - payloadOffset)));
        // Bytes 120..159 are reserved/hash and remain zero until the final digest is known.
    }

    private static void WritePayloadAndChunkDirectory(
        Span<byte> bytes,
        ReadOnlySpan<SourceChunk> ordered,
        int materialOffset,
        int heightOffset)
    {
        for (int index = 0; index < ordered.Length; index++)
        {
            SourceChunk chunk = ordered[index];
            int rowOffset = WorldPackFormat.HeaderBytes + index * WorldPackFormat.ChunkDirectoryRowBytes;
            Span<byte> row = bytes.Slice(rowOffset, WorldPackFormat.ChunkDirectoryRowBytes);
            int chunkMaterialOffset = checked(materialOffset + index * TerrainChunkFormat.MaterialPlaneBytes);
            int chunkHeightOffset = checked(heightOffset + index * TerrainChunkFormat.HeightPlaneBytes);
            BinaryPrimitives.WriteUInt64LittleEndian(row, chunk.Key.Space.Value);
            BinaryPrimitives.WriteInt32LittleEndian(row[8..], chunk.Key.Coord.X);
            BinaryPrimitives.WriteInt32LittleEndian(row[12..], chunk.Key.Coord.Z);
            BinaryPrimitives.WriteUInt64LittleEndian(row[16..], checked((ulong)chunkMaterialOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(row[24..], TerrainChunkFormat.MaterialPlaneBytes);
            BinaryPrimitives.WriteUInt32LittleEndian(row[28..], TerrainChunkFormat.MaterialCellCount);
            BinaryPrimitives.WriteUInt64LittleEndian(row[32..], checked((ulong)chunkHeightOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(row[40..], TerrainChunkFormat.HeightPlaneBytes);
            BinaryPrimitives.WriteUInt32LittleEndian(row[44..], TerrainChunkFormat.HeightVertexCount);

            Span<byte> materialDestination = bytes.Slice(chunkMaterialOffset, TerrainChunkFormat.MaterialPlaneBytes);
            for (int material = 0; material < chunk.Materials.Length; material++)
                BinaryPrimitives.WriteUInt16LittleEndian(materialDestination[(material * sizeof(ushort))..],
                    chunk.Materials[material]);
            Span<byte> heightDestination = bytes.Slice(chunkHeightOffset, TerrainChunkFormat.HeightPlaneBytes);
            BinaryPrimitives.WriteInt32LittleEndian(heightDestination, chunk.HeightBase.Millimeters);
            for (int vertex = 0; vertex < chunk.HeightOffsets.Length; vertex++)
                BinaryPrimitives.WriteUInt16LittleEndian(
                    heightDestination[(TerrainChunkFormat.HeightPlaneBaseBytes + vertex * sizeof(ushort))..],
                    chunk.HeightOffsets[vertex].Units);
        }
    }

    private static void WritePresencePayload(
        Span<byte> bytes,
        ReadOnlySpan<SourceChunk> ordered,
        int presenceOffset)
    {
        for (int chunk = 0; chunk < ordered.Length; chunk++)
        {
            Span<byte> destination = bytes.Slice(
                checked(presenceOffset + chunk * TerrainChunkFormat.SurfacePresencePlaneBytes),
                TerrainChunkFormat.SurfacePresencePlaneBytes);
            for (int word = 0; word < TerrainChunkFormat.SurfacePresenceWordCount; word++)
                BinaryPrimitives.WriteUInt64LittleEndian(destination[(word * sizeof(ulong))..],
                    ordered[chunk].SurfacePresence[word]);
        }
    }

    private static void WriteSectionDirectory(
        Span<byte> bytes,
        int chunkCount,
        int chunkDirectoryLength,
        int materialOffset,
        int materialLength,
        int heightOffset,
        int heightLength,
        int staticOffset,
        byte[]? staticPayload,
        int staticCount,
        int multiOffset,
        byte[]? multiPayload,
        int multiFragmentCount,
        int presenceOffset,
        int presenceLength,
        int additionalTerrainOffset,
        byte[]? additionalTerrainPayload,
        int additionalTerrainFragmentCount)
    {
        int sectionOffset = WorldPackFormat.HeaderBytes + chunkDirectoryLength;
        WriteSection(bytes, sectionOffset, WorldPackSectionKind.TerrainMaterials,
            WorldPackFormat.TerrainMaterialsSchemaVersion, materialOffset, materialLength,
            checked((ulong)chunkCount * TerrainChunkFormat.MaterialCellCount));
        WriteSection(bytes, sectionOffset + WorldPackFormat.SectionDirectoryRowBytes,
            WorldPackSectionKind.TerrainHeights, WorldPackFormat.TerrainHeightsSchemaVersion,
            heightOffset, heightLength, checked((ulong)chunkCount * TerrainChunkFormat.HeightVertexCount));
        int row = 2;
        if (staticPayload is not null)
        {
            WriteSection(bytes, sectionOffset + row++ * WorldPackFormat.SectionDirectoryRowBytes,
                WorldPackSectionKind.FrozenStatics, WorldPackFormat.FrozenStaticsSchemaVersion,
                staticOffset, staticPayload.Length, checked((ulong)staticCount));
        }
        if (multiPayload is not null)
        {
            WriteSection(bytes, sectionOffset + row++ * WorldPackFormat.SectionDirectoryRowBytes,
                WorldPackSectionKind.MultiFragments, WorldPackFormat.MultiFragmentsSchemaVersion,
                multiOffset, multiPayload.Length, checked((ulong)multiFragmentCount));
        }
        if (presenceLength != 0)
        {
            WriteSection(bytes, sectionOffset + row++ * WorldPackFormat.SectionDirectoryRowBytes,
                WorldPackSectionKind.TerrainPresence, WorldPackFormat.TerrainPresenceSchemaVersion,
                presenceOffset, presenceLength,
                checked((ulong)chunkCount * TerrainChunkFormat.MaterialCellCount));
        }
        if (additionalTerrainPayload is not null)
        {
            WriteSection(bytes, sectionOffset + row * WorldPackFormat.SectionDirectoryRowBytes,
                WorldPackSectionKind.AdditionalTerrainSurfaces,
                WorldPackFormat.AdditionalTerrainSurfacesSchemaVersion,
                additionalTerrainOffset, additionalTerrainPayload.Length,
                checked((ulong)additionalTerrainFragmentCount));
        }
    }

    private static void WriteSection(
        Span<byte> bytes,
        int rowOffset,
        WorldPackSectionKind kind,
        uint version,
        int payloadOffset,
        int payloadLength,
        ulong count)
    {
        Span<byte> row = bytes.Slice(rowOffset, WorldPackFormat.SectionDirectoryRowBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(row, (uint)kind);
        BinaryPrimitives.WriteUInt32LittleEndian(row[4..], version);
        BinaryPrimitives.WriteUInt64LittleEndian(row[8..], checked((ulong)payloadOffset));
        BinaryPrimitives.WriteUInt64LittleEndian(row[16..], checked((ulong)payloadLength));
        BinaryPrimitives.WriteUInt64LittleEndian(row[24..], count);
        WorldPackCodec.ComputeDigest128(bytes.Slice(payloadOffset, payloadLength)).CopyTo(row[32..]);
    }

    private sealed record SourceChunk(
        SpatialChunkKey Key,
        ushort[] Materials,
        TerrainHeightPlaneBase HeightBase,
        TerrainHeightOffset[] HeightOffsets,
        ulong[] SurfacePresence);
}

/// <summary>Immutable compiler output that can be copied, persisted, or opened without exposing mutable bytes.</summary>
public sealed class WorldPackArtifact
{
    private readonly byte[] _bytes;

    internal WorldPackArtifact(
        byte[] bytes,
        WorldPackFingerprint fingerprint,
        WorldContentFingerprint contentFingerprint,
        WorldPackLimits limits)
    {
        _bytes = bytes;
        Fingerprint = fingerprint;
        ContentFingerprint = contentFingerprint;
        Limits = limits;
    }

    public int Length => _bytes.Length;
    public WorldPackFingerprint Fingerprint { get; }
    public WorldContentFingerprint ContentFingerprint { get; }
    public WorldPackLimits Limits { get; }

    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < _bytes.Length) throw new ArgumentException("Destination is too small.", nameof(destination));
        _bytes.CopyTo(destination);
    }

    public void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(_bytes);
    }

    public WorldPack Open()
    {
        if (!TryOpen(Limits, out WorldPack? pack, out WorldPackOpenError error))
            throw new InvalidOperationException($"Compiler produced an invalid WorldPack: {error}.");
        return pack!;
    }

    /// <summary>Reopens owned canonical bytes under an explicit admission policy without copying them.</summary>
    public bool TryOpen(WorldPackLimits limits, out WorldPack? pack, out WorldPackOpenError error)
    {
        pack = null;
        if (!WorldPackCodec.TryValidate(_bytes, limits, ContentFingerprint,
                out WorldPackMetadata metadata, out error))
            return false;
        pack = new WorldPack(_bytes, metadata);
        return true;
    }
}

/// <summary>Immutable sectioned world data with allocation-free base-terrain random reads after opening.</summary>
public sealed class WorldPack
{
    private readonly byte[] _bytes;
    private readonly WorldPackMetadata _metadata;

    internal WorldPack(byte[] bytes, WorldPackMetadata metadata)
    {
        _bytes = bytes;
        _metadata = metadata;
    }

    public int Length => _bytes.Length;
    public int ChunkCount => _metadata.ChunkCount;
    public int SectionCount => _metadata.SectionCount;
    public int FrozenStaticCount => _metadata.FrozenStatics.IsPresent
        ? checked((int)_metadata.FrozenStatics.Count)
        : 0;
    public int MultiFragmentCount => _metadata.MultiFragments.IsPresent
        ? checked((int)_metadata.MultiFragments.Count)
        : 0;
    public int AdditionalTerrainSurfaceFragmentCount => _metadata.AdditionalTerrainSurfaces.IsPresent
        ? checked((int)_metadata.AdditionalTerrainSurfaces.Count)
        : 0;
    public int MultiInstanceCount => _metadata.MultiFragments.IsPresent
        ? BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(_metadata.MultiFragments.Offset + 4))
        : 0;
    public bool HasExplicitTerrainPresence => _metadata.TerrainPresence.IsPresent;
    public SpatialSpaceId Space => new(_metadata.SpaceValue);
    public WorldContentFingerprint ContentFingerprint => _metadata.ContentFingerprint;
    public WorldPackFingerprint Fingerprint => _metadata.PackFingerprint;

    /// <summary>Copies the complete canonical artifact bytes without exposing mutable ownership.</summary>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < _bytes.Length)
            throw new ArgumentException("Destination is too small.", nameof(destination));
        _bytes.CopyTo(destination);
    }

    public static bool TryOpen(
        ReadOnlySpan<byte> source,
        WorldPackLimits limits,
        WorldContentFingerprint expectedContent,
        out WorldPack? pack,
        out WorldPackOpenError error)
    {
        pack = null;
        if (!WorldPackCodec.TryReadBoundedLength(source, limits, out int length, out error)) return false;
        if (source.Length != length)
        {
            error = WorldPackOpenError.LengthMismatch;
            return false;
        }
        byte[] owned = source[..length].ToArray();
        if (!WorldPackCodec.TryValidate(owned, limits, expectedContent, out WorldPackMetadata metadata, out error))
            return false;
        pack = new WorldPack(owned, metadata);
        return true;
    }

    public static bool TryOpenFile(
        string path,
        WorldPackLimits limits,
        WorldContentFingerprint expectedContent,
        out WorldPack? pack,
        out WorldPackOpenError error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        pack = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                WorldPackFormat.HeaderBytes, FileOptions.SequentialScan);
            return TryOpen(stream, limits, expectedContent, out pack, out error);
        }
        catch (IOException)
        {
            error = WorldPackOpenError.IoFailure;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = WorldPackOpenError.IoFailure;
            return false;
        }
    }

    /// <summary>
    /// Opens the exact unread contents of a stream with one World-owned pack allocation. The
    /// header is admitted before proportional allocation, and trailing bytes fail closed.
    /// </summary>
    public static bool TryOpen(
        Stream source,
        WorldPackLimits limits,
        WorldContentFingerprint expectedContent,
        out WorldPack? pack,
        out WorldPackOpenError error)
    {
        ArgumentNullException.ThrowIfNull(source);
        pack = null;
        if (!source.CanRead)
        {
            error = WorldPackOpenError.IoFailure;
            return false;
        }
        try
        {
            Span<byte> header = stackalloc byte[WorldPackFormat.HeaderBytes];
            if (!ReadExactly(source, header))
            {
                error = WorldPackOpenError.Truncated;
                return false;
            }
            if (!WorldPackCodec.TryReadBoundedLength(header, limits, out int length, out error))
                return false;
            var owned = new byte[length];
            header.CopyTo(owned);
            if (!ReadExactly(source, owned.AsSpan(WorldPackFormat.HeaderBytes)))
            {
                error = WorldPackOpenError.Truncated;
                return false;
            }
            if (source.ReadByte() != -1)
            {
                error = WorldPackOpenError.LengthMismatch;
                return false;
            }
            if (!WorldPackCodec.TryValidate(owned, limits, expectedContent,
                    out WorldPackMetadata metadata, out error))
                return false;
            pack = new WorldPack(owned, metadata);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = WorldPackOpenError.IoFailure;
            return false;
        }
    }

    public bool TryGetBaseTerrain(SpatialChunkKey key, out WorldTerrainChunkView view)
    {
        if (key.Space != Space)
        {
            view = default;
            return false;
        }
        int index = WorldPackCodec.FindChunkIndex(_bytes, _metadata, key);
        if (index < 0)
        {
            view = default;
            return false;
        }
        int rowOffset = _metadata.ChunkDirectoryOffset + index * WorldPackFormat.ChunkDirectoryRowBytes;
        ReadOnlySpan<byte> row = _bytes.AsSpan(rowOffset, WorldPackFormat.ChunkDirectoryRowBytes);
        int materialOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(row[16..]));
        int heightOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(row[32..]));
        ReadOnlySpan<byte> presence = _metadata.TerrainPresence.IsPresent
            ? _bytes.AsSpan(checked(_metadata.TerrainPresence.Offset +
                index * TerrainChunkFormat.SurfacePresencePlaneBytes),
                TerrainChunkFormat.SurfacePresencePlaneBytes)
            : ReadOnlySpan<byte>.Empty;
        view = new WorldTerrainChunkView(key,
            _bytes.AsSpan(materialOffset, TerrainChunkFormat.MaterialPlaneBytes),
            _bytes.AsSpan(heightOffset, TerrainChunkFormat.HeightPlaneBytes), presence);
        return true;
    }

    /// <summary>Gets one canonical directory key by stable sorted pack index.</summary>
    public bool TryGetChunkKey(int index, out SpatialChunkKey key)
    {
        if (index < 0 || index >= _metadata.ChunkCount)
        {
            key = default;
            return false;
        }
        key = WorldPackCodec.ReadChunkKey(_bytes, _metadata.ChunkDirectoryOffset, index);
        return true;
    }

    /// <summary>
    /// Checks every canonical global chunk key against a declared structural Pack address.
    /// Neither the declaration nor this result changes v1 bytes, fingerprint, or runtime state.
    /// </summary>
    public bool TryValidateSpatialWindow(
        WorldPackCoord declaredPackCoord,
        out SpatialChunkKey offendingKey,
        out WorldPackSpatialValidationError error)
    {
        for (int index = 0; index < _metadata.ChunkCount; index++)
        {
            SpatialChunkKey key = WorldPackCodec.ReadChunkKey(_bytes, _metadata.ChunkDirectoryOffset, index);
            if (!WorldPackAddressing.Contains(declaredPackCoord, key.Coord))
            {
                offendingKey = key;
                error = WorldPackSpatialValidationError.ChunkOutsidePackWindow;
                return false;
            }
        }

        offendingKey = default;
        error = WorldPackSpatialValidationError.None;
        return true;
    }

    public bool TryGetFrozenStatics(SpatialChunkKey key, out WorldFrozenStaticChunkView view)
    {
        if (!_metadata.FrozenStatics.IsPresent || key.Space != Space)
        {
            view = default;
            return false;
        }
        int chunk = WorldPackCodec.FindChunkIndex(_bytes, _metadata, key);
        if (chunk < 0)
        {
            view = default;
            return false;
        }
        ReadOnlySpan<byte> payload = _bytes.AsSpan(
            _metadata.FrozenStatics.Offset, _metadata.FrozenStatics.Length);
        int rangesOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[16..]);
        int rowsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[20..]);
        ReadOnlySpan<byte> range = payload.Slice(
            rangesOffset + chunk * FrozenWorldStructureFormat.RangeRowBytes,
            FrozenWorldStructureFormat.RangeRowBytes);
        int start = BinaryPrimitives.ReadInt32LittleEndian(range);
        int count = BinaryPrimitives.ReadInt32LittleEndian(range[4..]);
        view = new WorldFrozenStaticChunkView(key, payload.Slice(
            rowsOffset + start * FrozenWorldStructureFormat.StaticRowBytes,
            count * FrozenWorldStructureFormat.StaticRowBytes));
        return true;
    }

    public bool TryGetMultiFragments(SpatialChunkKey key, out WorldMultiFragmentChunkView view)
    {
        if (!_metadata.MultiFragments.IsPresent || key.Space != Space)
        {
            view = default;
            return false;
        }
        int chunk = WorldPackCodec.FindChunkIndex(_bytes, _metadata, key);
        if (chunk < 0)
        {
            view = default;
            return false;
        }
        ReadOnlySpan<byte> payload = _bytes.AsSpan(
            _metadata.MultiFragments.Offset, _metadata.MultiFragments.Length);
        int rangesOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[24..]);
        int fragmentsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[32..]);
        ReadOnlySpan<byte> range = payload.Slice(
            rangesOffset + chunk * FrozenWorldStructureFormat.RangeRowBytes,
            FrozenWorldStructureFormat.RangeRowBytes);
        int start = BinaryPrimitives.ReadInt32LittleEndian(range);
        int count = BinaryPrimitives.ReadInt32LittleEndian(range[4..]);
        view = new WorldMultiFragmentChunkView(key, payload.Slice(
            fragmentsOffset + start * FrozenWorldStructureFormat.MultiFragmentRowBytes,
            count * FrozenWorldStructureFormat.MultiFragmentRowBytes));
        return true;
    }

    public bool TryGetAdditionalTerrainSurfaceBands(
        SpatialChunkKey key,
        out WorldTerrainSurfaceBandChunkView view)
    {
        if (!_metadata.AdditionalTerrainSurfaces.IsPresent || key.Space != Space)
        {
            view = default;
            return false;
        }
        int chunk = WorldPackCodec.FindChunkIndex(_bytes, _metadata, key);
        if (chunk < 0)
        {
            view = default;
            return false;
        }
        ReadOnlySpan<byte> payload = _bytes.AsSpan(
            _metadata.AdditionalTerrainSurfaces.Offset, _metadata.AdditionalTerrainSurfaces.Length);
        int rangesOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[16..]);
        ReadOnlySpan<byte> range = payload.Slice(
            rangesOffset + chunk * AdditionalTerrainSurfaceFormat.RangeBytes,
            AdditionalTerrainSurfaceFormat.RangeBytes);
        view = new WorldTerrainSurfaceBandChunkView(
            key, payload, BinaryPrimitives.ReadInt32LittleEndian(range),
            BinaryPrimitives.ReadInt32LittleEndian(range[4..]));
        return true;
    }

    public bool TryGetMultiInstance(MultiInstanceId id, out WorldMultiInstanceRecord instance)
    {
        if (!id.IsValid || !_metadata.MultiFragments.IsPresent)
        {
            instance = default;
            return false;
        }
        ReadOnlySpan<byte> payload = _bytes.AsSpan(
            _metadata.MultiFragments.Offset, _metadata.MultiFragments.Length);
        int count = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        int instancesOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[28..]);
        int low = 0;
        int high = count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            ReadOnlySpan<byte> row = payload.Slice(
                instancesOffset + middle * FrozenWorldStructureFormat.MultiInstanceRowBytes,
                FrozenWorldStructureFormat.MultiInstanceRowBytes);
            ulong candidate = BinaryPrimitives.ReadUInt64LittleEndian(row);
            if (candidate == id.Value)
            {
                instance = FrozenWorldStructureCodec.ReadMultiInstance(row);
                return true;
            }
            if (candidate < id.Value) low = middle + 1;
            else high = middle - 1;
        }
        instance = default;
        return false;
    }

    /// <summary>Gets one canonical logical Multi root by sorted section-local index.</summary>
    public bool TryGetMultiInstanceAt(int index, out WorldMultiInstanceRecord instance)
    {
        if (index < 0 || index >= MultiInstanceCount || !_metadata.MultiFragments.IsPresent)
        {
            instance = default;
            return false;
        }
        ReadOnlySpan<byte> payload = _bytes.AsSpan(
            _metadata.MultiFragments.Offset, _metadata.MultiFragments.Length);
        int instancesOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[28..]);
        instance = FrozenWorldStructureCodec.ReadMultiInstance(payload.Slice(
            instancesOffset + index * FrozenWorldStructureFormat.MultiInstanceRowBytes,
            FrozenWorldStructureFormat.MultiInstanceRowBytes));
        return true;
    }

    /// <summary>Validates every structural material/static definition before generation admission.</summary>
    public bool TryBind(
        WorldContentBinding binding,
        out BoundWorldPack? bound,
        out WorldPackBindingError error)
    {
        ArgumentNullException.ThrowIfNull(binding);
        bound = null;
        if (binding.Fingerprint != ContentFingerprint)
        {
            error = WorldPackBindingError.ContentMismatch;
            return false;
        }
        var staticOwners = new BoundWorldPack.StaticIdentityOwner[FrozenStaticCount];
        var pieceOwners = new BoundWorldPack.MultiPieceIdentityOwner[MultiFragmentCount];
        int staticOwnerCount = 0;
        int pieceOwnerCount = 0;
        for (int chunk = 0; chunk < _metadata.ChunkCount; chunk++)
        {
            SpatialChunkKey key = WorldPackCodec.ReadChunkKey(_bytes, _metadata.ChunkDirectoryOffset, chunk);
            if (!TryGetBaseTerrain(key, out WorldTerrainChunkView terrain))
                throw new InvalidOperationException("Validated WorldPack directory could not resolve its own chunk.");
            for (int material = 0; material < terrain.MaterialCount; material++)
            {
                if (!binding.ContainsTerrainMaterial(terrain.GetMaterial(material)))
                {
                    error = WorldPackBindingError.UnknownTerrainMaterial;
                    return false;
                }
            }
            if (TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics))
            {
                for (int index = 0; index < statics.Count; index++)
                {
                    WorldFrozenStaticRecord record = statics.GetRecord(index);
                    if (!binding.ContainsStaticDefinition(record.DefinitionId))
                    {
                        error = WorldPackBindingError.UnknownStaticDefinition;
                        return false;
                    }
                    staticOwners[staticOwnerCount++] = new BoundWorldPack.StaticIdentityOwner(record.Id, chunk);
                }
            }
            if (TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments))
            {
                for (int index = 0; index < fragments.Count; index++)
                {
                    WorldMultiFragmentRecord record = fragments.GetRecord(index);
                    if (!binding.ContainsStaticDefinition(record.DefinitionId))
                    {
                        error = WorldPackBindingError.UnknownStaticDefinition;
                        return false;
                    }
                    pieceOwners[pieceOwnerCount++] = new BoundWorldPack.MultiPieceIdentityOwner(
                        record.MultiId, record.PieceId, chunk);
                }
            }
            if (TryGetAdditionalTerrainSurfaceBands(key, out WorldTerrainSurfaceBandChunkView bands))
            {
                for (int bandIndex = 0; bandIndex < bands.Count; bandIndex++)
                {
                    WorldTerrainSurfaceBandView band = bands.GetBand(bandIndex);
                    for (int z = 0; z < band.Bounds.CellDepth; z++)
                    for (int x = 0; x < band.Bounds.CellWidth; x++)
                    {
                        if (!band.IsCellPresent(x, z)) continue;
                        if (band.HasTop && !binding.ContainsTerrainMaterial(
                                band.GetMaterial(WorldTerrainSurfaceFace.Top, x, z)) ||
                            band.HasBottom && !binding.ContainsTerrainMaterial(
                                band.GetMaterial(WorldTerrainSurfaceFace.Bottom, x, z)))
                        {
                            error = WorldPackBindingError.UnknownTerrainMaterial;
                            return false;
                        }
                    }
                    if (band.HasTop && band.HasBottom &&
                        !binding.ContainsTerrainMaterial(band.SideMaterialId))
                    {
                        error = WorldPackBindingError.UnknownTerrainMaterial;
                        return false;
                    }
                }
            }
        }
        if (staticOwnerCount != staticOwners.Length || pieceOwnerCount != pieceOwners.Length)
            throw new InvalidOperationException("Validated WorldPack structural counts changed during binding.");
        Array.Sort(staticOwners, static (left, right) => left.Id.CompareTo(right.Id));
        Array.Sort(pieceOwners, static (left, right) =>
        {
            int multi = left.MultiId.CompareTo(right.MultiId);
            return multi != 0 ? multi : left.PieceId.CompareTo(right.PieceId);
        });
        bound = new BoundWorldPack(this, binding, staticOwners, pieceOwners);
        error = WorldPackBindingError.None;
        return true;
    }

    private static bool ReadExactly(Stream stream, Span<byte> destination)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int count = stream.Read(destination[read..]);
            if (count == 0) return false;
            read += count;
        }
        return true;
    }
}

/// <summary>Immutable WorldPack proven compatible with one concrete game-content binding.</summary>
public sealed class BoundWorldPack
{
    private readonly StaticIdentityOwner[] _staticOwners;
    private readonly MultiPieceIdentityOwner[] _pieceOwners;

    internal BoundWorldPack(
        WorldPack pack,
        WorldContentBinding binding,
        StaticIdentityOwner[] staticOwners,
        MultiPieceIdentityOwner[] pieceOwners)
    {
        Pack = pack;
        Content = binding;
        _staticOwners = staticOwners;
        _pieceOwners = pieceOwners;
    }

    public WorldPack Pack { get; }
    public WorldContentBinding Content { get; }
    public WorldPackFingerprint Fingerprint => Pack.Fingerprint;
    public SpatialSpaceId Space => Pack.Space;
    public int ChunkCount => Pack.ChunkCount;
    public int FrozenStaticCount => Pack.FrozenStaticCount;
    public int MultiInstanceCount => Pack.MultiInstanceCount;
    public int MultiFragmentCount => Pack.MultiFragmentCount;
    public int AdditionalTerrainSurfaceFragmentCount => Pack.AdditionalTerrainSurfaceFragmentCount;
    public long StructuralIdentityIndexReservedBytes =>
        checked((long)_staticOwners.Length * Marshal.SizeOf<StaticIdentityOwner>() +
            (long)_pieceOwners.Length * Marshal.SizeOf<MultiPieceIdentityOwner>());

    public bool TryGetBaseTerrain(SpatialChunkKey key, out WorldTerrainChunkView view) =>
        Pack.TryGetBaseTerrain(key, out view);

    public bool TryGetChunkKey(int index, out SpatialChunkKey key) => Pack.TryGetChunkKey(index, out key);

    public bool TryGetFrozenStatics(SpatialChunkKey key, out WorldFrozenStaticChunkView view) =>
        Pack.TryGetFrozenStatics(key, out view);

    public bool TryGetMultiFragments(SpatialChunkKey key, out WorldMultiFragmentChunkView view) =>
        Pack.TryGetMultiFragments(key, out view);

    public bool TryGetAdditionalTerrainSurfaceBands(
        SpatialChunkKey key,
        out WorldTerrainSurfaceBandChunkView view) =>
        Pack.TryGetAdditionalTerrainSurfaceBands(key, out view);

    public bool TryGetMultiInstance(MultiInstanceId id, out WorldMultiInstanceRecord instance) =>
        Pack.TryGetMultiInstance(id, out instance);

    public bool TryGetMultiInstanceAt(int index, out WorldMultiInstanceRecord instance) =>
        Pack.TryGetMultiInstanceAt(index, out instance);

    /// <summary>
    /// Finds the immutable home of one validated static identity without rescanning the pack.
    /// This cold binding index supports changed-set-proportional overlay validation.
    /// </summary>
    internal bool TryGetStaticOwner(WorldStaticId id, out SpatialChunkKey key)
    {
        int low = 0;
        int high = _staticOwners.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = _staticOwners[middle].Id.CompareTo(id);
            if (order == 0)
            {
                return Pack.TryGetChunkKey(_staticOwners[middle].ChunkIndex, out key);
            }
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        key = default;
        return false;
    }

    /// <summary>Finds the immutable home of one validated logical Multi piece.</summary>
    internal bool TryGetMultiPieceOwner(
        MultiInstanceId multiId,
        MultiPieceId pieceId,
        out SpatialChunkKey key)
    {
        int low = 0;
        int high = _pieceOwners.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            ref readonly MultiPieceIdentityOwner candidate = ref _pieceOwners[middle];
            int order = candidate.MultiId.CompareTo(multiId);
            if (order == 0) order = candidate.PieceId.CompareTo(pieceId);
            if (order == 0)
            {
                return Pack.TryGetChunkKey(candidate.ChunkIndex, out key);
            }
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        key = default;
        return false;
    }

    internal readonly record struct StaticIdentityOwner(WorldStaticId Id, int ChunkIndex);
    internal readonly record struct MultiPieceIdentityOwner(
        MultiInstanceId MultiId,
        MultiPieceId PieceId,
        int ChunkIndex);
}

/// <summary>Borrowed immutable view of one dense base-terrain chunk.</summary>
public readonly ref struct WorldTerrainChunkView
{
    private readonly ReadOnlySpan<byte> _materials;
    private readonly ReadOnlySpan<byte> _heights;
    private readonly ReadOnlySpan<byte> _surfacePresence;

    internal WorldTerrainChunkView(
        SpatialChunkKey key,
        ReadOnlySpan<byte> materials,
        ReadOnlySpan<byte> heights,
        ReadOnlySpan<byte> surfacePresence = default)
    {
        Key = key;
        _materials = materials;
        _heights = heights;
        _surfacePresence = surfacePresence;
    }

    public SpatialChunkKey Key { get; }
    public int MaterialCount => TerrainChunkFormat.MaterialCellCount;
    public int HeightVertexCount => TerrainChunkFormat.HeightVertexCount;
    public bool HasExplicitSurfacePresence => !_surfacePresence.IsEmpty;
    public int PresentSurfaceCount
    {
        get
        {
            if (_surfacePresence.IsEmpty) return TerrainChunkFormat.MaterialCellCount;
            int count = 0;
            for (int word = 0; word < TerrainChunkFormat.SurfacePresenceWordCount; word++)
                count += System.Numerics.BitOperations.PopCount(BinaryPrimitives.ReadUInt64LittleEndian(
                    _surfacePresence[(word * sizeof(ulong))..]));
            return count;
        }
    }
    public TerrainHeightPlaneBase HeightBase
    {
        get
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(_heights);
            if (!TerrainHeightPlaneBase.TryCreate(value, out TerrainHeightPlaneBase heightBase))
                throw new InvalidOperationException("Validated pack exposed an invalid terrain base.");
            return heightBase;
        }
    }

    public ushort GetMaterial(int index)
    {
        if (index < 0 || index >= MaterialCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return BinaryPrimitives.ReadUInt16LittleEndian(_materials[(index * sizeof(ushort))..]);
    }

    public bool IsSurfacePresent(int materialCellIndex)
    {
        if (materialCellIndex < 0 || materialCellIndex >= TerrainChunkFormat.MaterialCellCount)
            throw new ArgumentOutOfRangeException(nameof(materialCellIndex));
        if (_surfacePresence.IsEmpty) return true;
        int word = materialCellIndex >> 6;
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_surfacePresence[(word * sizeof(ulong))..]);
        return (value & (1UL << (materialCellIndex & 63))) != 0;
    }

    public bool IsSurfacePresent(TerrainCellCoord cell) =>
        IsSurfacePresent(TerrainChunkFormat.GetMaterialIndex(cell));

    public TerrainHeightOffset GetHeightOffset(int index)
    {
        if (index < 0 || index >= HeightVertexCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        int offset = TerrainChunkFormat.HeightPlaneBaseBytes + index * sizeof(ushort);
        return new TerrainHeightOffset(BinaryPrimitives.ReadUInt16LittleEndian(_heights[offset..]));
    }

    public int GetAbsoluteHeightMillimeters(int index) => checked(
        HeightBase.Millimeters + GetHeightOffset(index).OffsetMillimeters);

    public void CopyMaterials(Span<ushort> destination)
    {
        if (destination.Length < MaterialCount) throw new ArgumentException("Destination is too small.", nameof(destination));
        for (int index = 0; index < MaterialCount; index++) destination[index] = GetMaterial(index);
    }

    public void CopyAbsoluteHeights(Span<int> destination)
    {
        if (destination.Length < HeightVertexCount) throw new ArgumentException("Destination is too small.", nameof(destination));
        TerrainHeightPlaneBase heightBase = HeightBase;
        for (int index = 0; index < HeightVertexCount; index++)
            destination[index] = checked(heightBase.Millimeters + GetHeightOffset(index).OffsetMillimeters);
    }

    public void CopySurfacePresence(Span<ulong> destination)
    {
        if (destination.Length < TerrainChunkFormat.SurfacePresenceWordCount)
            throw new ArgumentException("Destination is too small.", nameof(destination));
        if (_surfacePresence.IsEmpty)
        {
            TerrainSurfacePresence.FillPresent(destination[..TerrainChunkFormat.SurfacePresenceWordCount]);
            return;
        }
        for (int word = 0; word < TerrainChunkFormat.SurfacePresenceWordCount; word++)
            destination[word] = BinaryPrimitives.ReadUInt64LittleEndian(
                _surfacePresence[(word * sizeof(ulong))..]);
    }

    internal ReadOnlySpan<byte> MaterialBytes => _materials;
    internal ReadOnlySpan<byte> HeightBytes => _heights;
    internal ReadOnlySpan<byte> SurfacePresenceBytes => _surfacePresence;
}

internal readonly record struct WorldPackMetadata(
    int ChunkCount,
    int SectionCount,
    int ChunkDirectoryOffset,
    int SectionDirectoryOffset,
    int PayloadOffset,
    ulong SpaceValue,
    WorldContentFingerprint ContentFingerprint,
    WorldPackFingerprint PackFingerprint,
    WorldPackSectionMetadata FrozenStatics,
    WorldPackSectionMetadata MultiFragments,
    WorldPackSectionMetadata TerrainPresence,
    WorldPackSectionMetadata AdditionalTerrainSurfaces);

internal readonly record struct WorldPackSectionMetadata(
    uint Kind,
    uint Version,
    int Offset,
    int Length,
    ulong Count)
{
    internal bool IsPresent => Kind != 0;
}

internal static class WorldPackCodec
{
    internal static bool TryReadBoundedLength(
        ReadOnlySpan<byte> source,
        WorldPackLimits limits,
        out int length,
        out WorldPackOpenError error)
    {
        length = 0;
        if (!limits.IsValid) throw new ArgumentOutOfRangeException(nameof(limits));
        if (source.Length < WorldPackFormat.HeaderBytes)
        {
            error = WorldPackOpenError.Truncated;
            return false;
        }
        if (BinaryPrimitives.ReadUInt64LittleEndian(source) != WorldPackFormat.Magic)
        {
            error = WorldPackOpenError.InvalidMagic;
            return false;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) != WorldPackFormat.ContainerVersion)
        {
            error = WorldPackOpenError.UnsupportedVersion;
            return false;
        }
        ulong declared = BinaryPrimitives.ReadUInt64LittleEndian(source[16..]);
        if (declared > int.MaxValue || declared > (ulong)limits.MaximumPackBytes)
        {
            error = WorldPackOpenError.CapacityExceeded;
            return false;
        }
        length = checked((int)declared);
        if (length < WorldPackFormat.HeaderBytes)
        {
            error = WorldPackOpenError.LengthMismatch;
            return false;
        }
        error = WorldPackOpenError.None;
        return true;
    }

    internal static bool TryValidate(
        ReadOnlySpan<byte> bytes,
        WorldPackLimits limits,
        WorldContentFingerprint expectedContent,
        out WorldPackMetadata metadata,
        out WorldPackOpenError error)
    {
        metadata = default;
        if (!expectedContent.IsValid) throw new ArgumentOutOfRangeException(nameof(expectedContent));
        if (!TryReadBoundedLength(bytes, limits, out int length, out error)) return false;
        if (bytes.Length != length)
        {
            error = WorldPackOpenError.LengthMismatch;
            return false;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]) != WorldPackFormat.HeaderBytes ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[32..]) != GameWorldSpatialProfile.WorldChunkEdgeUnits ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[36..]) != GameWorldSpatialProfile.TerrainCellEdgeUnits ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[40..]) != TerrainChunkFormat.MaterialCellEdge ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[44..]) != TerrainChunkFormat.HeightVertexEdge ||
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[120..]) != 0)
        {
            error = WorldPackOpenError.InvalidHeader;
            return false;
        }
        ulong space = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]);
        if (space == 0)
        {
            error = WorldPackOpenError.InvalidHeader;
            return false;
        }
        ulong contentLow = BinaryPrimitives.ReadUInt64LittleEndian(bytes[48..]);
        ulong contentHigh = BinaryPrimitives.ReadUInt64LittleEndian(bytes[56..]);
        if (contentLow == 0 && contentHigh == 0)
        {
            error = WorldPackOpenError.InvalidHeader;
            return false;
        }
        var content = new WorldContentFingerprint(contentLow, contentHigh);
        if (content != expectedContent)
        {
            error = WorldPackOpenError.ContentMismatch;
            return false;
        }
        int chunkCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[64..]);
        int sectionCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[68..]);
        if (chunkCount <= 0 || chunkCount > limits.MaximumChunks || sectionCount < 2 ||
            sectionCount > limits.MaximumSections)
        {
            error = WorldPackOpenError.CapacityExceeded;
            return false;
        }
        if (!TryInt(bytes, 72, out int chunkDirectoryOffset) ||
            !TryInt(bytes, 80, out int chunkDirectoryLength) ||
            !TryInt(bytes, 88, out int sectionDirectoryOffset) ||
            !TryInt(bytes, 96, out int sectionDirectoryLength) ||
            !TryInt(bytes, 104, out int payloadOffset) ||
            !TryInt(bytes, 112, out int payloadLength))
        {
            error = WorldPackOpenError.InvalidHeader;
            return false;
        }
        int expectedChunkDirectoryLength;
        int expectedSectionDirectoryLength;
        try
        {
            expectedChunkDirectoryLength = checked(chunkCount * WorldPackFormat.ChunkDirectoryRowBytes);
            expectedSectionDirectoryLength = checked(sectionCount * WorldPackFormat.SectionDirectoryRowBytes);
        }
        catch (OverflowException)
        {
            error = WorldPackOpenError.InvalidHeader;
            return false;
        }
        if (chunkDirectoryOffset != WorldPackFormat.HeaderBytes ||
            chunkDirectoryLength != expectedChunkDirectoryLength ||
            sectionDirectoryOffset != WorldPackFormat.HeaderBytes + chunkDirectoryLength ||
            sectionDirectoryLength != expectedSectionDirectoryLength ||
            payloadOffset != sectionDirectoryOffset + sectionDirectoryLength ||
            payloadLength != length - payloadOffset)
        {
            error = WorldPackOpenError.InvalidHeader;
            return false;
        }

        WorldPackFingerprint storedFingerprint = WorldPackFingerprint.Read(
            bytes.Slice(WorldPackFormat.PackFingerprintOffset, WorldPackFormat.PackFingerprintBytes));
        if (!storedFingerprint.IsValid || storedFingerprint != ComputePackFingerprint(bytes))
        {
            error = WorldPackOpenError.HashMismatch;
            return false;
        }

        var candidate = new WorldPackMetadata(chunkCount, sectionCount, chunkDirectoryOffset,
            sectionDirectoryOffset, payloadOffset, space, content, storedFingerprint,
            default, default, default, default);
        if (!ValidateSections(bytes, candidate, out WorldPackSectionMetadata materials,
                out WorldPackSectionMetadata heights, out WorldPackSectionMetadata statics,
                out WorldPackSectionMetadata multis, out WorldPackSectionMetadata presence,
                out WorldPackSectionMetadata additionalTerrain, out error))
            return false;
        candidate = candidate with
        {
            FrozenStatics = statics,
            MultiFragments = multis,
            TerrainPresence = presence,
            AdditionalTerrainSurfaces = additionalTerrain,
        };
        if (!ValidateChunks(bytes, candidate, materials, heights, out error)) return false;
        if (statics.IsPresent && !FrozenWorldStructureCodec.TryValidateStaticSection(
                bytes, candidate, statics, limits))
        {
            error = WorldPackOpenError.InvalidFrozenStaticPayload;
            return false;
        }
        if (multis.IsPresent && !FrozenWorldStructureCodec.TryValidateMultiSection(
                bytes, candidate, multis, limits))
        {
            error = WorldPackOpenError.InvalidMultiPayload;
            return false;
        }
        if (additionalTerrain.IsPresent && !AdditionalTerrainSurfaceCodec.TryValidateSection(
                bytes, candidate, additionalTerrain, limits))
        {
            error = WorldPackOpenError.InvalidAdditionalTerrainSurfacePayload;
            return false;
        }
        metadata = candidate;
        error = WorldPackOpenError.None;
        return true;
    }

    internal static int FindChunkIndex(ReadOnlySpan<byte> bytes, WorldPackMetadata metadata, SpatialChunkKey key)
    {
        int low = 0;
        int high = metadata.ChunkCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            SpatialChunkKey candidate = ReadChunkKey(bytes, metadata.ChunkDirectoryOffset, middle);
            int order = candidate.CompareTo(key);
            if (order == 0) return middle;
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    internal static WorldPackFingerprint ComputePackFingerprint(ReadOnlySpan<byte> bytes)
    {
        Span<byte> header = stackalloc byte[WorldPackFormat.HeaderBytes];
        bytes[..WorldPackFormat.HeaderBytes].CopyTo(header);
        header.Slice(WorldPackFormat.PackFingerprintOffset, WorldPackFormat.PackFingerprintBytes).Clear();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(header);
        hash.AppendData(bytes[WorldPackFormat.HeaderBytes..]);
        Span<byte> digest = stackalloc byte[32];
        if (!hash.TryGetHashAndReset(digest, out int written) || written != digest.Length)
            throw new InvalidOperationException("SHA-256 did not produce its canonical digest width.");
        return WorldPackFingerprint.Read(digest);
    }

    internal static byte[] ComputeDigest128(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(bytes, digest);
        return digest[..16].ToArray();
    }

    private static bool ValidateSections(
        ReadOnlySpan<byte> bytes,
        WorldPackMetadata metadata,
        out WorldPackSectionMetadata materials,
        out WorldPackSectionMetadata heights,
        out WorldPackSectionMetadata statics,
        out WorldPackSectionMetadata multis,
        out WorldPackSectionMetadata presence,
        out WorldPackSectionMetadata additionalTerrain,
        out WorldPackOpenError error)
    {
        materials = default;
        heights = default;
        statics = default;
        multis = default;
        presence = default;
        additionalTerrain = default;
        uint priorKind = 0;
        int expectedOffset = metadata.PayloadOffset;
        Span<byte> digest = stackalloc byte[32];
        for (int index = 0; index < metadata.SectionCount; index++)
        {
            int rowOffset = metadata.SectionDirectoryOffset + index * WorldPackFormat.SectionDirectoryRowBytes;
            ReadOnlySpan<byte> row = bytes.Slice(rowOffset, WorldPackFormat.SectionDirectoryRowBytes);
            uint kind = BinaryPrimitives.ReadUInt32LittleEndian(row);
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(row[4..]);
            if (kind == 0 || kind <= priorKind || version == 0 ||
                !TryInt(row, 8, out int offset) || !TryInt(row, 16, out int length) ||
                offset != expectedOffset || length < 0 || offset > bytes.Length - length)
            {
                error = WorldPackOpenError.InvalidSectionDirectory;
                return false;
            }
            SHA256.HashData(bytes.Slice(offset, length), digest);
            if (!digest[..16].SequenceEqual(row[32..48]))
            {
                error = WorldPackOpenError.HashMismatch;
                return false;
            }
            ulong count = BinaryPrimitives.ReadUInt64LittleEndian(row[24..]);
            var section = new WorldPackSectionMetadata(kind, version, offset, length, count);
            if (kind == (uint)WorldPackSectionKind.TerrainMaterials) materials = section;
            if (kind == (uint)WorldPackSectionKind.TerrainHeights) heights = section;
            if (kind == (uint)WorldPackSectionKind.FrozenStatics) statics = section;
            if (kind == (uint)WorldPackSectionKind.MultiFragments) multis = section;
            if (kind == (uint)WorldPackSectionKind.TerrainPresence) presence = section;
            if (kind == (uint)WorldPackSectionKind.AdditionalTerrainSurfaces)
                additionalTerrain = section;
            priorKind = kind;
            expectedOffset = checked(offset + length);
        }
        if (expectedOffset != bytes.Length)
        {
            error = WorldPackOpenError.InvalidSectionDirectory;
            return false;
        }
        if (materials.Kind == 0 || heights.Kind == 0)
        {
            error = WorldPackOpenError.MissingRequiredSection;
            return false;
        }
        if (materials.Version != WorldPackFormat.TerrainMaterialsSchemaVersion ||
            heights.Version != WorldPackFormat.TerrainHeightsSchemaVersion ||
            statics.IsPresent && statics.Version != WorldPackFormat.FrozenStaticsSchemaVersion ||
            multis.IsPresent && multis.Version != WorldPackFormat.MultiFragmentsSchemaVersion ||
            presence.IsPresent && presence.Version != WorldPackFormat.TerrainPresenceSchemaVersion ||
            additionalTerrain.IsPresent &&
                additionalTerrain.Version != WorldPackFormat.AdditionalTerrainSurfacesSchemaVersion)
        {
            error = WorldPackOpenError.UnsupportedRequiredSection;
            return false;
        }
        try
        {
            if (materials.Length != checked(metadata.ChunkCount * TerrainChunkFormat.MaterialPlaneBytes) ||
                materials.Count != checked((ulong)metadata.ChunkCount * TerrainChunkFormat.MaterialCellCount) ||
                heights.Length != checked(metadata.ChunkCount * TerrainChunkFormat.HeightPlaneBytes) ||
                heights.Count != checked((ulong)metadata.ChunkCount * TerrainChunkFormat.HeightVertexCount) ||
                presence.IsPresent &&
                (presence.Length != checked(metadata.ChunkCount * TerrainChunkFormat.SurfacePresencePlaneBytes) ||
                    presence.Count != checked((ulong)metadata.ChunkCount * TerrainChunkFormat.MaterialCellCount)))
            {
                error = WorldPackOpenError.InvalidSectionDirectory;
                return false;
            }
        }
        catch (OverflowException)
        {
            error = WorldPackOpenError.InvalidSectionDirectory;
            return false;
        }
        if (presence.IsPresent)
        {
            bool hasAbsent = false;
            ReadOnlySpan<byte> payload = bytes.Slice(presence.Offset, presence.Length);
            for (int offset = 0; offset < payload.Length; offset += sizeof(ulong))
            {
                if (BinaryPrimitives.ReadUInt64LittleEndian(payload[offset..]) == ulong.MaxValue) continue;
                hasAbsent = true;
                break;
            }
            if (!hasAbsent)
            {
                error = WorldPackOpenError.InvalidTerrainPayload;
                return false;
            }
        }
        error = WorldPackOpenError.None;
        return true;
    }

    private static bool ValidateChunks(
        ReadOnlySpan<byte> bytes,
        WorldPackMetadata metadata,
        WorldPackSectionMetadata materials,
        WorldPackSectionMetadata heights,
        out WorldPackOpenError error)
    {
        SpatialChunkKey prior = default;
        for (int index = 0; index < metadata.ChunkCount; index++)
        {
            int rowOffset = metadata.ChunkDirectoryOffset + index * WorldPackFormat.ChunkDirectoryRowBytes;
            ReadOnlySpan<byte> row = bytes.Slice(rowOffset, WorldPackFormat.ChunkDirectoryRowBytes);
            if (BinaryPrimitives.ReadUInt64LittleEndian(row) != metadata.SpaceValue)
            {
                error = WorldPackOpenError.InvalidChunkDirectory;
                return false;
            }
            SpatialChunkKey key = ReadChunkKey(bytes, metadata.ChunkDirectoryOffset, index);
            if (index != 0 && key.CompareTo(prior) <= 0)
            {
                error = WorldPackOpenError.InvalidChunkDirectory;
                return false;
            }
            prior = key;
            if (!TryInt(row, 16, out int materialOffset) ||
                BinaryPrimitives.ReadUInt32LittleEndian(row[24..]) != TerrainChunkFormat.MaterialPlaneBytes ||
                BinaryPrimitives.ReadUInt32LittleEndian(row[28..]) != TerrainChunkFormat.MaterialCellCount ||
                !TryInt(row, 32, out int heightOffset) ||
                BinaryPrimitives.ReadUInt32LittleEndian(row[40..]) != TerrainChunkFormat.HeightPlaneBytes ||
                BinaryPrimitives.ReadUInt32LittleEndian(row[44..]) != TerrainChunkFormat.HeightVertexCount ||
                materialOffset != materials.Offset + index * TerrainChunkFormat.MaterialPlaneBytes ||
                heightOffset != heights.Offset + index * TerrainChunkFormat.HeightPlaneBytes)
            {
                error = WorldPackOpenError.InvalidChunkDirectory;
                return false;
            }
            ReadOnlySpan<byte> materialPlane = bytes.Slice(materialOffset, TerrainChunkFormat.MaterialPlaneBytes);
            for (int material = 0; material < TerrainChunkFormat.MaterialCellCount; material++)
            {
                if (BinaryPrimitives.ReadUInt16LittleEndian(materialPlane[(material * sizeof(ushort))..]) == 0)
                {
                    error = WorldPackOpenError.InvalidTerrainPayload;
                    return false;
                }
            }
            ReadOnlySpan<byte> heightPlane = bytes.Slice(heightOffset, TerrainChunkFormat.HeightPlaneBytes);
            int heightBase = BinaryPrimitives.ReadInt32LittleEndian(heightPlane);
            if (!TerrainHeightPlaneBase.TryCreate(heightBase, out _))
            {
                error = WorldPackOpenError.InvalidTerrainPayload;
                return false;
            }
            for (int vertex = 0; vertex < TerrainChunkFormat.HeightVertexCount; vertex++)
            {
                ushort units = BinaryPrimitives.ReadUInt16LittleEndian(
                    heightPlane[(TerrainChunkFormat.HeightPlaneBaseBytes + vertex * sizeof(ushort))..]);
                long absolute = (long)heightBase + units * TerrainChunkFormat.HeightQuantumMillimeters;
                if (absolute is < int.MinValue or > int.MaxValue)
                {
                    error = WorldPackOpenError.InvalidTerrainPayload;
                    return false;
                }
            }
        }

        for (int index = 0; index < metadata.ChunkCount; index++)
        {
            SpatialChunkKey key = ReadChunkKey(bytes, metadata.ChunkDirectoryOffset, index);
            if (key.Coord.X != int.MaxValue)
            {
                var right = new SpatialChunkKey(key.Space, key.Coord.X + 1, key.Coord.Z);
                int rightIndex = FindChunkIndex(bytes, metadata, right);
                if (rightIndex >= 0 && !PackedEdgesMatch(bytes, metadata, index, rightIndex, vertical: true))
                {
                    error = WorldPackOpenError.SharedEdgeMismatch;
                    return false;
                }
            }
            if (key.Coord.Z != int.MaxValue)
            {
                var bottom = new SpatialChunkKey(key.Space, key.Coord.X, key.Coord.Z + 1);
                int bottomIndex = FindChunkIndex(bytes, metadata, bottom);
                if (bottomIndex >= 0 && !PackedEdgesMatch(bytes, metadata, index, bottomIndex, vertical: false))
                {
                    error = WorldPackOpenError.SharedEdgeMismatch;
                    return false;
                }
            }
        }
        error = WorldPackOpenError.None;
        return true;
    }

    private static bool PackedEdgesMatch(
        ReadOnlySpan<byte> bytes,
        WorldPackMetadata metadata,
        int firstIndex,
        int secondIndex,
        bool vertical)
    {
        ReadOnlySpan<byte> firstRow = bytes.Slice(
            metadata.ChunkDirectoryOffset + firstIndex * WorldPackFormat.ChunkDirectoryRowBytes,
            WorldPackFormat.ChunkDirectoryRowBytes);
        ReadOnlySpan<byte> secondRow = bytes.Slice(
            metadata.ChunkDirectoryOffset + secondIndex * WorldPackFormat.ChunkDirectoryRowBytes,
            WorldPackFormat.ChunkDirectoryRowBytes);
        int firstOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(firstRow[32..]));
        int secondOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(secondRow[32..]));
        ReadOnlySpan<byte> first = bytes.Slice(firstOffset, TerrainChunkFormat.HeightPlaneBytes);
        ReadOnlySpan<byte> second = bytes.Slice(secondOffset, TerrainChunkFormat.HeightPlaneBytes);
        int firstBase = BinaryPrimitives.ReadInt32LittleEndian(first);
        int secondBase = BinaryPrimitives.ReadInt32LittleEndian(second);
        for (byte vertex = 0; vertex < TerrainChunkFormat.HeightVertexEdge; vertex++)
        {
            TerrainVertexCoord firstCoord = vertical
                ? new TerrainVertexCoord(32, vertex)
                : new TerrainVertexCoord(vertex, 32);
            TerrainVertexCoord secondCoord = vertical
                ? new TerrainVertexCoord(0, vertex)
                : new TerrainVertexCoord(vertex, 0);
            int firstVertex = TerrainChunkFormat.GetHeightIndex(firstCoord);
            int secondVertex = TerrainChunkFormat.GetHeightIndex(secondCoord);
            int firstHeight = checked(firstBase + BinaryPrimitives.ReadUInt16LittleEndian(
                first[(TerrainChunkFormat.HeightPlaneBaseBytes + firstVertex * sizeof(ushort))..]) *
                TerrainChunkFormat.HeightQuantumMillimeters);
            int secondHeight = checked(secondBase + BinaryPrimitives.ReadUInt16LittleEndian(
                second[(TerrainChunkFormat.HeightPlaneBaseBytes + secondVertex * sizeof(ushort))..]) *
                TerrainChunkFormat.HeightQuantumMillimeters);
            if (firstHeight != secondHeight) return false;
        }
        return true;
    }

    internal static SpatialChunkKey ReadChunkKey(ReadOnlySpan<byte> bytes, int directoryOffset, int index)
    {
        ReadOnlySpan<byte> row = bytes.Slice(
            directoryOffset + index * WorldPackFormat.ChunkDirectoryRowBytes,
            WorldPackFormat.ChunkDirectoryRowBytes);
        return new SpatialChunkKey(
            new SpatialSpaceId(BinaryPrimitives.ReadUInt64LittleEndian(row)),
            BinaryPrimitives.ReadInt32LittleEndian(row[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(row[12..]));
    }

    private static bool TryInt(ReadOnlySpan<byte> source, int offset, out int value)
    {
        ulong raw = BinaryPrimitives.ReadUInt64LittleEndian(source[offset..]);
        if (raw > int.MaxValue)
        {
            value = 0;
            return false;
        }
        value = (int)raw;
        return true;
    }

}
