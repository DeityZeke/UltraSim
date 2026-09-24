using System.Runtime.InteropServices;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Core;

/// <summary>Transient signed presentation coordinate relative to the viewer's home chunk.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly record struct ClientPresentationOffset(int X, int Z);

public enum ClientPresentationChangeKind : byte
{
    Enter = 1,
    Move = 2,
    Exit = 3,
}

/// <summary>One engine-neutral presentation-membership operation.</summary>
public readonly record struct ClientPresentationChange(
    ClientPresentationChangeKind Kind,
    SpatialChunkKey Key,
    ClientSemanticChunkHandle Handle,
    ClientPresentationOffset PreviousOffset,
    ClientPresentationOffset CurrentOffset);

public enum ClientPresentationWindowError : byte
{
    None = 0,
    InvalidPlayerPosition,
    MissingSemanticChunk,
    DestinationTooSmall,
    PlanPending,
    BackendRejected,
}

public readonly record struct ClientPresentationWindowTransition(
    SpatialChunkKey CenterChunk,
    int ActiveChunks,
    int Entered,
    int Retained,
    int Moved,
    int Exited);

public readonly record struct ClientPresentationWindowSnapshot(
    SpatialChunkKey CenterChunk,
    int PresentationRadiusChunks,
    int ActiveChunks,
    bool PlanPending,
    ulong Transitions,
    ulong EnteredChunks,
    ulong RetainedChunks,
    ulong ExitedChunks,
    ulong ResetTransitions,
    ulong SameCenterFastPaths,
    ulong CandidateKeyLookups,
    ulong CandidateComparisons,
    long ReservedBytes);

public readonly record struct ClientPresentationBinding(
    SpatialChunkKey Key,
    ClientSemanticChunkHandle SemanticHandle,
    ClientPresentationOffset Offset);

/// <summary>
/// Opaque authority to commit or cancel one prepared presentation-window plan. A plan is valid
/// only for the originating window and only until its first commit or cancellation.
/// </summary>
public readonly record struct ClientPresentationWindowPlan(ulong WindowIdentity, ulong Token)
{
    public bool IsValid => WindowIdentity != 0 && Token != 0;
}

/// <summary>
/// Owner-thread engine-neutral desired-presentation state. It converts canonical player movement
/// and resident semantic chunks into bounded Enter/Move/Exit operations. Native resource queues,
/// rendering objects, frame budgets, and realized presentation remain backend-owned.
/// </summary>
/// <remarks>
/// Planning is transactional. The caller first prepares operations, asks its concrete backend to
/// admit the complete operation set, then commits the opaque plan. Backend rejection must cancel
/// the plan, leaving the last committed desired window unchanged.
/// </remarks>
public sealed class ClientPresentationWindow
{
    private static long s_nextWindowIdentity;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly ulong _windowIdentity;
    private readonly ClientWorldSemanticCache _cache;
    private readonly SpatialSpaceId _space;
    private readonly int _radius;
    private readonly WindowEntry[] _active;
    private readonly WindowEntry[] _candidate;
    private readonly ClientPresentationChange[] _changeScratch;
    private int _activeCount;
    private SpatialChunkKey _center;
    private SpatialChunkKey _offsetOrigin;
    private ulong _nextPlanToken = 1;
    private ulong _pendingPlanToken;
    private PlanKind _pendingKind;
    private int _pendingCandidateCount;
    private SpatialChunkKey _pendingOffsetOrigin;
    private ClientPresentationWindowTransition _pendingTransition;
    private bool _pendingWindowChanged;
    private bool _pendingSameCenter;
    private ulong _transitions;
    private ulong _entered;
    private ulong _retained;
    private ulong _exited;
    private ulong _resets;
    private ulong _sameCenterFastPaths;
    private ulong _candidateKeyLookups;
    private ulong _candidateComparisons;

