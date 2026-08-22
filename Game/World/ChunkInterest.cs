using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>Relative query cadence for one canonical public-world chunk.</summary>
public enum ChunkInterestTier : byte
{
    Immediate = 1,
    Near = 2,
    Far = 3,
}

public enum ChunkInterestQueryStatus : byte
{
    Success = 1,
    DestinationTooSmall = 2,
    InvalidInput = 3,
}

/// <summary>One unique canonical chunk selected at its nearest retained tier.</summary>
[StructLayout(LayoutKind.Sequential, Size = 24)]
public readonly record struct ChunkInterestEntry(SpatialChunkKey Key, ChunkInterestTier Tier)
{
    public bool IsValid => Key.Space.IsValid && Tier is >= ChunkInterestTier.Immediate and <= ChunkInterestTier.Far;
}

/// <summary>
/// Exact circular X/Z chunk-interest radii. Enter radii admit new/promoted tiers; leave radii
/// retain an existing tier through small boundary oscillation.
/// </summary>
public readonly record struct ChunkInterestProfile(
    int ImmediateEnterRadiusMillimeters,
    int ImmediateLeaveRadiusMillimeters,
    int NearEnterRadiusMillimeters,
    int NearLeaveRadiusMillimeters,
    int FarEnterRadiusMillimeters,
    int FarLeaveRadiusMillimeters)
{
    public bool IsValid =>
        ImmediateEnterRadiusMillimeters >= 0 &&
        ImmediateLeaveRadiusMillimeters >= ImmediateEnterRadiusMillimeters &&
        NearEnterRadiusMillimeters >= ImmediateLeaveRadiusMillimeters &&
        NearLeaveRadiusMillimeters >= NearEnterRadiusMillimeters &&
        FarEnterRadiusMillimeters >= NearLeaveRadiusMillimeters &&
        FarLeaveRadiusMillimeters >= FarEnterRadiusMillimeters;
}

/// <summary>Frozen first-game chunk-interest policy; it is not a Spatial-kernel constant.</summary>
public static class GameChunkInterestProfile
{
    public const int ImmediateEnterRadiusMillimeters = 5_000;
    public const int ImmediateLeaveRadiusMillimeters = 5_500;
    public const int NearEnterRadiusMillimeters = 96_000;
    public const int NearLeaveRadiusMillimeters = 96_500;
    public const int FarEnterRadiusMillimeters = 192_000;
    public const int FarLeaveRadiusMillimeters = 192_500;

    public static ChunkInterestProfile Default { get; } = new(
        ImmediateEnterRadiusMillimeters,
        ImmediateLeaveRadiusMillimeters,
        NearEnterRadiusMillimeters,
        NearLeaveRadiusMillimeters,
        FarEnterRadiusMillimeters,
        FarLeaveRadiusMillimeters);
}

/// <summary>
/// Stateless exact chunk-overlap query. Previous entries supply consumer-owned hysteresis only;
/// neither the Spatial kernel nor publication chunks retain observer state.
/// </summary>
public static class ChunkInterestQuery
{
    // Prevent a malformed/custom profile from turning the complete count pass into an unbounded walk.
    // The first game profile examines fewer than 1,000 candidate chunks.
    public const int MaximumCandidateChunks = 4_096;

    public static ChunkInterestQueryStatus TryQuery(
        SpatialSpaceId space,
        in WorldPosition anchor,
        in ChunkInterestProfile profile,
        ReadOnlySpan<ChunkInterestEntry> previous,
        Span<ChunkInterestEntry> destination,
        out int count,
        out int required)
        => TryQuery(
            space, anchor, profile, previous, destination,
            out count, out required, out _);

