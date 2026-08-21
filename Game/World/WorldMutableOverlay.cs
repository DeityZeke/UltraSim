using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>Monotonic identity of one complete durable mutable-world overlay generation.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly record struct WorldOverlayRevision(ulong Value) : IComparable<WorldOverlayRevision>
{
    public bool IsValid => Value != 0;
    public int CompareTo(WorldOverlayRevision other) => Value.CompareTo(other.Value);
}

/// <summary>Monotonic content revision of one changed backing chunk.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly record struct WorldChunkContentRevision(ulong Value) : IComparable<WorldChunkContentRevision>
{
    public bool IsValid => Value != 0;
    public int CompareTo(WorldChunkContentRevision other) => Value.CompareTo(other.Value);
}

/// <summary>SHA-256 identity of one complete effective changed-chunk replacement.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public readonly struct WorldChunkContentHash : IEquatable<WorldChunkContentHash>
{
    public WorldChunkContentHash(ulong part0, ulong part1, ulong part2, ulong part3)
    {
        Part0 = part0; Part1 = part1; Part2 = part2; Part3 = part3;
    }

    public ulong Part0 { get; }
    public ulong Part1 { get; }
    public ulong Part2 { get; }
    public ulong Part3 { get; }
    public bool IsValid => Part0 != 0 || Part1 != 0 || Part2 != 0 || Part3 != 0;
    public bool Equals(WorldChunkContentHash other) =>
        Part0 == other.Part0 && Part1 == other.Part1 && Part2 == other.Part2 && Part3 == other.Part3;
    public override bool Equals(object? obj) => obj is WorldChunkContentHash other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Part0, Part1, Part2, Part3);
    public override string ToString() => $"{Part3:X16}{Part2:X16}{Part1:X16}{Part0:X16}";
    public static bool operator ==(WorldChunkContentHash left, WorldChunkContentHash right) => left.Equals(right);
    public static bool operator !=(WorldChunkContentHash left, WorldChunkContentHash right) => !left.Equals(right);

    internal static WorldChunkContentHash Read(ReadOnlySpan<byte> source) => new(
        BinaryPrimitives.ReadUInt64LittleEndian(source),
        BinaryPrimitives.ReadUInt64LittleEndian(source[8..]),
        BinaryPrimitives.ReadUInt64LittleEndian(source[16..]),
        BinaryPrimitives.ReadUInt64LittleEndian(source[24..]));

    internal void Write(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, Part0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], Part1);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], Part2);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], Part3);
    }
}

/// <summary>Cold bounds for one prepared durable overlay generation.</summary>
public readonly record struct WorldMutableOverlayLimits(
    int MaximumChangedChunks,
    int MaximumStaticsPerChunk,
    int MaximumMultiFragmentsPerChunk,
    int MaximumMultiInstances,
    long MaximumRetainedBytes)
{
    public static WorldMutableOverlayLimits Default => new(4_096, 1_024, 2_048, 262_144, 256L * 1024 * 1024);

    internal void Validate()
    {
        if (MaximumChangedChunks <= 0 || MaximumStaticsPerChunk < 0 ||
            MaximumMultiFragmentsPerChunk < 0 || MaximumMultiInstances < 0 ||
            MaximumRetainedBytes <= 0 || MaximumRetainedBytes > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(MaximumChangedChunks));
    }
}

[Flags]
internal enum WorldChunkReplacementKinds : byte
{
    None = 0,
    Terrain = 1 << 0,
    FrozenStatics = 1 << 1,
    MultiFragments = 1 << 2,
}

/// <summary>Bounded immutable durable replacements layered over one exact immutable WorldPack.</summary>
public sealed class WorldMutableOverlay
{
    private readonly ChunkReplacement[] _chunks;
    private readonly WorldMultiInstanceRecord[]? _multiInstances;

    internal WorldMutableOverlay(
        WorldPackFingerprint baseFingerprint,
        SpatialSpaceId space,
        WorldOverlayRevision revision,
        WorldMutableOverlayLimits limits,
        ChunkReplacement[] chunks,
        WorldMultiInstanceRecord[]? multiInstances)
    {
        BaseWorldFingerprint = baseFingerprint;
        Space = space;
        Revision = revision;
        Limits = limits;
        _chunks = chunks;
        _multiInstances = multiInstances;
    }

