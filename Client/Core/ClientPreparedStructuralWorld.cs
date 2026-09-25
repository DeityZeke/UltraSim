using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Core;

/// <summary>
/// Owner-thread client residency root for one immutable authored World composition. Publication
/// replaces only the prepared subset; it never changes manifest, Space, or composition revision.
/// </summary>
public sealed class ClientPreparedStructuralWorld
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly WorldStructuralManifest _manifest;
    private readonly int _maximumPreparedPacks;
    private PreparedWorldComposition _current;

    public ClientPreparedStructuralWorld(
        PreparedWorldComposition initial,
        int maximumPreparedPacks = 8)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
        if (maximumPreparedPacks <= 0 || maximumPreparedPacks > WorldStructuralManifestFormat.MaximumPacks ||
            initial.PreparedPackCount > maximumPreparedPacks)
            throw new ArgumentOutOfRangeException(nameof(maximumPreparedPacks));
        _manifest = initial.Manifest;
        _maximumPreparedPacks = maximumPreparedPacks;
    }

    public SpatialSpaceId Space => _current.Space;
    public WorldStructuralManifestFingerprint ManifestFingerprint => _manifest.Fingerprint;
    public WorldCompositionRevision Revision => _current.Revision;
    public int MaximumPreparedPacks => _maximumPreparedPacks;
    public PreparedWorldComposition Current
    {
        get { EnsureOwner(); return _current; }
    }

    /// <summary>Refuses any authored-identity change, over-capacity subset, or invalid candidate.</summary>
    internal bool TryPublish(PreparedWorldComposition candidate)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Space != _current.Space || candidate.Revision != _current.Revision ||
            !ReferenceEquals(candidate.Manifest, _manifest) ||
            candidate.PreparedPackCount > _maximumPreparedPacks)
            return false;
        _current = candidate;
        return true;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The prepared client structural source is owner-thread-bound.");
    }
}
