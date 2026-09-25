using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Core;

public enum ClientSemanticChunkState : byte
{
    Absent = 0,
    Requested = 1,
    Staged = 2,
    Resident = 3,
    Retiring = 4,
}

public enum ClientSemanticChunkError : byte
{
    None = 0,
    InvalidKey,
    InvalidHandle,
    InvalidState,
    CapacityExceeded,
    MissingTerrain,
    FrozenStaticCapacityExceeded,
    MultiFragmentCapacityExceeded,
    AdditionalTerrainSurfaceCapacityExceeded,
    PublicRecordCapacityExceeded,
    KnownIdentityCapacityExceeded,
    InvalidPublication,
    StalePublication,
    ContradictoryPublication,
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly record struct ClientSemanticChunkHandle(uint Index, uint Generation)
{
    public bool IsValid => Generation != 0;
}

[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly record struct ClientSemanticHash(ulong Low, ulong High)
{
    public bool IsValid => Low != 0 || High != 0;
}

/// <summary>Client-resident limits; none are derived from server ECS/public address capacity.</summary>
public readonly record struct ClientWorldSemanticCacheLimits(
    int MaximumResidentChunks,
    int MaximumFrozenStaticsPerChunk,
    int MaximumMultiFragmentsPerChunk,
    int MaximumPublicRecordsPerChunk,
    int MaximumKnownPublicIdentities,
    int MaximumAdditionalTerrainSurfaceFragmentsPerChunk = 64)
{
    public static ClientWorldSemanticCacheLimits Default => new(512, 64, 64, 64, 4_096);

    /// <summary>
    /// Derives structural semantic residency from a square client view radius plus bounded fringe
    /// preparation. Dynamic public-interest extent remains a separate server/game policy.
    /// </summary>
    public static ClientWorldSemanticCacheLimits ForStructuralViewRadius(
        int renderRadiusChunks,
        int spareResidentChunks,
        int maximumFrozenStaticsPerChunk = 64,
        int maximumMultiFragmentsPerChunk = 64,
        int maximumPublicRecordsPerChunk = 64,
        int maximumKnownPublicIdentities = 4_096,
        int maximumAdditionalTerrainSurfaceFragmentsPerChunk = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(renderRadiusChunks);
        ArgumentOutOfRangeException.ThrowIfNegative(spareResidentChunks);
        int diameter = checked(renderRadiusChunks * 2 + 1);
        int active = checked(diameter * diameter);
        return new ClientWorldSemanticCacheLimits(
            checked(active + spareResidentChunks),
            maximumFrozenStaticsPerChunk,
            maximumMultiFragmentsPerChunk,
            maximumPublicRecordsPerChunk,
            maximumKnownPublicIdentities,
            maximumAdditionalTerrainSurfaceFragmentsPerChunk);
    }

    internal void Validate()
    {
        if (MaximumResidentChunks <= 0 || MaximumFrozenStaticsPerChunk < 0 ||
            MaximumMultiFragmentsPerChunk < 0 || MaximumPublicRecordsPerChunk <= 0 ||
            MaximumKnownPublicIdentities <= 0 ||
            MaximumAdditionalTerrainSurfaceFragmentsPerChunk < 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumResidentChunks));
        _ = checked(MaximumResidentChunks * TerrainChunkFormat.MaterialCellCount);
        _ = checked(MaximumResidentChunks * TerrainChunkFormat.HeightVertexCount);
        _ = checked(MaximumResidentChunks * TerrainChunkFormat.SurfacePresenceWordCount);
        _ = checked(MaximumResidentChunks * MaximumFrozenStaticsPerChunk);
        _ = checked(MaximumResidentChunks * MaximumMultiFragmentsPerChunk);
        _ = checked(MaximumResidentChunks * MaximumPublicRecordsPerChunk);
    }
}

public readonly record struct ClientSemanticChunkInfo(
    SpatialChunkKey Key,
    ClientSemanticChunkHandle Handle,
    ClientSemanticChunkState State,
    WorldChunkContentRevision StructuralRevision,
    WorldChunkContentHash StructuralContentHash,
    ChunkPublicationCursor PublicationCursor,
    int FrozenStaticCount,
    int MultiFragmentCount,
    int AdditionalTerrainSurfaceFragmentCount,
    int PublicRecordCount,
    ClientSemanticHash SemanticHash);

public readonly record struct ClientWorldSemanticCacheSnapshot(
    WorldPackFingerprint BaseWorldFingerprint,
    WorldOverlayRevision OverlayRevision,
    PublicationEpoch PublicationEpoch,
    int RequestedChunks,
    int StagedChunks,
    int ResidentChunks,
    int RetiringChunks,
    int KnownPublicIdentities,
    ulong PublishedChunks,
    ulong AppliedSnapshots,
    ulong AppliedDeltas,
    ulong RetiredChunks,
    ulong ChunkKeyLookups,
    ulong ChunkKeyProbes,
    ClientSemanticHash WorldSemanticHash,
    long ReservedBytes)
{
    public WorldStructuralManifestFingerprint StructuralManifestFingerprint { get; init; }
    public WorldCompositionRevision CompositionRevision { get; init; }
    public bool HasCanonicalComposition { get; init; }
}

/// <summary>Borrowed semantic page; valid only until the next cache mutation.</summary>
public readonly ref struct ClientSemanticChunkView
{
    internal ClientSemanticChunkView(
        ClientSemanticChunkInfo info,
        ReadOnlySpan<ushort> terrainMaterials,
        ReadOnlySpan<int> terrainHeights,
        ReadOnlySpan<ulong> terrainSurfacePresence,
        WorldTerrainSurfaceBandChunkView additionalTerrainSurfaces,
        ReadOnlySpan<WorldFrozenStaticRecord> frozenStatics,
        ReadOnlySpan<WorldMultiFragmentRecord> multiFragments,
        ReadOnlySpan<WorldPublicRecord> publicRecords)
    {
        Info = info;
        TerrainMaterials = terrainMaterials;
        TerrainAbsoluteHeightsMillimeters = terrainHeights;
        TerrainSurfacePresence = terrainSurfacePresence;
        AdditionalTerrainSurfaces = additionalTerrainSurfaces;
        FrozenStatics = frozenStatics;
        MultiFragments = multiFragments;
        PublicRecords = publicRecords;
    }

    public ClientSemanticChunkInfo Info { get; }
    public ReadOnlySpan<ushort> TerrainMaterials { get; }
    public ReadOnlySpan<int> TerrainAbsoluteHeightsMillimeters { get; }
    public ReadOnlySpan<ulong> TerrainSurfacePresence { get; }
    public WorldTerrainSurfaceBandChunkView AdditionalTerrainSurfaces { get; }
    public ReadOnlySpan<WorldFrozenStaticRecord> FrozenStatics { get; }
    public ReadOnlySpan<WorldMultiFragmentRecord> MultiFragments { get; }
    public ReadOnlySpan<WorldPublicRecord> PublicRecords { get; }
}

/// <summary>
/// One owner-bound, fixed-capacity production client semantic world cache. Immutable base content,
/// publication knowledge, and client-local slots remain distinct from both server storage and Godot.
/// A private bounded key index resolves resident/requested chunks without scanning slot capacity.
/// </summary>
public sealed class ClientWorldSemanticCache
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly BoundWorldPack? _baseWorld;
    private readonly WorldPreparedView? _preparedWorld;
    private readonly ClientPreparedStructuralWorld? _structuralSource;
    private readonly SpatialSpaceId _structuralSpace;
    private readonly ClientWorldSemanticCacheLimits _limits;
    private readonly ChunkSlot[] _slots;
    private readonly int[] _freeSlots;
    private readonly int[] _sortScratch;
    private readonly bool[] _affectedScratch;
    private readonly int[] _affectedSlots;
    private readonly ushort[] _materials;
    private readonly int[] _heights;
    private readonly ulong[] _surfacePresence;
    private readonly WorldFrozenStaticRecord[] _frozenStatics;
    private readonly WorldMultiFragmentRecord[] _multiFragments;
    private readonly WorldPublicRecord[] _publicRecords;
    private readonly ushort[] _materialScratch;
    private readonly int[] _heightScratch;
    private readonly ulong[] _surfacePresenceScratch;
    private readonly WorldFrozenStaticRecord[] _frozenStaticScratch;
    private readonly WorldMultiFragmentRecord[] _multiFragmentScratch;
    private readonly WorldPublicRecord[] _publicScratch;
    private readonly WorldPublicDeltaRecord[] _acceptedDeltaScratch;
    private readonly KnowledgeEntry[] _knowledge;
    private readonly int[] _knowledgeBuckets;
    private readonly int[] _chunkKeyBuckets;
    private int _freeSlotCount;
    private int _knownIdentityCount;
    private PublicationEpoch _publicationEpoch;
    private ulong _publishedChunks;
    private ulong _appliedSnapshots;
    private ulong _appliedDeltas;
    private ulong _retiredChunks;
    private ulong _chunkKeyLookups;
    private ulong _chunkKeyProbes;
    private int _affectedCount;