    public WorldPackFingerprint BaseWorldFingerprint { get; }
    public SpatialSpaceId Space { get; }
    public WorldOverlayRevision Revision { get; }
    public WorldMutableOverlayLimits Limits { get; }
    public int ChangedChunkCount => _chunks.Length;
    public bool ReplacesMultiInstances => _multiInstances is not null;
    public int ReplacementMultiInstanceCount => _multiInstances?.Length ?? 0;
    public long ReservedBytes => CalculateReservedBytes();

    public static WorldMutableOverlay Empty(
        BoundWorldPack baseWorld,
        WorldMutableOverlayLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(baseWorld);
        WorldMutableOverlayLimits selected = limits ?? WorldMutableOverlayLimits.Default;
        selected.Validate();
        return new WorldMutableOverlay(baseWorld.Fingerprint, baseWorld.Space, default,
            selected, [], null);
    }

    public bool TryGetChunkRevision(
        SpatialChunkKey key,
        out WorldChunkContentRevision revision,
        out WorldChunkContentHash hash)
    {
        int index = FindChunk(key);
        if (index < 0)
        {
            revision = default;
            hash = default;
            return false;
        }
        revision = _chunks[index].Revision;
        hash = _chunks[index].Hash;
        return true;
    }

    internal bool TryGetTerrain(SpatialChunkKey key, out WorldTerrainChunkView view)
    {
        int index = FindChunk(key);
        if (index < 0 || _chunks[index].TerrainMaterials is null)
        {
            view = default;
            return false;
        }
        ChunkReplacement chunk = _chunks[index];
        view = new WorldTerrainChunkView(key, chunk.TerrainMaterials, chunk.TerrainHeights!);
        return true;
    }

    internal bool TryGetFrozenStatics(SpatialChunkKey key, out WorldFrozenStaticChunkView view)
    {
        int index = FindChunk(key);
        if (index < 0 || _chunks[index].FrozenStatics is null)
        {
            view = default;
            return false;
        }
        view = new WorldFrozenStaticChunkView(key, _chunks[index].FrozenStatics);
        return true;
    }

    internal bool TryGetMultiFragments(SpatialChunkKey key, out WorldMultiFragmentChunkView view)
    {
        int index = FindChunk(key);
        if (index < 0 || _chunks[index].MultiFragments is null)
        {
            view = default;
            return false;
        }
        view = new WorldMultiFragmentChunkView(key, _chunks[index].MultiFragments);
        return true;
    }

    internal bool TryGetMultiInstance(MultiInstanceId id, out WorldMultiInstanceRecord instance)
    {
        if (_multiInstances is null || !id.IsValid)
        {
            instance = default;
            return false;
        }
        int low = 0;
        int high = _multiInstances.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = _multiInstances[middle].Id.CompareTo(id);
            if (order == 0)
            {
                instance = _multiInstances[middle];
                return true;
            }
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        instance = default;
        return false;
    }

    internal ReadOnlySpan<ChunkReplacement> Chunks => _chunks;
    internal ReadOnlySpan<WorldMultiInstanceRecord> MultiInstances => _multiInstances;

    private int FindChunk(SpatialChunkKey key)
    {
        int low = 0;
        int high = _chunks.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = _chunks[middle].Key.CompareTo(key);
            if (order == 0) return middle;
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    private long CalculateReservedBytes()
    {
        long bytes = (long)_chunks.Length * 80L +
            (long)(_multiInstances?.Length ?? 0) * Marshal.SizeOf<WorldMultiInstanceRecord>();
        foreach (ChunkReplacement chunk in _chunks)
        {
            bytes = checked(bytes + (chunk.TerrainMaterials?.Length ?? 0) +
                (chunk.TerrainHeights?.Length ?? 0) +
                (long)(chunk.FrozenStatics?.Length ?? 0) * Marshal.SizeOf<WorldFrozenStaticRecord>() +
                (long)(chunk.MultiFragments?.Length ?? 0) * Marshal.SizeOf<WorldMultiFragmentRecord>());
        }
        return bytes;
    }

    internal sealed record ChunkReplacement(
        SpatialChunkKey Key,
        WorldChunkContentRevision Revision,
        WorldChunkContentHash Hash,
        byte[]? TerrainMaterials,
        byte[]? TerrainHeights,
        WorldFrozenStaticRecord[]? FrozenStatics,
        WorldMultiFragmentRecord[]? MultiFragments)
    {
        internal WorldChunkReplacementKinds Kinds =>
            (TerrainMaterials is null ? WorldChunkReplacementKinds.None : WorldChunkReplacementKinds.Terrain) |
            (FrozenStatics is null ? WorldChunkReplacementKinds.None : WorldChunkReplacementKinds.FrozenStatics) |
            (MultiFragments is null ? WorldChunkReplacementKinds.None : WorldChunkReplacementKinds.MultiFragments);
    }
}

/// <summary>Cold transactional builder for one complete overlay generation.</summary>
public sealed class WorldMutableOverlayBuilder
{
    private readonly BoundWorldPack _baseWorld;
    private readonly WorldMutableOverlayLimits _limits;
    private readonly WorldMutableOverlay? _prior;
    private readonly SortedDictionary<SpatialChunkKey, MutableChunk> _chunks = [];
    private WorldMultiInstanceRecord[]? _multiInstances;
    private bool _multiInstancesDirty;

