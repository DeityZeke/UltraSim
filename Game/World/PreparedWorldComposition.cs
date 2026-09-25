using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>One already bound and prepared structural shard at its declared Pack address.</summary>
public readonly record struct PreparedWorldPack(WorldPackCoord Coord, WorldPreparedView View);

public enum PreparedWorldCompositionError
{
    None,
    InvalidManifest,
    InvalidRevision,
    InvalidSpace,
    TooManyPreparedPacks,
    InvalidPreparedView,
    DuplicatePreparedPack,
    PackNotInManifest,
    PackFingerprintMismatch,
    PackSpaceMismatch,
    PackWindowMismatch,
    ContentBindingMismatch,
    IncompleteComposition,
    TerrainSeamMismatch,
    DuplicateWorldStaticId,
    DuplicateMultiInstanceId,
}

/// <summary>
/// Immutable runtime structural root. The manifest is complete authored truth; the sorted array
/// is only the currently prepared subset. Pack/view payloads are borrowed immutable snapshots.
/// </summary>
public sealed class PreparedWorldComposition
{
    private readonly PreparedWorldPack[] _packs;

    private PreparedWorldComposition(
        SpatialSpaceId space,
        WorldCompositionRevision revision,
        WorldStructuralManifest manifest,
        PreparedWorldPack[] packs)
    {
        Space = space;
        Revision = revision;
        Manifest = manifest;
        _packs = packs;
    }

    public SpatialSpaceId Space { get; }
    public WorldCompositionRevision Revision { get; }
    public WorldStructuralManifest Manifest { get; }
    public WorldStructuralManifestFingerprint ManifestFingerprint => Manifest.Fingerprint;
    public int ManifestEntryCount => Manifest.EntryCount;
    public int PreparedPackCount => _packs.Length;
    public long PreparedIndexReservedBytes => (long)_packs.Length * (sizeof(int) * 2 + IntPtr.Size);

    /// <summary>Number of authored chunks in the prepared subset, not the whole manifest.</summary>
    public int PreparedChunkCount
    {
        get
        {
            int count = 0;
            foreach (PreparedWorldPack pack in _packs)
                count = checked(count + pack.View.BaseWorld.ChunkCount);
            return count;
        }
    }

    /// <summary>Cold sparse enumeration for client-residency construction.</summary>
    public bool TryGetPreparedChunkKey(int index, out SpatialChunkKey key)
    {
        foreach (PreparedWorldPack pack in _packs)
        {
            int count = pack.View.BaseWorld.ChunkCount;
            if (index < count)
                return pack.View.BaseWorld.TryGetChunkKey(index, out key);
            index -= count;
        }
        key = default;
        return false;
    }

    /// <summary>
    /// Cold complete-world validation. A partial prepared subset is useful for queries but cannot
    /// establish claims about authored seams and durable identities in unavailable Pack bytes.
    /// </summary>
    public bool TryValidateComplete(out PreparedWorldCompositionError error)
    {
        if (_packs.Length != Manifest.EntryCount)
        {
            error = PreparedWorldCompositionError.IncompleteComposition;
            return false;
        }
        return TryValidatePreparedSubset(out error);
    }

