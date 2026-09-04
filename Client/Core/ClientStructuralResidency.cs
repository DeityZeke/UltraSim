using System.Runtime.InteropServices;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Core;

public enum ClientStructuralResidencyError : byte
{
    None = 0,
    InvalidAuthoredWorld,
    InvalidCenter,
    CapacityExceeded,
    SemanticCacheRejected,
    PlanPending,
}

public readonly record struct ClientStructuralResidencyPlan(ulong OwnerIdentity, ulong Token)
{
    public bool IsValid => OwnerIdentity != 0 && Token != 0;
}

public readonly record struct ClientStructuralResidencyTransition(
    SpatialChunkKey PresentationCenter,
    int DesiredChunks,
    int EnteredChunks,
    int RetainedChunks,
    int ExitedChunks);

public readonly record struct ClientStructuralResidencySnapshot(
    int ConfiguredRenderRadiusChunks,
    int StructuralFringeChunks,
    int AuthoredChunks,
    int DesiredResidentChunks,
    int PeakDesiredResidentChunks,
    int SemanticResidentChunks,
    int PeakSemanticResidentChunks,
    bool PlanPending,
    ulong Transitions,
    ulong SemanticEnters,
    ulong SemanticExits,
    ulong ResetTransitions,
    long ReservedBytes);

/// <summary>
/// Engine-neutral, owner-thread controller for the client-owned structural working set. The
/// authoritative/preinstalled world may be larger than this desired set. Preparing a transition
/// admits every entering structural chunk before presentation changes; committing it retires
/// no-longer-desired structural-only chunks. Dynamic-public rows keep their semantic chunk alive
/// independently until Game interest retires them.
/// </summary>
public sealed class ClientStructuralResidency
{
    public const int DefaultStructuralFringeChunks = 1;
    private static long s_nextIdentity;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly ulong _identity;
    private readonly ClientWorldSemanticCache _cache;
    private readonly SpatialSpaceId _space;
    private readonly int _renderRadius;
    private readonly int _fringe;
    private readonly int _minimumX;
    private readonly int _maximumX;
    private readonly int _minimumZ;
    private readonly int _maximumZ;
    private readonly int _authoredChunks;
    private readonly Entry[] _active;
    private readonly Entry[] _candidate;
    private readonly ClientSemanticChunkHandle[] _newlyStaged;
    private readonly ClientSemanticChunkInfo[] _residentScratch;
    private int _activeCount;
    private int _candidateCount;
    private int _newlyStagedCount;
    private ulong _nextPlanToken = 1;
    private ulong _pendingToken;
    private ClientStructuralResidencyTransition _pendingTransition;
    private bool _pendingReset;
    private int _peakDesired;
    private int _peakSemantic;
    private ulong _transitions;
    private ulong _enters;
    private ulong _exits;
    private ulong _resets;