    public WorldMutableOverlayBuilder(
        BoundWorldPack baseWorld,
        WorldMutableOverlayLimits? limits = null,
        WorldMutableOverlay? prior = null)
    {
        _baseWorld = baseWorld ?? throw new ArgumentNullException(nameof(baseWorld));
        _limits = limits ?? prior?.Limits ?? WorldMutableOverlayLimits.Default;
        _limits.Validate();
        if (prior is not null &&
            (prior.BaseWorldFingerprint != baseWorld.Fingerprint || prior.Space != baseWorld.Space))
            throw new ArgumentException("The prior overlay belongs to a different immutable base world.", nameof(prior));
        _prior = prior;
        if (prior is not null)
        {
            foreach (WorldMutableOverlay.ChunkReplacement chunk in prior.Chunks)
                _chunks.Add(chunk.Key, new MutableChunk(chunk));
            if (prior.ReplacesMultiInstances) _multiInstances = prior.MultiInstances.ToArray();
        }
    }

    public bool TryReplaceTerrain(
        SpatialChunkKey key,
        ReadOnlySpan<ushort> materials,
        TerrainHeightPlaneBase heightBase,
        ReadOnlySpan<TerrainHeightOffset> heights)
    {
        if (!IsBaseChunk(key) || materials.Length != TerrainChunkFormat.MaterialCellCount ||
            heights.Length != TerrainChunkFormat.HeightVertexCount ||
            !TerrainHeightPlaneBase.TryCreate(heightBase.Millimeters, out TerrainHeightPlaneBase validatedBase) ||
            validatedBase != heightBase)
            return false;
        for (int index = 0; index < materials.Length; index++)
            if (!_baseWorld.Content.ContainsTerrainMaterial(materials[index])) return false;
        Span<int> decoded = stackalloc int[TerrainChunkFormat.HeightVertexCount];
        if (!TerrainHeightEncoding.TryDecodePlane(heightBase, heights, decoded, out _)) return false;

        byte[] materialBytes = new byte[TerrainChunkFormat.MaterialPlaneBytes];
        byte[] heightBytes = new byte[TerrainChunkFormat.HeightPlaneBytes];
        for (int index = 0; index < materials.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(materialBytes.AsSpan(index * sizeof(ushort)), materials[index]);
        BinaryPrimitives.WriteInt32LittleEndian(heightBytes, heightBase.Millimeters);
        for (int index = 0; index < heights.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(
                heightBytes.AsSpan(TerrainChunkFormat.HeightPlaneBaseBytes + index * sizeof(ushort)),
                heights[index].Units);
        MutableChunk mutable = Mutable(key);
        mutable.TerrainMaterials = materialBytes;
        mutable.TerrainHeights = heightBytes;
        mutable.Dirty = true;
        return true;
    }

    public bool TryReplaceFrozenStatics(
        SpatialChunkKey key,
        ReadOnlySpan<WorldFrozenStaticRecord> records)
    {
        if (!IsBaseChunk(key) || records.Length > _limits.MaximumStaticsPerChunk) return false;
        var owned = records.ToArray();
        foreach (WorldFrozenStaticRecord record in owned)
        {
            if (!record.Id.IsValid || !record.DefinitionId.IsValid ||
                !_baseWorld.Content.ContainsStaticDefinition(record.DefinitionId) ||
                record.Position.ToChunkKey(key.Space) != key ||
                !FrozenWorldStructureCodec.BoundsFitHomeHalo(record.Position, record.Bounds) ||
                (record.Flags & ~(WorldStaticFlags.Renderable | WorldStaticFlags.Collidable)) != 0)
                return false;
        }
        Array.Sort(owned, FrozenStaticComparer.Instance);
        for (int index = 1; index < owned.Length; index++)
            if (owned[index - 1].Id == owned[index].Id) return false;
        MutableChunk mutable = Mutable(key);
        mutable.FrozenStatics = owned;
        mutable.Dirty = true;
        return true;
    }

    public bool TryReplaceMultiFragments(
        SpatialChunkKey key,
        ReadOnlySpan<WorldMultiFragmentRecord> records)
    {
        if (!IsBaseChunk(key) || records.Length > _limits.MaximumMultiFragmentsPerChunk) return false;
        var owned = records.ToArray();
        foreach (WorldMultiFragmentRecord record in owned)
        {
            if (!record.MultiId.IsValid || !record.PieceId.IsValid || !record.DefinitionId.IsValid ||
                !_baseWorld.Content.ContainsStaticDefinition(record.DefinitionId) ||
                record.Position.ToChunkKey(key.Space) != key ||
                !FrozenWorldStructureCodec.BoundsFitHomeHalo(record.Position, record.Bounds) ||
                (record.Flags & ~(WorldStaticFlags.Renderable | WorldStaticFlags.Collidable)) != 0)
                return false;
        }
        Array.Sort(owned, MultiFragmentComparer.Instance);
        for (int index = 1; index < owned.Length; index++)
            if (owned[index - 1].MultiId == owned[index].MultiId &&
                owned[index - 1].PieceId == owned[index].PieceId) return false;
        MutableChunk mutable = Mutable(key);
        mutable.MultiFragments = owned;
        mutable.Dirty = true;
        return true;
    }

    public bool TryReplaceMultiInstances(ReadOnlySpan<WorldMultiInstanceRecord> records)
    {
        if (records.Length > _limits.MaximumMultiInstances) return false;
        var owned = records.ToArray();
        Array.Sort(owned, static (left, right) => left.Id.CompareTo(right.Id));
        for (int index = 0; index < owned.Length; index++)
        {
            if (!owned[index].Id.IsValid || owned[index].PieceCount <= 0 ||
                !_baseWorld.TryGetBaseTerrain(
                    owned[index].Root.ToChunkKey(_baseWorld.Space), out _) ||
                index != 0 && owned[index - 1].Id == owned[index].Id)
                return false;
        }
        _multiInstances = owned;
        _multiInstancesDirty = true;
        return true;
    }

    public bool TryBuild(out WorldMutableOverlay? overlay)
    {
        overlay = null;
        if (_chunks.Count > _limits.MaximumChangedChunks) return false;
        foreach ((SpatialChunkKey key, MutableChunk chunk) in _chunks)
        {
            if (!chunk.HasReplacement ||
                chunk.TerrainMaterials is not null && !EdgesMatchEffectiveWorld(key, chunk))
                return false;
        }
        if (!ValidateEffectiveIdentities()) return false;

        var replacements = new WorldMutableOverlay.ChunkReplacement[_chunks.Count];
        int output = 0;
        foreach ((SpatialChunkKey key, MutableChunk mutable) in _chunks)
        {
            WorldMutableOverlay.ChunkReplacement? prior = FindPrior(key);
            WorldChunkContentRevision revision = prior is null
                ? new WorldChunkContentRevision(1)
                : mutable.Dirty
                    ? new WorldChunkContentRevision(checked(prior.Revision.Value + 1))
                    : prior.Revision;
            WorldChunkContentHash hash = prior is not null && !mutable.Dirty
                ? prior.Hash
                : Hash(mutable);
            replacements[output++] = new WorldMutableOverlay.ChunkReplacement(
                key, revision, hash,
                mutable.TerrainMaterials, mutable.TerrainHeights,
                mutable.FrozenStatics, mutable.MultiFragments);
        }
        bool changed = _prior is null || _multiInstancesDirty || _chunks.Values.Any(static chunk => chunk.Dirty);
        WorldOverlayRevision overlayRevision = !changed && _prior is not null
            ? _prior.Revision
            : _prior is null || !_prior.Revision.IsValid
                ? new WorldOverlayRevision(1)
                : new WorldOverlayRevision(checked(_prior.Revision.Value + 1));
        var candidate = new WorldMutableOverlay(
            _baseWorld.Fingerprint, _baseWorld.Space, overlayRevision, _limits,
            replacements, _multiInstances);
        if (candidate.ReservedBytes > _limits.MaximumRetainedBytes) return false;
        overlay = candidate;
        return true;
    }

    private MutableChunk Mutable(SpatialChunkKey key)
    {
        if (!_chunks.TryGetValue(key, out MutableChunk? chunk))
            _chunks.Add(key, chunk = new MutableChunk(key));
        return chunk;
    }

    private bool IsBaseChunk(SpatialChunkKey key) =>
        key.Space == _baseWorld.Space && _baseWorld.TryGetBaseTerrain(key, out _);

    private bool EdgesMatchEffectiveWorld(SpatialChunkKey key, MutableChunk replacement)
    {
        WorldTerrainChunkView root = new(key, replacement.TerrainMaterials!, replacement.TerrainHeights!);
        return CompareNeighbor(key.Coord.X - 1, key.Coord.Z, root, 0, true) &&
            CompareNeighbor(key.Coord.X + 1, key.Coord.Z, root, TerrainChunkFormat.HeightVertexEdge - 1, true) &&
            CompareNeighbor(key.Coord.X, key.Coord.Z - 1, root, 0, false) &&
            CompareNeighbor(key.Coord.X, key.Coord.Z + 1, root, TerrainChunkFormat.HeightVertexEdge - 1, false);

        bool CompareNeighbor(int x, int z, WorldTerrainChunkView current, int currentEdge, bool vertical)
        {
            var neighborKey = new SpatialChunkKey(key.Space, x, z);
            if (!TryGetEffectiveTerrain(neighborKey, out WorldTerrainChunkView neighbor)) return true;
            int neighborEdge = currentEdge == 0 ? TerrainChunkFormat.HeightVertexEdge - 1 : 0;
            for (int index = 0; index < TerrainChunkFormat.HeightVertexEdge; index++)
            {
                int currentIndex = vertical
                    ? index * TerrainChunkFormat.HeightVertexEdge + currentEdge
                    : currentEdge * TerrainChunkFormat.HeightVertexEdge + index;
                int neighborIndex = vertical
                    ? index * TerrainChunkFormat.HeightVertexEdge + neighborEdge
                    : neighborEdge * TerrainChunkFormat.HeightVertexEdge + index;
                if (current.GetAbsoluteHeightMillimeters(currentIndex) !=
                    neighbor.GetAbsoluteHeightMillimeters(neighborIndex)) return false;
            }
            return true;
        }
    }

    private bool TryGetEffectiveTerrain(SpatialChunkKey key, out WorldTerrainChunkView view)
    {
        if (_chunks.TryGetValue(key, out MutableChunk? chunk) && chunk.TerrainMaterials is not null)
        {
            view = new WorldTerrainChunkView(key, chunk.TerrainMaterials, chunk.TerrainHeights!);
            return true;
        }
        return _baseWorld.TryGetBaseTerrain(key, out view);
    }

    private bool ValidateEffectiveIdentities()
    {
        var statics = new HashSet<WorldStaticId>();
        var pieces = new HashSet<(MultiInstanceId, MultiPieceId)>();
        var pieceCounts = new Dictionary<MultiInstanceId, int>();
        for (int chunkIndex = 0; chunkIndex < _baseWorld.ChunkCount; chunkIndex++)
        {
            if (!_baseWorld.TryGetChunkKey(chunkIndex, out SpatialChunkKey key)) return false;
            if (_chunks.TryGetValue(key, out MutableChunk? changed) && changed.FrozenStatics is not null)
            {
                foreach (WorldFrozenStaticRecord record in changed.FrozenStatics)
                    if (!statics.Add(record.Id)) return false;
            }
            else if (_baseWorld.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView frozen))
            {
                for (int index = 0; index < frozen.Count; index++)
                    if (!statics.Add(frozen.GetRecord(index).Id)) return false;
            }

            if (_chunks.TryGetValue(key, out changed) && changed.MultiFragments is not null)
            {
                foreach (WorldMultiFragmentRecord record in changed.MultiFragments)
                    if (!AddPiece(record)) return false;
            }
            else if (_baseWorld.TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments))
            {
                for (int index = 0; index < fragments.Count; index++)
                    if (!AddPiece(fragments.GetRecord(index))) return false;
            }
        }

        ReadOnlySpan<WorldMultiInstanceRecord> roots = _multiInstances is not null
            ? _multiInstances
            : GetBaseMultiInstances();
        if (roots.Length != pieceCounts.Count && (roots.Length != 0 || pieceCounts.Count != 0)) return false;
        foreach (WorldMultiInstanceRecord root in roots)
            if (!pieceCounts.TryGetValue(root.Id, out int count) || count != root.PieceCount) return false;
        return true;

        bool AddPiece(WorldMultiFragmentRecord record)
        {
            if (!pieces.Add((record.MultiId, record.PieceId))) return false;
            pieceCounts.TryGetValue(record.MultiId, out int count);
            pieceCounts[record.MultiId] = checked(count + 1);
            return true;
        }
    }