    /// <summary>
    /// Validates identities and seams among the currently prepared Packs. Missing manifest Packs
    /// are permitted, but cannot be used to excuse conflicts between Packs already present.
    /// </summary>
    public bool TryValidatePreparedSubset(out PreparedWorldCompositionError error)
    {
        var staticIds = new HashSet<WorldStaticId>();
        var multiIds = new HashSet<MultiInstanceId>();
        foreach (PreparedWorldPack pack in _packs)
        {
            BoundWorldPack authored = pack.View.BaseWorld;
            int rootCount = pack.View.Overlay.ReplacesMultiInstances
                ? pack.View.Overlay.ReplacementMultiInstanceCount : authored.MultiInstanceCount;
            for (int index = 0; index < rootCount; index++)
            {
                WorldMultiInstanceRecord root;
                if (pack.View.Overlay.ReplacesMultiInstances)
                    root = pack.View.Overlay.MultiInstances[index];
                else if (!authored.TryGetMultiInstanceAt(index, out root))
                    throw new InvalidOperationException("A validated Pack lost a Multi root.");
                if (!multiIds.Add(root.Id))
                {
                    error = PreparedWorldCompositionError.DuplicateMultiInstanceId;
                    return false;
                }
            }
            for (int index = 0; index < authored.ChunkCount; index++)
            {
                if (!authored.TryGetChunkKey(index, out SpatialChunkKey key))
                    throw new InvalidOperationException("A validated Pack lost an authored chunk.");
                if (pack.View.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics))
                    for (int row = 0; row < statics.Count; row++)
                        if (!staticIds.Add(statics.GetRecord(row).Id))
                        {
                            error = PreparedWorldCompositionError.DuplicateWorldStaticId;
                            return false;
                        }
                if (key.Coord.X != int.MaxValue &&
                    !ValidateNeighborEdge(key, new SpatialChunkKey(Space, key.Coord.X + 1, key.Coord.Z), true))
                {
                    error = PreparedWorldCompositionError.TerrainSeamMismatch;
                    return false;
                }
                if (key.Coord.Z != int.MaxValue &&
                    !ValidateNeighborEdge(key, new SpatialChunkKey(Space, key.Coord.X, key.Coord.Z + 1), false))
                {
                    error = PreparedWorldCompositionError.TerrainSeamMismatch;
                    return false;
                }
            }
        }
        error = PreparedWorldCompositionError.None;
        return true;
    }

    private bool ValidateNeighborEdge(SpatialChunkKey firstKey, SpatialChunkKey secondKey, bool east)
    {
        WorldPackAddressing.ToPackLocal(firstKey.Coord, out WorldPackCoord firstPack, out _);
        WorldPackAddressing.ToPackLocal(secondKey.Coord, out WorldPackCoord secondPack, out _);
        if (firstPack == secondPack || !TryGetBaseTerrain(secondKey, out WorldTerrainChunkView second))
            return true; // Per-Pack validation owns internal edges; absent neighbors are not seams.
        if (!TryGetBaseTerrain(firstKey, out WorldTerrainChunkView first))
            throw new InvalidOperationException("A validated Pack lost an authored terrain chunk.");
        for (byte vertex = 0; vertex < TerrainChunkFormat.HeightVertexEdge; vertex++)
        {
            int firstIndex = TerrainChunkFormat.GetHeightIndex(east
                ? new TerrainVertexCoord(32, vertex) : new TerrainVertexCoord(vertex, 32));
            int secondIndex = TerrainChunkFormat.GetHeightIndex(east
                ? new TerrainVertexCoord(0, vertex) : new TerrainVertexCoord(vertex, 0));
            if (first.GetAbsoluteHeightMillimeters(firstIndex) != second.GetAbsoluteHeightMillimeters(secondIndex))
                return false;
        }
        return true;
    }

    /// <summary>Validates the complete candidate before publishing any runtime root.</summary>
    public static bool TryCreate(
        SpatialSpaceId space,
        WorldCompositionRevision revision,
        WorldStructuralManifest? manifest,
        ReadOnlySpan<PreparedWorldPack> preparedPacks,
        out PreparedWorldComposition? composition,
        out PreparedWorldCompositionError error)
    {
        composition = null;
        if (manifest is null)
        {
            error = PreparedWorldCompositionError.InvalidManifest;
            return false;
        }
        if (!revision.IsValid)
        {
            error = PreparedWorldCompositionError.InvalidRevision;
            return false;
        }
        if (!space.IsValid)
        {
            error = PreparedWorldCompositionError.InvalidSpace;
            return false;
        }
        if (preparedPacks.Length > manifest.EntryCount)
        {
            error = PreparedWorldCompositionError.TooManyPreparedPacks;
            return false;
        }

        PreparedWorldPack[] candidate = preparedPacks.ToArray();
        Array.Sort(candidate, static (left, right) => left.Coord.CompareTo(right.Coord));
        WorldContentFingerprint content = default;
        for (int index = 0; index < candidate.Length; index++)
        {
            PreparedWorldPack pack = candidate[index];
            if (index != 0 && candidate[index - 1].Coord == pack.Coord)
            {
                error = PreparedWorldCompositionError.DuplicatePreparedPack;
                return false;
            }
            if (pack.View is null)
            {
                error = PreparedWorldCompositionError.InvalidPreparedView;
                return false;
            }
            if (!manifest.TryGetPack(pack.Coord, out WorldPackFingerprint expected))
            {
                error = PreparedWorldCompositionError.PackNotInManifest;
                return false;
            }
            if (pack.View.BaseWorldFingerprint != expected)
            {
                error = PreparedWorldCompositionError.PackFingerprintMismatch;
                return false;
            }
            if (pack.View.Space != space)
            {
                error = PreparedWorldCompositionError.PackSpaceMismatch;
                return false;
            }
            if (!pack.View.BaseWorld.Pack.TryValidateSpatialWindow(pack.Coord, out _, out _))
            {
                error = PreparedWorldCompositionError.PackWindowMismatch;
                return false;
            }
            WorldContentFingerprint fingerprint = pack.View.BaseWorld.Content.Fingerprint;
            if (index != 0 && fingerprint != content)
            {
                error = PreparedWorldCompositionError.ContentBindingMismatch;
                return false;
            }
            content = fingerprint;
        }

        composition = new PreparedWorldComposition(space, revision, manifest, candidate);
        error = PreparedWorldCompositionError.None;
        return true;
    }

    public bool TryGetPreparedPackAt(int index, out PreparedWorldPack pack)
    {
        if ((uint)index >= (uint)_packs.Length)
        {
            pack = default;
            return false;
        }
        pack = _packs[index];
        return true;
    }

    /// <summary>Exact allocation-free binary search over canonical prepared addresses.</summary>
    public bool TryGetPreparedPack(WorldPackCoord coord, out PreparedWorldPack pack)
    {
        int low = 0;
        int high = _packs.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = coord.CompareTo(_packs[middle].Coord);
            if (comparison == 0)
            {
                pack = _packs[middle];
                return true;
            }
            if (comparison < 0) high = middle - 1;
            else low = middle + 1;
        }
        pack = default;
        return false;
    }

    /// <summary>Separates immutable Pack/chunk authorship from this snapshot's prepared residency.</summary>
    public bool TryProbeStructuralChunk(SpatialChunkKey key, out WorldStructuralChunkProbe probe)
    {
        if (key.Space != Space)
        {
            probe = default;
            return false;
        }
        WorldPackAddressing.ToPackLocal(key.Coord, out WorldPackCoord owner, out _);
        if (!Manifest.TryGetPack(owner, out WorldPackFingerprint expected))
        {
            probe = new(WorldStructuralChunkStatus.NotAuthoredPack, owner, default);
            return true;
        }
        if (!TryGetPreparedPack(owner, out PreparedWorldPack prepared))
        {
            probe = new(WorldStructuralChunkStatus.KnownPackButNotPrepared, owner, expected);
            return true;
        }
        probe = new(prepared.View.TryGetBaseTerrain(key, out _)
            ? WorldStructuralChunkStatus.PreparedChunk
            : WorldStructuralChunkStatus.AuthoredPackMissingChunk, owner, expected);
        return true;
    }

    /// <summary>Resolves one X/Z base cell; Y is ignored. Additional vertical surfaces are orthogonal.</summary>
    public bool TryProbeBaseTerrainResolution(
        in WorldPosition location, out WorldBaseTerrainResolution resolution)
    {
        if (!TryProbeStructuralChunk(location.ToChunkKey(Space), out WorldStructuralChunkProbe chunk))
        {
            resolution = default;
            return false;
        }
        WorldBaseTerrainResolutionStatus status;
        int height = 0;
        ushort material = 0;
        switch (chunk.Status)
        {
            case WorldStructuralChunkStatus.NotAuthoredPack:
                status = WorldBaseTerrainResolutionStatus.NotAuthoredPack;
                break;
            case WorldStructuralChunkStatus.KnownPackButNotPrepared:
                status = WorldBaseTerrainResolutionStatus.KnownPackButNotPrepared;
                break;
            case WorldStructuralChunkStatus.AuthoredPackMissingChunk:
                status = WorldBaseTerrainResolutionStatus.AuthoredPackMissingChunk;
                break;
            default:
                BaseTerrainSurfaceSample baseSample = this.ProbeBaseTerrainSurface(location);
                status = baseSample.IsPresent
                    ? WorldBaseTerrainResolutionStatus.Present
                    : WorldBaseTerrainResolutionStatus.IntentionalVoid;
                if (baseSample.IsPresent)
                {
                    height = baseSample.HeightMillimeters;
                    material = baseSample.MaterialId;
                }
                break;
        }
        resolution = new(status, chunk.OwnerPack, chunk.ExpectedPackFingerprint, height, material);
        return true;
    }

    private bool TryResolve(SpatialChunkKey key, out WorldPreparedView view)
    {
        if (key.Space == Space)
        {
            WorldPackAddressing.ToPackLocal(key.Coord, out WorldPackCoord coord, out _);
            if (TryGetPreparedPack(coord, out PreparedWorldPack pack))
            {
                view = pack.View;
                return true;
            }
        }
        view = null!;
        return false;
    }

    public bool TryGetBaseTerrain(SpatialChunkKey key, out WorldTerrainChunkView terrain)
    {
        if (TryResolve(key, out WorldPreparedView view))
            return view.TryGetBaseTerrain(key, out terrain);
        terrain = default;
        return false;
    }

    public bool TryGetFrozenStatics(SpatialChunkKey key, out WorldFrozenStaticChunkView statics)
    {
        if (TryResolve(key, out WorldPreparedView view))
            return view.TryGetFrozenStatics(key, out statics);
        statics = default;
        return false;
    }

    public bool TryGetMultiFragments(SpatialChunkKey key, out WorldMultiFragmentChunkView fragments)
    {
        if (TryResolve(key, out WorldPreparedView view))
            return view.TryGetMultiFragments(key, out fragments);
        fragments = default;
        return false;
    }

    public bool TryGetAdditionalTerrainSurfaceBands(
        SpatialChunkKey key, out WorldTerrainSurfaceBandChunkView bands)
    {
        if (TryResolve(key, out WorldPreparedView view))
            return view.TryGetAdditionalTerrainSurfaceBands(key, out bands);
        bands = default;
        return false;
    }

    public bool TryGetReplacementIdentity(
        SpatialChunkKey key, out WorldChunkContentRevision revision, out WorldChunkContentHash hash)
    {
        if (TryResolve(key, out WorldPreparedView view))
            return view.TryGetReplacementIdentity(key, out revision, out hash);
        revision = default;
        hash = default;
        return false;
    }
}
