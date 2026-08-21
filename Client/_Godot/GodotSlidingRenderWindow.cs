using System.Runtime.InteropServices;
using UltraSim.Client.Core;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Godot;

/// <summary>Transient signed presentation coordinate relative to the player's home chunk.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly record struct RenderWindowOffset(int X, int Z);

public enum RenderWindowChangeKind : byte
{
    Entered = 1,
    RetainedMoved = 2,
    Exited = 3,
}

/// <summary>One deterministic presentation-membership change; no field is persistent identity.</summary>
public readonly record struct RenderWindowChange(
    RenderWindowChangeKind Kind,
    SpatialChunkKey Key,
    ClientSemanticChunkHandle Handle,
    RenderWindowOffset PreviousOffset,
    RenderWindowOffset CurrentOffset);

public enum SlidingRenderWindowError : byte
{
    None = 0,
    InvalidPlayerPosition,
    MissingSemanticChunk,
    DestinationTooSmall,
    RendererRejected,
}

public readonly record struct SlidingRenderWindowTransition(
    SpatialChunkKey CenterChunk,
    int ActiveChunks,
    int Entered,
    int Retained,
    int RetainedMoved,
    int Exited);

public readonly record struct SlidingRenderWindowSnapshot(
    SpatialChunkKey CenterChunk,
    int RenderRadiusChunks,
    int ActiveChunks,
    ulong Transitions,
    ulong EnteredChunks,
    ulong RetainedChunks,
    ulong ExitedChunks,
    ulong ResetTransitions,
    long ReservedBytes);

public readonly record struct RenderWindowBinding(
    SpatialChunkKey Key,
    ClientSemanticChunkHandle SemanticHandle,
    RenderWindowOffset Offset);