    private WorldMultiInstanceRecord[] GetBaseMultiInstances()
    {
        var roots = new WorldMultiInstanceRecord[_baseWorld.MultiInstanceCount];
        for (int index = 0; index < roots.Length; index++)
            if (!_baseWorld.TryGetMultiInstanceAt(index, out roots[index]))
                throw new InvalidOperationException("The validated base pack lost a Multi root.");
        return roots;
    }

    private WorldMutableOverlay.ChunkReplacement? FindPrior(SpatialChunkKey key)
    {
        if (_prior is null) return null;
        foreach (WorldMutableOverlay.ChunkReplacement chunk in _prior.Chunks)
            if (chunk.Key == key) return chunk;
        return null;
    }

    private static WorldChunkContentHash Hash(MutableChunk chunk)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(chunk.Key.Space.Value);
            writer.Write(chunk.Key.Coord.X);
            writer.Write(chunk.Key.Coord.Z);
            WriteBytes(writer, chunk.TerrainMaterials);
            WriteBytes(writer, chunk.TerrainHeights);
            writer.Write(chunk.FrozenStatics is null ? -1 : chunk.FrozenStatics.Length);
            if (chunk.FrozenStatics is not null)
                foreach (WorldFrozenStaticRecord record in chunk.FrozenStatics) WorldOverlayBinary.Write(writer, record);
            writer.Write(chunk.MultiFragments is null ? -1 : chunk.MultiFragments.Length);
            if (chunk.MultiFragments is not null)
                foreach (WorldMultiFragmentRecord record in chunk.MultiFragments) WorldOverlayBinary.Write(writer, record);
        }
        return WorldChunkContentHash.Read(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));

        static void WriteBytes(BinaryWriter writer, byte[]? bytes)
        {
            writer.Write(bytes?.Length ?? -1);
            if (bytes is not null) writer.Write(bytes);
        }
    }

    private sealed class MutableChunk
    {
        internal MutableChunk(SpatialChunkKey key) => Key = key;
        internal MutableChunk(WorldMutableOverlay.ChunkReplacement source)
        {
            Key = source.Key;
            TerrainMaterials = source.TerrainMaterials;
            TerrainHeights = source.TerrainHeights;
            FrozenStatics = source.FrozenStatics;
            MultiFragments = source.MultiFragments;
        }
        internal SpatialChunkKey Key { get; }
        internal byte[]? TerrainMaterials { get; set; }
        internal byte[]? TerrainHeights { get; set; }
        internal WorldFrozenStaticRecord[]? FrozenStatics { get; set; }
        internal WorldMultiFragmentRecord[]? MultiFragments { get; set; }
        internal bool Dirty { get; set; }
        internal bool HasReplacement => TerrainMaterials is not null || FrozenStatics is not null || MultiFragments is not null;
    }

    private sealed class FrozenStaticComparer : IComparer<WorldFrozenStaticRecord>
    {
        internal static readonly FrozenStaticComparer Instance = new();
        public int Compare(WorldFrozenStaticRecord left, WorldFrozenStaticRecord right)
        {
            int render = left.RenderKey.CompareTo(right.RenderKey);
            return render != 0 ? render : left.Id.CompareTo(right.Id);
        }
    }

    private sealed class MultiFragmentComparer : IComparer<WorldMultiFragmentRecord>
    {
        internal static readonly MultiFragmentComparer Instance = new();
        public int Compare(WorldMultiFragmentRecord left, WorldMultiFragmentRecord right)
        {
            int render = left.RenderKey.CompareTo(right.RenderKey);
            if (render != 0) return render;
            int multi = left.MultiId.CompareTo(right.MultiId);
            return multi != 0 ? multi : left.PieceId.CompareTo(right.PieceId);
        }
    }
}

