using UltraSim.Client.Core;
using UltraSim.Game.World;
using UltraSim.Spatial;

internal static class ClientPresentationWindowTests
{
    internal static void Run()
    {
        BoundWorldPack world = ClientWorldSemanticCacheTests.CreateWorld();
        var cache = new ClientWorldSemanticCache(world, new(25, 1, 1, 1, 1));
        for (int x = -2; x <= 2; x++)
        {
            for (int z = -2; z <= 2; z++)
            {
                SpatialChunkKey key = ClientWorldSemanticCacheTests.Key(x, z);
                True(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
                True(cache.TryStageFromBaseWorld(handle, [], default, out _));
                True(cache.TryPublish(handle, out _));
            }
        }

        var window = new ClientPresentationWindow(cache, presentationRadiusChunks: 1);
        var backend = new RecordingPresentationBackend(maximumDesired: 9);
        var changes = new ClientPresentationChange[18];
        var bindings = new ClientPresentationBinding[9];

        WorldPosition origin = Position(0, 0, 100, 200);
        True(window.TryPlanTrack(origin, changes,
            out ClientPresentationWindowPlan rejectedPlan,
            out ClientPresentationWindowTransition initial,
            out int initialRequired,
            out ClientPresentationWindowError error));
        Equal(9, initial.Entered);
        Equal(9, initialRequired);
        False(window.TryPlanTrack(origin, changes, out _, out _, out _, out error));
        Equal(ClientPresentationWindowError.PlanPending, error);
        var foreignWindow = new ClientPresentationWindow(cache, presentationRadiusChunks: 1);
        var foreignChanges = new ClientPresentationChange[18];
        True(foreignWindow.TryPlanTrack(origin, foreignChanges,
            out ClientPresentationWindowPlan foreignPlan, out _, out _, out _));
        Throws<InvalidOperationException>(() => foreignWindow.Commit(rejectedPlan));
        True(foreignWindow.GetSnapshot().PlanPending);
        foreignWindow.Cancel(foreignPlan);
        backend.RejectNext = true;
        False(backend.TryAccept(changes.AsSpan(0, initialRequired)));
        window.Cancel(rejectedPlan);
        Equal(0, window.GetSnapshot().ActiveChunks);
        False(window.GetSnapshot().PlanPending);
        Throws<InvalidOperationException>(() => window.Commit(rejectedPlan));

        Track(origin);
        ClientPresentationWindowSnapshot beforeLocal = window.GetSnapshot();
        Track(Position(0, 0, 15_000, 15_000));
        ClientPresentationWindowSnapshot afterLocal = window.GetSnapshot();
        Equal(beforeLocal.ActiveChunks, afterLocal.ActiveChunks);
        Equal(beforeLocal.CandidateKeyLookups, afterLocal.CandidateKeyLookups);
        Equal(beforeLocal.CandidateComparisons, afterLocal.CandidateComparisons);
        Equal(beforeLocal.SameCenterFastPaths + 1, afterLocal.SameCenterFastPaths);

        ClientPresentationWindowTransition cardinal = Track(Position(1, 0));
        Equal(3, cardinal.Entered);
        Equal(6, cardinal.Retained);
        Equal(6, cardinal.Moved);
        Equal(3, cardinal.Exited);

        ClientPresentationWindowTransition diagonal = Track(Position(0, 1));
        Equal(5, diagonal.Entered);
        Equal(4, diagonal.Retained);
        Equal(4, diagonal.Moved);
        Equal(5, diagonal.Exited);

        // The recording backend deliberately retains accepted batches as unrealized work. A
        // second crossing and reversal therefore prove that Core plans against latest desired
        // state rather than an engine's native realization progress.
        _ = Track(Position(-1, 1));
        _ = Track(Position(0, 1));
        True(backend.PendingAcceptedBatches >= 5);

        True(window.TryPlanReset(Position(-1, -1), changes,
            out ClientPresentationWindowPlan resetPlan,
            out ClientPresentationWindowTransition reset,
            out int resetRequired,
            out error));
        Equal(9, reset.Exited);
        Equal(9, resetRequired);
        True(backend.TryAccept(changes.AsSpan(0, resetRequired)));
        window.Commit(resetPlan);
        Equal(0, window.GetSnapshot().ActiveChunks);
        Equal(0, backend.DesiredCount);

        Span<ClientPresentationChange> tooSmall = [];
        False(window.TryPlanTrack(Position(-1, -1), tooSmall,
            out _, out _, out int required, out error));
        Equal(9, required);
        Equal(ClientPresentationWindowError.DestinationTooSmall, error);
        False(window.GetSnapshot().PlanPending);

        _ = Track(Position(-1, -1));
        Equal(9, backend.MaximumDesiredObserved);
        AssertEquivalent();

        // A finite authored world may clamp structural membership inward while the player and
        // camera continue through the boundary chunk. Membership stays centered on (1,1), but
        // every presentation offset remains relative to the actual player home at (2,2).
        SpatialChunkKey edgeCenter = ClientWorldSemanticCacheTests.Key(1, 1);
        WorldPosition edgePlayer = Position(2, 2, 15_000, 14_000);
        True(window.TryPlanTrack(edgePlayer, edgeCenter, changes,
            out ClientPresentationWindowPlan edgePlan,
            out ClientPresentationWindowTransition edgeTransition,
            out int edgeRequired,
            out error));
        Equal(edgeCenter, edgeTransition.CenterChunk);
        True(backend.TryAccept(changes.AsSpan(0, edgeRequired)));
        window.Commit(edgePlan);
        True(window.TryCopyBindings(bindings, out int edgeCount, out _));
        Equal(9, edgeCount);
        ClientPresentationBinding playerHome = bindings.Single(
            binding => binding.Key == ClientWorldSemanticCacheTests.Key(2, 2));
        ClientPresentationBinding inwardCorner = bindings.Single(
            binding => binding.Key == ClientWorldSemanticCacheTests.Key(0, 0));
        Equal(new ClientPresentationOffset(0, 0), playerHome.Offset);
        Equal(new ClientPresentationOffset(-2, -2), inwardCorner.Offset);
        ClientPresentationWindowSnapshot beforeEdgeLocal = window.GetSnapshot();
        TrackCentered(Position(2, 2, 15_500, 14_500), edgeCenter);
        ClientPresentationWindowSnapshot afterEdgeLocal = window.GetSnapshot();
        Equal(beforeEdgeLocal.CandidateKeyLookups, afterEdgeLocal.CandidateKeyLookups);
        Equal(beforeEdgeLocal.SameCenterFastPaths + 1, afterEdgeLocal.SameCenterFastPaths);

        for (int warm = 0; warm < 8; warm++)
            PlanAndCommit((warm & 1) == 0 ? Position(0, -1) : Position(-1, -1));
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
            PlanAndCommit((iteration & 1) == 0 ? Position(0, -1) : Position(-1, -1));
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);

        static WorldPosition Position(
            int chunkX,
            int chunkZ,
            ushort localX = 0,
            ushort localZ = 0) => new(chunkX, chunkZ, localX, localZ, 0);

        ClientPresentationWindowTransition Track(WorldPosition player)
            => TrackCentered(player, player.ToChunkKey(cache.BaseWorld.Space));

        ClientPresentationWindowTransition TrackCentered(
            WorldPosition player,
            SpatialChunkKey presentationCenter)
        {
            True(window.TryPlanTrack(player, presentationCenter, changes,
                out ClientPresentationWindowPlan plan,
                out ClientPresentationWindowTransition transition,
                out int required,
                out ClientPresentationWindowError planError));
            Equal(ClientPresentationWindowError.None, planError);
            if (!backend.TryAccept(changes.AsSpan(0, required)))
            {
                window.Cancel(plan);
                throw new InvalidOperationException("The recording backend unexpectedly rejected.");
            }
            window.Commit(plan);
            AssertEquivalent();
            return transition;
        }

        void PlanAndCommit(WorldPosition player)
        {
            if (!window.TryPlanTrack(player, changes,
                    out ClientPresentationWindowPlan plan,
                    out _, out _, out _))
                throw new InvalidOperationException("Warmed presentation planning failed.");
            window.Commit(plan);
        }

        void AssertEquivalent()
        {
            True(window.TryCopyBindings(bindings, out int count, out int required));
            Equal(count, required);
            Equal(backend.DesiredCount, count);
            for (int index = 0; index < count; index++)
                True(backend.Contains(bindings[index]));
        }
    }

