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
    private ClientWorldSemanticCache? _owner;

    public ClientPreparedStructuralWorld(
        PreparedWorldComposition initial,
        int maximumPreparedPacks = 8)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
        if (maximumPreparedPacks <= 0 || maximumPreparedPacks > WorldStructuralManifestFormat.MaximumPacks ||
            initial.PreparedPackCount > maximumPreparedPacks)
            throw new ArgumentOutOfRangeException(nameof(maximumPreparedPacks));
        if (!initial.TryValidatePreparedSubset(out _))
            throw new ArgumentException("The prepared subset has conflicting Packs.", nameof(initial));
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
    internal void Attach(ClientWorldSemanticCache owner)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner is not null)
            throw new InvalidOperationException("A prepared structural source already has a semantic cache owner.");
        _owner = owner;
    }

    internal bool TryPublish(ClientWorldSemanticCache owner, PreparedWorldComposition candidate)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(candidate);
        if (!ReferenceEquals(_owner, owner) ||
            candidate.Space != _current.Space || candidate.Revision != _current.Revision ||
            !ReferenceEquals(candidate.Manifest, _manifest) ||
            candidate.PreparedPackCount > _maximumPreparedPacks ||
            !candidate.TryValidatePreparedSubset(out _))
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