/// <summary>One immutable resolved current-world view over an exact base pack plus durable overlay.</summary>
public sealed class WorldPreparedView
{
    public WorldPreparedView(BoundWorldPack baseWorld, WorldMutableOverlay? overlay = null)
    {
        BaseWorld = baseWorld ?? throw new ArgumentNullException(nameof(baseWorld));
        Overlay = overlay ?? WorldMutableOverlay.Empty(baseWorld);
        if (Overlay.BaseWorldFingerprint != baseWorld.Fingerprint || Overlay.Space != baseWorld.Space)
            throw new ArgumentException("The mutable overlay belongs to a different immutable base world.", nameof(overlay));
    }

    public BoundWorldPack BaseWorld { get; }
    public WorldMutableOverlay Overlay { get; }
    public WorldPackFingerprint BaseWorldFingerprint => BaseWorld.Fingerprint;
    public SpatialSpaceId Space => BaseWorld.Space;

    /// <summary>
    /// Gets durable replacement lineage for a changed chunk. A base-only chunk has no overlay
    /// revision/hash and remains identified by <see cref="BaseWorldFingerprint"/>.
    /// </summary>
    public bool TryGetReplacementIdentity(
        SpatialChunkKey key,
        out WorldChunkContentRevision revision,
        out WorldChunkContentHash hash) =>
        Overlay.TryGetChunkRevision(key, out revision, out hash);