    private sealed class RecordingPresentationBackend(int maximumDesired)
    {
        private readonly Dictionary<SpatialChunkKey, ClientPresentationBinding> _desired = [];

        internal bool RejectNext { get; set; }
        internal int PendingAcceptedBatches { get; private set; }
        internal int MaximumDesiredObserved { get; private set; }
        internal int DesiredCount => _desired.Count;

        internal bool TryAccept(ReadOnlySpan<ClientPresentationChange> changes)
        {
            if (RejectNext)
            {
                RejectNext = false;
                return false;
            }

            var candidate = new Dictionary<SpatialChunkKey, ClientPresentationBinding>(_desired);
            for (int index = 0; index < changes.Length; index++)
            {
                ClientPresentationChange change = changes[index];
                if (change.Kind != ClientPresentationChangeKind.Exit) continue;
                if (!candidate.TryGetValue(change.Key, out ClientPresentationBinding current) ||
                    current.SemanticHandle != change.Handle ||
                    current.Offset != change.PreviousOffset ||
                    !candidate.Remove(change.Key)) return false;
            }
            for (int index = 0; index < changes.Length; index++)
            {
                ClientPresentationChange change = changes[index];
                if (change.Kind != ClientPresentationChangeKind.Move) continue;
                if (!candidate.TryGetValue(change.Key, out ClientPresentationBinding current) ||
                    current.SemanticHandle != change.Handle ||
                    current.Offset != change.PreviousOffset) return false;
                candidate[change.Key] = current with { Offset = change.CurrentOffset };
            }
            for (int index = 0; index < changes.Length; index++)
            {
                ClientPresentationChange change = changes[index];
                if (change.Kind != ClientPresentationChangeKind.Enter) continue;
                if (!candidate.TryAdd(change.Key, new ClientPresentationBinding(
                        change.Key, change.Handle, change.CurrentOffset))) return false;
            }
            if (candidate.Count > maximumDesired) return false;

            _desired.Clear();
            foreach ((SpatialChunkKey key, ClientPresentationBinding binding) in candidate)
                _desired.Add(key, binding);
            PendingAcceptedBatches++;
            MaximumDesiredObserved = Math.Max(MaximumDesiredObserved, _desired.Count);
            return true;
        }

        internal bool Contains(ClientPresentationBinding binding) =>
            _desired.TryGetValue(binding.Key, out ClientPresentationBinding current) &&
            current == binding;
    }

    private static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }

    private static void False(bool condition)
    {
        if (condition) throw new InvalidOperationException("Expected false.");
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
