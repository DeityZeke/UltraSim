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
    private readonly int _authoredChunks;
    private readonly SpatialChunkKey[] _validCenters;
    private readonly int _validCenterCount;
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
    private SpatialChunkKey _lastPresentationCenter;
    private bool _hasLastPresentationCenter;

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
        _space = cache.StructuralSpace;
        _authoredChunks = cache.PreparedStructuralChunkCount;
        if (_authoredChunks <= 0)
            throw new ArgumentException("The structural source must contain authored chunks.", nameof(cache));
        _validCenters = new SpatialChunkKey[_authoredChunks];
        for (int index = 0; index < _authoredChunks; index++)
        {
            if (!cache.TryGetPreparedStructuralChunkKey(index, out SpatialChunkKey key) ||
                key.Space != _space)
                throw new ArgumentException(
                    "The structural source contains an invalid authored chunk key.", nameof(cache));
            if (IsCompletePresentationCenter(cache, key, renderRadiusChunks))
                _validCenters[_validCenterCount++] = key;
        }
        if (_validCenterCount == 0)
            throw new ArgumentException(
                "The authored structural source cannot contain the requested presentation window.",
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

    /// <summary>
    /// Selects the nearest complete authored presentation neighborhood. Missing authored chunks
    /// remain outside authored world; they are never synthesized as resident intentional void.
    /// </summary>
    public SpatialChunkKey GetPresentationCenter(in WorldPosition player)
    {
        EnsureOwner();
        SpatialChunkKey playerKey = player.ToChunkKey(_space);
        if (Contains(_validCenters, _validCenterCount, playerKey))
            return playerKey;
        if (_hasLastPresentationCenter &&
            Distance(playerKey, _lastPresentationCenter) <= checked((ulong)_renderRadius))
            return _lastPresentationCenter;

        SpatialChunkKey nearest = _validCenters[0];
        ulong nearestDistance = Distance(playerKey, nearest);
        for (int index = 1; index < _validCenterCount; index++)
        {
            SpatialChunkKey candidate = _validCenters[index];
            ulong distance = Distance(playerKey, candidate);
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }
        return nearest;
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
            !Contains(_validCenters, _validCenterCount, presentationCenter))
        {
            plan = default;
            transition = default;
            error = ClientStructuralResidencyError.InvalidCenter;
            return false;
        }

        _candidateCount = 0;
        _newlyStagedCount = 0;
        long extent = checked(_renderRadius + _fringe);
        long minimumX = Math.Max(int.MinValue, (long)presentationCenter.Coord.X - extent);
        long maximumX = Math.Min(int.MaxValue, (long)presentationCenter.Coord.X + extent);
        long minimumZ = Math.Max(int.MinValue, (long)presentationCenter.Coord.Z - extent);
        long maximumZ = Math.Min(int.MaxValue, (long)presentationCenter.Coord.Z + extent);
        for (long x = minimumX; x <= maximumX; x++)
        for (long z = minimumZ; z <= maximumZ; z++)
        {
            var key = new SpatialChunkKey(_space, checked((int)x), checked((int)z));
            if (!_cache.TryGetStructuralTerrain(key, out _))
                continue;
            if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle))
            {
                if (!_cache.TryRequest(key, out handle, out _) ||
                    !RememberNewlyStaged(handle) ||
                    !_cache.TryStageFromStructuralWorld(handle, [], default, out _) ||
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
        return BeginPlan(transition, reset, out plan, out error);
    }

    /// <summary>
    /// Prepares retirement of the complete desired structural set. Distant discontinuities use
    /// this after presentation retirement so the old and new neighborhoods never need to coexist.
    /// Dynamic-public rows remain resident until their independent Game interest is retired.
    /// </summary>
    public bool TryPrepareReset(
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
        _candidateCount = 0;
        _newlyStagedCount = 0;
        SpatialChunkKey center = _hasLastPresentationCenter
            ? _lastPresentationCenter
            : _validCenters[0];
        transition = new ClientStructuralResidencyTransition(
            center, 0, 0, 0, _activeCount);
        return BeginPlan(transition, reset: true, out plan, out error);
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
        if (_activeCount == 0)
        {
            _hasLastPresentationCenter = false;
            _lastPresentationCenter = default;
        }
        else
        {
            _lastPresentationCenter = _pendingTransition.PresentationCenter;
            _hasLastPresentationCenter = true;
        }
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
            (long)_validCenters.Length * Marshal.SizeOf<SpatialChunkKey>() +
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

    private static bool Contains(SpatialChunkKey[] values, int count, SpatialChunkKey key)
    {
        int low = 0;
        int high = count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = values[middle].CompareTo(key);
            if (order == 0) return true;
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        return false;
    }

    private bool BeginPlan(
        in ClientStructuralResidencyTransition transition,
        bool reset,
        out ClientStructuralResidencyPlan plan,
        out ClientStructuralResidencyError error)
    {
        if (_nextPlanToken == 0)
            throw new InvalidOperationException("Structural residency plan identity exhausted.");
        _pendingToken = _nextPlanToken++;
        _pendingTransition = transition;
        _pendingReset = reset;
        plan = new ClientStructuralResidencyPlan(_identity, _pendingToken);
        error = ClientStructuralResidencyError.None;
        return true;
    }

    private static ulong Distance(SpatialChunkKey left, SpatialChunkKey right)
    {
        ulong x = checked((ulong)Math.Abs((long)left.Coord.X - right.Coord.X));
        ulong z = checked((ulong)Math.Abs((long)left.Coord.Z - right.Coord.Z));
        return Math.Max(x, z);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Structural residency is client-owner-thread bound.");
    }

    private static bool IsCompletePresentationCenter(
        ClientWorldSemanticCache world,
        SpatialChunkKey center,
        int radius)
    {
        long minimumX = Math.Max(int.MinValue, (long)center.Coord.X - radius);
        long maximumX = Math.Min(int.MaxValue, (long)center.Coord.X + radius);
        long minimumZ = Math.Max(int.MinValue, (long)center.Coord.Z - radius);
        long maximumZ = Math.Min(int.MaxValue, (long)center.Coord.Z + radius);
        if (maximumX - minimumX != radius * 2L || maximumZ - minimumZ != radius * 2L)
            return false;
        for (long x = minimumX; x <= maximumX; x++)
        for (long z = minimumZ; z <= maximumZ; z++)
        {
            if (!world.TryGetStructuralTerrain(
                    new SpatialChunkKey(world.StructuralSpace, checked((int)x), checked((int)z)), out _))
                return false;
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private readonly record struct Entry(
        SpatialChunkKey Key,
        ClientSemanticChunkHandle Handle);
}