    public ClientWorldSemanticCache(
        BoundWorldPack baseWorld,
        ClientWorldSemanticCacheLimits limits = default)
        : this(new WorldPreparedView(baseWorld), limits) { }

    public ClientWorldSemanticCache(
        WorldPreparedView preparedWorld,
        ClientWorldSemanticCacheLimits limits = default)
        : this(preparedWorld ?? throw new ArgumentNullException(nameof(preparedWorld)), null, limits) { }

    public ClientWorldSemanticCache(
        PreparedWorldComposition preparedComposition,
        ClientWorldSemanticCacheLimits limits = default)
        : this(null, new ClientPreparedStructuralWorld(
            preparedComposition ?? throw new ArgumentNullException(nameof(preparedComposition)),
            Math.Max(8, preparedComposition.PreparedPackCount)), limits) { }

    public ClientWorldSemanticCache(
        ClientPreparedStructuralWorld structuralSource,
        ClientWorldSemanticCacheLimits limits = default)
        : this(null, structuralSource ?? throw new ArgumentNullException(nameof(structuralSource)), limits) { }

    private ClientWorldSemanticCache(
        WorldPreparedView? preparedWorld,
        ClientPreparedStructuralWorld? structuralSource,
        ClientWorldSemanticCacheLimits limits)
    {
        _preparedWorld = preparedWorld;
        _baseWorld = preparedWorld?.BaseWorld;
        _structuralSource = structuralSource;
        _structuralSpace = preparedWorld?.Space ?? structuralSource!.Space;
        if (limits == default) limits = ClientWorldSemanticCacheLimits.Default;
        limits.Validate();
        int knowledgeBucketCapacity = SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo(
            limits.MaximumKnownPublicIdentities);
        int chunkKeyBucketCapacity = SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo(
            limits.MaximumResidentChunks);
        _limits = limits;
        _slots = new ChunkSlot[limits.MaximumResidentChunks];
        _freeSlots = new int[limits.MaximumResidentChunks];
        _sortScratch = new int[limits.MaximumResidentChunks];
        _affectedScratch = new bool[limits.MaximumResidentChunks];
        _affectedSlots = new int[limits.MaximumResidentChunks];
        _materials = new ushort[checked(limits.MaximumResidentChunks * TerrainChunkFormat.MaterialCellCount)];
        _heights = new int[checked(limits.MaximumResidentChunks * TerrainChunkFormat.HeightVertexCount)];
        _surfacePresence = new ulong[
            checked(limits.MaximumResidentChunks * TerrainChunkFormat.SurfacePresenceWordCount)];
        _frozenStatics = new WorldFrozenStaticRecord[
            checked(limits.MaximumResidentChunks * limits.MaximumFrozenStaticsPerChunk)];
        _multiFragments = new WorldMultiFragmentRecord[
            checked(limits.MaximumResidentChunks * limits.MaximumMultiFragmentsPerChunk)];
        _publicRecords = new WorldPublicRecord[
            checked(limits.MaximumResidentChunks * limits.MaximumPublicRecordsPerChunk)];
        _materialScratch = new ushort[TerrainChunkFormat.MaterialCellCount];
        _heightScratch = new int[TerrainChunkFormat.HeightVertexCount];
        _surfacePresenceScratch = new ulong[TerrainChunkFormat.SurfacePresenceWordCount];
        _frozenStaticScratch = new WorldFrozenStaticRecord[limits.MaximumFrozenStaticsPerChunk];
        _multiFragmentScratch = new WorldMultiFragmentRecord[limits.MaximumMultiFragmentsPerChunk];
        _publicScratch = new WorldPublicRecord[limits.MaximumPublicRecordsPerChunk];
        _acceptedDeltaScratch = new WorldPublicDeltaRecord[limits.MaximumPublicRecordsPerChunk];
        _knowledge = new KnowledgeEntry[limits.MaximumKnownPublicIdentities];
        _knowledgeBuckets = new int[knowledgeBucketCapacity];
        _chunkKeyBuckets = new int[chunkKeyBucketCapacity];
        for (int index = 0; index < _freeSlots.Length; index++)
            _freeSlots[index] = _freeSlots.Length - index - 1;
        _freeSlotCount = _freeSlots.Length;
    }

    public ClientWorldSemanticCacheLimits Limits => _limits;
    public BoundWorldPack BaseWorld => _baseWorld ??
        throw new InvalidOperationException("A canonical structural composition is not one BaseWorld.");
    public WorldPreparedView PreparedWorld => _preparedWorld ??
        throw new InvalidOperationException("A canonical structural composition is not one prepared Pack.");
    public PreparedWorldComposition? PreparedComposition => _structuralSource?.Current;
    public ClientPreparedStructuralWorld? StructuralSource => _structuralSource;
    public SpatialSpaceId StructuralSpace => _structuralSpace;