    public bool TryGetBaseTerrain(SpatialChunkKey key, out WorldTerrainChunkView view) =>
        Overlay.TryGetTerrain(key, out view) || BaseWorld.TryGetBaseTerrain(key, out view);

    public bool TryGetFrozenStatics(SpatialChunkKey key, out WorldFrozenStaticChunkView view) =>
        Overlay.TryGetFrozenStatics(key, out view) || BaseWorld.TryGetFrozenStatics(key, out view);

    public bool TryGetMultiFragments(SpatialChunkKey key, out WorldMultiFragmentChunkView view) =>
        Overlay.TryGetMultiFragments(key, out view) || BaseWorld.TryGetMultiFragments(key, out view);

    public bool TryGetMultiInstance(MultiInstanceId id, out WorldMultiInstanceRecord instance) =>
        Overlay.ReplacesMultiInstances
            ? Overlay.TryGetMultiInstance(id, out instance)
            : BaseWorld.TryGetMultiInstance(id, out instance);
}

internal static class WorldOverlayBinary
{
    internal static void Write(BinaryWriter writer, in WorldFrozenStaticRecord record)
    {
        writer.Write(record.Id.Value); writer.Write(record.DefinitionId.Value);
        Write(writer, record.Position); writer.Write(record.Orientation.Units);
        writer.Write(record.Variant); writer.Write((ushort)record.Flags); Write(writer, record.Bounds);
    }