    /// <summary>
    /// Executes the exact query and returns a conservative Manhattan movement budget around this
    /// anchor. Reusing the result while cumulative X/Z movement remains within that budget cannot
    /// cross any enter/leave threshold, so membership and tiers are provably unchanged.
    /// </summary>
    public static ChunkInterestQueryStatus TryQuery(
        SpatialSpaceId space,
        in WorldPosition anchor,
        in ChunkInterestProfile profile,
        ReadOnlySpan<ChunkInterestEntry> previous,
        Span<ChunkInterestEntry> destination,
        out int count,
        out int required,
        out int stableManhattanMovementBudgetMillimeters)
    {
        count = 0;
        required = 0;
        stableManhattanMovementBudgetMillimeters = 0;
        if (!space.IsValid || !profile.IsValid || !ValidatePrevious(space, previous) ||
            !TryGetCandidateRange(anchor, profile.FarLeaveRadiusMillimeters,
                out int minimumX, out int maximumX, out int minimumZ, out int maximumZ))
            return ChunkInterestQueryStatus.InvalidInput;

        long width = (long)maximumX - minimumX + 1;
        long depth = (long)maximumZ - minimumZ + 1;
        if (width * depth > MaximumCandidateChunks)
            return ChunkInterestQueryStatus.InvalidInput;

        long centerX = anchor.GlobalXMillimeters;
        long centerZ = anchor.GlobalZMillimeters;
        int stableBudget = Math.Max(
            0,
            profile.FarLeaveRadiusMillimeters - profile.FarEnterRadiusMillimeters - 1);
        int previousIndex = 0;
        for (int chunkX = minimumX; ; chunkX++)
        {
            for (int chunkZ = minimumZ; ; chunkZ++)
            {
                var key = new SpatialChunkKey(space, chunkX, chunkZ);
                while (previousIndex < previous.Length && previous[previousIndex].Key.CompareTo(key) < 0)
                    previousIndex++;
                ChunkInterestTier previousTier = previousIndex < previous.Length && previous[previousIndex].Key == key
                    ? previous[previousIndex].Tier
                    : default;
                Int128 distanceSquared = GameWorldSpatialProfile.ChunkGeometry
                    .GetBounds(key.Coord).SquaredDistanceTo(centerX, centerZ);
                stableBudget = TightenThresholdMovementBudget(
                    distanceSquared, previousTier, profile, stableBudget);
                ChunkInterestTier? tier = Classify(distanceSquared, previousTier, profile);
                if (tier.HasValue)
                {
                    if (required < destination.Length)
                        destination[required] = new ChunkInterestEntry(key, tier.Value);
                    required++;
                }
                if (chunkZ == maximumZ) break;
            }
            if (chunkX == maximumX) break;
        }

        if (destination.Length < required)
            return ChunkInterestQueryStatus.DestinationTooSmall;
        count = required;
        stableManhattanMovementBudgetMillimeters = stableBudget;
        return ChunkInterestQueryStatus.Success;
    }

    private static int TightenThresholdMovementBudget(
        Int128 distanceSquared,
        ChunkInterestTier previous,
        in ChunkInterestProfile profile,
        int currentBudget)
    {
        if (currentBudget == 0) return 0;
        long distanceFloor = -1;
        int budget = currentBudget;
        switch (previous)
        {
            case ChunkInterestTier.Immediate:
                Consider(profile.ImmediateLeaveRadiusMillimeters);
                Consider(profile.NearLeaveRadiusMillimeters);
                Consider(profile.FarLeaveRadiusMillimeters);
                break;
            case ChunkInterestTier.Near:
                Consider(profile.ImmediateEnterRadiusMillimeters);
                Consider(profile.NearLeaveRadiusMillimeters);
                Consider(profile.FarLeaveRadiusMillimeters);
                break;
            case ChunkInterestTier.Far:
                Consider(profile.ImmediateEnterRadiusMillimeters);
                Consider(profile.NearEnterRadiusMillimeters);
                Consider(profile.FarLeaveRadiusMillimeters);
                break;
            default:
                Consider(profile.ImmediateEnterRadiusMillimeters);
                Consider(profile.NearEnterRadiusMillimeters);
                Consider(profile.FarEnterRadiusMillimeters);
                break;
        }
        return budget;

        void Consider(int radius)
        {
            int reach = checked(budget + 1);
            long minimum = Math.Max(0L, (long)radius - reach);
            long maximum = (long)radius + reach;
            if (distanceSquared < (Int128)minimum * minimum ||
                distanceSquared > (Int128)maximum * maximum)
                return;
            if (distanceFloor < 0) distanceFloor = FloorSquareRoot(distanceSquared);
            long lowerBound = distanceFloor >= radius
                ? distanceFloor - radius
                : (long)radius - distanceFloor - 1;
            int thresholdBudget = lowerBound <= 0
                ? 0
                : checked((int)Math.Min(lowerBound - 1, int.MaxValue));
            budget = Math.Min(budget, thresholdBudget);
        }
    }

