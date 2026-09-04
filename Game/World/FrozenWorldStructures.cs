using System.Buffers.Binary;
using System.Runtime.InteropServices;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>Stable numeric content identity for one frozen static definition.</summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct WorldStaticDefinitionId : IEquatable<WorldStaticDefinitionId>, IComparable<WorldStaticDefinitionId>
{
    public WorldStaticDefinitionId(uint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public uint Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(WorldStaticDefinitionId other) => Value.CompareTo(other.Value);
    public bool Equals(WorldStaticDefinitionId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is WorldStaticDefinitionId other && Equals(other);
    public override int GetHashCode() => unchecked((int)Value);
    public override string ToString() => IsValid ? $"StaticDefinition({Value})" : "StaticDefinition(Invalid)";
    public static bool operator ==(WorldStaticDefinitionId left, WorldStaticDefinitionId right) => left.Equals(right);
    public static bool operator !=(WorldStaticDefinitionId left, WorldStaticDefinitionId right) => !left.Equals(right);
}

/// <summary>Stable identity of one independent frozen static within one WorldPack domain.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct WorldStaticId : IEquatable<WorldStaticId>, IComparable<WorldStaticId>
{
    public WorldStaticId(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(WorldStaticId other) => Value.CompareTo(other.Value);
    public bool Equals(WorldStaticId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is WorldStaticId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Value);
    public override string ToString() => IsValid ? $"WorldStatic({Value})" : "WorldStatic(Invalid)";
    public static bool operator ==(WorldStaticId left, WorldStaticId right) => left.Equals(right);
    public static bool operator !=(WorldStaticId left, WorldStaticId right) => !left.Equals(right);
}

/// <summary>Stable identity of one static placed logical Multi within one WorldPack domain.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct MultiInstanceId : IEquatable<MultiInstanceId>, IComparable<MultiInstanceId>
{
    public MultiInstanceId(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(MultiInstanceId other) => Value.CompareTo(other.Value);
    public bool Equals(MultiInstanceId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is MultiInstanceId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Value);
    public override string ToString() => IsValid ? $"Multi({Value})" : "Multi(Invalid)";
    public static bool operator ==(MultiInstanceId left, MultiInstanceId right) => left.Equals(right);
    public static bool operator !=(MultiInstanceId left, MultiInstanceId right) => !left.Equals(right);
}

/// <summary>Stable source-piece identity within one logical Multi.</summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct MultiPieceId : IEquatable<MultiPieceId>, IComparable<MultiPieceId>
{
    public MultiPieceId(uint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public uint Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(MultiPieceId other) => Value.CompareTo(other.Value);
    public bool Equals(MultiPieceId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is MultiPieceId other && Equals(other);
    public override int GetHashCode() => unchecked((int)Value);
    public static bool operator ==(MultiPieceId left, MultiPieceId right) => left.Equals(right);
    public static bool operator !=(MultiPieceId left, MultiPieceId right) => !left.Equals(right);
}

[Flags]
public enum WorldStaticFlags : ushort
{
    None = 0,
    Renderable = 1 << 0,
    Collidable = 1 << 1,
    /// <summary>
    /// Rare physical-policy override for a solid whose top must not establish standing/landing.
    /// Ordinary collidable solids support on top by default.
    /// </summary>
    NoTopSupport = 1 << 2,
}

/// <summary>Shared physical interpretation of compact frozen-static/Multi flags.</summary>
public static class WorldStaticPhysicalSemantics
{
    public static bool BlocksBody(WorldStaticFlags flags) =>
        (flags & WorldStaticFlags.Collidable) != 0;

    public static bool SupportsTop(WorldStaticFlags flags) =>
        BlocksBody(flags) && (flags & WorldStaticFlags.NoTopSupport) == 0;
}

/// <summary>
/// Engine-neutral yaw for one static world placement. The unsigned 16-bit domain spans one complete
/// turn: zero preserves the authored canonical facing and increasing values apply right-handed rotation
/// around +Y (a quarter turn maps local +Z to +X and local +X to -Z).
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 2)]
public readonly struct WorldYaw : IEquatable<WorldYaw>, IComparable<WorldYaw>
{
    public const int UnitsPerTurn = 65_536;
    public const ushort QuarterTurnUnits = 16_384;
    public const ushort HalfTurnUnits = 32_768;
    public const ushort ThreeQuarterTurnUnits = 49_152;

    public WorldYaw(ushort units) => Units = units;

    public ushort Units { get; }
    public static WorldYaw Zero => default;
    public static WorldYaw QuarterTurn => new(QuarterTurnUnits);
    public static WorldYaw HalfTurn => new(HalfTurnUnits);
    public static WorldYaw ThreeQuarterTurn => new(ThreeQuarterTurnUnits);

    public int CompareTo(WorldYaw other) => Units.CompareTo(other.Units);
    public bool Equals(WorldYaw other) => Units == other.Units;
    public override bool Equals(object? obj) => obj is WorldYaw other && Equals(other);
    public override int GetHashCode() => Units;
    public override string ToString() => $"Yaw({Units}/{UnitsPerTurn})";
    public static bool operator ==(WorldYaw left, WorldYaw right) => left.Equals(right);
    public static bool operator !=(WorldYaw left, WorldYaw right) => !left.Equals(right);
}

/// <summary>Exact half-open authoritative bounds for immutable static geometry.</summary>
public readonly struct WorldStaticBounds : IEquatable<WorldStaticBounds>
{
    public WorldStaticBounds(
        long minimumXMillimeters,
        long minimumYMillimeters,
        long minimumZMillimeters,
        long maximumXExclusiveMillimeters,
        long maximumYExclusiveMillimeters,
        long maximumZExclusiveMillimeters)
    {
        if (minimumXMillimeters < GameWorldSpatialProfile.MinimumGlobalAxisUnits ||
            minimumZMillimeters < GameWorldSpatialProfile.MinimumGlobalAxisUnits ||
            maximumXExclusiveMillimeters > GameWorldSpatialProfile.MaximumGlobalAxisUnits + 1 ||
            maximumZExclusiveMillimeters > GameWorldSpatialProfile.MaximumGlobalAxisUnits + 1 ||
            minimumYMillimeters < int.MinValue || maximumYExclusiveMillimeters > (long)int.MaxValue + 1 ||
            minimumXMillimeters >= maximumXExclusiveMillimeters ||
            minimumYMillimeters >= maximumYExclusiveMillimeters ||
            minimumZMillimeters >= maximumZExclusiveMillimeters)
            throw new ArgumentOutOfRangeException(nameof(minimumXMillimeters));
        MinimumXMillimeters = minimumXMillimeters;
        MinimumYMillimeters = minimumYMillimeters;
        MinimumZMillimeters = minimumZMillimeters;
        MaximumXExclusiveMillimeters = maximumXExclusiveMillimeters;
        MaximumYExclusiveMillimeters = maximumYExclusiveMillimeters;
        MaximumZExclusiveMillimeters = maximumZExclusiveMillimeters;
    }

    public long MinimumXMillimeters { get; }
    public long MinimumYMillimeters { get; }
    public long MinimumZMillimeters { get; }
    public long MaximumXExclusiveMillimeters { get; }
    public long MaximumYExclusiveMillimeters { get; }
    public long MaximumZExclusiveMillimeters { get; }
    public bool IsValid =>
        MinimumXMillimeters >= GameWorldSpatialProfile.MinimumGlobalAxisUnits &&
        MinimumZMillimeters >= GameWorldSpatialProfile.MinimumGlobalAxisUnits &&
        MaximumXExclusiveMillimeters <= GameWorldSpatialProfile.MaximumGlobalAxisUnits + 1 &&
        MaximumZExclusiveMillimeters <= GameWorldSpatialProfile.MaximumGlobalAxisUnits + 1 &&
        MinimumYMillimeters >= int.MinValue && MaximumYExclusiveMillimeters <= (long)int.MaxValue + 1 &&
        MinimumXMillimeters < MaximumXExclusiveMillimeters &&
        MinimumYMillimeters < MaximumYExclusiveMillimeters &&
        MinimumZMillimeters < MaximumZExclusiveMillimeters;

    public bool Intersects(in WorldStaticBounds other) =>
        MinimumXMillimeters < other.MaximumXExclusiveMillimeters &&
        MaximumXExclusiveMillimeters > other.MinimumXMillimeters &&
        MinimumYMillimeters < other.MaximumYExclusiveMillimeters &&
        MaximumYExclusiveMillimeters > other.MinimumYMillimeters &&
        MinimumZMillimeters < other.MaximumZExclusiveMillimeters &&
        MaximumZExclusiveMillimeters > other.MinimumZMillimeters;

    public bool Equals(WorldStaticBounds other) =>
        MinimumXMillimeters == other.MinimumXMillimeters &&
        MinimumYMillimeters == other.MinimumYMillimeters &&
        MinimumZMillimeters == other.MinimumZMillimeters &&
        MaximumXExclusiveMillimeters == other.MaximumXExclusiveMillimeters &&
        MaximumYExclusiveMillimeters == other.MaximumYExclusiveMillimeters &&
        MaximumZExclusiveMillimeters == other.MaximumZExclusiveMillimeters;
    public override bool Equals(object? obj) => obj is WorldStaticBounds other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        HashCode.Combine(MinimumXMillimeters, MinimumYMillimeters, MinimumZMillimeters),
        HashCode.Combine(MaximumXExclusiveMillimeters, MaximumYExclusiveMillimeters,
            MaximumZExclusiveMillimeters));
}

/// <summary>Cold compiler input for one independent frozen static.</summary>
public readonly record struct WorldFrozenStaticSource(
    WorldStaticId Id,
    WorldStaticDefinitionId DefinitionId,
    WorldPosition Position,
    WorldYaw Orientation,
    ushort Variant,
    WorldStaticFlags Flags,
    WorldStaticBounds Bounds);

/// <summary>Cold already-resolved source piece of one static placed logical Multi.</summary>
public readonly record struct WorldMultiPieceSource(
    MultiPieceId PieceId,
    WorldStaticDefinitionId DefinitionId,
    WorldPosition Position,
    WorldYaw Orientation,
    ushort Variant,
    WorldStaticFlags Flags,
    WorldStaticBounds Bounds);

/// <summary>
/// Internal deterministic packed-row grouping hint. Instance transform and identity are deliberately
/// excluded; this is not a renderer, Godot, Game.Contracts, or compatibility ABI.
/// </summary>
public readonly record struct WorldStaticRenderKey(
    WorldStaticDefinitionId DefinitionId,
    ushort Variant,
    WorldStaticFlags Flags) : IComparable<WorldStaticRenderKey>
{
    public int CompareTo(WorldStaticRenderKey other)
    {
        int definition = DefinitionId.CompareTo(other.DefinitionId);
        if (definition != 0) return definition;
        int variant = Variant.CompareTo(other.Variant);
        return variant != 0 ? variant : Flags.CompareTo(other.Flags);
    }
}

public readonly record struct WorldStaticRenderGroup(WorldStaticRenderKey Key, int Offset, int Count);

/// <summary>Decoded immutable independent-static record.</summary>
public readonly record struct WorldFrozenStaticRecord(
    WorldStaticId Id,
    WorldStaticDefinitionId DefinitionId,
    WorldPosition Position,
    WorldYaw Orientation,
    ushort Variant,
    WorldStaticFlags Flags,
    WorldStaticBounds Bounds)
{
    public WorldStaticRenderKey RenderKey => new(DefinitionId, Variant, Flags);
}

/// <summary>Decoded immutable Multi-fragment record.</summary>
public readonly record struct WorldMultiFragmentRecord(
    MultiInstanceId MultiId,
    MultiPieceId PieceId,
    WorldStaticDefinitionId DefinitionId,
    WorldPosition Position,
    WorldYaw Orientation,
    ushort Variant,
    WorldStaticFlags Flags,
    WorldStaticBounds Bounds)
{
    public WorldStaticRenderKey RenderKey => new(DefinitionId, Variant, Flags);
}

/// <summary>Logical root retained independently from chunk-local Multi fragments.</summary>
public readonly record struct WorldMultiInstanceRecord(
    MultiInstanceId Id,
    WorldPosition Root,
    WorldYaw Orientation,
    int PieceCount);

/// <summary>Borrowed packed independent-static rows belonging to one canonical home chunk.</summary>
public readonly ref struct WorldFrozenStaticChunkView
{
    private readonly ReadOnlySpan<byte> _rows;
    private readonly ReadOnlySpan<WorldFrozenStaticRecord> _records;

    internal WorldFrozenStaticChunkView(SpatialChunkKey key, ReadOnlySpan<byte> rows)
    {
        Key = key;
        _rows = rows;
        _records = default;
    }

    internal WorldFrozenStaticChunkView(SpatialChunkKey key, ReadOnlySpan<WorldFrozenStaticRecord> records)
    {
        Key = key;
        _rows = default;
        _records = records;
    }

    public SpatialChunkKey Key { get; }
    public int Count => !_records.IsEmpty
        ? _records.Length
        : _rows.Length / FrozenWorldStructureFormat.StaticRowBytes;

    public WorldFrozenStaticRecord GetRecord(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count);
        return !_records.IsEmpty ? _records[index] : FrozenWorldStructureCodec.ReadStaticRecord(
            _rows.Slice(index * FrozenWorldStructureFormat.StaticRowBytes,
                FrozenWorldStructureFormat.StaticRowBytes), Key);
    }

    public int GetRenderGroupCount()
    {
        if (Count == 0) return 0;
        int groups = 1;
        WorldStaticRenderKey prior = GetRecord(0).RenderKey;
        for (int index = 1; index < Count; index++)
        {
            WorldStaticRenderKey current = GetRecord(index).RenderKey;
            if (current != prior) groups++;
            prior = current;
        }
        return groups;
    }

    public bool TryCopyRenderGroups(Span<WorldStaticRenderGroup> destination, out int required)
    {
        required = GetRenderGroupCount();
        if (destination.Length < required) return false;
        if (Count == 0) return true;
        int group = 0;
        int start = 0;
        WorldStaticRenderKey key = GetRecord(0).RenderKey;
        for (int index = 1; index <= Count; index++)
        {
            WorldStaticRenderKey next = index == Count ? default : GetRecord(index).RenderKey;
            if (index != Count && next == key) continue;
            destination[group++] = new WorldStaticRenderGroup(key, start, index - start);
            start = index;
            key = next;
        }
        return true;
    }

}

/// <summary>Borrowed packed Multi fragments belonging to one canonical home chunk.</summary>
public readonly ref struct WorldMultiFragmentChunkView
{
    private readonly ReadOnlySpan<byte> _rows;
    private readonly ReadOnlySpan<WorldMultiFragmentRecord> _records;

    internal WorldMultiFragmentChunkView(SpatialChunkKey key, ReadOnlySpan<byte> rows)
    {
        Key = key;
        _rows = rows;
        _records = default;
    }

    internal WorldMultiFragmentChunkView(SpatialChunkKey key, ReadOnlySpan<WorldMultiFragmentRecord> records)
    {
        Key = key;
        _rows = default;
        _records = records;
    }

    public SpatialChunkKey Key { get; }
    public int Count => !_records.IsEmpty
        ? _records.Length
        : _rows.Length / FrozenWorldStructureFormat.MultiFragmentRowBytes;

    public WorldMultiFragmentRecord GetRecord(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count);
        return !_records.IsEmpty ? _records[index] : FrozenWorldStructureCodec.ReadMultiFragment(
            _rows.Slice(index * FrozenWorldStructureFormat.MultiFragmentRowBytes,
                FrozenWorldStructureFormat.MultiFragmentRowBytes), Key);
    }

    public int GetRenderGroupCount()
    {
        if (Count == 0) return 0;
        int groups = 1;
        WorldStaticRenderKey prior = GetRecord(0).RenderKey;
        for (int index = 1; index < Count; index++)
        {
            WorldStaticRenderKey current = GetRecord(index).RenderKey;
            if (current != prior) groups++;
            prior = current;
        }
        return groups;
    }

    public bool TryCopyRenderGroups(Span<WorldStaticRenderGroup> destination, out int required)
    {
        required = GetRenderGroupCount();
        if (destination.Length < required) return false;
        if (Count == 0) return true;
        int group = 0;
        int start = 0;
        WorldStaticRenderKey key = GetRecord(0).RenderKey;
        for (int index = 1; index <= Count; index++)
        {
            WorldStaticRenderKey next = index == Count ? default : GetRecord(index).RenderKey;
            if (index != Count && next == key) continue;
            destination[group++] = new WorldStaticRenderGroup(key, start, index - start);
            start = index;
            key = next;
        }
        return true;
    }
}

internal static class FrozenWorldStructureFormat
{
    internal const int RangeRowBytes = 8;
    internal const int StaticHeaderBytes = 32;
    internal const int StaticRowBytes = 56;
    internal const int MultiHeaderBytes = 40;
    internal const int MultiInstanceRowBytes = 40;
    internal const int MultiFragmentRowBytes = 56;
    internal const WorldStaticFlags KnownFlags =
        WorldStaticFlags.Renderable | WorldStaticFlags.Collidable | WorldStaticFlags.NoTopSupport;
}

public sealed partial class WorldPackBuilder
{
    private readonly List<WorldFrozenStaticSource> _frozenStatics = [];
    private readonly HashSet<WorldStaticId> _frozenStaticIds = [];
    private readonly List<SourceStaticMulti> _staticMultis = [];
    private readonly HashSet<MultiInstanceId> _staticMultiIds = [];
    private int _multiFragmentCount;

    public int FrozenStaticCount => _frozenStatics.Count;
    public int StaticMultiCount => _staticMultis.Count;
    public int MultiFragmentCount => _multiFragmentCount;

    public bool TryAddFrozenStatic(in WorldFrozenStaticSource source, out WorldPackBuildError error)
    {
        if (_frozenStatics.Count >= _limits.MaximumFrozenStatics)
        {
            error = WorldPackBuildError.CapacityExceeded;
            return false;
        }
        if (!source.Id.IsValid || !source.DefinitionId.IsValid ||
            !FrozenWorldStructureCodec.FlagsAreValid(source.Flags) ||
            !FrozenWorldStructureCodec.BoundsFitHomeHalo(source.Position, source.Bounds))
        {
            error = WorldPackBuildError.InvalidStaticPlacement;
            return false;
        }
        if (!_frozenStaticIds.Add(source.Id))
        {
            error = WorldPackBuildError.DuplicateStaticId;
            return false;
        }
        _frozenStatics.Add(source);
        error = WorldPackBuildError.None;
        return true;
    }

    public bool TryAddStaticMulti(
        MultiInstanceId id,
        in WorldPosition root,
        WorldYaw orientation,
        ReadOnlySpan<WorldMultiPieceSource> pieces,
        out WorldPackBuildError error)
    {
        if (!id.IsValid || pieces.IsEmpty)
        {
            error = WorldPackBuildError.InvalidMulti;
            return false;
        }
        if (_staticMultis.Count >= _limits.MaximumMultiInstances ||
            pieces.Length > _limits.MaximumMultiFragments - _multiFragmentCount)
        {
            error = WorldPackBuildError.CapacityExceeded;
            return false;
        }
        if (_staticMultiIds.Contains(id))
        {
            error = WorldPackBuildError.DuplicateMultiId;
            return false;
        }
        var pieceIds = new HashSet<MultiPieceId>();
        for (int index = 0; index < pieces.Length; index++)
        {
            ref readonly WorldMultiPieceSource piece = ref pieces[index];
            if (!piece.PieceId.IsValid || !piece.DefinitionId.IsValid ||
                !FrozenWorldStructureCodec.FlagsAreValid(piece.Flags) ||
                !FrozenWorldStructureCodec.BoundsFitHomeHalo(piece.Position, piece.Bounds) ||
                !pieceIds.Add(piece.PieceId))
            {
                error = WorldPackBuildError.InvalidMulti;
                return false;
            }
        }
        _staticMultiIds.Add(id);
        _staticMultis.Add(new SourceStaticMulti(id, root, orientation, pieces.ToArray()));
        _multiFragmentCount = checked(_multiFragmentCount + pieces.Length);
        error = WorldPackBuildError.None;
        return true;
    }

    internal sealed record SourceStaticMulti(
        MultiInstanceId Id,
        WorldPosition Root,
        WorldYaw Orientation,
        WorldMultiPieceSource[] Pieces);
}

internal static class FrozenWorldStructureCodec
{
    internal static bool FlagsAreValid(WorldStaticFlags flags) =>
        (flags & ~FrozenWorldStructureFormat.KnownFlags) == 0 &&
        ((flags & WorldStaticFlags.NoTopSupport) == 0 ||
         (flags & WorldStaticFlags.Collidable) != 0);

    internal static bool BoundsFitHomeHalo(in WorldPosition position, in WorldStaticBounds bounds)
    {
        if (!bounds.IsValid) return false;
        if (!TryChunk(bounds.MinimumXMillimeters, bounds.MinimumZMillimeters, out SpatialChunkCoord minimum) ||
            !TryChunk(bounds.MaximumXExclusiveMillimeters - 1,
                bounds.MaximumZExclusiveMillimeters - 1, out SpatialChunkCoord maximum))
            return false;
        if ((long)minimum.X < (long)position.Chunk.X - 1 ||
            (long)minimum.Z < (long)position.Chunk.Z - 1 ||
            (long)maximum.X > (long)position.Chunk.X + 1 ||
            (long)maximum.Z > (long)position.Chunk.Z + 1)
            return false;
        long originX = checked((long)position.Chunk.X * GameWorldSpatialProfile.WorldChunkEdgeUnits);
        long originZ = checked((long)position.Chunk.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits);
        return FitsInt(bounds.MinimumXMillimeters - originX) &&
            FitsInt(bounds.MaximumXExclusiveMillimeters - originX) &&
            FitsInt(bounds.MinimumYMillimeters - position.YMillimeters) &&
            FitsInt(bounds.MaximumYExclusiveMillimeters - position.YMillimeters) &&
            FitsInt(bounds.MinimumZMillimeters - originZ) &&
            FitsInt(bounds.MaximumZExclusiveMillimeters - originZ);
    }

    internal static bool TryMeasure(
        int chunkCount,
        int staticCount,
        int multiInstanceCount,
        int multiFragmentCount,
        out int staticBytes,
        out int multiBytes)
    {
        try
        {
            staticBytes = staticCount == 0 ? 0 : checked(
                FrozenWorldStructureFormat.StaticHeaderBytes +
                chunkCount * FrozenWorldStructureFormat.RangeRowBytes +
                staticCount * FrozenWorldStructureFormat.StaticRowBytes);
            multiBytes = multiInstanceCount == 0 ? 0 : checked(
                FrozenWorldStructureFormat.MultiHeaderBytes +
                chunkCount * FrozenWorldStructureFormat.RangeRowBytes +
                multiInstanceCount * FrozenWorldStructureFormat.MultiInstanceRowBytes +
                multiFragmentCount * FrozenWorldStructureFormat.MultiFragmentRowBytes);
            return true;
        }
        catch (OverflowException)
        {
            staticBytes = 0;
            multiBytes = 0;
            return false;
        }
    }

    internal static bool TryCompile(
        ReadOnlySpan<SpatialChunkKey> chunks,
        IReadOnlyList<WorldFrozenStaticSource> statics,
        IReadOnlyList<WorldPackBuilder.SourceStaticMulti> multis,
        WorldPackLimits limits,
        out byte[]? staticPayload,
        out byte[]? multiPayload,
        out WorldPackBuildError error)
    {
        staticPayload = null;
        multiPayload = null;
        var chunkIndices = new Dictionary<SpatialChunkKey, int>(chunks.Length);
        for (int index = 0; index < chunks.Length; index++) chunkIndices.Add(chunks[index], index);
        var staticRows = new StaticSourceRow[statics.Count];
        for (int index = 0; index < statics.Count; index++)
        {
            WorldFrozenStaticSource source = statics[index];
            if (!chunkIndices.TryGetValue(source.Position.ToChunkKey(chunks[0].Space), out int chunkIndex))
            {
                error = WorldPackBuildError.MissingChunk;
                return false;
            }
            staticRows[index] = new StaticSourceRow(chunkIndex, source);
        }
        Array.Sort(staticRows, StaticSourceRowComparer.Instance);
        if (staticRows.Length != 0)
            staticPayload = WriteStaticPayload(chunks.Length, staticRows);

        var instances = new WorldPackBuilder.SourceStaticMulti[multis.Count];
        for (int index = 0; index < instances.Length; index++) instances[index] = multis[index];
        Array.Sort(instances, static (left, right) => left.Id.CompareTo(right.Id));
        int fragmentCount = 0;
        for (int index = 0; index < instances.Length; index++)
            fragmentCount = checked(fragmentCount + instances[index].Pieces.Length);
        var fragments = new MultiSourceRow[fragmentCount];
        int fragment = 0;
        foreach (WorldPackBuilder.SourceStaticMulti instance in instances)
        {
            if (!chunkIndices.ContainsKey(instance.Root.ToChunkKey(chunks[0].Space)))
            {
                error = WorldPackBuildError.MissingChunk;
                return false;
            }
            foreach (WorldMultiPieceSource piece in instance.Pieces)
            {
                if (!chunkIndices.TryGetValue(piece.Position.ToChunkKey(chunks[0].Space), out int chunkIndex))
                {
                    error = WorldPackBuildError.MissingChunk;
                    return false;
                }
                fragments[fragment++] = new MultiSourceRow(chunkIndex, instance.Id, piece);
            }
        }
        Array.Sort(fragments, MultiSourceRowComparer.Instance);
        if (instances.Length != 0)
            multiPayload = WriteMultiPayload(chunks.Length, instances, fragments);
        error = WorldPackBuildError.None;
        return true;
    }

    internal static bool TryValidateStaticSection(
        ReadOnlySpan<byte> pack,
        WorldPackMetadata metadata,
        WorldPackSectionMetadata section,
        WorldPackLimits limits)
    {
        if (section.Count > (ulong)limits.MaximumFrozenStatics ||
            section.Length < FrozenWorldStructureFormat.StaticHeaderBytes)
            return false;
        ReadOnlySpan<byte> payload = pack.Slice(section.Offset, section.Length);
        int chunkCount = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int rowCount = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        int rangeStride = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);
        int rowStride = BinaryPrimitives.ReadInt32LittleEndian(payload[12..]);
        int rangesOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[16..]);
        int rowsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[20..]);
        if (chunkCount != metadata.ChunkCount || rowCount < 0 || section.Count != (ulong)rowCount ||
            rangeStride != FrozenWorldStructureFormat.RangeRowBytes ||
            rowStride != FrozenWorldStructureFormat.StaticRowBytes ||
            rangesOffset != FrozenWorldStructureFormat.StaticHeaderBytes ||
            BinaryPrimitives.ReadUInt64LittleEndian(payload[24..]) != 0)
            return false;
        int expectedRowsOffset;
        int expectedLength;
        try
        {
            expectedRowsOffset = checked(rangesOffset + chunkCount * rangeStride);
            expectedLength = checked(expectedRowsOffset + rowCount * rowStride);
        }
        catch (OverflowException)
        {
            return false;
        }
        if (rowsOffset != expectedRowsOffset || expectedLength != payload.Length) return false;

        var identities = new HashSet<ulong>(rowCount);
        int expectedStart = 0;
        for (int chunk = 0; chunk < chunkCount; chunk++)
        {
            ReadOnlySpan<byte> range = payload.Slice(rangesOffset + chunk * rangeStride, rangeStride);
            int start = BinaryPrimitives.ReadInt32LittleEndian(range);
            int count = BinaryPrimitives.ReadInt32LittleEndian(range[4..]);
            if (start != expectedStart || count < 0 || start > rowCount - count) return false;
            SpatialChunkKey key = WorldPackCodec.ReadChunkKey(pack, metadata.ChunkDirectoryOffset, chunk);
            WorldStaticRenderKey priorRender = default;
            WorldStaticId priorId = default;
            for (int local = 0; local < count; local++)
            {
                ReadOnlySpan<byte> row = payload.Slice(rowsOffset + (start + local) * rowStride, rowStride);
                ulong rawId = BinaryPrimitives.ReadUInt64LittleEndian(row);
                uint rawDefinition = BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);
                ushort localX = BinaryPrimitives.ReadUInt16LittleEndian(row[16..]);
                ushort localZ = BinaryPrimitives.ReadUInt16LittleEndian(row[18..]);
                WorldStaticFlags flags = (WorldStaticFlags)BinaryPrimitives.ReadUInt16LittleEndian(row[24..]);
                if (rawId == 0 || rawDefinition == 0 || localX >= GameWorldSpatialProfile.WorldChunkEdgeUnits ||
                    localZ >= GameWorldSpatialProfile.WorldChunkEdgeUnits || !FlagsAreValid(flags) ||
                    BinaryPrimitives.ReadUInt16LittleEndian(row[26..]) != 0 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(row[52..]) != 0 || !identities.Add(rawId))
                    return false;
                WorldFrozenStaticRecord record;
                try
                {
                    record = ReadStaticRecord(row, key);
                }
                catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
                {
                    return false;
                }
                if (!BoundsFitHomeHalo(record.Position, record.Bounds)) return false;
                if (local != 0)
                {
                    int order = priorRender.CompareTo(record.RenderKey);
                    if (order > 0 || order == 0 && priorId.CompareTo(record.Id) >= 0) return false;
                }
                priorRender = record.RenderKey;
                priorId = record.Id;
            }
            expectedStart = checked(start + count);
        }
        return expectedStart == rowCount;
    }

    internal static bool TryValidateMultiSection(
        ReadOnlySpan<byte> pack,
        WorldPackMetadata metadata,
        WorldPackSectionMetadata section,
        WorldPackLimits limits)
    {
        if (section.Count > (ulong)limits.MaximumMultiFragments ||
            section.Length < FrozenWorldStructureFormat.MultiHeaderBytes)
            return false;
        ReadOnlySpan<byte> payload = pack.Slice(section.Offset, section.Length);
        int chunkCount = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int instanceCount = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        int fragmentCount = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);
        int rangeStride = BinaryPrimitives.ReadInt32LittleEndian(payload[12..]);
        int instanceStride = BinaryPrimitives.ReadInt32LittleEndian(payload[16..]);
        int fragmentStride = BinaryPrimitives.ReadInt32LittleEndian(payload[20..]);
        int rangesOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[24..]);
        int instancesOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[28..]);
        int fragmentsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[32..]);
        if (chunkCount != metadata.ChunkCount || instanceCount <= 0 ||
            instanceCount > limits.MaximumMultiInstances || fragmentCount <= 0 ||
            fragmentCount > limits.MaximumMultiFragments || section.Count != (ulong)fragmentCount ||
            rangeStride != FrozenWorldStructureFormat.RangeRowBytes ||
            instanceStride != FrozenWorldStructureFormat.MultiInstanceRowBytes ||
            fragmentStride != FrozenWorldStructureFormat.MultiFragmentRowBytes ||
            rangesOffset != FrozenWorldStructureFormat.MultiHeaderBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload[36..]) != 0)
            return false;
        int expectedInstancesOffset;
        int expectedFragmentsOffset;
        int expectedLength;
        try
        {
            expectedInstancesOffset = checked(rangesOffset + chunkCount * rangeStride);
            expectedFragmentsOffset = checked(expectedInstancesOffset + instanceCount * instanceStride);
            expectedLength = checked(expectedFragmentsOffset + fragmentCount * fragmentStride);
        }
        catch (OverflowException)
        {
            return false;
        }
        if (instancesOffset != expectedInstancesOffset || fragmentsOffset != expectedFragmentsOffset ||
            expectedLength != payload.Length)
            return false;

        var remaining = new Dictionary<ulong, int>(instanceCount);
        ulong priorInstance = 0;
        long expectedPieces = 0;
        for (int index = 0; index < instanceCount; index++)
        {
            ReadOnlySpan<byte> row = payload.Slice(instancesOffset + index * instanceStride, instanceStride);
            ulong id = BinaryPrimitives.ReadUInt64LittleEndian(row);
            int rootX = BinaryPrimitives.ReadInt32LittleEndian(row[8..]);
            int rootZ = BinaryPrimitives.ReadInt32LittleEndian(row[12..]);
            ushort localX = BinaryPrimitives.ReadUInt16LittleEndian(row[16..]);
            ushort localZ = BinaryPrimitives.ReadUInt16LittleEndian(row[18..]);
            int pieceCount = BinaryPrimitives.ReadInt32LittleEndian(row[28..]);
            if (id == 0 || id <= priorInstance || localX >= GameWorldSpatialProfile.WorldChunkEdgeUnits ||
                localZ >= GameWorldSpatialProfile.WorldChunkEdgeUnits || pieceCount <= 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(row[26..]) != 0 ||
                BinaryPrimitives.ReadUInt64LittleEndian(row[32..]) != 0 ||
                WorldPackCodec.FindChunkIndex(pack, metadata,
                    new SpatialChunkKey(new SpatialSpaceId(metadata.SpaceValue), rootX, rootZ)) < 0)
                return false;
            remaining.Add(id, pieceCount);
            expectedPieces += pieceCount;
            priorInstance = id;
        }
        if (expectedPieces != fragmentCount) return false;

        var pieces = new HashSet<(ulong Multi, uint Piece)>(fragmentCount);
        int expectedStart = 0;
        for (int chunk = 0; chunk < chunkCount; chunk++)
        {
            ReadOnlySpan<byte> range = payload.Slice(rangesOffset + chunk * rangeStride, rangeStride);
            int start = BinaryPrimitives.ReadInt32LittleEndian(range);
            int count = BinaryPrimitives.ReadInt32LittleEndian(range[4..]);
            if (start != expectedStart || count < 0 || start > fragmentCount - count) return false;
            SpatialChunkKey key = WorldPackCodec.ReadChunkKey(pack, metadata.ChunkDirectoryOffset, chunk);
            WorldStaticRenderKey priorRender = default;
            MultiInstanceId priorMulti = default;
            MultiPieceId priorPiece = default;
            for (int local = 0; local < count; local++)
            {
                ReadOnlySpan<byte> row = payload.Slice(fragmentsOffset + (start + local) * fragmentStride,
                    fragmentStride);
                ulong multi = BinaryPrimitives.ReadUInt64LittleEndian(row);
                uint piece = BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);
                uint definition = BinaryPrimitives.ReadUInt32LittleEndian(row[12..]);
                ushort localX = BinaryPrimitives.ReadUInt16LittleEndian(row[20..]);
                ushort localZ = BinaryPrimitives.ReadUInt16LittleEndian(row[22..]);
                WorldStaticFlags flags = (WorldStaticFlags)BinaryPrimitives.ReadUInt16LittleEndian(row[28..]);
                if (multi == 0 || piece == 0 || definition == 0 ||
                    !remaining.TryGetValue(multi, out int left) || left <= 0 ||
                    localX >= GameWorldSpatialProfile.WorldChunkEdgeUnits ||
                    localZ >= GameWorldSpatialProfile.WorldChunkEdgeUnits || !FlagsAreValid(flags) ||
                    BinaryPrimitives.ReadUInt16LittleEndian(row[30..]) != 0 ||
                    !pieces.Add((multi, piece)))
                    return false;
                WorldMultiFragmentRecord record;
                try
                {
                    record = ReadMultiFragment(row, key);
                }
                catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
                {
                    return false;
                }
                if (!BoundsFitHomeHalo(record.Position, record.Bounds)) return false;
                if (local != 0)
                {
                    int renderOrder = priorRender.CompareTo(record.RenderKey);
                    int multiOrder = priorMulti.CompareTo(record.MultiId);
                    if (renderOrder > 0 || renderOrder == 0 &&
                        (multiOrder > 0 || multiOrder == 0 && priorPiece.CompareTo(record.PieceId) >= 0))
                        return false;
                }
                priorRender = record.RenderKey;
                priorMulti = record.MultiId;
                priorPiece = record.PieceId;
                remaining[multi] = left - 1;
            }
            expectedStart = checked(start + count);
        }
        if (expectedStart != fragmentCount) return false;
        foreach (int left in remaining.Values) if (left != 0) return false;
        return true;
    }

    internal static WorldFrozenStaticRecord ReadStaticRecord(ReadOnlySpan<byte> row, SpatialChunkKey key)
    {
        var position = new WorldPosition(key.Coord,
            new WorldLocalPosition(
                BinaryPrimitives.ReadUInt16LittleEndian(row[16..]),
                BinaryPrimitives.ReadUInt16LittleEndian(row[18..]),
                BinaryPrimitives.ReadInt32LittleEndian(row[12..])));
        return new WorldFrozenStaticRecord(
            new WorldStaticId(BinaryPrimitives.ReadUInt64LittleEndian(row)),
            new WorldStaticDefinitionId(BinaryPrimitives.ReadUInt32LittleEndian(row[8..])),
            position,
            new WorldYaw(BinaryPrimitives.ReadUInt16LittleEndian(row[20..])),
            BinaryPrimitives.ReadUInt16LittleEndian(row[22..]),
            (WorldStaticFlags)BinaryPrimitives.ReadUInt16LittleEndian(row[24..]),
            ReadBounds(row[28..], position));
    }

    internal static WorldMultiFragmentRecord ReadMultiFragment(ReadOnlySpan<byte> row, SpatialChunkKey key)
    {
        var position = new WorldPosition(key.Coord,
            new WorldLocalPosition(
                BinaryPrimitives.ReadUInt16LittleEndian(row[20..]),
                BinaryPrimitives.ReadUInt16LittleEndian(row[22..]),
                BinaryPrimitives.ReadInt32LittleEndian(row[16..])));
        return new WorldMultiFragmentRecord(
            new MultiInstanceId(BinaryPrimitives.ReadUInt64LittleEndian(row)),
            new MultiPieceId(BinaryPrimitives.ReadUInt32LittleEndian(row[8..])),
            new WorldStaticDefinitionId(BinaryPrimitives.ReadUInt32LittleEndian(row[12..])),
            position,
            new WorldYaw(BinaryPrimitives.ReadUInt16LittleEndian(row[24..])),
            BinaryPrimitives.ReadUInt16LittleEndian(row[26..]),
            (WorldStaticFlags)BinaryPrimitives.ReadUInt16LittleEndian(row[28..]),
            ReadBounds(row[32..], position));
    }

    internal static WorldMultiInstanceRecord ReadMultiInstance(ReadOnlySpan<byte> row)
    {
        var root = new WorldPosition(
            new SpatialChunkCoord(
                BinaryPrimitives.ReadInt32LittleEndian(row[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(row[12..])),
            new WorldLocalPosition(
                BinaryPrimitives.ReadUInt16LittleEndian(row[16..]),
                BinaryPrimitives.ReadUInt16LittleEndian(row[18..]),
                BinaryPrimitives.ReadInt32LittleEndian(row[20..])));
        return new WorldMultiInstanceRecord(
            new MultiInstanceId(BinaryPrimitives.ReadUInt64LittleEndian(row)),
            root,
            new WorldYaw(BinaryPrimitives.ReadUInt16LittleEndian(row[24..])),
            BinaryPrimitives.ReadInt32LittleEndian(row[28..]));
    }

    private static byte[] WriteStaticPayload(int chunkCount, ReadOnlySpan<StaticSourceRow> rows)
    {
        int rangesOffset = FrozenWorldStructureFormat.StaticHeaderBytes;
        int rowsOffset = checked(rangesOffset + chunkCount * FrozenWorldStructureFormat.RangeRowBytes);
        var payload = new byte[checked(rowsOffset + rows.Length * FrozenWorldStructureFormat.StaticRowBytes)];
        WriteStaticHeader(payload, chunkCount, rows.Length, rangesOffset, rowsOffset);
        WriteRanges(payload.AsSpan(rangesOffset), chunkCount, rows, static row => row.ChunkIndex);
        for (int index = 0; index < rows.Length; index++)
            WriteStaticRow(payload.AsSpan(rowsOffset + index * FrozenWorldStructureFormat.StaticRowBytes,
                FrozenWorldStructureFormat.StaticRowBytes), rows[index].Source);
        return payload;
    }

    private static byte[] WriteMultiPayload(
        int chunkCount,
        ReadOnlySpan<WorldPackBuilder.SourceStaticMulti> instances,
        ReadOnlySpan<MultiSourceRow> fragments)
    {
        int rangesOffset = FrozenWorldStructureFormat.MultiHeaderBytes;
        int instancesOffset = checked(rangesOffset + chunkCount * FrozenWorldStructureFormat.RangeRowBytes);
        int fragmentsOffset = checked(instancesOffset +
            instances.Length * FrozenWorldStructureFormat.MultiInstanceRowBytes);
        var payload = new byte[checked(fragmentsOffset +
            fragments.Length * FrozenWorldStructureFormat.MultiFragmentRowBytes)];
        WriteMultiHeader(payload, chunkCount, instances.Length, fragments.Length,
            rangesOffset, instancesOffset, fragmentsOffset);
        WriteRanges(payload.AsSpan(rangesOffset), chunkCount, fragments, static row => row.ChunkIndex);
        for (int index = 0; index < instances.Length; index++)
            WriteMultiInstance(payload.AsSpan(instancesOffset +
                index * FrozenWorldStructureFormat.MultiInstanceRowBytes,
                FrozenWorldStructureFormat.MultiInstanceRowBytes), instances[index]);
        for (int index = 0; index < fragments.Length; index++)
            WriteMultiFragment(payload.AsSpan(fragmentsOffset +
                index * FrozenWorldStructureFormat.MultiFragmentRowBytes,
                FrozenWorldStructureFormat.MultiFragmentRowBytes), fragments[index]);
        return payload;
    }

    private static void WriteStaticHeader(
        Span<byte> payload, int chunks, int rows, int rangesOffset, int rowsOffset)
    {
        BinaryPrimitives.WriteInt32LittleEndian(payload, chunks);
        BinaryPrimitives.WriteInt32LittleEndian(payload[4..], rows);
        BinaryPrimitives.WriteInt32LittleEndian(payload[8..], FrozenWorldStructureFormat.RangeRowBytes);
        BinaryPrimitives.WriteInt32LittleEndian(payload[12..], FrozenWorldStructureFormat.StaticRowBytes);
        BinaryPrimitives.WriteInt32LittleEndian(payload[16..], rangesOffset);
        BinaryPrimitives.WriteInt32LittleEndian(payload[20..], rowsOffset);
    }

    private static void WriteMultiHeader(
        Span<byte> payload,
        int chunks,
        int instances,
        int fragments,
        int rangesOffset,
        int instancesOffset,
        int fragmentsOffset)
    {
        BinaryPrimitives.WriteInt32LittleEndian(payload, chunks);
        BinaryPrimitives.WriteInt32LittleEndian(payload[4..], instances);
        BinaryPrimitives.WriteInt32LittleEndian(payload[8..], fragments);
        BinaryPrimitives.WriteInt32LittleEndian(payload[12..], FrozenWorldStructureFormat.RangeRowBytes);
        BinaryPrimitives.WriteInt32LittleEndian(payload[16..], FrozenWorldStructureFormat.MultiInstanceRowBytes);
        BinaryPrimitives.WriteInt32LittleEndian(payload[20..], FrozenWorldStructureFormat.MultiFragmentRowBytes);
        BinaryPrimitives.WriteInt32LittleEndian(payload[24..], rangesOffset);
        BinaryPrimitives.WriteInt32LittleEndian(payload[28..], instancesOffset);
        BinaryPrimitives.WriteInt32LittleEndian(payload[32..], fragmentsOffset);
    }

    private static void WriteRanges<TRow>(
        Span<byte> destination,
        int chunkCount,
        ReadOnlySpan<TRow> rows,
        Func<TRow, int> getChunk)
    {
        int cursor = 0;
        for (int chunk = 0; chunk < chunkCount; chunk++)
        {
            int start = cursor;
            while (cursor < rows.Length && getChunk(rows[cursor]) == chunk) cursor++;
            Span<byte> range = destination.Slice(chunk * FrozenWorldStructureFormat.RangeRowBytes,
                FrozenWorldStructureFormat.RangeRowBytes);
            BinaryPrimitives.WriteInt32LittleEndian(range, start);
            BinaryPrimitives.WriteInt32LittleEndian(range[4..], cursor - start);
        }
    }

    private static void WriteStaticRow(Span<byte> row, in WorldFrozenStaticSource source)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(row, source.Id.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(row[8..], source.DefinitionId.Value);
        BinaryPrimitives.WriteInt32LittleEndian(row[12..], source.Position.YMillimeters);
        BinaryPrimitives.WriteUInt16LittleEndian(row[16..], source.Position.Local.XMillimeters);
        BinaryPrimitives.WriteUInt16LittleEndian(row[18..], source.Position.Local.ZMillimeters);
        BinaryPrimitives.WriteUInt16LittleEndian(row[20..], source.Orientation.Units);
        BinaryPrimitives.WriteUInt16LittleEndian(row[22..], source.Variant);
        BinaryPrimitives.WriteUInt16LittleEndian(row[24..], (ushort)source.Flags);
        WriteBounds(row[28..], source.Position, source.Bounds);
    }

    private static void WriteMultiInstance(Span<byte> row, WorldPackBuilder.SourceStaticMulti source)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(row, source.Id.Value);
        BinaryPrimitives.WriteInt32LittleEndian(row[8..], source.Root.Chunk.X);
        BinaryPrimitives.WriteInt32LittleEndian(row[12..], source.Root.Chunk.Z);
        BinaryPrimitives.WriteUInt16LittleEndian(row[16..], source.Root.Local.XMillimeters);
        BinaryPrimitives.WriteUInt16LittleEndian(row[18..], source.Root.Local.ZMillimeters);
        BinaryPrimitives.WriteInt32LittleEndian(row[20..], source.Root.YMillimeters);
        BinaryPrimitives.WriteUInt16LittleEndian(row[24..], source.Orientation.Units);
        BinaryPrimitives.WriteInt32LittleEndian(row[28..], source.Pieces.Length);
    }

    private static void WriteMultiFragment(Span<byte> row, in MultiSourceRow source)
    {
        WorldMultiPieceSource piece = source.Piece;
        BinaryPrimitives.WriteUInt64LittleEndian(row, source.MultiId.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(row[8..], piece.PieceId.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(row[12..], piece.DefinitionId.Value);
        BinaryPrimitives.WriteInt32LittleEndian(row[16..], piece.Position.YMillimeters);
        BinaryPrimitives.WriteUInt16LittleEndian(row[20..], piece.Position.Local.XMillimeters);
        BinaryPrimitives.WriteUInt16LittleEndian(row[22..], piece.Position.Local.ZMillimeters);
        BinaryPrimitives.WriteUInt16LittleEndian(row[24..], piece.Orientation.Units);
        BinaryPrimitives.WriteUInt16LittleEndian(row[26..], piece.Variant);
        BinaryPrimitives.WriteUInt16LittleEndian(row[28..], (ushort)piece.Flags);
        WriteBounds(row[32..], piece.Position, piece.Bounds);
    }

    private static void WriteBounds(Span<byte> row, in WorldPosition position, in WorldStaticBounds bounds)
    {
        long originX = checked((long)position.Chunk.X * GameWorldSpatialProfile.WorldChunkEdgeUnits);
        long originZ = checked((long)position.Chunk.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits);
        BinaryPrimitives.WriteInt32LittleEndian(row, checked((int)(bounds.MinimumXMillimeters - originX)));
        BinaryPrimitives.WriteInt32LittleEndian(row[4..], checked((int)(bounds.MinimumYMillimeters - position.YMillimeters)));
        BinaryPrimitives.WriteInt32LittleEndian(row[8..], checked((int)(bounds.MinimumZMillimeters - originZ)));
        BinaryPrimitives.WriteInt32LittleEndian(row[12..], checked((int)(bounds.MaximumXExclusiveMillimeters - originX)));
        BinaryPrimitives.WriteInt32LittleEndian(row[16..], checked((int)(bounds.MaximumYExclusiveMillimeters - position.YMillimeters)));
        BinaryPrimitives.WriteInt32LittleEndian(row[20..], checked((int)(bounds.MaximumZExclusiveMillimeters - originZ)));
    }

    private static WorldStaticBounds ReadBounds(ReadOnlySpan<byte> row, in WorldPosition position)
    {
        long originX = checked((long)position.Chunk.X * GameWorldSpatialProfile.WorldChunkEdgeUnits);
        long originZ = checked((long)position.Chunk.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits);
        return new WorldStaticBounds(
            checked(originX + BinaryPrimitives.ReadInt32LittleEndian(row)),
            checked((long)position.YMillimeters + BinaryPrimitives.ReadInt32LittleEndian(row[4..])),
            checked(originZ + BinaryPrimitives.ReadInt32LittleEndian(row[8..])),
            checked(originX + BinaryPrimitives.ReadInt32LittleEndian(row[12..])),
            checked((long)position.YMillimeters + BinaryPrimitives.ReadInt32LittleEndian(row[16..])),
            checked(originZ + BinaryPrimitives.ReadInt32LittleEndian(row[20..])));
    }

    private static bool TryChunk(long x, long z, out SpatialChunkCoord chunk) =>
        GameWorldSpatialProfile.ChunkGeometry.TryNormalize(x, z, out chunk, out _, out _);

    private static bool FitsInt(long value) => value is >= int.MinValue and <= int.MaxValue;

    private readonly record struct StaticSourceRow(int ChunkIndex, WorldFrozenStaticSource Source);
    private readonly record struct MultiSourceRow(
        int ChunkIndex,
        MultiInstanceId MultiId,
        WorldMultiPieceSource Piece);

    private sealed class StaticSourceRowComparer : IComparer<StaticSourceRow>
    {
        internal static StaticSourceRowComparer Instance { get; } = new();
        public int Compare(StaticSourceRow left, StaticSourceRow right)
        {
            int chunk = left.ChunkIndex.CompareTo(right.ChunkIndex);
            if (chunk != 0) return chunk;
            int render = new WorldStaticRenderKey(left.Source.DefinitionId, left.Source.Variant, left.Source.Flags)
                .CompareTo(new WorldStaticRenderKey(right.Source.DefinitionId, right.Source.Variant, right.Source.Flags));
            return render != 0 ? render : left.Source.Id.CompareTo(right.Source.Id);
        }
    }

    private sealed class MultiSourceRowComparer : IComparer<MultiSourceRow>
    {
        internal static MultiSourceRowComparer Instance { get; } = new();
        public int Compare(MultiSourceRow left, MultiSourceRow right)
        {
            int chunk = left.ChunkIndex.CompareTo(right.ChunkIndex);
            if (chunk != 0) return chunk;
            int render = new WorldStaticRenderKey(left.Piece.DefinitionId, left.Piece.Variant, left.Piece.Flags)
                .CompareTo(new WorldStaticRenderKey(right.Piece.DefinitionId, right.Piece.Variant, right.Piece.Flags));
            if (render != 0) return render;
            int multi = left.MultiId.CompareTo(right.MultiId);
            return multi != 0 ? multi : left.Piece.PieceId.CompareTo(right.Piece.PieceId);
        }
    }
}