    /// <summary>
    /// Publishes a different prepared subset without replacing this cache or its semantic handles.
    /// A Pack cannot leave the prepared subset while any client semantic slot still names it.
    /// </summary>
    public bool TryPublishPreparedComposition(PreparedWorldComposition candidate)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(candidate);
        if (_structuralSource is null) return false;
        PreparedWorldComposition previous = _structuralSource.Current;
        for (int index = 0; index < previous.PreparedPackCount; index++)
        {
            if (!previous.TryGetPreparedPackAt(index, out PreparedWorldPack pack))
                throw new InvalidOperationException("A validated prepared Pack disappeared.");
            if (candidate.TryGetPreparedPack(pack.Coord, out _)) continue;
            for (int slot = 0; slot < _slots.Length; slot++)
            {
                if (_slots[slot].State == ClientSemanticChunkState.Absent) continue;
                WorldPackAddressing.ToPackLocal(_slots[slot].Key.Coord, out WorldPackCoord owner, out _);
                if (owner == pack.Coord) return false;
            }
        }
        return _structuralSource.TryPublish(candidate);
    }

    /// <summary>Read-only canonical source status; legacy one-Pack sources have no manifest truth.</summary>
    public bool TryProbeStructuralChunk(SpatialChunkKey key, out WorldStructuralChunkProbe probe)
    {
        if (PreparedComposition is { } composition)
            return composition.TryProbeStructuralChunk(key, out probe);
        probe = default;
        return false;
    }
    internal int PreparedStructuralChunkCount => PreparedComposition?.PreparedChunkCount ?? _baseWorld!.ChunkCount;

    internal bool TryGetPreparedStructuralChunkKey(int index, out SpatialChunkKey key) =>
        PreparedComposition is { } composition
            ? composition.TryGetPreparedChunkKey(index, out key)
            : _baseWorld!.TryGetChunkKey(index, out key);

    internal bool TryGetStructuralTerrain(SpatialChunkKey key, out WorldTerrainChunkView terrain) =>
        PreparedComposition is { } composition
            ? composition.TryGetBaseTerrain(key, out terrain)
            : _preparedWorld!.TryGetBaseTerrain(key, out terrain);

    internal bool TryGetStructuralStatics(SpatialChunkKey key, out WorldFrozenStaticChunkView statics) =>
        PreparedComposition is { } composition
            ? composition.TryGetFrozenStatics(key, out statics)
            : _preparedWorld!.TryGetFrozenStatics(key, out statics);

    internal bool TryGetStructuralMultiFragments(SpatialChunkKey key, out WorldMultiFragmentChunkView fragments) =>
        PreparedComposition is { } composition
            ? composition.TryGetMultiFragments(key, out fragments)
            : _preparedWorld!.TryGetMultiFragments(key, out fragments);

    internal bool TryGetStructuralSurfaceBands(SpatialChunkKey key, out WorldTerrainSurfaceBandChunkView bands) =>
        PreparedComposition is { } composition
            ? composition.TryGetAdditionalTerrainSurfaceBands(key, out bands)
            : _preparedWorld!.TryGetAdditionalTerrainSurfaceBands(key, out bands);

    internal bool TryGetStructuralReplacementIdentity(SpatialChunkKey key,
        out WorldChunkContentRevision revision, out WorldChunkContentHash hash) =>
        PreparedComposition is { } composition
            ? composition.TryGetReplacementIdentity(key, out revision, out hash)
            : _preparedWorld!.TryGetReplacementIdentity(key, out revision, out hash);

    public bool TryRequest(
        SpatialChunkKey key,
        out ClientSemanticChunkHandle handle,
        out ClientSemanticChunkError error)
    {
        EnsureOwner();
        if (!key.Space.IsValid || key.Space != _structuralSpace)
        {
            handle = default;
            error = ClientSemanticChunkError.InvalidKey;
            return false;
        }
        int existing = FindSlot(key);
        if (existing >= 0)
        {
            if (_slots[existing].State == ClientSemanticChunkState.Retiring)
            {
                handle = default;
                error = ClientSemanticChunkError.InvalidState;
                return false;
            }
            handle = Handle(existing);
            error = ClientSemanticChunkError.None;
            return true;
        }
        if (_freeSlotCount == 0)
        {
            handle = default;
            error = ClientSemanticChunkError.CapacityExceeded;
            return false;
        }
        int slot = _freeSlots[--_freeSlotCount];
        uint generation = _slots[slot].Generation == uint.MaxValue
            ? throw new InvalidOperationException("The client semantic chunk generation is exhausted.")
            : _slots[slot].Generation + 1;
        _slots[slot] = new ChunkSlot(key, generation, ClientSemanticChunkState.Requested);
        InsertChunkKey(key, slot);
        handle = new ClientSemanticChunkHandle(checked((uint)slot), generation);
        error = ClientSemanticChunkError.None;
        return true;
    }

    public bool TryStageFromBaseWorld(
        ClientSemanticChunkHandle handle,
        ReadOnlySpan<WorldPublicRecord> publicSnapshot,
        ChunkPublicationCursor publicationCursor,
        out ClientSemanticChunkError error) =>
        TryStageFromStructuralWorld(handle, publicSnapshot, publicationCursor, out error);

    public bool TryStageFromStructuralWorld(
        ClientSemanticChunkHandle handle,
        ReadOnlySpan<WorldPublicRecord> publicSnapshot,
        ChunkPublicationCursor publicationCursor,
        out ClientSemanticChunkError error)
    {
        EnsureOwner();
        if (!TryResolve(handle, out int slot))
        {
            error = ClientSemanticChunkError.InvalidHandle;
            return false;
        }
        ref ChunkSlot state = ref _slots[slot];
        if (state.State != ClientSemanticChunkState.Requested)
        {
            error = ClientSemanticChunkError.InvalidState;
            return false;
        }
        if (!TryGetStructuralTerrain(state.Key, out WorldTerrainChunkView terrain))
        {
            error = ClientSemanticChunkError.MissingTerrain;
            return false;
        }
        int frozenCount = 0;
        if (TryGetStructuralStatics(state.Key, out WorldFrozenStaticChunkView frozenView))
        {
            frozenCount = frozenView.Count;
            if (frozenCount > _frozenStaticScratch.Length)
            {
                error = ClientSemanticChunkError.FrozenStaticCapacityExceeded;
                return false;
            }
        }
        int multiCount = 0;
        if (TryGetStructuralMultiFragments(state.Key, out WorldMultiFragmentChunkView multiView))
        {
            multiCount = multiView.Count;
            if (multiCount > _multiFragmentScratch.Length)
            {
                error = ClientSemanticChunkError.MultiFragmentCapacityExceeded;
                return false;
            }
        }
        int additionalTerrainCount = 0;
        if (TryGetStructuralSurfaceBands(
                state.Key, out WorldTerrainSurfaceBandChunkView additionalTerrainView))
        {
            additionalTerrainCount = additionalTerrainView.Count;
            if (additionalTerrainCount > _limits.MaximumAdditionalTerrainSurfaceFragmentsPerChunk)
            {
                error = ClientSemanticChunkError.AdditionalTerrainSurfaceCapacityExceeded;
                return false;
            }
        }
        if (publicSnapshot.Length > _publicScratch.Length)
        {
            error = ClientSemanticChunkError.PublicRecordCapacityExceeded;
            return false;
        }
        if (!CursorIsAdmissible(state.Key, publicationCursor) ||
            !publicationCursor.IsValid && !publicSnapshot.IsEmpty)
        {
            error = publicationCursor.IsValid
                ? ClientSemanticChunkError.StalePublication
                : ClientSemanticChunkError.InvalidPublication;
            return false;
        }
        if (!TryPrepareSnapshot(
                state.Key, publicSnapshot, publicationCursor.Revision, out int publicCount, out error))
            return false;

        terrain.CopyMaterials(_materialScratch);
        terrain.CopyAbsoluteHeights(_heightScratch);
        terrain.CopySurfacePresence(_surfacePresenceScratch);
        if (frozenCount != 0)
        {
            TryGetStructuralStatics(state.Key, out WorldFrozenStaticChunkView stagedFrozen);
            for (int index = 0; index < frozenCount; index++)
                _frozenStaticScratch[index] = stagedFrozen.GetRecord(index);
        }
        if (multiCount != 0)
        {
            TryGetStructuralMultiFragments(state.Key, out WorldMultiFragmentChunkView stagedMulti);
            for (int index = 0; index < multiCount; index++)
                _multiFragmentScratch[index] = stagedMulti.GetRecord(index);
        }
        _materialScratch.CopyTo(MaterialSpan(slot));
        _heightScratch.CopyTo(HeightSpan(slot));
        _surfacePresenceScratch.CopyTo(SurfacePresenceSpan(slot));
        _frozenStaticScratch.AsSpan(0, frozenCount).CopyTo(FrozenStaticSpan(slot));
        _multiFragmentScratch.AsSpan(0, multiCount).CopyTo(MultiFragmentSpan(slot));
        _publicScratch.AsSpan(0, publicCount).CopyTo(PublicSpan(slot));
        state.FrozenStaticCount = frozenCount;
        state.MultiFragmentCount = multiCount;
        state.AdditionalTerrainSurfaceFragmentCount = additionalTerrainCount;
        state.PublicRecordCount = publicCount;
        TryGetStructuralReplacementIdentity(
            state.Key, out state.StructuralRevision, out state.StructuralContentHash);
        state.PublicationCursor = publicationCursor;
        AdmitCursorEpoch(publicationCursor);
        state.State = ClientSemanticChunkState.Staged;
        state.SemanticHash = ComputeChunkHash(slot);
        error = ClientSemanticChunkError.None;
        return true;
    }

    public bool TryPublish(
        ClientSemanticChunkHandle handle,
        out ClientSemanticChunkError error)
    {
        EnsureOwner();
        if (!TryResolve(handle, out int slot))
        {
            error = ClientSemanticChunkError.InvalidHandle;
            return false;
        }
        ref ChunkSlot state = ref _slots[slot];
        if (state.State != ClientSemanticChunkState.Staged)
        {
            error = ClientSemanticChunkError.InvalidState;
            return false;
        }
        ReadOnlySpan<WorldPublicRecord> rows = PublicSpan(slot)[..state.PublicRecordCount];
        if (!TryPrepareSnapshot(
                state.Key, rows, state.PublicationCursor.Revision, out int preparedCount, out error))
            return false;
        _publicScratch.AsSpan(0, preparedCount).CopyTo(PublicSpan(slot));
        state.PublicRecordCount = preparedCount;
        rows = PublicSpan(slot)[..preparedCount];

        state.State = ClientSemanticChunkState.Resident;
        ResetAffected();
        for (int index = 0; index < rows.Length; index++)
            CommitKnowledge(WorldPublicOperation.Present, state.Key, rows[index], slot);
        _publishedChunks++;
        RecomputeAffectedHashes(slot);
        error = ClientSemanticChunkError.None;
        return true;
    }

    public bool TryApplySnapshot(
        ClientSemanticChunkHandle handle,
        in ChunkPublicationBatch batch,
        ReadOnlySpan<WorldPublicRecord> records,
        out ClientSemanticChunkError error)
    {
        EnsureOwner();
        if (!TryResolveResident(handle, out int slot, out error)) return false;
        ref ChunkSlot state = ref _slots[slot];
        if (batch.Status != ChunkPublicationReadStatus.Snapshot || batch.Key != state.Key ||
            batch.Count != records.Length ||
            !batch.Cursor.IsValid ||
            !CursorIsAdmissible(state.Key, batch.Cursor))
        {
            error = ClientSemanticChunkError.InvalidPublication;
            return false;
        }
        if (state.PublicationCursor.IsValid &&
            (state.PublicationCursor.Epoch != batch.Cursor.Epoch ||
             state.PublicationCursor.Revision.Value > batch.Cursor.Revision.Value))
        {
            error = ClientSemanticChunkError.StalePublication;
            return false;
        }
        if (records.Length > _publicScratch.Length)
        {
            error = ClientSemanticChunkError.PublicRecordCapacityExceeded;
            return false;
        }
        if (!TryPrepareSnapshot(
                state.Key, records, batch.Cursor.Revision, out int preparedCount, out error))
            return false;

        if (state.PublicationCursor.IsValid &&
            state.PublicationCursor.Revision == batch.Cursor.Revision)
        {
            if (!PublicSpan(slot)[..state.PublicRecordCount]
                    .SequenceEqual(_publicScratch.AsSpan(0, preparedCount)))
            {
                error = ClientSemanticChunkError.ContradictoryPublication;
                return false;
            }
            error = ClientSemanticChunkError.None;
            return true;
        }

        DetachKnowledgeLocations(slot);
        ResetAffected();
        for (int index = 0; index < preparedCount; index++)
            CommitKnowledge(WorldPublicOperation.Present, state.Key, _publicScratch[index], slot);
        _publicScratch.AsSpan(0, preparedCount).CopyTo(PublicSpan(slot));
        state.PublicRecordCount = preparedCount;
        state.PublicationCursor = batch.Cursor;
        AdmitCursorEpoch(batch.Cursor);
        _appliedSnapshots++;
        RecomputeAffectedHashes(slot);
        error = ClientSemanticChunkError.None;
        return true;
    }

    public bool TryApplyDelta(
        ClientSemanticChunkHandle handle,
        in ChunkPublicationBatch batch,
        ReadOnlySpan<WorldPublicDeltaRecord> deltas,
        out ClientSemanticChunkError error)
    {
        EnsureOwner();
        if (!TryResolveResident(handle, out int slot, out error)) return false;
        ref ChunkSlot state = ref _slots[slot];
        if (batch.Status != ChunkPublicationReadStatus.Delta || batch.Key != state.Key ||
            batch.Count != deltas.Length || !batch.Cursor.IsValid || batch.Cursor.Key != state.Key ||
            !state.PublicationCursor.IsValid || batch.Cursor.Epoch != state.PublicationCursor.Epoch ||
            batch.Cursor.Revision.Value <= state.PublicationCursor.Revision.Value)
        {
            error = ClientSemanticChunkError.StalePublication;
            return false;
        }
        if (deltas.Length > _acceptedDeltaScratch.Length)
        {
            error = ClientSemanticChunkError.PublicRecordCapacityExceeded;
            return false;
        }

        PublicSpan(slot)[..state.PublicRecordCount].CopyTo(_publicScratch);
        int scratchCount = state.PublicRecordCount;
        int acceptedCount = 0;
        uint priorIndex = 0;
        bool hasPrior = false;
        int requiredKnowledge = 0;
        for (int index = 0; index < deltas.Length; index++)
        {
            WorldPublicDeltaRecord delta = deltas[index];
            if (!TryValidateDelta(state.Key, batch.Cursor.Revision, delta, out error) ||
                hasPrior && delta.Id.Index <= priorIndex)
            {
                if (error == ClientSemanticChunkError.None)
                    error = ClientSemanticChunkError.InvalidPublication;
                return false;
            }
            priorIndex = delta.Id.Index;
            hasPrior = true;
            KnowledgeDecision decision = CompareKnowledge(
                delta.Operation, state.Key, delta.Id, delta.EntityPublicVersion, delta.Current);
            if (decision == KnowledgeDecision.Conflict)
            {
                error = ClientSemanticChunkError.ContradictoryPublication;
                return false;
            }
            if (decision is KnowledgeDecision.Stale or KnowledgeDecision.Duplicate) continue;
            if (FindKnowledge(delta.Id.Index) < 0) requiredKnowledge++;
            int existing = FindPublic(_publicScratch.AsSpan(0, scratchCount), delta.Id.Index);
            if (existing >= 0)
            {
                _publicScratch[existing] = _publicScratch[scratchCount - 1];
                scratchCount--;
            }
            if (delta.Operation == WorldPublicOperation.Present)
            {
                if (scratchCount == _publicScratch.Length)
                {
                    error = ClientSemanticChunkError.PublicRecordCapacityExceeded;
                    return false;
                }
                _publicScratch[scratchCount++] = delta.Current;
            }
            _acceptedDeltaScratch[acceptedCount++] = delta;
        }
        if (_knownIdentityCount + requiredKnowledge > _knowledge.Length)
        {
            error = ClientSemanticChunkError.KnownIdentityCapacityExceeded;
            return false;
        }
        SortPublicRecords(_publicScratch.AsSpan(0, scratchCount));

        ResetAffected();
        for (int index = 0; index < acceptedCount; index++)
        {
            WorldPublicDeltaRecord delta = _acceptedDeltaScratch[index];
            CommitKnowledge(delta.Operation, state.Key,
                delta.Operation == WorldPublicOperation.Present
                    ? delta.Current
                    : new WorldPublicRecord(delta.Id, delta.EntityPublicVersion, 0, default, default, default, default),
                slot);
        }
        _publicScratch.AsSpan(0, scratchCount).CopyTo(PublicSpan(slot));
        state.PublicRecordCount = scratchCount;
        state.PublicationCursor = batch.Cursor;
        _appliedDeltas++;
        RecomputeAffectedHashes(slot);
        error = ClientSemanticChunkError.None;
        return true;
    }

    public bool TryApplyChunkAbsent(
        ClientSemanticChunkHandle handle,
        PublicationEpoch sourceEpoch,
        in ChunkPublicationBatch batch,
        out ClientSemanticChunkError error)
    {
        // The caller must already have established that this response belongs to the current
        // request/subscription attempt. The semantic cache fences handle generation and
        // publication epoch; same-epoch transport request ordering belongs to the bridge.
        EnsureOwner();
        if (!TryResolveResident(handle, out int slot, out error)) return false;
        ref ChunkSlot state = ref _slots[slot];
        if (!sourceEpoch.IsValid ||
            batch.Status != ChunkPublicationReadStatus.ChunkAbsent ||
            batch.Key != state.Key || batch.Count != 0 || batch.Required != 0 ||
            batch.Cursor.IsValid)
        {
            error = ClientSemanticChunkError.InvalidPublication;
            return false;
        }
        if (_publicationEpoch.IsValid && _publicationEpoch != sourceEpoch)
        {
            error = ClientSemanticChunkError.StalePublication;
            return false;
        }

        DetachKnowledgeLocations(slot);
        state.PublicRecordCount = 0;
        state.PublicationCursor = default;
        if (!_publicationEpoch.IsValid) _publicationEpoch = sourceEpoch;
        state.SemanticHash = ComputeChunkHash(slot);
        _appliedSnapshots++;
        error = ClientSemanticChunkError.None;
        return true;
    }

    /// <summary>
    /// Retires only dynamic-public knowledge when Game interest removes a
    /// chunk. Structural base/overlay residency remains client-owned and may
    /// continue to serve the render window. This is topology application, not
    /// an assertion that the authoritative publication chunk is absent.
    /// </summary>
    public bool TryRetirePublicInterest(
        ClientSemanticChunkHandle handle,
        PublicationEpoch sourceEpoch,
        out ClientSemanticChunkError error)
    {
        EnsureOwner();
        if (!TryResolveResident(handle, out int slot, out error)) return false;
        if (!sourceEpoch.IsValid ||
            _publicationEpoch.IsValid && _publicationEpoch != sourceEpoch)
        {
            error = ClientSemanticChunkError.StalePublication;
            return false;
        }

        ref ChunkSlot state = ref _slots[slot];
        DetachKnowledgeLocations(slot);
        state.PublicRecordCount = 0;
        state.PublicationCursor = default;
        if (!_publicationEpoch.IsValid) _publicationEpoch = sourceEpoch;
        state.SemanticHash = ComputeChunkHash(slot);
        error = ClientSemanticChunkError.None;
        return true;
    }

    public void ResetPublicationEpoch(PublicationEpoch epoch)
    {
        EnsureOwner();
        if (!epoch.IsValid) throw new ArgumentException("A valid publication epoch is required.", nameof(epoch));
        if (_publicationEpoch == epoch) return;
        _publicationEpoch = epoch;
        Array.Clear(_knowledge);
        Array.Clear(_knowledgeBuckets);
        _knownIdentityCount = 0;
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            if (_slots[slot].State is not (ClientSemanticChunkState.Staged or ClientSemanticChunkState.Resident))
                continue;
            _slots[slot].PublicRecordCount = 0;
            _slots[slot].PublicationCursor = default;
            _slots[slot].SemanticHash = ComputeChunkHash(slot);
        }
    }

    public bool BeginRetire(ClientSemanticChunkHandle handle)
    {
        EnsureOwner();
        if (!TryResolve(handle, out int slot)) return false;
        ref ChunkSlot state = ref _slots[slot];
        if (state.State is ClientSemanticChunkState.Absent or ClientSemanticChunkState.Retiring) return false;
        DetachKnowledgeLocations(slot);
        state.State = ClientSemanticChunkState.Retiring;
        return true;
    }

    public bool CompleteRetire(ClientSemanticChunkHandle handle)
    {
        EnsureOwner();
        if (!TryResolve(handle, out int slot) || _slots[slot].State != ClientSemanticChunkState.Retiring)
            return false;
        uint generation = _slots[slot].Generation;
        RemoveChunkKey(_slots[slot].Key, slot);
        _slots[slot] = default;
        _slots[slot].Generation = generation;
        _freeSlots[_freeSlotCount++] = slot;
        _retiredChunks++;
        return true;
    }

    public bool TryGetInfo(ClientSemanticChunkHandle handle, out ClientSemanticChunkInfo info)
    {
        EnsureOwner();
        if (!TryResolve(handle, out int slot))
        {
            info = default;
            return false;
        }
        info = Info(slot);
        return true;
    }

    public bool TryGetResidentHandle(SpatialChunkKey key, out ClientSemanticChunkHandle handle)
    {
        EnsureOwner();
        int slot = FindSlot(key);
        if (slot < 0 || _slots[slot].State != ClientSemanticChunkState.Resident)
        {
            handle = default;
            return false;
        }
        handle = Handle(slot);
        return true;
    }

    public bool TryGetResidentView(ClientSemanticChunkHandle handle, out ClientSemanticChunkView view)
    {
        EnsureOwner();
        if (!TryResolve(handle, out int slot) || _slots[slot].State != ClientSemanticChunkState.Resident)
        {
            view = default;
            return false;
        }
        ref readonly ChunkSlot state = ref _slots[slot];
        TryGetStructuralSurfaceBands(
            state.Key, out WorldTerrainSurfaceBandChunkView additionalTerrain);
        view = new ClientSemanticChunkView(
            Info(slot),
            MaterialSpan(slot),
            HeightSpan(slot),
            SurfacePresenceSpan(slot),
            additionalTerrain,
            FrozenStaticSpan(slot)[..state.FrozenStaticCount],
            MultiFragmentSpan(slot)[..state.MultiFragmentCount],
            PublicSpan(slot)[..state.PublicRecordCount]);
        return true;
    }

    /// <summary>
    /// Builds the current resident chunk's engine-neutral terrain payload through the client-world
    /// ownership boundary. Mesh preparation resolves the same prepared base-plus-overlay world
    /// used to stage the resident structural page.
    /// </summary>
    public bool TryBuildResidentTerrainMesh(
        ClientSemanticChunkHandle handle,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        Span<ushort> triangleMaterials,
        out TerrainBaseMeshSummary summary,
        out ClientSemanticChunkError semanticError,
        out TerrainBaseMeshBuildError meshError)
    {
        EnsureOwner();
        if (!TryResolve(handle, out int slot))
        {
            summary = default;
            semanticError = ClientSemanticChunkError.InvalidHandle;
            meshError = TerrainBaseMeshBuildError.None;
            return false;
        }
        if (_slots[slot].State != ClientSemanticChunkState.Resident)
        {
            summary = default;
            semanticError = ClientSemanticChunkError.InvalidState;
            meshError = TerrainBaseMeshBuildError.None;
            return false;
        }
        PreparedWorldComposition? composition = PreparedComposition;
        bool built = composition is not null
            ? composition.TryBuildBaseTerrainMesh(
                _slots[slot].Key, vertices, indices, triangleMaterials, out summary, out meshError)
            : _preparedWorld!.TryBuildBaseTerrainMesh(
                _slots[slot].Key, vertices, indices, triangleMaterials, out summary, out meshError);
        if (!built)
        {
            semanticError = ClientSemanticChunkError.None;
            return false;
        }
        semanticError = ClientSemanticChunkError.None;
        return true;
    }

    public bool TryBuildResidentAdditionalTerrainSurfaceMesh(
        ClientSemanticChunkHandle handle,
        int fragmentIndex,
        Span<TerrainBaseMeshVertex> vertices,
        Span<ushort> indices,
        Span<ushort> triangleMaterials,
        out TerrainSurfaceBandMeshSummary summary,
        out ClientSemanticChunkError semanticError,
        out TerrainSurfaceBandMeshBuildError meshError)
    {
        EnsureOwner();
        if (!TryResolve(handle, out int slot))
        {
            summary = default;
            semanticError = ClientSemanticChunkError.InvalidHandle;
            meshError = TerrainSurfaceBandMeshBuildError.None;
            return false;
        }
        if (_slots[slot].State != ClientSemanticChunkState.Resident ||
            fragmentIndex < 0 ||
            fragmentIndex >= _slots[slot].AdditionalTerrainSurfaceFragmentCount)
        {
            summary = default;
            semanticError = ClientSemanticChunkError.InvalidState;
            meshError = TerrainSurfaceBandMeshBuildError.None;
            return false;
        }
        if (!TryGetStructuralSurfaceBands(
                _slots[slot].Key, out WorldTerrainSurfaceBandChunkView fragments))
            throw new InvalidOperationException("A resident additional terrain fragment disappeared.");
        if (!TerrainSurfaceBandMesh.TryBuild(
                fragments.GetBand(fragmentIndex), vertices, indices, triangleMaterials,
                out summary, out meshError))
        {
            semanticError = ClientSemanticChunkError.None;
            return false;
        }
        semanticError = ClientSemanticChunkError.None;
        return true;
    }

    public bool TryCopyResidentInfo(
        Span<ClientSemanticChunkInfo> destination,
        out int count,
        out int required)
    {
        EnsureOwner();
        required = 0;
        for (int slot = 0; slot < _slots.Length; slot++)
            if (_slots[slot].State == ClientSemanticChunkState.Resident)
                _sortScratch[required++] = slot;
        count = 0;
        if (destination.Length < required) return false;
        SortSlotIndices(required);
        for (int index = 0; index < required; index++) destination[count++] = Info(_sortScratch[index]);
        return true;
    }

    public ClientWorldSemanticCacheSnapshot GetSnapshot()
    {
        EnsureOwner();
        int requested = 0;
        int staged = 0;
        int resident = 0;
        int retiring = 0;
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            switch (_slots[slot].State)
            {
                case ClientSemanticChunkState.Requested: requested++; break;
                case ClientSemanticChunkState.Staged: staged++; break;
                case ClientSemanticChunkState.Resident: resident++; break;
                case ClientSemanticChunkState.Retiring: retiring++; break;
            }
        }
        return new ClientWorldSemanticCacheSnapshot(
            _baseWorld?.Fingerprint ?? default,
            _preparedWorld?.Overlay.Revision ?? default,
            _publicationEpoch,
            requested,
            staged,
            resident,
            retiring,
            _knownIdentityCount,
            _publishedChunks,
            _appliedSnapshots,
            _appliedDeltas,
            _retiredChunks,
            _chunkKeyLookups,
            _chunkKeyProbes,
            ComputeWorldHash(),
            CalculateReservedBytes())
        {
            HasCanonicalComposition = PreparedComposition is not null,
            StructuralManifestFingerprint = PreparedComposition?.ManifestFingerprint ?? default,
            CompositionRevision = PreparedComposition?.Revision ?? default,
        };
    }

    private bool TryPrepareSnapshot(
        SpatialChunkKey key,
        ReadOnlySpan<WorldPublicRecord> records,
        ChunkPublicationRevision maximumRevision,
        out int preparedCount,
        out ClientSemanticChunkError error)
    {
        preparedCount = 0;
        if (records.Length > _publicScratch.Length)
        {
            error = ClientSemanticChunkError.PublicRecordCapacityExceeded;
            return false;
        }
        uint priorIndex = 0;
        bool hasPrior = false;
        int requiredKnowledge = 0;
        for (int index = 0; index < records.Length; index++)
        {
            WorldPublicRecord record = records[index];
            if (!IsCanonicalPresent(record, key, maximumRevision) ||
                hasPrior && record.Id.Index <= priorIndex)
            {
                error = ClientSemanticChunkError.InvalidPublication;
                return false;
            }
            priorIndex = record.Id.Index;
            hasPrior = true;
            KnowledgeDecision decision = CompareKnowledge(
                WorldPublicOperation.Present, key, record.Id, record.EntityPublicVersion, record);
            if (decision == KnowledgeDecision.Conflict)
            {
                error = ClientSemanticChunkError.ContradictoryPublication;
                return false;
            }
            if (decision == KnowledgeDecision.Stale) continue;
            if (decision == KnowledgeDecision.Newer && FindKnowledge(record.Id.Index) < 0)
                requiredKnowledge++;
            _publicScratch[preparedCount++] = record;
        }
        if (_knownIdentityCount + requiredKnowledge > _knowledge.Length)
        {
            error = ClientSemanticChunkError.KnownIdentityCapacityExceeded;
            return false;
        }
        error = ClientSemanticChunkError.None;
        return true;
    }

    private static bool TryValidateDelta(
        SpatialChunkKey key,
        ChunkPublicationRevision maximumRevision,
        in WorldPublicDeltaRecord delta,
        out ClientSemanticChunkError error)
    {
        if (!delta.Id.IsValid || delta.EntityPublicVersion == 0 ||
            (delta.Fields & ~WorldPublicFieldMask.Complete) != 0 ||
            delta.Operation is not (WorldPublicOperation.Present or WorldPublicOperation.Remove) ||
            delta.Operation == WorldPublicOperation.Present &&
            (delta.Current.Id != delta.Id ||
             delta.Current.EntityPublicVersion != delta.EntityPublicVersion ||
             !IsCanonicalPresent(delta.Current, key, maximumRevision)) ||
            delta.Operation == WorldPublicOperation.Remove &&
            (delta.Fields != WorldPublicFieldMask.None || delta.Current != default))
        {
            error = ClientSemanticChunkError.InvalidPublication;
            return false;
        }
        error = ClientSemanticChunkError.None;
        return true;
    }

    private static bool IsCanonicalPresent(
        in WorldPublicRecord record,
        SpatialChunkKey key,
        ChunkPublicationRevision maximumRevision) =>
        record.Id.IsValid &&
        record.EntityPublicVersion != 0 &&
        record.PrefabId != 0 &&
        record.Position.Chunk == key.Coord &&
        (record.StateFlags & ~WorldPublicDescriptor.AdmittedFlags) == 0 &&
        ((record.StateFlags & WorldPublicStateFlags.GroundItem) == 0 ||
         (record.StateFlags & WorldPublicStateFlags.Visible) != 0) &&
        record.LastChangedRevision.IsValid &&
        maximumRevision.IsValid &&
        record.LastChangedRevision.Value <= maximumRevision.Value;

    private KnowledgeDecision CompareKnowledge(
        WorldPublicOperation operation,
        SpatialChunkKey key,
        WorldPublicEntityId id,
        ulong version,
        in WorldPublicRecord current)
    {
        int slot = FindKnowledge(id.Index);
        if (slot < 0) return KnowledgeDecision.Newer;
        ref readonly KnowledgeEntry floor = ref _knowledge[slot];
        int generation = id.Generation.CompareTo(floor.Generation);
        if (generation < 0) return KnowledgeDecision.Stale;
        if (generation > 0) return KnowledgeDecision.Newer;
        int order = version.CompareTo(floor.Version);
        if (order < 0) return KnowledgeDecision.Stale;
        if (order > 0)
        {
            if (operation == WorldPublicOperation.Remove && key != floor.Key)
                return KnowledgeDecision.Conflict;
            return KnowledgeDecision.Newer;
        }
        if (operation != floor.Operation || key != floor.Key)
            return KnowledgeDecision.Conflict;
        if (operation == WorldPublicOperation.Present && current != floor.Current)
            return KnowledgeDecision.Conflict;
        return KnowledgeDecision.Duplicate;
    }

    private void CommitKnowledge(
        WorldPublicOperation operation,
        SpatialChunkKey key,
        in WorldPublicRecord record,
        int targetSlot)
    {
        int knowledge = FindKnowledge(record.Id.Index);
        if (knowledge < 0)
        {
            knowledge = FindFreeKnowledge();
            _knownIdentityCount++;
            InsertKnowledge(record.Id.Index, knowledge);
        }
        if (_knowledge[knowledge].Used &&
            TryResolve(_knowledge[knowledge].ResidentHandle, out int priorSlot) &&
            _slots[priorSlot].State == ClientSemanticChunkState.Resident)
        {
            int row = FindPublic(
                PublicSpan(priorSlot)[.._slots[priorSlot].PublicRecordCount], record.Id.Index);
            if (row >= 0)
            {
                RemovePublicAt(priorSlot, row);
                MarkAffected(priorSlot);
            }
        }
        ClientSemanticChunkHandle residentHandle = default;
        if (operation == WorldPublicOperation.Present &&
            _slots[targetSlot].State == ClientSemanticChunkState.Resident &&
            _slots[targetSlot].Key == key)
            residentHandle = Handle(targetSlot);
        _knowledge[knowledge] = new KnowledgeEntry(
            true, record.Id.Index, record.Id.Generation, record.EntityPublicVersion,
            operation, key, operation == WorldPublicOperation.Present ? record : default,
            residentHandle);
    }

    private void DetachKnowledgeLocations(int slot)
    {
        ClientSemanticChunkHandle handle = Handle(slot);
        ReadOnlySpan<WorldPublicRecord> records =
            PublicSpan(slot)[.._slots[slot].PublicRecordCount];
        for (int row = 0; row < records.Length; row++)
        {
            int knowledge = FindKnowledge(records[row].Id.Index);
            if (knowledge >= 0 && _knowledge[knowledge].ResidentHandle == handle)
                _knowledge[knowledge] = _knowledge[knowledge] with { ResidentHandle = default };
        }
    }

    private int FindFreeKnowledge()
    {
        for (int index = 0; index < _knowledge.Length; index++)
            if (!_knowledge[index].Used) return index;
        throw new InvalidOperationException("Knowledge capacity changed after successful preflight.");
    }

    private int FindKnowledge(uint publicIndex)
    {
        int mask = _knowledgeBuckets.Length - 1;
        int bucket = unchecked((int)(publicIndex * 2_654_435_761U)) & mask;
        for (int probe = 0; probe < _knowledgeBuckets.Length; probe++)
        {
            int entry = _knowledgeBuckets[bucket];
            if (entry == 0) return -1;
            int knowledge = entry - 1;
            if (_knowledge[knowledge].Used && _knowledge[knowledge].Index == publicIndex)
                return knowledge;
            bucket = (bucket + 1) & mask;
        }
        return -1;
    }

    private void InsertKnowledge(uint publicIndex, int knowledge)
    {
        int mask = _knowledgeBuckets.Length - 1;
        int bucket = unchecked((int)(publicIndex * 2_654_435_761U)) & mask;
        for (int probe = 0; probe < _knowledgeBuckets.Length; probe++)
        {
            if (_knowledgeBuckets[bucket] == 0)
            {
                _knowledgeBuckets[bucket] = knowledge + 1;
                return;
            }
            bucket = (bucket + 1) & mask;
        }
        throw new InvalidOperationException("The bounded client knowledge index is full.");
    }

    private void RemovePublicAt(int slot, int row)
    {
        ref ChunkSlot state = ref _slots[slot];
        Span<WorldPublicRecord> records = PublicSpan(slot);
        records[row] = records[state.PublicRecordCount - 1];
        state.PublicRecordCount--;
        SortPublicRecords(records[..state.PublicRecordCount]);
    }

    private void RecomputeAffectedHashes(int primary)
    {
        MarkAffected(primary);
        for (int index = 0; index < _affectedCount; index++)
        {
            int slot = _affectedSlots[index];
            if (_slots[slot].State is ClientSemanticChunkState.Staged or ClientSemanticChunkState.Resident)
                _slots[slot].SemanticHash = ComputeChunkHash(slot);
            _affectedScratch[slot] = false;
        }
        _affectedCount = 0;
    }

    private void MarkAffected(int slot)
    {
        if (_affectedScratch[slot]) return;
        _affectedScratch[slot] = true;
        _affectedSlots[_affectedCount++] = slot;
    }

    private void ResetAffected()
    {
        for (int index = 0; index < _affectedCount; index++)
            _affectedScratch[_affectedSlots[index]] = false;
        _affectedCount = 0;
    }

    private ClientSemanticHash ComputeChunkHash(int slot)
    {
        ref readonly ChunkSlot state = ref _slots[slot];
        var hash = new SemanticHasher();
        hash.Add(unchecked((ulong)state.Key.Space.Value));
        hash.Add(unchecked((uint)state.Key.Coord.X));
        hash.Add(unchecked((uint)state.Key.Coord.Z));
        foreach (ushort material in MaterialSpan(slot)) hash.Add(material);
        foreach (int height in HeightSpan(slot)) hash.Add(unchecked((uint)height));
        ReadOnlySpan<ulong> surfacePresence = SurfacePresenceSpan(slot);
        if (TerrainSurfacePresence.HasAnyAbsent(surfacePresence))
        {
            hash.Add(0x5431_5650U); // T1VP: optional topology extension marker.
            foreach (ulong word in surfacePresence) hash.Add(word);
        }
        hash.Add(unchecked((uint)state.AdditionalTerrainSurfaceFragmentCount));
        if (state.AdditionalTerrainSurfaceFragmentCount != 0)
        {
            if (!TryGetStructuralSurfaceBands(
                    state.Key, out WorldTerrainSurfaceBandChunkView bands) ||
                bands.Count != state.AdditionalTerrainSurfaceFragmentCount)
                throw new InvalidOperationException("Resident additional terrain semantics changed.");
            for (int bandIndex = 0; bandIndex < bands.Count; bandIndex++)
            {
                WorldTerrainSurfaceBandView band = bands.GetBand(bandIndex);
                hash.Add(band.Id.Value);
                hash.Add(band.Bounds.MinimumCellX); hash.Add(band.Bounds.MinimumCellZ);
                hash.Add(band.Bounds.CellWidth); hash.Add(band.Bounds.CellDepth);
                hash.Add((byte)band.Faces); hash.Add(band.SideMaterialId);
                for (int z = 0; z < band.Bounds.CellDepth; z++)
                for (int x = 0; x < band.Bounds.CellWidth; x++)
                {
                    bool present = band.IsCellPresent(x, z);
                    hash.Add(present ? (byte)1 : (byte)0);
                    if (!present) continue;
                    hash.Add((byte)band.GetEdgeConnections(x, z));
                    if (band.HasTop) hash.Add(band.GetMaterial(WorldTerrainSurfaceFace.Top, x, z));
                    if (band.HasBottom) hash.Add(band.GetMaterial(WorldTerrainSurfaceFace.Bottom, x, z));
                }
                for (int z = 0; z <= band.Bounds.CellDepth; z++)
                for (int x = 0; x <= band.Bounds.CellWidth; x++)
                {
                    if (band.HasTop)
                        hash.Add(unchecked((uint)band.GetHeight(WorldTerrainSurfaceFace.Top, x, z)));
                    if (band.HasBottom)
                        hash.Add(unchecked((uint)band.GetHeight(WorldTerrainSurfaceFace.Bottom, x, z)));
                }
            }
        }
        hash.Add(unchecked((uint)state.FrozenStaticCount));
        foreach (WorldFrozenStaticRecord record in FrozenStaticSpan(slot)[..state.FrozenStaticCount])
            AddStatic(ref hash, record.Id.Value, record.DefinitionId.Value, record.Position,
                record.Orientation, record.Variant, record.Flags, record.Bounds);
        hash.Add(unchecked((uint)state.MultiFragmentCount));
        foreach (WorldMultiFragmentRecord record in MultiFragmentSpan(slot)[..state.MultiFragmentCount])
            AddStatic(ref hash, record.MultiId.Value, record.DefinitionId.Value, record.Position,
                record.Orientation, record.Variant, record.Flags, record.Bounds, record.PieceId.Value);
        hash.Add(unchecked((uint)state.PublicRecordCount));
        foreach (WorldPublicRecord record in PublicSpan(slot)[..state.PublicRecordCount])
        {
            hash.Add(record.Id.Index);
            hash.Add(record.Id.Generation);
            hash.Add(record.EntityPublicVersion);
            hash.Add(record.PrefabId);
            AddPosition(ref hash, record.Position);
            hash.Add(record.Orientation.Units);
            hash.Add((ushort)record.StateFlags);
        }
        return hash.Complete();
    }

    private ClientSemanticHash ComputeWorldHash()
    {
        int count = 0;
        for (int slot = 0; slot < _slots.Length; slot++)
            if (_slots[slot].State == ClientSemanticChunkState.Resident)
                _sortScratch[count++] = slot;
        SortSlotIndices(count);
        var hash = new SemanticHasher();
        hash.Add(unchecked((uint)count));
        for (int index = 0; index < count; index++)
        {
            ClientSemanticHash chunk = _slots[_sortScratch[index]].SemanticHash;
            hash.Add(chunk.Low);
            hash.Add(chunk.High);
        }
        return hash.Complete();
    }

    private static void AddStatic(
        ref SemanticHasher hash,
        ulong identity,
        uint definition,
        in WorldPosition position,
        WorldYaw orientation,
        ushort variant,
        WorldStaticFlags flags,
        in WorldStaticBounds bounds,
        uint piece = 0)
    {
        hash.Add(identity);
        hash.Add(piece);
        hash.Add(definition);
        AddPosition(ref hash, position);
        hash.Add(orientation.Units);
        hash.Add(variant);
        hash.Add((ushort)flags);
        hash.Add(unchecked((ulong)bounds.MinimumXMillimeters));
        hash.Add(unchecked((ulong)bounds.MinimumYMillimeters));
        hash.Add(unchecked((ulong)bounds.MinimumZMillimeters));
        hash.Add(unchecked((ulong)bounds.MaximumXExclusiveMillimeters));
        hash.Add(unchecked((ulong)bounds.MaximumYExclusiveMillimeters));
        hash.Add(unchecked((ulong)bounds.MaximumZExclusiveMillimeters));
    }

    private static void AddPosition(ref SemanticHasher hash, in WorldPosition position)
    {
        hash.Add(unchecked((uint)position.Chunk.X));
        hash.Add(unchecked((uint)position.Chunk.Z));
        hash.Add(position.Local.XMillimeters);
        hash.Add(position.Local.ZMillimeters);
        hash.Add(unchecked((uint)position.YMillimeters));
    }

    private void SortSlotIndices(int count)
    {
        for (int index = 1; index < count; index++)
        {
            int value = _sortScratch[index];
            int cursor = index - 1;
            while (cursor >= 0 && _slots[_sortScratch[cursor]].Key.CompareTo(_slots[value].Key) > 0)
            {
                _sortScratch[cursor + 1] = _sortScratch[cursor];
                cursor--;
            }
            _sortScratch[cursor + 1] = value;
        }
    }

    private static void SortPublicRecords(Span<WorldPublicRecord> records)
    {
        for (int index = 1; index < records.Length; index++)
        {
            WorldPublicRecord value = records[index];
            int cursor = index - 1;
            while (cursor >= 0 && records[cursor].Id.CompareTo(value.Id) > 0)
            {
                records[cursor + 1] = records[cursor];
                cursor--;
            }
            records[cursor + 1] = value;
        }
    }

    private static int FindPublic(ReadOnlySpan<WorldPublicRecord> records, uint publicIndex)
    {
        for (int index = 0; index < records.Length; index++)
            if (records[index].Id.Index == publicIndex) return index;
        return -1;
    }

    private bool CursorIsAdmissible(SpatialChunkKey key, ChunkPublicationCursor cursor) =>
        cursor == default || cursor.IsValid && cursor.Key == key &&
        (!_publicationEpoch.IsValid || cursor.Epoch == _publicationEpoch);

    private void AdmitCursorEpoch(ChunkPublicationCursor cursor)
    {
        if (cursor.IsValid && !_publicationEpoch.IsValid) _publicationEpoch = cursor.Epoch;
    }

    private bool TryResolveResident(
        ClientSemanticChunkHandle handle,
        out int slot,
        out ClientSemanticChunkError error)
    {
        if (!TryResolve(handle, out slot))
        {
            error = ClientSemanticChunkError.InvalidHandle;
            return false;
        }
        if (_slots[slot].State != ClientSemanticChunkState.Resident)
        {
            error = ClientSemanticChunkError.InvalidState;
            return false;
        }
        error = ClientSemanticChunkError.None;
        return true;
    }

    private bool TryResolve(ClientSemanticChunkHandle handle, out int slot)
    {
        if (!handle.IsValid || handle.Index >= (uint)_slots.Length)
        {
            slot = -1;
            return false;
        }
        slot = (int)handle.Index;
        return
            _slots[slot].Generation == handle.Generation &&
            _slots[slot].State != ClientSemanticChunkState.Absent;
    }

    private int FindSlot(SpatialChunkKey key)
    {
        _chunkKeyLookups++;
        int mask = _chunkKeyBuckets.Length - 1;
        int bucket = ChunkKeyBucket(key, mask);
        for (int probe = 0; probe < _chunkKeyBuckets.Length; probe++)
        {
            _chunkKeyProbes++;
            int entry = _chunkKeyBuckets[bucket];
            if (entry == 0) return -1;
            if (entry > 0)
            {
                int slot = entry - 1;
                if (_slots[slot].State != ClientSemanticChunkState.Absent &&
                    _slots[slot].Key == key)
                    return slot;
            }
            bucket = (bucket + 1) & mask;
        }
        return -1;
    }

    private void InsertChunkKey(SpatialChunkKey key, int slot)
    {
        int mask = _chunkKeyBuckets.Length - 1;
        int bucket = ChunkKeyBucket(key, mask);
        for (int probe = 0; probe < _chunkKeyBuckets.Length; probe++)
        {
            int entry = _chunkKeyBuckets[bucket];
            if (entry == 0)
            {
                _chunkKeyBuckets[bucket] = slot + 1;
                return;
            }
            bucket = (bucket + 1) & mask;
        }
        throw new InvalidOperationException("The bounded client chunk-key index is full.");
    }

    private void RemoveChunkKey(SpatialChunkKey key, int slot)
    {
        int mask = _chunkKeyBuckets.Length - 1;
        int bucket = ChunkKeyBucket(key, mask);
        for (int probe = 0; probe < _chunkKeyBuckets.Length; probe++)
        {
            int entry = _chunkKeyBuckets[bucket];
            if (entry == 0) break;
            if (entry == slot + 1)
            {
                CloseChunkKeyDeletionGap(bucket);
                return;
            }
            bucket = (bucket + 1) & mask;
        }
        throw new InvalidOperationException("The client chunk-key index lost an admitted slot.");
    }

    private void CloseChunkKeyDeletionGap(int hole)
    {
        int mask = _chunkKeyBuckets.Length - 1;
        int scan = (hole + 1) & mask;
        while (_chunkKeyBuckets[scan] != 0)
        {
            int entry = _chunkKeyBuckets[scan];
            int slot = entry - 1;
            int home = ChunkKeyBucket(_slots[slot].Key, mask);
            if (((hole - home) & mask) < ((scan - home) & mask))
            {
                _chunkKeyBuckets[hole] = entry;
                hole = scan;
            }
            scan = (scan + 1) & mask;
        }
        _chunkKeyBuckets[hole] = 0;
    }

    private static int ChunkKeyBucket(SpatialChunkKey key, int mask)
    {
        uint hash = unchecked((uint)key.GetHashCode());
        hash ^= hash >> 16;
        hash = unchecked(hash * 2_246_822_519U);
        hash ^= hash >> 13;
        return unchecked((int)hash) & mask;
    }

    private ClientSemanticChunkHandle Handle(int slot) =>
        new(checked((uint)slot), _slots[slot].Generation);

    private ClientSemanticChunkInfo Info(int slot)
    {
        ref readonly ChunkSlot state = ref _slots[slot];
        return new ClientSemanticChunkInfo(
            state.Key, Handle(slot), state.State,
            state.StructuralRevision, state.StructuralContentHash, state.PublicationCursor,
            state.FrozenStaticCount, state.MultiFragmentCount,
            state.AdditionalTerrainSurfaceFragmentCount, state.PublicRecordCount,
            state.SemanticHash);
    }

    private Span<ushort> MaterialSpan(int slot) =>
        _materials.AsSpan(slot * TerrainChunkFormat.MaterialCellCount, TerrainChunkFormat.MaterialCellCount);
    private Span<int> HeightSpan(int slot) =>
        _heights.AsSpan(slot * TerrainChunkFormat.HeightVertexCount, TerrainChunkFormat.HeightVertexCount);
    private Span<ulong> SurfacePresenceSpan(int slot) =>
        _surfacePresence.AsSpan(slot * TerrainChunkFormat.SurfacePresenceWordCount,
            TerrainChunkFormat.SurfacePresenceWordCount);
    private Span<WorldFrozenStaticRecord> FrozenStaticSpan(int slot) =>
        _frozenStatics.AsSpan(slot * _limits.MaximumFrozenStaticsPerChunk,
            _limits.MaximumFrozenStaticsPerChunk);
    private Span<WorldMultiFragmentRecord> MultiFragmentSpan(int slot) =>
        _multiFragments.AsSpan(slot * _limits.MaximumMultiFragmentsPerChunk,
            _limits.MaximumMultiFragmentsPerChunk);
    private Span<WorldPublicRecord> PublicSpan(int slot) =>
        _publicRecords.AsSpan(slot * _limits.MaximumPublicRecordsPerChunk,
            _limits.MaximumPublicRecordsPerChunk);

    private long CalculateReservedBytes() => checked(
        (long)_slots.Length * Unsafe.SizeOf<ChunkSlot>() +
        (long)(_freeSlots.Length + _sortScratch.Length + _affectedSlots.Length) * sizeof(int) +
        _affectedScratch.Length * sizeof(bool) +
        (long)_materials.Length * sizeof(ushort) +
        (long)_heights.Length * sizeof(int) +
        (long)_surfacePresence.Length * sizeof(ulong) +
        (long)_frozenStatics.Length * Unsafe.SizeOf<WorldFrozenStaticRecord>() +
        (long)_multiFragments.Length * Unsafe.SizeOf<WorldMultiFragmentRecord>() +
        (long)_publicRecords.Length * Unsafe.SizeOf<WorldPublicRecord>() +
        (long)_materialScratch.Length * sizeof(ushort) +
        (long)_heightScratch.Length * sizeof(int) +
        (long)_surfacePresenceScratch.Length * sizeof(ulong) +
        (long)_frozenStaticScratch.Length * Unsafe.SizeOf<WorldFrozenStaticRecord>() +
        (long)_multiFragmentScratch.Length * Unsafe.SizeOf<WorldMultiFragmentRecord>() +
        (long)_publicScratch.Length * Unsafe.SizeOf<WorldPublicRecord>() +
        (long)_acceptedDeltaScratch.Length * Unsafe.SizeOf<WorldPublicDeltaRecord>() +
        (long)_knowledge.Length * Unsafe.SizeOf<KnowledgeEntry>() +
        (long)(_knowledgeBuckets.Length + _chunkKeyBuckets.Length) * sizeof(int));

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The client semantic world cache is owner-thread bound.");
    }

    private struct ChunkSlot
    {
        internal ChunkSlot(SpatialChunkKey key, uint generation, ClientSemanticChunkState state)
        {
            Key = key;
            Generation = generation;
            State = state;
        }

        internal SpatialChunkKey Key;
        internal uint Generation;
        internal ClientSemanticChunkState State;
        internal WorldChunkContentRevision StructuralRevision;
        internal WorldChunkContentHash StructuralContentHash;
        internal ChunkPublicationCursor PublicationCursor;
        internal int FrozenStaticCount;
        internal int MultiFragmentCount;
        internal int AdditionalTerrainSurfaceFragmentCount;
        internal int PublicRecordCount;
        internal ClientSemanticHash SemanticHash;
    }

    private readonly record struct KnowledgeEntry(
        bool Used,
        uint Index,
        uint Generation,
        ulong Version,
        WorldPublicOperation Operation,
        SpatialChunkKey Key,
        WorldPublicRecord Current,
        ClientSemanticChunkHandle ResidentHandle);

    private enum KnowledgeDecision : byte
    {
        Newer,
        Duplicate,
        Stale,
        Conflict,
    }

    private struct SemanticHasher
    {
        private const ulong PrimeA = 1_099_511_628_211UL;
        private const ulong PrimeB = 14_029_467_366_897_019_727UL;
        private ulong _low;
        private ulong _high;

        internal void Add(ushort value) => Add((ulong)value);
        internal void Add(uint value) => Add((ulong)value);
        internal void Add(ulong value)
        {
            if (_low == 0 && _high == 0)
            {
                _low = 14_695_981_039_346_656_037UL;
                _high = 7_809_847_782_469_553_081UL;
            }
            _low = unchecked((_low ^ value) * PrimeA);
            _high = unchecked((_high ^ BitOperations.RotateLeft(value, 29)) * PrimeB);
        }

        internal ClientSemanticHash Complete()
        {
            Add(0x9E37_79B9_7F4A_7C15UL);
            ulong low = Avalanche(_low ^ BitOperations.RotateLeft(_high, 17));
            ulong high = Avalanche(_high ^ BitOperations.RotateLeft(_low, 41));
            return new ClientSemanticHash(low, high);
        }

        private static ulong Avalanche(ulong value)
        {
            unchecked
            {
                value ^= value >> 30;
                value *= 0xBF58_476D_1CE4_E5B9UL;
                value ^= value >> 27;
                value *= 0x94D0_49BB_1331_11EBUL;
                return value ^ value >> 31;
            }
        }
    }
}