    private static long FloorSquareRoot(Int128 value)
    {
        if (value <= 0) return 0;
        long estimate = checked((long)Math.Sqrt((double)value));
        while ((Int128)(estimate + 1) * (estimate + 1) <= value) estimate++;
        while ((Int128)estimate * estimate > value) estimate--;
        return estimate;
    }

    private static bool ValidatePrevious(SpatialSpaceId space, ReadOnlySpan<ChunkInterestEntry> previous)
    {
        for (int index = 0; index < previous.Length; index++)
        {
            ChunkInterestEntry entry = previous[index];
            if (!entry.IsValid || entry.Key.Space != space ||
                index != 0 && previous[index - 1].Key.CompareTo(entry.Key) >= 0)
                return false;
        }
        return true;
    }

    private static bool TryGetCandidateRange(
        in WorldPosition anchor,
        int radius,
        out int minimumX,
        out int maximumX,
        out int minimumZ,
        out int maximumZ)
    {
        long centerX = anchor.GlobalXMillimeters;
        long centerZ = anchor.GlobalZMillimeters;
        SpatialMath.FloorDivRem(checked(centerX - radius), GameWorldSpatialProfile.WorldChunkEdgeUnits,
            out long wideMinimumX, out int minimumRemainderX);
        SpatialMath.FloorDivRem(checked(centerX + radius), GameWorldSpatialProfile.WorldChunkEdgeUnits,
            out long wideMaximumX, out _);
        SpatialMath.FloorDivRem(checked(centerZ - radius), GameWorldSpatialProfile.WorldChunkEdgeUnits,
            out long wideMinimumZ, out int minimumRemainderZ);
        SpatialMath.FloorDivRem(checked(centerZ + radius), GameWorldSpatialProfile.WorldChunkEdgeUnits,
            out long wideMaximumZ, out _);
        // Chunk maximum faces participate in geometric contact even though ownership is half-open.
        // If the circle's minimum lands exactly on a chunk boundary, include the chunk below it.
        if (minimumRemainderX == 0) wideMinimumX--;
        if (minimumRemainderZ == 0) wideMinimumZ--;
        minimumX = checked((int)Math.Max(wideMinimumX, int.MinValue));
        maximumX = checked((int)Math.Min(wideMaximumX, int.MaxValue));
        minimumZ = checked((int)Math.Max(wideMinimumZ, int.MinValue));
        maximumZ = checked((int)Math.Min(wideMaximumZ, int.MaxValue));
        return minimumX <= maximumX && minimumZ <= maximumZ;
    }

    private static ChunkInterestTier? Classify(
        Int128 distanceSquared,
        ChunkInterestTier previous,
        in ChunkInterestProfile profile)
    {
        bool Within(int radius)
        {
            Int128 wide = radius;
            return distanceSquared <= checked(wide * wide);
        }

        return previous switch
        {
            ChunkInterestTier.Immediate when Within(profile.ImmediateLeaveRadiusMillimeters) =>
                ChunkInterestTier.Immediate,
            ChunkInterestTier.Immediate when Within(profile.NearLeaveRadiusMillimeters) =>
                ChunkInterestTier.Near,
            ChunkInterestTier.Immediate when Within(profile.FarLeaveRadiusMillimeters) =>
                ChunkInterestTier.Far,
            ChunkInterestTier.Near when Within(profile.ImmediateEnterRadiusMillimeters) =>
                ChunkInterestTier.Immediate,
            ChunkInterestTier.Near when Within(profile.NearLeaveRadiusMillimeters) =>
                ChunkInterestTier.Near,
            ChunkInterestTier.Near when Within(profile.FarLeaveRadiusMillimeters) =>
                ChunkInterestTier.Far,
            ChunkInterestTier.Far when Within(profile.ImmediateEnterRadiusMillimeters) =>
                ChunkInterestTier.Immediate,
            ChunkInterestTier.Far when Within(profile.NearEnterRadiusMillimeters) =>
                ChunkInterestTier.Near,
            ChunkInterestTier.Far when Within(profile.FarLeaveRadiusMillimeters) =>
                ChunkInterestTier.Far,
            _ when Within(profile.ImmediateEnterRadiusMillimeters) => ChunkInterestTier.Immediate,
            _ when Within(profile.NearEnterRadiusMillimeters) => ChunkInterestTier.Near,
            _ when Within(profile.FarEnterRadiusMillimeters) => ChunkInterestTier.Far,
            _ => null,
        };
    }
}