    public ClientStructuralResidency(
        ClientWorldSemanticCache cache,
        int renderRadiusChunks,
        int structuralFringeChunks = DefaultStructuralFringeChunks)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        if (renderRadiusChunks is < ClientSettingsResolver.MinimumRenderDistanceChunks or
                > ClientSettingsResolver.MaximumRenderDistanceChunks)
            throw new ArgumentOutOfRangeException(nameof(renderRadiusChunks));
        ArgumentOutOfRangeException.ThrowIfNegative(structuralFringeChunks);
        _renderRadius = renderRadiusChunks;
        _fringe = structuralFringeChunks;
        _space = cache.BaseWorld.Space;
        _authoredChunks = cache.BaseWorld.ChunkCount;
        if (_authoredChunks <= 0 || !TryReadAuthoredBounds(
                cache.BaseWorld, out _minimumX, out _maximumX, out _minimumZ, out _maximumZ))
            throw new ArgumentException("The structural source must be a canonical rectangular WorldPack.", nameof(cache));
        int width = checked(_maximumX - _minimumX + 1);
        int depth = checked(_maximumZ - _minimumZ + 1);
        int presentationDiameter = checked(renderRadiusChunks * 2 + 1);
        if (checked(width * depth) != _authoredChunks ||
            width < presentationDiameter || depth < presentationDiameter)
            throw new ArgumentException(
                "The authored structural rectangle cannot contain the requested presentation window.",
                nameof(cache));
        int residentRadius = checked(renderRadiusChunks + structuralFringeChunks);
        int diameter = checked(residentRadius * 2 + 1);
        int maximumDesired = checked(diameter * diameter);
        if (maximumDesired > cache.Limits.MaximumResidentChunks)
            throw new ArgumentException(
                "The structural working set exceeds semantic cache capacity.", nameof(cache));
        _active = new Entry[maximumDesired];
        _candidate = new Entry[maximumDesired];
        _newlyStaged = new ClientSemanticChunkHandle[maximumDesired];
        _residentScratch = new ClientSemanticChunkInfo[cache.Limits.MaximumResidentChunks];
        long identity = Interlocked.Increment(ref s_nextIdentity);
        if (identity <= 0) throw new InvalidOperationException("Structural residency identity exhausted.");
        _identity = checked((ulong)identity);
    }

    public int ConfiguredRenderRadiusChunks => _renderRadius;
    public int StructuralFringeChunks => _fringe;
    public int AuthoredChunkCount => _authoredChunks;

    /// <summary>Clamps only the presentation center; missing authored chunks are not fake void.</summary>
    public SpatialChunkKey GetPresentationCenter(in WorldPosition player)
    {
        EnsureOwner();
        SpatialChunkKey playerKey = player.ToChunkKey(_space);
        return new SpatialChunkKey(
            _space,
            Math.Clamp(playerKey.Coord.X, checked(_minimumX + _renderRadius), checked(_maximumX - _renderRadius)),
            Math.Clamp(playerKey.Coord.Z, checked(_minimumZ + _renderRadius), checked(_maximumZ - _renderRadius)));
    }

    public bool TryPrepare(
        SpatialChunkKey presentationCenter,
        bool reset,
        out ClientStructuralResidencyPlan plan,
        out ClientStructuralResidencyTransition transition,
        out ClientStructuralResidencyError error)
    {
        EnsureOwner();
        if (_pendingToken != 0)
        {
            plan = default;
            transition = default;
            error = ClientStructuralResidencyError.PlanPending;
            return false;
        }
        if (!presentationCenter.Space.IsValid || presentationCenter.Space != _space ||
            presentationCenter.Coord.X < _minimumX + _renderRadius ||
            presentationCenter.Coord.X > _maximumX - _renderRadius ||
            presentationCenter.Coord.Z < _minimumZ + _renderRadius ||
            presentationCenter.Coord.Z > _maximumZ - _renderRadius)
        {
            plan = default;
            transition = default;
            error = ClientStructuralResidencyError.InvalidCenter;
            return false;
        }

        _candidateCount = 0;
        _newlyStagedCount = 0;
        int extent = checked(_renderRadius + _fringe);
        int minimumX = Math.Max(_minimumX, checked(presentationCenter.Coord.X - extent));
        int maximumX = Math.Min(_maximumX, checked(presentationCenter.Coord.X + extent));
        int minimumZ = Math.Max(_minimumZ, checked(presentationCenter.Coord.Z - extent));
        int maximumZ = Math.Min(_maximumZ, checked(presentationCenter.Coord.Z + extent));
        for (int x = minimumX; x <= maximumX; x++)
        for (int z = minimumZ; z <= maximumZ; z++)
        {
            var key = new SpatialChunkKey(_space, x, z);
            if (!_cache.PreparedWorld.TryGetBaseTerrain(key, out _))
            {
                RollbackNewlyStaged();
                plan = default;
                transition = default;
                error = ClientStructuralResidencyError.InvalidAuthoredWorld;
                return false;
            }
            if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle))
            {
                if (!_cache.TryRequest(key, out handle, out _) ||
                    !RememberNewlyStaged(handle) ||
                    !_cache.TryStageFromBaseWorld(handle, [], default, out _) ||
                    !_cache.TryPublish(handle, out _))
                {
                    RollbackNewlyStaged();
                    plan = default;
                    transition = default;
                    error = ClientStructuralResidencyError.SemanticCacheRejected;
                    return false;
                }
            }
            if (_candidateCount == _candidate.Length)
            {
                RollbackNewlyStaged();
                plan = default;
                transition = default;
                error = ClientStructuralResidencyError.CapacityExceeded;
                return false;
            }
            _candidate[_candidateCount++] = new Entry(key, handle);
        }

        int entered = 0;
        int retained = 0;
        for (int index = 0; index < _candidateCount; index++)
        {
            if (Contains(_active, _activeCount, _candidate[index].Key)) retained++;
            else entered++;
        }
        int exited = 0;
        for (int index = 0; index < _activeCount; index++)
            if (!Contains(_candidate, _candidateCount, _active[index].Key)) exited++;
        transition = new ClientStructuralResidencyTransition(
            presentationCenter, _candidateCount, entered, retained, exited);
        if (_nextPlanToken == 0)
            throw new InvalidOperationException("Structural residency plan identity exhausted.");
        _pendingToken = _nextPlanToken++;
        _pendingTransition = transition;
        _pendingReset = reset;
        plan = new ClientStructuralResidencyPlan(_identity, _pendingToken);
        error = ClientStructuralResidencyError.None;
        return true;
    }

    public void Commit(in ClientStructuralResidencyPlan plan)
    {
        EnsureOwner();
        ValidatePlan(plan);
        // Reconcile the complete semantic cache, not only the prior desired set. A chunk that was
        // temporarily retained by public debt must become retireable after Game later clears that
        // independent interest; otherwise public churn could leak structural residency forever.
        if (!_cache.TryCopyResidentInfo(_residentScratch, out int residentCount, out _))
            throw new InvalidOperationException("The semantic residency scratch is undersized.");
        int retiredCount = 0;
        for (int index = 0; index < residentCount; index++)
        {
            ClientSemanticChunkInfo info = _residentScratch[index];
            if (Contains(_candidate, _candidateCount, info.Key) || info.PublicRecordCount != 0)
                continue;
            if (_cache.BeginRetire(info.Handle) && _cache.CompleteRetire(info.Handle)) retiredCount++;
        }
        _candidate.AsSpan(0, _candidateCount).CopyTo(_active);
        if (_activeCount > _candidateCount)
            _active.AsSpan(_candidateCount, _activeCount - _candidateCount).Clear();
        _activeCount = _candidateCount;
        _peakDesired = Math.Max(_peakDesired, _activeCount);
        int semanticResidents = checked(residentCount - retiredCount);
        _peakSemantic = Math.Max(_peakSemantic, semanticResidents);
        if (_pendingTransition.EnteredChunks != 0 || _pendingTransition.ExitedChunks != 0 ||
            _pendingReset)
        {
            _transitions++;
            if (_pendingReset) _resets++;
        }
        _enters += checked((ulong)_newlyStagedCount);
        _exits += checked((ulong)retiredCount);
        ClearPending();
    }

    public void Cancel(in ClientStructuralResidencyPlan plan)
    {
        EnsureOwner();
        ValidatePlan(plan);
        RollbackNewlyStaged();
        ClearPending();
    }

    public ClientStructuralResidencySnapshot GetSnapshot()
    {
        EnsureOwner();
        int semantic = _cache.GetSnapshot().ResidentChunks;
        _peakSemantic = Math.Max(_peakSemantic, semantic);
        long reserved = checked(
            (long)(_active.Length + _candidate.Length) * Marshal.SizeOf<Entry>() +
            (long)_newlyStaged.Length * Marshal.SizeOf<ClientSemanticChunkHandle>() +
            (long)_residentScratch.Length * Marshal.SizeOf<ClientSemanticChunkInfo>());
        return new ClientStructuralResidencySnapshot(
            _renderRadius, _fringe, _authoredChunks, _activeCount, _peakDesired,
            semantic, _peakSemantic, _pendingToken != 0, _transitions, _enters, _exits,
            _resets, reserved);
    }

    private bool RememberNewlyStaged(ClientSemanticChunkHandle handle)
    {
        if (_newlyStagedCount == _newlyStaged.Length) return false;
        _newlyStaged[_newlyStagedCount++] = handle;
        return true;
    }

    private void RollbackNewlyStaged()
    {
        for (int index = _newlyStagedCount - 1; index >= 0; index--)
        {
            ClientSemanticChunkHandle handle = _newlyStaged[index];
            if (_cache.BeginRetire(handle)) _ = _cache.CompleteRetire(handle);
            _newlyStaged[index] = default;
        }
        _newlyStagedCount = 0;
        _candidateCount = 0;
    }

    private void ValidatePlan(in ClientStructuralResidencyPlan plan)
    {
        if (!plan.IsValid || plan.OwnerIdentity != _identity || plan.Token != _pendingToken)
            throw new InvalidOperationException("The structural residency plan is stale or foreign.");
    }

    private void ClearPending()
    {
        _newlyStaged.AsSpan(0, _newlyStagedCount).Clear();
        _newlyStagedCount = 0;
        _candidateCount = 0;
        _pendingToken = 0;
        _pendingTransition = default;
        _pendingReset = false;
    }

    private static bool Contains(Entry[] values, int count, SpatialChunkKey key)
    {
        for (int index = 0; index < count; index++)
            if (values[index].Key == key) return true;
        return false;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Structural residency is client-owner-thread bound.");
    }

    private static bool TryReadAuthoredBounds(
        BoundWorldPack world,
        out int minimumX,
        out int maximumX,
        out int minimumZ,
        out int maximumZ)
    {
        minimumX = int.MaxValue;
        maximumX = int.MinValue;
        minimumZ = int.MaxValue;
        maximumZ = int.MinValue;
        for (int index = 0; index < world.ChunkCount; index++)
        {
            if (!world.TryGetChunkKey(index, out SpatialChunkKey key) || key.Space != world.Space)
                return false;
            minimumX = Math.Min(minimumX, key.Coord.X);
            maximumX = Math.Max(maximumX, key.Coord.X);
            minimumZ = Math.Min(minimumZ, key.Coord.Z);
            maximumZ = Math.Max(maximumZ, key.Coord.Z);
        }
        return minimumX <= maximumX && minimumZ <= maximumZ;
    }

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private readonly record struct Entry(
        SpatialChunkKey Key,
        ClientSemanticChunkHandle Handle);
}