/// <summary>
/// Engine-thread, X/Z-only presentation window. It maps absolute world keys to transient relative
/// offsets and delegates private resource-slot ownership to <see cref="GodotWorldRenderer"/>.
/// Semantic chunks must already be resident; acquisition/freshness remains an upstream client
/// streaming concern rather than presentation policy.
/// </summary>
public sealed class GodotSlidingRenderWindow
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly ClientWorldSemanticCache _cache;
    private readonly GodotWorldRenderer _renderer;
    private readonly SpatialSpaceId _space;
    private readonly int _radius;
    private readonly WindowEntry[] _active;
    private readonly WindowEntry[] _candidate;
    private readonly RenderWindowChange[] _changeScratch;
    private int _activeCount;
    private SpatialChunkKey _center;
    private ulong _transitions;
    private ulong _entered;
    private ulong _retained;
    private ulong _exited;
    private ulong _resets;

    public GodotSlidingRenderWindow(
        ClientWorldSemanticCache cache,
        GodotWorldRenderer renderer,
        int renderRadiusChunks)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        ArgumentOutOfRangeException.ThrowIfNegative(renderRadiusChunks);
        int diameter = checked(renderRadiusChunks * 2 + 1);
        int maximumActive = checked(diameter * diameter);
        if (maximumActive > renderer.Limits.MaximumRenderChunks)
            throw new ArgumentOutOfRangeException(nameof(renderRadiusChunks),
                "The complete render window must fit within concurrent renderer capacity.");
        if (maximumActive > cache.Limits.MaximumResidentChunks)
            throw new ArgumentOutOfRangeException(nameof(renderRadiusChunks),
                "The complete render window must fit within semantic resident capacity.");
        _space = cache.BaseWorld.Space;
        _radius = renderRadiusChunks;
        _active = new WindowEntry[maximumActive];
        _candidate = new WindowEntry[maximumActive];
        _changeScratch = new RenderWindowChange[checked(maximumActive * 2)];
    }

    public int RenderRadiusChunks => _radius;
    public int MaximumActiveChunks => _active.Length;

    /// <summary>Computes exact player-relative X/Z before any presentation float conversion.</summary>
    public static void GetPlayerRelativeMillimeters(
        in WorldPosition subject,
        in WorldPosition player,
        out long xMillimeters,
        out long zMillimeters)
    {
        xMillimeters = checked(
            checked((long)subject.Chunk.X - player.Chunk.X) *
            GameWorldSpatialProfile.WorldChunkEdgeUnits +
            subject.Local.XMillimeters - player.Local.XMillimeters);
        zMillimeters = checked(
            checked((long)subject.Chunk.Z - player.Chunk.Z) *
            GameWorldSpatialProfile.WorldChunkEdgeUnits +
            subject.Local.ZMillimeters - player.Local.ZMillimeters);
    }

    /// <summary>
    /// Tracks the canonical player position. A chunk crossing immediately changes the discrete
    /// window; local X/Z movement changes one common presentation root only.
    /// </summary>
    public bool TryTrackPlayer(
        in WorldPosition player,
        Span<RenderWindowChange> changes,
        out SlidingRenderWindowTransition transition,
        out int required,
        out SlidingRenderWindowError error,
        out GodotWorldRenderQueueError rendererError)
    {
        EnsureOwner();
        SpatialChunkKey center = player.ToChunkKey(_space);
        int candidateCount = FillCandidate(center, out error);
        if (candidateCount < 0)
        {
            transition = default;
            required = 0;
            rendererError = GodotWorldRenderQueueError.None;
            return false;
        }

        int entered = 0;
        int retained = 0;
        int retainedMoved = 0;
        int exited = 0;
        int changeCount = BuildChanges(candidateCount,
            out entered, out retained, out retainedMoved, out exited);
        required = changeCount;
        transition = new SlidingRenderWindowTransition(
            center, candidateCount, entered, retained, retainedMoved, exited);
        if (changes.Length < changeCount)
        {
            error = SlidingRenderWindowError.DestinationTooSmall;
            rendererError = GodotWorldRenderQueueError.None;
            return false;
        }
        if (changeCount != 0 &&
            !_renderer.TryQueueWindowTransition(
                _changeScratch.AsSpan(0, changeCount), hideExits: false, out rendererError))
        {
            error = SlidingRenderWindowError.RendererRejected;
            return false;
        }
        rendererError = GodotWorldRenderQueueError.None;

        bool windowChanged = _activeCount == 0 || _center != center;
        _candidate.AsSpan(0, candidateCount).CopyTo(_active);
        _active.AsSpan(candidateCount, _activeCount - candidateCount > 0
            ? _activeCount - candidateCount
            : 0).Clear();
        _activeCount = candidateCount;
        _center = center;
        _changeScratch.AsSpan(0, changeCount).CopyTo(changes);
        _renderer.SetRenderOriginFromWindow(new WorldRenderOrigin(
            player.Chunk,
            player.YMillimeters,
            player.Local.XMillimeters,
            player.Local.ZMillimeters));
        if (windowChanged)
        {
            _transitions++;
            _entered += checked((ulong)entered);
            _retained += checked((ulong)retained);
            _exited += checked((ulong)exited);
        }
        error = SlidingRenderWindowError.None;
        return true;
    }

    /// <summary>
    /// Immediately advances the desired center while retiring the old desired window. This is the
    /// bounded fast-travel path: callers may release retired semantic pages, prepare the newest
    /// target, and then call <see cref="TryTrackPlayer"/> without reserving two complete windows.
    /// Pending presentation work is coalesced away and no obsolete entering chunk may be built.
    /// </summary>
    public bool TryResetForJump(
        in WorldPosition player,
        Span<RenderWindowChange> changes,
        out SlidingRenderWindowTransition transition,
        out int required,
        out SlidingRenderWindowError error,
        out GodotWorldRenderQueueError rendererError)
    {
        EnsureOwner();
        SpatialChunkKey center = player.ToChunkKey(_space);
        required = _activeCount;
        transition = new SlidingRenderWindowTransition(
            center, 0, 0, 0, 0, _activeCount);
        if (changes.Length < required)
        {
            error = SlidingRenderWindowError.DestinationTooSmall;
            rendererError = GodotWorldRenderQueueError.None;
            return false;
        }

        for (int index = 0; index < _activeCount; index++)
        {
            WindowEntry entry = _active[index];
            _changeScratch[index] = new RenderWindowChange(
                RenderWindowChangeKind.Exited,
                entry.Key,
                entry.Handle,
                entry.Offset,
                default);
        }
        if (required != 0 && !_renderer.TryQueueWindowTransition(
            _changeScratch.AsSpan(0, required), hideExits: true, out rendererError))
        {
            error = SlidingRenderWindowError.RendererRejected;
            return false;
        }

        _changeScratch.AsSpan(0, required).CopyTo(changes);
        _active.AsSpan(0, _activeCount).Clear();
        _activeCount = 0;
        _center = center;
        _renderer.SetRenderOriginFromWindow(new WorldRenderOrigin(
            player.Chunk,
            player.YMillimeters,
            player.Local.XMillimeters,
            player.Local.ZMillimeters), rebasePendingRetirements: false);
        _transitions++;
        _resets++;
        _exited += checked((ulong)required);
        rendererError = GodotWorldRenderQueueError.None;
        error = SlidingRenderWindowError.None;
        return true;
    }

    public bool TryCopyBindings(
        Span<RenderWindowBinding> destination,
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
            destination[index] = new RenderWindowBinding(entry.Key, entry.Handle, entry.Offset);
        }
        count = required;
        return true;
    }

    public SlidingRenderWindowSnapshot GetSnapshot()
    {
        EnsureOwner();
        long reserved = checked(
            (long)(_active.Length + _candidate.Length) * Marshal.SizeOf<WindowEntry>() +
            (long)_changeScratch.Length * Marshal.SizeOf<RenderWindowChange>());
        return new SlidingRenderWindowSnapshot(
            _center, _radius, _activeCount, _transitions,
            _entered, _retained, _exited, _resets, reserved);
    }

    private int FillCandidate(SpatialChunkKey center, out SlidingRenderWindowError error)
    {
        if (!center.Space.IsValid || center.Space != _space)
        {
            error = SlidingRenderWindowError.InvalidPlayerPosition;
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
                if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle))
                {
                    error = SlidingRenderWindowError.MissingSemanticChunk;
                    return -1;
                }
                _candidate[count++] = new WindowEntry(
                    key,
                    handle,
                    new RenderWindowOffset(
                        checked((int)(x - center.Coord.X)),
                        checked((int)(z - center.Coord.Z))));
            }
        }
        error = SlidingRenderWindowError.None;
        return count;
    }

    private int BuildChanges(
        int candidateCount,
        out int entered,
        out int retained,
        out int retainedMoved,
        out int exited)
    {
        int enteredCount = 0;
        int retainedCount = 0;
        int retainedMovedCount = 0;
        int exitedCount = 0;
        int oldIndex = 0;
        int newIndex = 0;
        int changeCount = 0;
        while (oldIndex < _activeCount || newIndex < candidateCount)
        {
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
                    _changeScratch[changeCount++] = new RenderWindowChange(
                        RenderWindowChangeKind.RetainedMoved,
                        newEntry.Key,
                        newEntry.Handle,
                        oldEntry.Offset,
                        newEntry.Offset);
                    retainedMovedCount++;
                }
                oldIndex++;
                newIndex++;
            }
        }
        entered = enteredCount;
        retained = retainedCount;
        retainedMoved = retainedMovedCount;
        exited = exitedCount;
        return changeCount;

        void AddEntered(WindowEntry entry)
        {
            _changeScratch[changeCount++] = new RenderWindowChange(
                RenderWindowChangeKind.Entered,
                entry.Key,
                entry.Handle,
                default,
                entry.Offset);
            enteredCount++;
        }

        void AddExited(WindowEntry entry)
        {
            _changeScratch[changeCount++] = new RenderWindowChange(
                RenderWindowChangeKind.Exited,
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
            throw new InvalidOperationException("The sliding render window is engine-thread bound.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct WindowEntry(
        SpatialChunkKey Key,
        ClientSemanticChunkHandle Handle,
        RenderWindowOffset Offset);
}