    public ClientPresentationWindow(
        ClientWorldSemanticCache cache,
        int presentationRadiusChunks)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        ArgumentOutOfRangeException.ThrowIfNegative(presentationRadiusChunks);
        int diameter = checked(presentationRadiusChunks * 2 + 1);
        int maximumActive = checked(diameter * diameter);
        if (maximumActive > cache.Limits.MaximumResidentChunks)
            throw new ArgumentOutOfRangeException(nameof(presentationRadiusChunks),
                "The complete presentation window must fit within semantic resident capacity.");
        long identity = Interlocked.Increment(ref s_nextWindowIdentity);
        if (identity <= 0) throw new InvalidOperationException("Presentation window identity exhausted.");
        _windowIdentity = checked((ulong)identity);
        _space = cache.StructuralSpace;
        _radius = presentationRadiusChunks;
        _active = new WindowEntry[maximumActive];
        _candidate = new WindowEntry[maximumActive];
        _changeScratch = new ClientPresentationChange[checked(maximumActive * 2)];
    }

    public int PresentationRadiusChunks => _radius;
    public int MaximumActiveChunks => _active.Length;

    /// <summary>Computes exact viewer-relative X/Z before any presentation float conversion.</summary>
    public static void GetRelativeMillimeters(
        in WorldPosition subject,
        in WorldPosition viewer,
        out long xMillimeters,
        out long zMillimeters)
    {
        xMillimeters = checked(
            checked((long)subject.Chunk.X - viewer.Chunk.X) *
            GameWorldSpatialProfile.WorldChunkEdgeUnits +
            subject.Local.XMillimeters - viewer.Local.XMillimeters);
        zMillimeters = checked(
            checked((long)subject.Chunk.Z - viewer.Chunk.Z) *
            GameWorldSpatialProfile.WorldChunkEdgeUnits +
            subject.Local.ZMillimeters - viewer.Local.ZMillimeters);
    }

    public bool TryPlanTrack(
        in WorldPosition player,
        Span<ClientPresentationChange> changes,
        out ClientPresentationWindowPlan plan,
        out ClientPresentationWindowTransition transition,
        out int required,
        out ClientPresentationWindowError error)
    {
        SpatialChunkKey playerChunk = player.ToChunkKey(_space);
        return TryPlanTrack(
            player, playerChunk, changes, out plan, out transition, out required, out error);
    }

    /// <summary>
    /// Plans a complete presentation window around <paramref name="presentationCenter"/> while
    /// expressing every retained chunk relative to the player's actual home chunk. The center may
    /// therefore clamp to a finite authored-world boundary without detaching the viewer origin.
    /// </summary>
    public bool TryPlanTrack(
        in WorldPosition player,
        SpatialChunkKey presentationCenter,
        Span<ClientPresentationChange> changes,
        out ClientPresentationWindowPlan plan,
        out ClientPresentationWindowTransition transition,
        out int required,
        out ClientPresentationWindowError error)
    {
        EnsureOwner();
        if (_pendingPlanToken != 0)
        {
            plan = default;
            transition = default;
            required = 0;
            error = ClientPresentationWindowError.PlanPending;
            return false;
        }

        SpatialChunkKey offsetOrigin = player.ToChunkKey(_space);
        if (!presentationCenter.Space.IsValid || presentationCenter.Space != _space ||
            !offsetOrigin.Space.IsValid || offsetOrigin.Space != _space)
        {
            plan = default;
            transition = default;
            required = 0;
            error = ClientPresentationWindowError.InvalidPlayerPosition;
            return false;
        }
        if (_activeCount != 0 && presentationCenter == _center && offsetOrigin == _offsetOrigin)
        {
            transition = new ClientPresentationWindowTransition(
                presentationCenter, _activeCount, 0, _activeCount, 0, 0);
            required = 0;
            plan = BeginPlan(
                PlanKind.Track, presentationCenter, offsetOrigin, _activeCount, transition,
                windowChanged: false, sameCenter: true);
            error = ClientPresentationWindowError.None;
            return true;
        }

        int candidateCount = FillCandidate(presentationCenter, offsetOrigin, out error);
        if (candidateCount < 0)
        {
            plan = default;
            transition = default;
            required = 0;
            return false;
        }

        int changeCount = BuildChanges(candidateCount,
            out int entered, out int retained, out int moved, out int exited);
        required = changeCount;
        transition = new ClientPresentationWindowTransition(
            presentationCenter, candidateCount, entered, retained, moved, exited);
        if (changes.Length < changeCount)
        {
            plan = default;
            error = ClientPresentationWindowError.DestinationTooSmall;
            return false;
        }

        _changeScratch.AsSpan(0, changeCount).CopyTo(changes);
        plan = BeginPlan(
            PlanKind.Track, presentationCenter, offsetOrigin, candidateCount, transition,
            windowChanged: _activeCount == 0 || _center != presentationCenter ||
                _offsetOrigin != offsetOrigin,
            sameCenter: false);
        error = ClientPresentationWindowError.None;
        return true;
    }

    /// <summary>
    /// Plans immediate retirement of the committed desired window. The caller may then reuse
    /// backend capacity and semantic residency for a distant target without two-window capacity.
    /// </summary>
    public bool TryPlanReset(
        in WorldPosition player,
        Span<ClientPresentationChange> changes,
        out ClientPresentationWindowPlan plan,
        out ClientPresentationWindowTransition transition,
        out int required,
        out ClientPresentationWindowError error)
    {
        EnsureOwner();
        if (_pendingPlanToken != 0)
        {
            plan = default;
            transition = default;
            required = 0;
            error = ClientPresentationWindowError.PlanPending;
            return false;
        }

        SpatialChunkKey center = player.ToChunkKey(_space);
        required = _activeCount;
        transition = new ClientPresentationWindowTransition(
            center, 0, 0, 0, 0, _activeCount);
        if (changes.Length < required)
        {
            plan = default;
            error = ClientPresentationWindowError.DestinationTooSmall;
            return false;
        }

        for (int index = 0; index < _activeCount; index++)
        {
            WindowEntry entry = _active[index];
            changes[index] = new ClientPresentationChange(
                ClientPresentationChangeKind.Exit,
                entry.Key,
                entry.Handle,
                entry.Offset,
                default);
        }
        plan = BeginPlan(
            PlanKind.Reset, center, center, 0, transition,
            windowChanged: true, sameCenter: false);
        error = ClientPresentationWindowError.None;
        return true;
    }

    /// <summary>Publishes a backend-admitted plan as the new desired presentation window.</summary>
    public void Commit(in ClientPresentationWindowPlan plan)
    {
        EnsureOwner();
        ValidatePending(plan);
        if (_pendingKind == PlanKind.Track)
        {
            if (!_pendingSameCenter)
            {
                int previousCount = _activeCount;
                _candidate.AsSpan(0, _pendingCandidateCount).CopyTo(_active);
                if (previousCount > _pendingCandidateCount)
                    _active.AsSpan(_pendingCandidateCount, previousCount - _pendingCandidateCount)
                        .Clear();
                _activeCount = _pendingCandidateCount;
                _center = _pendingTransition.CenterChunk;
                _offsetOrigin = _pendingOffsetOrigin;
            }
            if (_pendingSameCenter) _sameCenterFastPaths++;
            if (_pendingWindowChanged)
            {
                _transitions++;
                _entered += checked((ulong)_pendingTransition.Entered);
                _retained += checked((ulong)_pendingTransition.Retained);
                _exited += checked((ulong)_pendingTransition.Exited);
            }
        }
        else
        {
            _active.AsSpan(0, _activeCount).Clear();
            _activeCount = 0;
            _center = _pendingTransition.CenterChunk;
            _offsetOrigin = _pendingOffsetOrigin;
            _transitions++;
            _resets++;
            _exited += checked((ulong)_pendingTransition.Exited);
        }
        ClearPending();
    }

    /// <summary>Abandons a backend-rejected plan without changing desired presentation state.</summary>
    public void Cancel(in ClientPresentationWindowPlan plan)
    {
        EnsureOwner();
        ValidatePending(plan);
        ClearPending();
    }

    public bool TryCopyBindings(
        Span<ClientPresentationBinding> destination,
        out int count,
        out int required)
    {
        EnsureOwner();
        required = _activeCount;
        if (destination.Length < required)
        {
            count = 0;
            return false;
        }
        for (int index = 0; index < _activeCount; index++)
        {
            WindowEntry entry = _active[index];
            destination[index] = new ClientPresentationBinding(entry.Key, entry.Handle, entry.Offset);
        }
        count = required;
        return true;
    }

    public ClientPresentationWindowSnapshot GetSnapshot()
    {
        EnsureOwner();
        long reserved = checked(
            (long)(_active.Length + _candidate.Length) * Marshal.SizeOf<WindowEntry>() +
            (long)_changeScratch.Length * Marshal.SizeOf<ClientPresentationChange>());
        return new ClientPresentationWindowSnapshot(
            _center, _radius, _activeCount, _pendingPlanToken != 0,
            _transitions, _entered, _retained, _exited, _resets,
            _sameCenterFastPaths, _candidateKeyLookups, _candidateComparisons, reserved);
    }

    private ClientPresentationWindowPlan BeginPlan(
        PlanKind kind,
        SpatialChunkKey center,
        SpatialChunkKey offsetOrigin,
        int candidateCount,
        ClientPresentationWindowTransition transition,
        bool windowChanged,
        bool sameCenter)
    {
        if (_nextPlanToken == ulong.MaxValue)
            throw new InvalidOperationException("Presentation plan identity exhausted.");
        ulong token = _nextPlanToken++;
        _pendingPlanToken = token;
        _pendingKind = kind;
        _pendingCandidateCount = candidateCount;
        _pendingOffsetOrigin = offsetOrigin;
        _pendingTransition = transition with { CenterChunk = center };
        _pendingWindowChanged = windowChanged;
        _pendingSameCenter = sameCenter;
        return new ClientPresentationWindowPlan(_windowIdentity, token);
    }

    private void ValidatePending(in ClientPresentationWindowPlan plan)
    {
        if (!plan.IsValid || plan.WindowIdentity != _windowIdentity ||
            _pendingPlanToken == 0 || plan.Token != _pendingPlanToken)
            throw new InvalidOperationException("The presentation-window plan is stale or foreign.");
    }

    private void ClearPending()
    {
        _pendingPlanToken = 0;
        _pendingKind = default;
        _pendingCandidateCount = 0;
        _pendingOffsetOrigin = default;
        _pendingTransition = default;
        _pendingWindowChanged = false;
        _pendingSameCenter = false;
    }

    private int FillCandidate(
        SpatialChunkKey center,
        SpatialChunkKey offsetOrigin,
        out ClientPresentationWindowError error)
    {
        if (!center.Space.IsValid || center.Space != _space)
        {
            error = ClientPresentationWindowError.InvalidPlayerPosition;
            return -1;
        }
        long minimumX = Math.Max(int.MinValue, (long)center.Coord.X - _radius);
        long maximumX = Math.Min(int.MaxValue, (long)center.Coord.X + _radius);
        long minimumZ = Math.Max(int.MinValue, (long)center.Coord.Z - _radius);
        long maximumZ = Math.Min(int.MaxValue, (long)center.Coord.Z + _radius);
        int count = 0;
        for (long x = minimumX; x <= maximumX; x++)
        {
            for (long z = minimumZ; z <= maximumZ; z++)
            {
                var key = new SpatialChunkKey(_space, checked((int)x), checked((int)z));
                _candidateKeyLookups++;
                if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle))
                {
                    error = ClientPresentationWindowError.MissingSemanticChunk;
                    return -1;
                }
                _candidate[count++] = new WindowEntry(
                    key,
                    handle,
                    new ClientPresentationOffset(
                        checked((int)(x - offsetOrigin.Coord.X)),
                        checked((int)(z - offsetOrigin.Coord.Z))));
            }
        }
        error = ClientPresentationWindowError.None;
        return count;
    }

    private int BuildChanges(
        int candidateCount,
        out int entered,
        out int retained,
        out int moved,
        out int exited)
    {
        int enteredCount = 0;
        int retainedCount = 0;
        int movedCount = 0;
        int exitedCount = 0;
        int oldIndex = 0;
        int newIndex = 0;
        int changeCount = 0;
        while (oldIndex < _activeCount || newIndex < candidateCount)
        {
            _candidateComparisons++;
            if (oldIndex == _activeCount)
            {
                AddEntered(_candidate[newIndex++]);
                continue;
            }
            if (newIndex == candidateCount)
            {
                AddExited(_active[oldIndex++]);
                continue;
            }
            WindowEntry oldEntry = _active[oldIndex];
            WindowEntry newEntry = _candidate[newIndex];
            int comparison = oldEntry.Key.CompareTo(newEntry.Key);
            if (comparison < 0)
            {
                AddExited(oldEntry);
                oldIndex++;
            }
            else if (comparison > 0)
            {
                AddEntered(newEntry);
                newIndex++;
            }
            else if (oldEntry.Handle != newEntry.Handle)
            {
                AddExited(oldEntry);
                AddEntered(newEntry);
                oldIndex++;
                newIndex++;
            }
            else
            {
                retainedCount++;
                if (oldEntry.Offset != newEntry.Offset)
                {
                    _changeScratch[changeCount++] = new ClientPresentationChange(
                        ClientPresentationChangeKind.Move,
                        newEntry.Key,
                        newEntry.Handle,
                        oldEntry.Offset,
                        newEntry.Offset);
                    movedCount++;
                }
                oldIndex++;
                newIndex++;
            }
        }
        entered = enteredCount;
        retained = retainedCount;
        moved = movedCount;
        exited = exitedCount;
        return changeCount;

        void AddEntered(WindowEntry entry)
        {
            _changeScratch[changeCount++] = new ClientPresentationChange(
                ClientPresentationChangeKind.Enter,
                entry.Key,
                entry.Handle,
                default,
                entry.Offset);
            enteredCount++;
        }

        void AddExited(WindowEntry entry)
        {
            _changeScratch[changeCount++] = new ClientPresentationChange(
                ClientPresentationChangeKind.Exit,
                entry.Key,
                entry.Handle,
                entry.Offset,
                default);
            exitedCount++;
        }
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The presentation window is owner-thread bound.");
    }

    private enum PlanKind : byte
    {
        Track = 1,
        Reset = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct WindowEntry(
        SpatialChunkKey Key,
        ClientSemanticChunkHandle Handle,
        ClientPresentationOffset Offset);
}