    internal static void Write(BinaryWriter writer, in WorldMultiFragmentRecord record)
    {
        writer.Write(record.MultiId.Value); writer.Write(record.PieceId.Value);
        writer.Write(record.DefinitionId.Value); Write(writer, record.Position);
        writer.Write(record.Orientation.Units); writer.Write(record.Variant);
        writer.Write((ushort)record.Flags); Write(writer, record.Bounds);
    }

    internal static void Write(BinaryWriter writer, in WorldMultiInstanceRecord record)
    {
        writer.Write(record.Id.Value); Write(writer, record.Root);
        writer.Write(record.Orientation.Units); writer.Write(record.PieceCount);
    }

    private static void Write(BinaryWriter writer, in WorldPosition position)
    {
        writer.Write(position.Chunk.X); writer.Write(position.Chunk.Z);
        writer.Write(position.Local.XMillimeters); writer.Write(position.Local.ZMillimeters);
        writer.Write(position.YMillimeters);
    }

    private static void Write(BinaryWriter writer, in WorldStaticBounds bounds)
    {
        writer.Write(bounds.MinimumXMillimeters); writer.Write(bounds.MinimumYMillimeters);
        writer.Write(bounds.MinimumZMillimeters); writer.Write(bounds.MaximumXExclusiveMillimeters);
        writer.Write(bounds.MaximumYExclusiveMillimeters); writer.Write(bounds.MaximumZExclusiveMillimeters);
    }
}