/// <summary>Client-local generation-validated semantic chunk slot; never world or wire identity.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly record struct ClientSemanticChunkSlot(uint Index, uint Generation)
{
    public bool IsValid => Generation != 0;
}

public readonly record struct LocalChunkStreamingCadence(
    uint ImmediateSteps,
    uint NearSteps,
    uint FarSteps)
{
    public static LocalChunkStreamingCadence EvidenceProfile { get; } = new(1, 2, 4);
    public bool IsValid => ImmediateSteps > 0 && NearSteps >= ImmediateSteps && FarSteps >= NearSteps;

    internal uint Period(ChunkInterestTier tier) => tier switch
    {
        ChunkInterestTier.Immediate => ImmediateSteps,
        ChunkInterestTier.Near => NearSteps,
        ChunkInterestTier.Far => FarSteps,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}

public readonly record struct LocalChunkStreamingLimits(
    int MaximumSubscriptions,
    int MaximumPublicEntities)
{
    public bool IsValid => MaximumSubscriptions > 0 && MaximumPublicEntities > 0;
}

public enum LocalChunkStreamingStatus : byte
{
    Synchronized = 1,
    InvalidInterest = 2,
    SubscriptionCapacityExceeded = 3,
}

public readonly record struct LocalChunkSubscription(
    ChunkInterestEntry Interest,
    ChunkPublicationCursor Cursor,
    bool HasKnowledge,
    uint CadencePhase,
    ClientSemanticChunkSlot ResidentSlot);

public readonly record struct LocalChunkSemanticRecord(
    SpatialChunkKey Key,
    WorldPublicRecord Record);

public readonly record struct LocalChunkStreamingSnapshot(
    PublicationEpoch SourceEpoch,
    int SubscriptionCount,
    int ResidentRecordCount,
    ulong Synchronizations,
    ulong SnapshotApplications,
    ulong ChunkAbsentApplications,
    ulong DeltaApplications,
    ulong NoChangeQueries,
    ulong SnapshotFallbacks,
    ulong CadenceDeferrals,
    long ReservedBytes);

/// <summary>
/// Fixed-capacity OfflineDirect oracle for T0.7. It owns recipient subscriptions/cursors and a
/// local semantic projection; the authoritative publication store remains observer-independent.
/// </summary>
public sealed class LocalChunkStreamingOracle
{
    private readonly int _ownerThreadId;
    private readonly LocalChunkStreamingLimits _limits;
    private readonly ChunkInterestProfile _interestProfile;
    private readonly LocalChunkStreamingCadence _cadence;
    private LocalChunkSubscription[] _subscriptions;
    private LocalChunkSubscription[] _nextSubscriptions;
    private readonly ChunkInterestEntry[] _previousInterest;
    private readonly ChunkInterestEntry[] _desiredInterest;
    private readonly bool[] _retainedSlotScratch;
    private readonly uint[] _residentSlotGenerations;
    private readonly WorldPublicRecord[] _snapshotScratch;
    private readonly WorldPublicDeltaRecord[] _deltaScratch;
    private bool[] _present;
    private SpatialChunkKey[] _recordKeys;
    private WorldPublicRecord[] _records;
    private uint[] _floorGenerations;
    private ulong[] _floorVersions;
    private WorldPublicOperation[] _floorOperations;
    private bool[] _candidatePresent;
    private SpatialChunkKey[] _candidateRecordKeys;
    private WorldPublicRecord[] _candidateRecords;
    private uint[] _candidateFloorGenerations;
    private ulong[] _candidateFloorVersions;
    private WorldPublicOperation[] _candidateFloorOperations;
    private int _subscriptionCount;
    private int _residentRecordCount;
    private ulong _synchronizations;
    private ulong _snapshotApplications;
    private ulong _chunkAbsentApplications;
    private ulong _deltaApplications;
    private ulong _noChangeQueries;
    private ulong _snapshotFallbacks;
    private ulong _cadenceDeferrals;
    private PublicationEpoch _sourceEpoch;

    public LocalChunkStreamingOracle(
        LocalChunkStreamingLimits limits,
        ChunkInterestProfile interestProfile,
        LocalChunkStreamingCadence cadence)
    {
        if (!limits.IsValid) throw new ArgumentOutOfRangeException(nameof(limits));
        if (!interestProfile.IsValid) throw new ArgumentOutOfRangeException(nameof(interestProfile));
        if (!cadence.IsValid) throw new ArgumentOutOfRangeException(nameof(cadence));
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _limits = limits;
        _interestProfile = interestProfile;
        _cadence = cadence;
        _subscriptions = new LocalChunkSubscription[limits.MaximumSubscriptions];
        _nextSubscriptions = new LocalChunkSubscription[limits.MaximumSubscriptions];
        _previousInterest = new ChunkInterestEntry[limits.MaximumSubscriptions];
        _desiredInterest = new ChunkInterestEntry[limits.MaximumSubscriptions];
        _retainedSlotScratch = new bool[limits.MaximumSubscriptions];
        _residentSlotGenerations = new uint[limits.MaximumSubscriptions];
        _snapshotScratch = new WorldPublicRecord[limits.MaximumPublicEntities];
        _deltaScratch = new WorldPublicDeltaRecord[limits.MaximumPublicEntities];
        _present = new bool[limits.MaximumPublicEntities];
        _recordKeys = new SpatialChunkKey[limits.MaximumPublicEntities];
        _records = new WorldPublicRecord[limits.MaximumPublicEntities];
        _floorGenerations = new uint[limits.MaximumPublicEntities];
        _floorVersions = new ulong[limits.MaximumPublicEntities];
        _floorOperations = new WorldPublicOperation[limits.MaximumPublicEntities];
        _candidatePresent = new bool[limits.MaximumPublicEntities];
        _candidateRecordKeys = new SpatialChunkKey[limits.MaximumPublicEntities];
        _candidateRecords = new WorldPublicRecord[limits.MaximumPublicEntities];
        _candidateFloorGenerations = new uint[limits.MaximumPublicEntities];
        _candidateFloorVersions = new ulong[limits.MaximumPublicEntities];
        _candidateFloorOperations = new WorldPublicOperation[limits.MaximumPublicEntities];
    }

    /// <summary>
    /// Borrows the current subscription rows. The span is valid only until the next
    /// <see cref="Synchronize"/> call on this oracle.
    /// </summary>
    public ReadOnlySpan<LocalChunkSubscription> Subscriptions
    {
        get
        {
            EnsureOwner();
            return _subscriptions.AsSpan(0, _subscriptionCount);
        }
    }

    public LocalChunkStreamingStatus Synchronize(
        ChunkPublicationStore source,
        SpatialSpaceId space,
        in WorldPosition anchor,
        ulong cadenceStep,
        out int requiredSubscriptions)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureOwner();
        if (source.Limits.MaximumPublicEntities > _limits.MaximumPublicEntities)
            throw new ArgumentException("The local semantic capacity cannot address the publication store.", nameof(source));

        for (int index = 0; index < _subscriptionCount; index++)
            _previousInterest[index] = _subscriptions[index].Interest;
        ChunkInterestQueryStatus interestStatus = ChunkInterestQuery.TryQuery(
            space, anchor, _interestProfile, _previousInterest.AsSpan(0, _subscriptionCount),
            _desiredInterest, out int desiredCount, out requiredSubscriptions);
        if (interestStatus == ChunkInterestQueryStatus.InvalidInput)
            return LocalChunkStreamingStatus.InvalidInterest;
        if (interestStatus == ChunkInterestQueryStatus.DestinationTooSmall)
            return LocalChunkStreamingStatus.SubscriptionCapacityExceeded;

        if (_sourceEpoch.IsValid && _sourceEpoch != source.Epoch)
            ResetSourceKnowledge();
        _sourceEpoch = source.Epoch;
        PrepareSubscriptions(desiredCount);
        SynchronizePublications(source, desiredCount, cadenceStep);
        (_subscriptions, _nextSubscriptions) = (_nextSubscriptions, _subscriptions);
        _subscriptionCount = desiredCount;
        _synchronizations++;
        return LocalChunkStreamingStatus.Synchronized;
    }

    public bool TryCopyResidentRecords(
        Span<LocalChunkSemanticRecord> destination,
        out int count,
        out int required)
    {
        EnsureOwner();
        count = 0;
        required = _residentRecordCount;
        if (destination.Length < required) return false;
        for (int index = 0; index < _present.Length; index++)
        {
            if (!_present[index]) continue;
            destination[count++] = new LocalChunkSemanticRecord(_recordKeys[index], _records[index]);
        }
        return true;
    }

    public LocalChunkStreamingSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new LocalChunkStreamingSnapshot(
            _sourceEpoch,
            _subscriptionCount,
            _residentRecordCount,
            _synchronizations,
            _snapshotApplications,
            _chunkAbsentApplications,
            _deltaApplications,
            _noChangeQueries,
            _snapshotFallbacks,
            _cadenceDeferrals,
            CalculateReservedBytes());
    }

    /// <summary>Exercises the reference consumer's semantic admission rules without a transport.</summary>
    internal void ApplySemanticDeltaForTesting(
        SpatialChunkKey key,
        in WorldPublicDeltaRecord delta)
    {
        EnsureOwner();
        StageSemanticCandidate();
        ApplyCandidateDelta(key, delta);
        PublishSemanticCandidate();
    }

    private void PrepareSubscriptions(int desiredCount)
    {
        Array.Clear(_retainedSlotScratch);
        int previousIndex = 0;
        for (int desiredIndex = 0; desiredIndex < desiredCount; desiredIndex++)
        {
            ChunkInterestEntry desired = _desiredInterest[desiredIndex];
            while (previousIndex < _subscriptionCount &&
                _subscriptions[previousIndex].Interest.Key.CompareTo(desired.Key) < 0)
                previousIndex++;
            if (previousIndex < _subscriptionCount && _subscriptions[previousIndex].Interest.Key == desired.Key)
            {
                LocalChunkSubscription retained = _subscriptions[previousIndex];
                uint period = _cadence.Period(desired.Tier);
                uint phase = StablePhase(desired.Key, period);
                _nextSubscriptions[desiredIndex] = retained with
                {
                    Interest = desired,
                    CadencePhase = phase,
                };
                _retainedSlotScratch[checked((int)retained.ResidentSlot.Index)] = true;
            }
            else
            {
                _nextSubscriptions[desiredIndex] = new LocalChunkSubscription(desired, default, false,
                    StablePhase(desired.Key, _cadence.Period(desired.Tier)), default);
            }
        }

        for (int index = 0; index < _subscriptionCount; index++)
        {
            int slot = checked((int)_subscriptions[index].ResidentSlot.Index);
            if (!_retainedSlotScratch[slot]) ClearSemanticKey(_subscriptions[index].Interest.Key);
        }
        for (int index = 0; index < desiredCount; index++)
        {
            if (_nextSubscriptions[index].ResidentSlot.IsValid) continue;
            int slot = FindFreeResidentSlot();
            uint generation = _residentSlotGenerations[slot] == uint.MaxValue
                ? throw new InvalidOperationException("The client semantic slot generation is exhausted.")
                : _residentSlotGenerations[slot] + 1;
            _residentSlotGenerations[slot] = generation;
            _retainedSlotScratch[slot] = true;
            _nextSubscriptions[index] = _nextSubscriptions[index] with
            {
                ResidentSlot = new ClientSemanticChunkSlot(checked((uint)slot), generation),
            };
        }
    }

    private void ResetSourceKnowledge()
    {
        Array.Clear(_present);
        Array.Clear(_floorGenerations);
        Array.Clear(_floorVersions);
        Array.Clear(_floorOperations);
        _residentRecordCount = 0;
        for (int index = 0; index < _subscriptionCount; index++)
            _subscriptions[index] = _subscriptions[index] with { Cursor = default, HasKnowledge = false };
    }

    private void SynchronizePublications(ChunkPublicationStore source, int desiredCount, ulong cadenceStep)
    {
        for (int index = 0; index < desiredCount; index++)
        {
            LocalChunkSubscription subscription = _nextSubscriptions[index];
            bool isNew = !subscription.HasKnowledge;
            uint period = _cadence.Period(subscription.Interest.Tier);
            if (!isNew && cadenceStep % period != subscription.CadencePhase)
            {
                _cadenceDeferrals++;
                continue;
            }

            ChunkPublicationReadStatus status;
            ChunkPublicationBatch batch;
            if (subscription.Cursor.IsValid)
            {
                status = source.TryCopyDelta(subscription.Interest.Key, subscription.Cursor,
                    _deltaScratch, out batch);
                if (status == ChunkPublicationReadStatus.Delta)
                {
                    StageSemanticCandidate();
                    for (int row = 0; row < batch.Count; row++)
                        ApplyCandidateDelta(subscription.Interest.Key, _deltaScratch[row]);
                    PublishSemanticCandidate();
                    _nextSubscriptions[index] = subscription with { Cursor = batch.Cursor, HasKnowledge = true };
                    _deltaApplications++;
                    continue;
                }
                if (status == ChunkPublicationReadStatus.NoChanges)
                {
                    _nextSubscriptions[index] = subscription with { Cursor = batch.Cursor, HasKnowledge = true };
                    _noChangeQueries++;
                    continue;
                }
                if (status != ChunkPublicationReadStatus.SnapshotRequired)
                    throw new InvalidOperationException($"Unexpected publication delta result: {status}.");
                _snapshotFallbacks++;
            }

            status = source.TryCopySnapshot(subscription.Interest.Key, _snapshotScratch, out batch);
            if (status == ChunkPublicationReadStatus.ChunkAbsent)
            {
                if (HasSemanticKey(subscription.Interest.Key))
                {
                    StageSemanticCandidate();
                    ClearCandidateKey(subscription.Interest.Key);
                    PublishSemanticCandidate();
                }
                _nextSubscriptions[index] = subscription with { Cursor = default, HasKnowledge = true };
                _snapshotApplications++;
                _chunkAbsentApplications++;
                continue;
            }
            if (status != ChunkPublicationReadStatus.Snapshot)
                throw new InvalidOperationException($"Unexpected publication snapshot result: {status}.");
            StageSemanticCandidate();
            ClearCandidateKey(subscription.Interest.Key);
            for (int row = 0; row < batch.Count; row++)
                ApplyCandidatePresent(subscription.Interest.Key, _snapshotScratch[row]);
            PublishSemanticCandidate();
            _nextSubscriptions[index] = subscription with { Cursor = batch.Cursor, HasKnowledge = true };
            _snapshotApplications++;
        }
    }

    private void ApplyCandidatePresent(SpatialChunkKey key, in WorldPublicRecord record) =>
        ApplyCandidateDelta(key, new WorldPublicDeltaRecord(
            WorldPublicOperation.Present,
            WorldPublicFieldMask.Complete,
            record.Id,
            record.EntityPublicVersion,
            record));

    private void ApplyCandidateDelta(SpatialChunkKey key, in WorldPublicDeltaRecord delta)
    {
        int index = checked((int)delta.Id.Index);
        if ((uint)index >= (uint)_candidatePresent.Length)
            throw new InvalidOperationException("Publication identity exceeds local semantic capacity.");
        if (delta.Operation is not (WorldPublicOperation.Present or WorldPublicOperation.Remove))
            throw new InvalidOperationException("The public operation is not recognized.");
        if (delta.Operation == WorldPublicOperation.Present &&
            (delta.Current.Id != delta.Id ||
             delta.Current.EntityPublicVersion != delta.EntityPublicVersion ||
             delta.Current.Position.Chunk != key.Coord))
            throw new InvalidOperationException("The public Present operation is not self-consistent.");
        uint floorGeneration = _candidateFloorGenerations[index];
        ulong floorVersion = _candidateFloorVersions[index];
        if (delta.Id.Generation < floorGeneration ||
            delta.Id.Generation == floorGeneration && delta.EntityPublicVersion < floorVersion)
            return;
        if (delta.Id.Generation == floorGeneration && delta.EntityPublicVersion == floorVersion)
        {
            if (delta.Operation != _candidateFloorOperations[index])
                throw new InvalidOperationException(
                    "One public identity version cannot describe contradictory operations.");
            if (delta.Operation == WorldPublicOperation.Remove)
                return;
            if (_candidateRecordKeys[index] != key || _candidateRecords[index] != delta.Current)
                throw new InvalidOperationException(
                    "One public identity version cannot describe contradictory Present state.");
            if (_candidatePresent[index]) return;
        }

        _candidatePresent[index] = false;
        _candidateFloorGenerations[index] = delta.Id.Generation;
        _candidateFloorVersions[index] = delta.EntityPublicVersion;
        _candidateFloorOperations[index] = delta.Operation;
        if (delta.Operation == WorldPublicOperation.Present)
        {
            _candidatePresent[index] = true;
            _candidateRecordKeys[index] = key;
            _candidateRecords[index] = delta.Current;
        }
    }

    private void StageSemanticCandidate()
    {
        Array.Copy(_present, _candidatePresent, _present.Length);
        Array.Copy(_recordKeys, _candidateRecordKeys, _recordKeys.Length);
        Array.Copy(_records, _candidateRecords, _records.Length);
        Array.Copy(_floorGenerations, _candidateFloorGenerations, _floorGenerations.Length);
        Array.Copy(_floorVersions, _candidateFloorVersions, _floorVersions.Length);
        Array.Copy(_floorOperations, _candidateFloorOperations, _floorOperations.Length);
    }

    private void PublishSemanticCandidate()
    {
        (_present, _candidatePresent) = (_candidatePresent, _present);
        (_recordKeys, _candidateRecordKeys) = (_candidateRecordKeys, _recordKeys);
        (_records, _candidateRecords) = (_candidateRecords, _records);
        (_floorGenerations, _candidateFloorGenerations) =
            (_candidateFloorGenerations, _floorGenerations);
        (_floorVersions, _candidateFloorVersions) = (_candidateFloorVersions, _floorVersions);
        (_floorOperations, _candidateFloorOperations) = (_candidateFloorOperations, _floorOperations);
        int count = 0;
        for (int index = 0; index < _present.Length; index++)
            if (_present[index]) count++;
        _residentRecordCount = count;
    }

    private void ClearCandidateKey(SpatialChunkKey key)
    {
        for (int index = 0; index < _candidatePresent.Length; index++)
            if (_candidatePresent[index] && _candidateRecordKeys[index] == key)
                _candidatePresent[index] = false;
    }

    private void ClearSemanticKey(SpatialChunkKey key)
    {
        for (int index = 0; index < _present.Length; index++)
        {
            if (!_present[index] || _recordKeys[index] != key) continue;
            _present[index] = false;
            _residentRecordCount--;
        }
    }

    private bool HasSemanticKey(SpatialChunkKey key)
    {
        for (int index = 0; index < _present.Length; index++)
            if (_present[index] && _recordKeys[index] == key) return true;
        return false;
    }

    private int FindFreeResidentSlot()
    {
        for (int index = 0; index < _retainedSlotScratch.Length; index++)
            if (!_retainedSlotScratch[index]) return index;
        throw new InvalidOperationException("Interest sizing admitted more semantic slots than configured.");
    }

    private static uint StablePhase(SpatialChunkKey key, uint period)
    {
        if (period == 1) return 0;
        unchecked
        {
            uint hash = 2_166_136_261u;
            hash = (hash ^ (uint)key.Space.Value) * 16_777_619u;
            hash = (hash ^ (uint)(key.Space.Value >> 32)) * 16_777_619u;
            hash = (hash ^ (uint)key.Coord.X) * 16_777_619u;
            hash = (hash ^ (uint)key.Coord.Z) * 16_777_619u;
            return hash % period;
        }
    }

    private long CalculateReservedBytes() => checked(
        (long)(_subscriptions.Length + _nextSubscriptions.Length) * Unsafe.SizeOf<LocalChunkSubscription>() +
        (long)(_previousInterest.Length + _desiredInterest.Length) * Unsafe.SizeOf<ChunkInterestEntry>() +
        _retainedSlotScratch.Length * sizeof(bool) +
        _residentSlotGenerations.Length * sizeof(uint) +
        (long)_snapshotScratch.Length * Unsafe.SizeOf<WorldPublicRecord>() +
        (long)_deltaScratch.Length * Unsafe.SizeOf<WorldPublicDeltaRecord>() +
        _present.Length * sizeof(bool) +
        (long)_recordKeys.Length * Unsafe.SizeOf<SpatialChunkKey>() +
        (long)_records.Length * Unsafe.SizeOf<WorldPublicRecord>() +
        _floorGenerations.Length * sizeof(uint) +
        _floorVersions.Length * sizeof(ulong) +
        (long)_floorOperations.Length * Unsafe.SizeOf<WorldPublicOperation>() +
        _candidatePresent.Length * sizeof(bool) +
        (long)_candidateRecordKeys.Length * Unsafe.SizeOf<SpatialChunkKey>() +
        (long)_candidateRecords.Length * Unsafe.SizeOf<WorldPublicRecord>() +
        _candidateFloorGenerations.Length * sizeof(uint) +
        _candidateFloorVersions.Length * sizeof(ulong) +
        (long)_candidateFloorOperations.Length * Unsafe.SizeOf<WorldPublicOperation>());

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The local chunk-streaming oracle is owner-thread bound.");
    }
}
