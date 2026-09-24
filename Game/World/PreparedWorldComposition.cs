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
