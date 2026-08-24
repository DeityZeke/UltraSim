using System.Runtime.CompilerServices;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Game.Replication;

/// <summary>Latest authoritative public value supplied to DirtyEntitySpatialFanout.</summary>
/// <remarks>
/// This is a value, not a history entry.  A later value replaces the earlier value before
/// fanout is drained.  <see cref="IsPublic"/> is deliberately explicit so proximity never
/// becomes a disclosure decision.
/// </remarks>
public readonly record struct DirtyEntityLatestState(
    ReplicationEntityId Entity,
    ulong CanonicalVersion,
    bool IsPresent,
    bool IsPublic,
    GameReplicationHorizon Horizon,
    ReplicationSpatialPoint Position,
    GameSimulationState SimulationState)
{
    public bool IsDiscoverable => IsPresent && IsPublic && Horizon != GameReplicationHorizon.None;

    public static DirtyEntityLatestState Absent(
        ReplicationEntityId entity,
        ulong canonicalVersion,
        in ReplicationSpatialPoint lastPosition) => new(
            entity, canonicalVersion, false, true, GameReplicationHorizon.None,
            lastPosition, default);
}

/// <summary>One recipient-relative semantic operation produced by the dirty fanout.</summary>
public readonly record struct DirtyEntityDeliveryRecord(
    ulong DeliveryId,
    ReplicationEntityId Entity,
    ulong CanonicalVersion,
    bool IsPresent,
    GameReplicationHorizon Horizon,
    ReplicationSpatialPoint Position,
    GameSimulationState SimulationState)
{
    public const int MaximumEncodedBytes =
        sizeof(ulong) + sizeof(uint) + sizeof(uint) + sizeof(ulong) + sizeof(byte) +
        sizeof(byte) + sizeof(int) * 4;
}

public readonly record struct DirtyEntityBundle(
    ParticipantId Participant,
    StateEpoch StateEpoch,
    ulong DeliveryBundleId,
    int RecordCount,
    int EstimatedEncodedBytes);

public readonly record struct DirtyEntitySemanticKnowledgeSnapshot(
    ParticipantId Participant,
    ReplicationEntityId Entity,
    bool KnownPresent,
    ulong KnownCanonicalVersion,
    bool DesiredPresent,
    ulong PendingCanonicalVersion,
    bool PendingPresent,
    GameInterestTier PendingTier,
    long EligibleAtMilliseconds,
    bool PendingWasSent);

public readonly record struct DirtyEntitySpatialFanoutLimits(
    int MaximumEntities,
    int MaximumRecipients,
    int MaximumSpatialRegions,
    int MaximumSemanticKnowledgeRows,
    int MaximumOutstandingBundles,
    int MaximumRecordsPerBundle)
{
    public static DirtyEntitySpatialFanoutLimits Default => new(
        MaximumEntities: 4_096,
        MaximumRecipients: 256,
        MaximumSpatialRegions: 16_384,
        MaximumSemanticKnowledgeRows: 262_144,
        MaximumOutstandingBundles: 1_024,
        MaximumRecordsPerBundle: 32);

    internal void Validate()
    {
        if (MaximumEntities <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumEntities));
        if (MaximumRecipients <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumRecipients));
        if (MaximumSpatialRegions <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumSpatialRegions));
        if (MaximumSemanticKnowledgeRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumSemanticKnowledgeRows));
        if (MaximumOutstandingBundles < MaximumRecipients)
            throw new ArgumentOutOfRangeException(nameof(MaximumOutstandingBundles));
        if (MaximumRecordsPerBundle is <= 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(MaximumRecordsPerBundle));
        _ = checked(MaximumOutstandingBundles * MaximumRecordsPerBundle);
    }
}

public enum DirtyEntityUpdateStatus : byte
{
    Success = 0,
    NoChange = 1,
    InvalidInput = 2,
    StaleVersion = 3,
    CapacityExceeded = 4,
    SequenceExhausted = 5,
}

public enum DirtyEntityBuildStatus : byte
{
    Success = 0,
    NoData = 1,
    UnknownRecipient = 2,
    DestinationTooSmall = 3,
    ByteBudgetTooSmall = 4,
    OutstandingCapacityExceeded = 5,
    TransportNotAccepting = 6,
    SpatialCapacityExceeded = 7,
}

public enum DirtyEntityProcessStatus : byte
{
    Success = 0,
    NoData = 1,
    CapacityExceeded = 2,
}

/// <summary>
/// Cold diagnostics for the concrete dirty-time entity fanout profile.
/// <see cref="SemanticKnowledgeRowCount"/> is knowledge/debt only; there is no retained
/// spatial Recipient×Entity relation count in this profile.
/// </summary>
public readonly record struct DirtyEntitySpatialFanoutSnapshot(
    StateEpoch StateEpoch,
    int AuthoritativeEntityCount,
    int RecipientCount,
    int SpatialRegionCount,
    int SemanticKnowledgeRowCount,
    int PendingObligationCount,
    int OutstandingBundleCount,
    int DirtyEntityCount,
    ulong AuthoritativeEntityChangesObserved,
    ulong DirtyEntitiesProcessed,
    ulong SpatialRecipientDiscoveryOperations,
    ulong SpatialEntityDiscoveryOperations,
    ulong CandidateRecipientsExamined,
    ulong InterestedRecipientsDiscovered,
    ulong NewSemanticObligationsCreated,
    ulong UnsentCoalesces,
    ulong SentWorkSupersessions,
    ulong CadenceDeferrals,
    ulong RecipientTopologyReconciliations,
    ulong EntrantPresents,
    ulong LeaverRemovals,
    ulong ApplicationProofsApplied,
    ulong DuplicateProofs,
    ulong StaleOrRejectedProofs,
    ulong TimedOutDeliveries,
    ulong BundlesBuilt,
    ulong BundlesRejected,
    ulong CapacityFailures,
    long ReservedBytes);

/// <summary>Optional cold work attribution.  It is dormant on the warmed path.</summary>
public readonly record struct DirtyEntitySpatialFanoutAttributionSnapshot(
    bool Enabled,
    ulong DirtyEntityCommits,
    ulong DirtyEntityDiscoveries,
    ulong SpatialCellsExamined,
    ulong SpatialCandidatesExamined,
    ulong SemanticRowsExamined,
    ulong RowsCreated,
    ulong RowsRemoved,
    ulong RecordsEncoded,
    ulong EncodedBytes);

/// <summary>
/// Concrete Game-owned Entity Dirty Push profile.
///
/// The two broadphases are intentionally one-sided: dirty entities discover recipient cells,
/// while recipient topology changes discover entity cells.  The only retained recipient/entity
/// rows are semantic knowledge and delivery debt; they are not an AOI membership graph.
/// </summary>
public sealed class DirtyEntitySpatialFanout
{
    public static GamePublicReplicationProfileDescriptor PublicReplicationProfile =>
        GamePublicReplicationProfiles.DirtyEntitySpatialFanout;

    private readonly DirtyEntitySpatialFanoutLimits _limits;
    private readonly GameInterestProfile _profile;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly DirtyEntitySlot[] _entities;
    private readonly RecipientSlot[] _recipients;
    private readonly SpatialRegion[] _regions;
    private readonly DirtyRecord[] _rows;
    private readonly uint[] _rowGenerations;
    private readonly DirtyBundleState[] _bundles;
    private readonly DirtyBundleItem[] _bundleItems;
    private readonly int[] _selectionScratch;
    private readonly int[] _dirtyEntities;
    private readonly int[] _freeRecipients;
    private readonly int[] _freeRegions;
    private readonly int[] _freeRows;
    private readonly int[] _freeBundles;
    private readonly int[] _recipientStamp;
    private readonly int[] _entityStamp;
    private readonly Dictionary<ParticipantId, int> _recipientMap;
    private readonly Dictionary<ReplicationSpatialRegionId, int> _regionMap;
    private readonly Dictionary<DirtyRowKey, int> _rowMap;
    private readonly Dictionary<ulong, int> _bundleMap;
    private readonly int _recipientMapCapacity;
    private readonly int _regionMapCapacity;
    private readonly int _rowMapCapacity;
    private readonly int _bundleMapCapacity;

    private StateEpoch _stateEpoch;
    private int _freeRecipientCount;
    private int _freeRegionCount;
    private int _freeRowCount;
    private int _freeBundleCount;
    private int _entityCount;
    private int _recipientCount;
    private int _regionCount;
    private int _rowCount;
    private int _pendingCount;
    private int _bundleCount;
    private int _dirtyCount;
    private int _nextRecipient;
    private int _discoveryStamp;
    private int _entityDiscoveryStamp;
    private ulong _nextDeliveryBundleId = 1;
    private ulong _nextDeliveryId = 1;
    private ulong _authoritativeChanges;
    private ulong _dirtyProcessed;
    private ulong _recipientDiscoveryOperations;
    private ulong _entityDiscoveryOperations;
    private ulong _candidateRecipientsExamined;
    private ulong _interestedRecipientsDiscovered;
    private ulong _obligationsCreated;
    private ulong _unsentCoalesces;
    private ulong _sentSupersessions;
    private ulong _cadenceDeferrals;
    private ulong _topologyReconciliations;
    private ulong _entrantPresents;
    private ulong _leaverRemovals;
    private ulong _proofsApplied;
    private ulong _duplicateProofs;
    private ulong _staleOrRejectedProofs;
    private ulong _timedOutDeliveries;
    private ulong _bundlesBuilt;
    private ulong _bundlesRejected;
    private ulong _capacityFailures;
    private int _lastDiscoveryCandidates;
    private bool _attributionEnabled;
    private ulong _attributionDirtyCommits;
    private ulong _attributionDirtyDiscoveries;
    private ulong _attributionCells;
    private ulong _attributionCandidates;
    private ulong _attributionRows;
    private ulong _attributionRowsCreated;
    private ulong _attributionRowsRemoved;
    private ulong _attributionRecords;
    private ulong _attributionBytes;
    private readonly int _maximumOutstandingBundlesPerRecipient;

    public DirtyEntitySpatialFanout(
        StateEpoch stateEpoch,
        DirtyEntitySpatialFanoutLimits limits = default,
        GameInterestProfile profile = default)
    {
        if (!stateEpoch.IsValid) throw new ArgumentException("A valid projection epoch is required.", nameof(stateEpoch));
        if (limits == default) limits = DirtyEntitySpatialFanoutLimits.Default;
        if (profile == default) profile = GameInterestProfile.Reference;
        limits.Validate();
        profile.Validate();
        _stateEpoch = stateEpoch;
        _limits = limits;
        _profile = profile;
        _maximumOutstandingBundlesPerRecipient = Math.Max(
            1, limits.MaximumOutstandingBundles / limits.MaximumRecipients);
        _entities = new DirtyEntitySlot[limits.MaximumEntities];
        _recipients = new RecipientSlot[limits.MaximumRecipients];
        _regions = new SpatialRegion[limits.MaximumSpatialRegions];
        _rows = new DirtyRecord[limits.MaximumSemanticKnowledgeRows];
        _rowGenerations = new uint[limits.MaximumSemanticKnowledgeRows];
        _bundles = new DirtyBundleState[limits.MaximumOutstandingBundles];
        _bundleItems = new DirtyBundleItem[checked(limits.MaximumOutstandingBundles * limits.MaximumRecordsPerBundle)];
        _selectionScratch = new int[limits.MaximumRecordsPerBundle];
        _dirtyEntities = new int[limits.MaximumEntities];
        _freeRecipients = new int[limits.MaximumRecipients];
        _freeRegions = new int[limits.MaximumSpatialRegions];
        _freeRows = new int[limits.MaximumSemanticKnowledgeRows];
        _freeBundles = new int[limits.MaximumOutstandingBundles];
        _recipientStamp = new int[limits.MaximumRecipients];
        _entityStamp = new int[limits.MaximumEntities];
        _recipientMap = new Dictionary<ParticipantId, int>(limits.MaximumRecipients);
        _regionMap = new Dictionary<ReplicationSpatialRegionId, int>(limits.MaximumSpatialRegions);
        _rowMap = new Dictionary<DirtyRowKey, int>(limits.MaximumSemanticKnowledgeRows);
        _bundleMap = new Dictionary<ulong, int>(limits.MaximumOutstandingBundles);
        _recipientMapCapacity = _recipientMap.EnsureCapacity(limits.MaximumRecipients);
        _regionMapCapacity = _regionMap.EnsureCapacity(limits.MaximumSpatialRegions);
        _rowMapCapacity = _rowMap.EnsureCapacity(limits.MaximumSemanticKnowledgeRows);
        _bundleMapCapacity = _bundleMap.EnsureCapacity(limits.MaximumOutstandingBundles);
        for (int index = 0; index < _freeRecipients.Length; index++) _freeRecipients[index] = _freeRecipients.Length - index - 1;
        for (int index = 0; index < _freeRegions.Length; index++) _freeRegions[index] = _freeRegions.Length - index - 1;
        for (int index = 0; index < _freeRows.Length; index++) _freeRows[index] = _freeRows.Length - index - 1;
        for (int index = 0; index < _freeBundles.Length; index++) _freeBundles[index] = _freeBundles.Length - index - 1;
        _freeRecipientCount = _freeRecipients.Length;
        _freeRegionCount = _freeRegions.Length;
        _freeRowCount = _freeRows.Length;
        _freeBundleCount = _freeBundles.Length;
    }

    public StateEpoch StateEpoch { get { EnsureOwnerThread(); return _stateEpoch; } }
    public DirtyEntitySpatialFanoutLimits Limits => _limits;
    public GameInterestProfile InterestProfile => _profile;

    /// <summary>Replaces the authoritative source epoch and drops all epoch-local knowledge/debt.</summary>
    public bool ReplaceSourceEpoch(StateEpoch nextEpoch)
    {
        EnsureOwnerThread();
        if (!nextEpoch.IsValid || nextEpoch.Value <= _stateEpoch.Value) return false;
        _stateEpoch = nextEpoch;
        for (int index = 0; index < _entities.Length; index++)
        {
            if (_entities[index].Active) UnlinkEntity(index);
            _entities[index] = default;
        }
        _entityCount = 0;
        _dirtyCount = 0;
        Array.Clear(_dirtyEntities);
        for (int index = 0; index < _rows.Length; index++)
            if (_rows[index].Active) RemoveRow(index);
        for (int index = 0; index < _bundles.Length; index++)
            if (_bundles[index].Active) ReleaseBundle(index);
        return true;
    }

    /// <summary>Adds or moves an admitted recipient and reconciles current truth immediately.</summary>
    public DirtyEntityUpdateStatus SetRecipientAnchor(
        ParticipantId participant,
        in ReplicationSpatialPoint anchor,
        long nowMilliseconds)
    {
        EnsureOwnerThread();
        if (!participant.IsValid || nowMilliseconds < 0) return DirtyEntityUpdateStatus.InvalidInput;
        ReplicationSpatialRegionId region = ToRegion(anchor);
        if (!_recipientMap.TryGetValue(participant, out int recipientIndex))
        {
            if (!TryAllocateRecipient(out recipientIndex)) return CapacityFailure();
            if (!TryGetOrCreateRegion(region, out int regionIndex))
            {
                FreeRecipient(recipientIndex);
                return CapacityFailure();
            }
            ref RecipientSlot recipient = ref _recipients[recipientIndex];
            recipient = new RecipientSlot
            {
                Active = true, Participant = participant, Anchor = anchor,
                Region = regionIndex, RegionPrevious = -1, RegionNext = -1, RowHead = -1,
            };
            _recipientMap.Add(participant, recipientIndex);
            _recipientCount++;
            LinkRecipient(recipientIndex, regionIndex);
            _topologyReconciliations++;
            if (!ReconcileRecipient(recipientIndex, anchor, anchor, nowMilliseconds))
            {
                int rollbackRow = _recipients[recipientIndex].RowHead;
                while (rollbackRow >= 0)
                {
                    int next = _rows[rollbackRow].RecipientNext;
                    RemoveRow(rollbackRow);
                    rollbackRow = next;
                }
                UnlinkRecipient(recipientIndex);
                _recipientMap.Remove(participant);
                FreeRecipient(recipientIndex);
                _recipientCount--;
                return CapacityFailure();
            }
            return DirtyEntityUpdateStatus.Success;
        }

        ref RecipientSlot current = ref _recipients[recipientIndex];
        if (!current.Active) return DirtyEntityUpdateStatus.InvalidInput;
        if (current.Anchor == anchor) return DirtyEntityUpdateStatus.NoChange;
        if (!CanReconcileRecipient(recipientIndex, anchor)) return CapacityFailure();
        ReplicationSpatialPoint previous = current.Anchor;
        int nextRegion = current.Region;
        if (current.Region != RegionIndex(region))
        {
            if (!TryGetOrCreateRegion(region, out nextRegion)) return CapacityFailure();
            UnlinkRecipient(recipientIndex);
            current.Region = nextRegion;
            LinkRecipient(recipientIndex, nextRegion);
        }
        current.Anchor = anchor;
        _topologyReconciliations++;
        return ReconcileRecipient(recipientIndex, previous, anchor, nowMilliseconds)
            ? DirtyEntityUpdateStatus.Success
            : CapacityFailure();
    }

    public bool RemoveRecipient(ParticipantId participant)
    {
        EnsureOwnerThread();
        if (!_recipientMap.TryGetValue(participant, out int recipientIndex)) return false;
        int row = _recipients[recipientIndex].RowHead;
        int rowVisits = 0;
        while (row >= 0)
        {
            if (++rowVisits > _rows.Length)
                throw new InvalidOperationException("Dirty-entity recipient row list contains a cycle.");
            int next = _rows[row].RecipientNext;
            RemoveRow(row);
            row = next;
        }
        for (int index = 0; index < _bundles.Length; index++)
            if (_bundles[index].Active && _bundles[index].Recipient == recipientIndex)
                ReleaseBundle(index);
        UnlinkRecipient(recipientIndex);
        _recipientMap.Remove(participant);
        FreeRecipient(recipientIndex);
        _recipientCount--;
        return true;
    }

    /// <summary>Re-runs current-truth discovery for every admitted recipient after recovery.</summary>
    public DirtyEntityProcessStatus ReconcileAllRecipients(long nowMilliseconds)
    {
        EnsureOwnerThread();
        if (nowMilliseconds < 0) return DirtyEntityProcessStatus.NoData;
        bool success = true;
        for (int index = 0; index < _recipients.Length; index++)
            if (_recipients[index].Active)
            {
                _topologyReconciliations++;
                success &= ReconcileRecipient(
                    index, _recipients[index].Anchor, _recipients[index].Anchor, nowMilliseconds);
            }
        return success ? DirtyEntityProcessStatus.Success : DirtyEntityProcessStatus.CapacityExceeded;
    }

    /// <summary>Commits one newest public value and creates at most one dirty work item per entity.</summary>
    public DirtyEntityUpdateStatus CommitEntity(in DirtyEntityLatestState value, long nowMilliseconds)
    {
        EnsureOwnerThread();
        if (!value.Entity.IsValid || value.Entity.Index >= _entities.Length ||
            value.CanonicalVersion == 0 || nowMilliseconds < 0 ||
            (value.IsPresent && value.Horizon == GameReplicationHorizon.None) ||
            (!value.IsPresent && value.Horizon != GameReplicationHorizon.None))
            return DirtyEntityUpdateStatus.InvalidInput;
        int entityIndex = checked((int)value.Entity.Index);
        ref DirtyEntitySlot slot = ref _entities[entityIndex];
        if (slot.Entity.IsValid)
        {
            if (value.Entity.Generation < slot.Entity.Generation ||
                (value.Entity.Generation == slot.Entity.Generation && value.CanonicalVersion < slot.CanonicalVersion))
                return DirtyEntityUpdateStatus.StaleVersion;
            if (value.Entity == slot.Entity && value.CanonicalVersion == slot.CanonicalVersion &&
                slot.Active == value.IsDiscoverable && slot.Position == value.Position &&
                slot.Horizon == value.Horizon && slot.IsPublic == value.IsPublic &&
                slot.Projected.TickMarker == value.SimulationState.TickMarker &&
                slot.Projected.InputVelocity == value.SimulationState.InputVelocity)
                return DirtyEntityUpdateStatus.NoChange;
            if (value.Entity.Generation == slot.Entity.Generation &&
                value.CanonicalVersion <= slot.CanonicalVersion)
                return DirtyEntityUpdateStatus.StaleVersion;
        }

        DirtyEntityLatestState old = slot.Entity.IsValid
            ? new(slot.Entity, slot.CanonicalVersion, slot.Active, slot.IsPublic, slot.Horizon,
                slot.Position, slot.Projected)
            : default;
        int nextRegion = -1;
        if (value.IsDiscoverable)
        {
            ReplicationSpatialRegionId region = ToRegion(value.Position);
            if (!TryGetOrCreateRegion(region, out nextRegion)) return CapacityFailure();
        }
        if (!slot.DirtyQueued)
        {
            slot.DirtyQueued = true;
            slot.DirtyOld = old;
            if (_dirtyCount == _dirtyEntities.Length)
                throw new InvalidOperationException("The entity dirty queue exceeded its admitted capacity.");
            _dirtyEntities[_dirtyCount++] = entityIndex;
        }
        bool oldActive = slot.Active;
        bool retainsRegion = oldActive && value.IsDiscoverable && slot.Region == nextRegion;
        if (oldActive && !retainsRegion) UnlinkEntity(entityIndex);
        slot.Entity = value.Entity;
        slot.CanonicalVersion = value.CanonicalVersion;
        slot.Active = value.IsDiscoverable;
        slot.IsPublic = value.IsPublic;
        slot.Horizon = value.Horizon;
        slot.Position = value.Position;
        slot.Projected = value.SimulationState;
        if (slot.Active && !retainsRegion) LinkEntity(entityIndex, nextRegion);
        _entityCount += slot.Active ? (oldActive ? 0 : 1) : (oldActive ? -1 : 0);
        SaturatingIncrement(ref _authoritativeChanges);
        if (_attributionEnabled) SaturatingIncrement(ref _attributionDirtyCommits);
        return DirtyEntityUpdateStatus.Success;
    }

    public DirtyEntityUpdateStatus UpsertEntity(
        GameProjectedEntity entity,
        ulong canonicalVersion,
        in ReplicationSpatialPoint position,
        GameReplicationHorizon horizon,
        long nowMilliseconds,
        bool isPublic = true) => CommitEntity(
            new DirtyEntityLatestState(entity.Entity, canonicalVersion, true,
                isPublic, horizon, position, entity.SimulationState), nowMilliseconds);

    public DirtyEntityUpdateStatus RemoveEntity(
        ReplicationEntityId entity,
        ulong canonicalVersion,
        long nowMilliseconds)
    {
        EnsureOwnerThread();
        if (!entity.IsValid || entity.Index >= _entities.Length) return DirtyEntityUpdateStatus.InvalidInput;
        ref DirtyEntitySlot slot = ref _entities[checked((int)entity.Index)];
        if (!slot.Entity.IsValid || slot.Entity != entity) return DirtyEntityUpdateStatus.StaleVersion;
        return CommitEntity(new DirtyEntityLatestState(
            entity, canonicalVersion, false, true, GameReplicationHorizon.None,
            slot.Position, default), nowMilliseconds);
    }

    /// <summary>Drains each dirty entity once. Clean entities are never inspected by this operation.</summary>
    public DirtyEntityProcessStatus TryProcessDirtyEntities(long nowMilliseconds, out int processed)
    {
        EnsureOwnerThread();
        processed = 0;
        if (nowMilliseconds < 0) return DirtyEntityProcessStatus.NoData;
        int count = _dirtyCount;
        for (int index = 0; index < count; index++)
        {
            int entityIndex = _dirtyEntities[index];
            ref DirtyEntitySlot entity = ref _entities[entityIndex];
            if (!ProcessDirtyEntity(entityIndex, entity.DirtyOld, nowMilliseconds))
            {
                _capacityFailures++;
                int remaining = count - index;
                _dirtyEntities.AsSpan(index, remaining).CopyTo(_dirtyEntities);
                Array.Clear(_dirtyEntities, remaining, index);
                _dirtyCount = remaining;
                return DirtyEntityProcessStatus.CapacityExceeded;
            }
            entity.DirtyQueued = false;
            processed++;
            _dirtyProcessed++;
        }
        _dirtyCount = 0;
        Array.Clear(_dirtyEntities, 0, count);
        return count == 0 ? DirtyEntityProcessStatus.NoData : DirtyEntityProcessStatus.Success;
    }

    public int ProcessDirtyEntities(long nowMilliseconds)
    {
        TryProcessDirtyEntities(nowMilliseconds, out int processed);
        return processed;
    }

    /// <summary>Builds one bounded recipient bundle; building never advances semantic knowledge.</summary>
    public DirtyEntityBuildStatus TryBuildBundle(
        ParticipantId participant,
        long nowMilliseconds,
        int byteBudget,
        Span<DirtyEntityDeliveryRecord> destination,
        out DirtyEntityBundle bundle)
    {
        EnsureOwnerThread();
        bundle = default;
        if (!_recipientMap.TryGetValue(participant, out int recipientIndex)) return DirtyEntityBuildStatus.UnknownRecipient;
        if (nowMilliseconds < 0) return DirtyEntityBuildStatus.NoData;
        ExpireBundles(nowMilliseconds);
        ref RecipientSlot recipient = ref _recipients[recipientIndex];
        if (recipient.OutstandingBundleCount >= _maximumOutstandingBundlesPerRecipient)
            return DirtyEntityBuildStatus.OutstandingCapacityExceeded;
        int maximum = Math.Min(_limits.MaximumRecordsPerBundle, destination.Length);
        if (maximum == 0) return DirtyEntityBuildStatus.DestinationTooSmall;
        int estimate = DirtyEntityDeliveryRecord.MaximumEncodedBytes;
        int available = byteBudget - RemoteDirtyEntityStateHeaderCodec.EncodedSize;
        if (available < estimate) return DirtyEntityBuildStatus.ByteBudgetTooSmall;
        int capacityByBytes = Math.Min(maximum, available / estimate);
        if (capacityByBytes == 0) return DirtyEntityBuildStatus.ByteBudgetTooSmall;
        if (_bundleCount == _bundles.Length) return DirtyEntityBuildStatus.OutstandingCapacityExceeded;

        int selectedCount = SelectRows(recipientIndex, nowMilliseconds, _selectionScratch);
        if (selectedCount == 0) return DirtyEntityBuildStatus.NoData;
        selectedCount = Math.Min(selectedCount, capacityByBytes);
        if (!TryAllocateBundle(out int bundleIndex)) return DirtyEntityBuildStatus.OutstandingCapacityExceeded;
        if (_nextDeliveryBundleId == ulong.MaxValue)
        {
            FreeBundle(bundleIndex);
            return DirtyEntityBuildStatus.OutstandingCapacityExceeded;
        }
        ulong bundleId = _nextDeliveryBundleId++;
        ref DirtyBundleState bundleState = ref _bundles[bundleIndex];
        bundleState = new DirtyBundleState
        {
            Active = true, Recipient = recipientIndex, BundleId = bundleId,
            Count = selectedCount, SentAtMilliseconds = nowMilliseconds,
        };
        _bundleMap.Add(bundleId, bundleIndex);
        _bundleCount++;
        recipient.OutstandingBundleCount++;
        int baseItem = bundleIndex * _limits.MaximumRecordsPerBundle;
        for (int index = 0; index < selectedCount; index++)
        {
            int rowIndex = _selectionScratch[index];
            ref DirtyRecord row = ref _rows[rowIndex];
            if (_nextDeliveryId == ulong.MaxValue)
            {
                ReleaseBundle(bundleIndex);
                return DirtyEntityBuildStatus.OutstandingCapacityExceeded;
            }
            ulong deliveryId = _nextDeliveryId++;
            row.PendingDeliveryId = deliveryId;
            row.PendingWasSent = true;
            row.PendingSentAtMilliseconds = nowMilliseconds;
            _bundleItems[baseItem + index] = new(
                rowIndex, _rowGenerations[rowIndex], deliveryId, row.Entity,
                row.PendingCanonicalVersion, row.PendingPresent);
            DirtyEntityDeliveryRecord record = new(
                deliveryId, row.Entity, row.PendingCanonicalVersion, row.PendingPresent,
                row.PendingPresent ? row.PendingHorizon : GameReplicationHorizon.None,
                row.PendingPresent ? row.PendingPosition : default,
                row.PendingPresent ? row.PendingSimulationState : default);
            destination[index] = record;
            if (_attributionEnabled)
            {
                SaturatingIncrement(ref _attributionRecords);
                SaturatingAdd(ref _attributionBytes, checked((ulong)estimate));
            }
        }
        SaturatingIncrement(ref _bundlesBuilt);
        bundle = new DirtyEntityBundle(
            participant, _stateEpoch, bundleId, selectedCount,
            checked(RemoteDirtyEntityStateHeaderCodec.EncodedSize + selectedCount * estimate));
        return DirtyEntityBuildStatus.Success;
    }

    public DirtyEntityBuildStatus TryBuildNextBundle(
        long nowMilliseconds,
        int byteBudget,
        Span<DirtyEntityDeliveryRecord> destination,
        out DirtyEntityBundle bundle)
    {
        EnsureOwnerThread();
        bundle = default;
        for (int offset = 0; offset < _recipients.Length; offset++)
        {
            int index = (_nextRecipient + offset) % _recipients.Length;
            if (!_recipients[index].Active) continue;
            DirtyEntityBuildStatus status = TryBuildBundle(
                _recipients[index].Participant, nowMilliseconds, byteBudget, destination, out bundle);
            if (status == DirtyEntityBuildStatus.NoData) continue;
            _nextRecipient = (index + 1) % _recipients.Length;
            return status;
        }
        return DirtyEntityBuildStatus.NoData;
    }

    /// <summary>Applies only an exact application proof; queueing and transport ACKs are irrelevant.</summary>
    public bool TryAcknowledgeBundle(ParticipantId participant, ulong bundleId, bool applied)
    {
        EnsureOwnerThread();
        if (!participant.IsValid || bundleId == 0 || !_bundleMap.TryGetValue(bundleId, out int bundleIndex) ||
            !_bundles[bundleIndex].Active || _recipients[_bundles[bundleIndex].Recipient].Participant != participant)
        {
            if (applied) _duplicateProofs++; else _staleOrRejectedProofs++;
            return false;
        }
        ref DirtyBundleState bundle = ref _bundles[bundleIndex];
        int baseItem = bundleIndex * _limits.MaximumRecordsPerBundle;
        for (int index = 0; index < bundle.Count; index++)
        {
            DirtyBundleItem item = _bundleItems[baseItem + index];
            ref DirtyRecord row = ref _rows[item.RowIndex];
            if (!row.Active || _rowGenerations[item.RowIndex] != item.RowGeneration ||
                !row.Pending || row.PendingDeliveryId != item.DeliveryId)
            {
                // A sent bundle can remain in the ledger after newer work supersedes it.
                // Validate the complete bundle before touching any row so a stale proof is
                // both classified correctly and incapable of partially advancing knowledge.
                _staleOrRejectedProofs++;
                // Retire the stale ledger while rearming every sibling whose sent
                // delivery is still its current pending obligation. Superseded or
                // reused rows do not match and remain governed by their newer debt.
                ReleaseBundle(bundleIndex, retry: true);
                return false;
            }
        }
        for (int index = 0; index < bundle.Count; index++)
        {
            DirtyBundleItem item = _bundleItems[baseItem + index];
            ref DirtyRecord row = ref _rows[item.RowIndex];
            if (applied)
            {
                row.KnownEntity = item.Entity;
                row.KnownCanonicalVersion = item.CanonicalVersion;
                row.KnownPresent = item.IsPresent;
                row.Pending = false;
                row.PendingWasSent = false;
                row.PendingDeliveryId = 0;
                _pendingCount--;
                if (!row.DesiredPresent && !row.KnownPresent) RemoveRow(item.RowIndex);
            }
            else
            {
                row.PendingWasSent = false;
                row.PendingDeliveryId = 0;
            }
        }
        if (applied) _proofsApplied++; else _bundlesRejected++;
        ReleaseBundle(bundleIndex);
        return true;
    }

    public bool AbandonBundle(ulong bundleId)
    {
        EnsureOwnerThread();
        if (bundleId == 0 || !_bundleMap.TryGetValue(bundleId, out int bundleIndex)) return false;
        int baseItem = bundleIndex * _limits.MaximumRecordsPerBundle;
        ref DirtyBundleState bundle = ref _bundles[bundleIndex];
        for (int index = 0; index < bundle.Count; index++)
        {
            DirtyBundleItem item = _bundleItems[baseItem + index];
            ref DirtyRecord row = ref _rows[item.RowIndex];
            if (row.Active && _rowGenerations[item.RowIndex] == item.RowGeneration &&
                row.Pending && row.PendingDeliveryId == item.DeliveryId)
            {
                row.PendingWasSent = false;
                row.PendingDeliveryId = 0;
            }
        }
        ReleaseBundle(bundleIndex);
        return true;
    }

    public int CopySemanticKnowledge(Span<DirtyEntitySemanticKnowledgeSnapshot> destination)
    {
        EnsureOwnerThread();
        if (destination.Length < _rowCount)
            throw new ArgumentException("Destination cannot contain all semantic knowledge rows.", nameof(destination));
        int count = 0;
        for (int index = 0; index < _rows.Length; index++)
        {
            ref readonly DirtyRecord row = ref _rows[index];
            if (!row.Active) continue;
            destination[count++] = new(
                _recipients[row.Recipient].Participant, row.Entity, row.KnownPresent,
                row.KnownCanonicalVersion, row.DesiredPresent, row.PendingCanonicalVersion,
                row.PendingPresent, row.PendingTier, row.EligibleAtMilliseconds, row.PendingWasSent);
        }
        destination[..count].Sort(static (left, right) =>
        {
            int participant = left.Participant.CompareTo(right.Participant);
            if (participant != 0) return participant;
            return left.Entity.CompareTo(right.Entity);
        });
        return count;
    }

    public void ResetAttribution(bool enabled)
    {
        EnsureOwnerThread();
        _attributionEnabled = enabled;
        _attributionDirtyCommits = 0;
        _attributionDirtyDiscoveries = 0;
        _attributionCells = 0;
        _attributionCandidates = 0;
        _attributionRows = 0;
        _attributionRowsCreated = 0;
        _attributionRowsRemoved = 0;
        _attributionRecords = 0;
        _attributionBytes = 0;
    }

    public DirtyEntitySpatialFanoutAttributionSnapshot GetAttributionSnapshot()
    {
        EnsureOwnerThread();
        return new(_attributionEnabled, _attributionDirtyCommits, _attributionDirtyDiscoveries,
            _attributionCells, _attributionCandidates, _attributionRows, _attributionRowsCreated,
            _attributionRowsRemoved, _attributionRecords, _attributionBytes);
    }

    public DirtyEntitySpatialFanoutSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(
            _stateEpoch, _entityCount, _recipientCount, _regionCount, _rowCount, _pendingCount,
            _bundleCount, _dirtyCount, _authoritativeChanges, _dirtyProcessed,
            _recipientDiscoveryOperations, _entityDiscoveryOperations, _candidateRecipientsExamined,
            _interestedRecipientsDiscovered, _obligationsCreated, _unsentCoalesces,
            _sentSupersessions, _cadenceDeferrals, _topologyReconciliations, _entrantPresents,
            _leaverRemovals, _proofsApplied, _duplicateProofs, _staleOrRejectedProofs,
            _timedOutDeliveries, _bundlesBuilt, _bundlesRejected, _capacityFailures,
            CalculateReservedBytes());
    }

    private bool ProcessDirtyEntity(int entityIndex, in DirtyEntityLatestState old, long now)
    {
        ref readonly DirtyEntitySlot current = ref _entities[entityIndex];
        if (!CanProcessDirtyEntity(old, current)) return false;
        if (_attributionEnabled) _attributionDirtyDiscoveries++;
        _lastDiscoveryCandidates = 0;
        bool success = true;
        bool sameFootprint = old.IsDiscoverable && current.Active &&
            old.Entity == current.Entity && old.Position == current.Position &&
            old.Horizon == current.Horizon;
        if (sameFootprint)
            success &= VisitRecipients(current.Position,
                Math.Max(HorizonRadius(old.Horizon), HorizonRadius(current.Horizon)),
                entityIndex, old, current, now);
        else if (old.IsDiscoverable)
            success &= VisitRecipients(old.Position, HorizonRadius(old.Horizon), entityIndex, old, current, now);
        if (!sameFootprint && current.Active)
            success &= VisitRecipients(current.Position, HorizonRadius(current.Horizon), entityIndex, old, current, now);
        if (old.IsDiscoverable && current.Active && old.Position != current.Position)
        {
            // The two broadphase queries above are the complete old/new footprint union.
            // This branch is intentionally diagnostic only; it makes movement accounting
            // explicit without introducing a subscriber reverse index.
        }
        return success;
    }

    private bool CanProcessDirtyEntity(
        in DirtyEntityLatestState old,
        in DirtyEntitySlot current)
    {
        int needed = 0;
        NextDiscoveryStamp();
        if (old.IsDiscoverable)
            needed = CountDirtyRecipientRows(old.Position, HorizonRadius(old.Horizon), old, current, needed);
        if (needed <= _freeRowCount && current.Active)
            needed = CountDirtyRecipientRows(current.Position, HorizonRadius(current.Horizon), old, current, needed);
        return needed <= _freeRowCount;
    }

    private int CountDirtyRecipientRows(
        in ReplicationSpatialPoint point,
        int radius,
        in DirtyEntityLatestState old,
        in DirtyEntitySlot current,
        int needed)
    {
        ReplicationSpatialRegionId center = ToRegion(point);
        int cellRadius = CellRadius(radius);
        for (int dx = -cellRadius; dx <= cellRadius; dx++)
            for (int dz = -cellRadius; dz <= cellRadius; dz++)
            {
                if (!TryOffset(center, dx, dz, out ReplicationSpatialRegionId region) ||
                    !_regionMap.TryGetValue(region, out int regionIndex)) continue;
                int recipient = _regions[regionIndex].RecipientHead;
                while (recipient >= 0)
                {
                    int next = _recipients[recipient].RegionNext;
                    if (_recipientStamp[recipient] != _discoveryStamp)
                    {
                        _recipientStamp[recipient] = _discoveryStamp;
                        bool oldRelevant = old.IsDiscoverable &&
                            IsWithinOld(_recipients[recipient].Anchor, old.Position, old.Horizon);
                        bool currentRelevant = IsCurrentRelevant(
                            recipient, current, _recipients[recipient].Anchor);
                        if ((oldRelevant || currentRelevant) && currentRelevant &&
                            !_rowMap.ContainsKey(new DirtyRowKey(recipient, checked((uint)current.Entity.Index))))
                        {
                            needed++;
                            if (needed > _freeRowCount) return needed;
                        }
                    }
                    recipient = next;
                }
            }
        return needed;
    }

    private bool VisitRecipients(
        in ReplicationSpatialPoint point,
        int radius,
        int entityIndex,
        in DirtyEntityLatestState old,
        in DirtyEntitySlot current,
        long now)
    {
        _recipientDiscoveryOperations++;
        NextDiscoveryStamp();
        ReplicationSpatialRegionId center = ToRegion(point);
        int cellRadius = CellRadius(radius);
        for (int dx = -cellRadius; dx <= cellRadius; dx++)
            for (int dz = -cellRadius; dz <= cellRadius; dz++)
            {
                if (!TryOffset(center, dx, dz, out ReplicationSpatialRegionId region)) continue;
                if (!_regionMap.TryGetValue(region, out int regionIndex)) continue;
                if (_attributionEnabled) _attributionCells++;
                int recipient = _regions[regionIndex].RecipientHead;
                while (recipient >= 0)
                {
                    int next = _recipients[recipient].RegionNext;
                    _candidateRecipientsExamined++;
                    _lastDiscoveryCandidates++;
                    if (_attributionEnabled) _attributionCandidates++;
                    if (_recipientStamp[recipient] != _discoveryStamp)
                    {
                        _recipientStamp[recipient] = _discoveryStamp;
                        bool oldRelevant = old.IsDiscoverable && IsWithinOld(_recipients[recipient].Anchor, old.Position, old.Horizon);
                        bool currentRelevant = IsCurrentRelevant(
                            recipient, current, _recipients[recipient].Anchor);
                        if (oldRelevant || currentRelevant)
                        {
                            _interestedRecipientsDiscovered++;
                            if (!EnsureEntityObligation(recipient, entityIndex, current, currentRelevant, now))
                                return false;
                        }
                    }
                    recipient = next;
                }
            }
        return true;
    }

    private bool ReconcileRecipient(
        int recipientIndex,
        in ReplicationSpatialPoint oldAnchor,
        in ReplicationSpatialPoint currentAnchor,
        long now)
    {
        if (!CanReconcileRecipient(recipientIndex, currentAnchor)) return false;
        NextEntityDiscoveryStamp();
        bool success = VisitEntities(oldAnchor, recipientIndex, oldAnchor, currentAnchor, now);
        if (oldAnchor != currentAnchor)
            success &= VisitEntities(currentAnchor, recipientIndex, oldAnchor, currentAnchor, now);

        int row = _recipients[recipientIndex].RowHead;
        int rowVisits = 0;
        while (row >= 0)
        {
            if (++rowVisits > _rows.Length)
                throw new InvalidOperationException("Dirty-entity recipient row list contains a cycle.");
            int next = _rows[row].RecipientNext;
            if (_rows[row].Active)
            {
                int entityIndex = checked((int)_rows[row].Entity.Index);
                ref readonly DirtyEntitySlot entity = ref _entities[entityIndex];
                bool relevant = IsCurrentRelevant(recipientIndex, entity, currentAnchor);
                if (!EnsureEntityObligation(recipientIndex, entityIndex, entity, relevant, now))
                { _capacityFailures++; success = false; }
            }
            row = next;
        }
        return success;
    }

    private bool CanReconcileRecipient(int recipientIndex, in ReplicationSpatialPoint anchor)
    {
        int needed = 0;
        NextEntityDiscoveryStamp();
        ReplicationSpatialRegionId center = ToRegion(anchor);
        int cellRadius = CellRadius(_profile.FarRadiusMillimeters);
        for (int dx = -cellRadius; dx <= cellRadius; dx++)
            for (int dz = -cellRadius; dz <= cellRadius; dz++)
            {
                if (!TryOffset(center, dx, dz, out ReplicationSpatialRegionId region) ||
                    !_regionMap.TryGetValue(region, out int regionIndex)) continue;
                int entity = _regions[regionIndex].EntityHead;
                int entityVisits = 0;
                while (entity >= 0)
                {
                    if (++entityVisits > _entities.Length)
                        throw new InvalidOperationException("Dirty-entity spatial entity list contains a cycle.");
                    int next = _entities[entity].RegionNext;
                    if (_entityStamp[entity] != _entityDiscoveryStamp)
                    {
                        _entityStamp[entity] = _entityDiscoveryStamp;
                        ref readonly DirtyEntitySlot current = ref _entities[entity];
                        if (IsCurrentRelevant(recipientIndex, current, anchor) &&
                            !_rowMap.ContainsKey(new DirtyRowKey(recipientIndex, checked((uint)entity))))
                        {
                            needed++;
                            if (needed > _freeRowCount) return false;
                        }
                    }
                    entity = next;
                }
            }
        return true;
    }

    private bool VisitEntities(
        in ReplicationSpatialPoint anchor,
        int recipientIndex,
        in ReplicationSpatialPoint oldAnchor,
        in ReplicationSpatialPoint currentAnchor,
        long now)
    {
        _entityDiscoveryOperations++;
        ReplicationSpatialRegionId center = ToRegion(anchor);
        int cellRadius = CellRadius(_profile.FarRadiusMillimeters);
        for (int dx = -cellRadius; dx <= cellRadius; dx++)
            for (int dz = -cellRadius; dz <= cellRadius; dz++)
            {
                if (!TryOffset(center, dx, dz, out ReplicationSpatialRegionId region)) continue;
                if (!_regionMap.TryGetValue(region, out int regionIndex)) continue;
                if (_attributionEnabled) _attributionCells++;
                int entity = _regions[regionIndex].EntityHead;
                int entityVisits = 0;
                while (entity >= 0)
                {
                    if (++entityVisits > _entities.Length)
                        throw new InvalidOperationException("Dirty-entity spatial entity list contains a cycle.");
                    int next = _entities[entity].RegionNext;
                    if (_entityStamp[entity] != _entityDiscoveryStamp)
                    {
                        _entityStamp[entity] = _entityDiscoveryStamp;
                        ref readonly DirtyEntitySlot current = ref _entities[entity];
                        bool relevant = IsCurrentRelevant(recipientIndex, current, currentAnchor);
                        if (!EnsureEntityObligation(recipientIndex, entity, current, relevant, now))
                        { _capacityFailures++; return false; }
                    }
                    entity = next;
                }
            }
        return true;
    }

    private bool EnsureEntityObligation(
        int recipientIndex,
        int entityIndex,
        in DirtyEntitySlot entity,
        bool desiredPresent,
        long now)
    {
        if (!entity.Entity.IsValid) return true;
        DirtyRowKey key = new(recipientIndex, checked((uint)entityIndex));
        if (!_rowMap.TryGetValue(key, out int rowIndex))
        {
            if (!desiredPresent) return true;
            if (!TryAllocateRow(recipientIndex, entityIndex, out rowIndex)) return false;
            ref DirtyRecord created = ref _rows[rowIndex];
            created.Entity = entity.Entity;
            created.KnownEntity = default;
            created.KnownCanonicalVersion = 0;
            created.KnownPresent = false;
            created.DesiredPresent = true;
            created.Pending = true;
            created.PendingPresent = true;
            created.PendingCanonicalVersion = entity.CanonicalVersion;
            created.PendingHorizon = entity.Horizon;
            created.PendingPosition = entity.Position;
            created.PendingSimulationState = entity.Projected;
            created.PendingTier = DetermineTier(_recipients[recipientIndex].Anchor, entity, GameInterestTier.None);
            created.EligibleAtMilliseconds = DueAt(now, created.PendingTier);
            _pendingCount++;
            _obligationsCreated++;
            _entrantPresents++;
            return true;
        }

        ref DirtyRecord record = ref _rows[rowIndex];
        if (!record.Active) return true;
        bool wasDesired = record.DesiredPresent;
        bool sameKnown = record.KnownPresent == desiredPresent &&
            record.KnownEntity == entity.Entity && record.KnownCanonicalVersion >= entity.CanonicalVersion;
        record.Entity = entity.Entity;
        record.DesiredPresent = desiredPresent;
        if (!desiredPresent)
        {
            record.PendingPresent = false;
            record.PendingCanonicalVersion = entity.CanonicalVersion;
            record.PendingHorizon = GameReplicationHorizon.None;
            record.PendingPosition = default;
            record.PendingSimulationState = default;
            record.PendingTier = GameInterestTier.Immediate;
            if (!sameKnown || record.Pending)
            {
                if (record.Pending)
                {
                    SupersedePending(ref record, now, false, entity);
                }
                else if (record.KnownPresent)
                {
                    CreatePending(ref record, now, false, entity);
                    _leaverRemovals++;
                }
            }
            else if (!record.KnownPresent)
            {
                RemoveRow(rowIndex);
            }
            return true;
        }

        record.PendingPresent = true;
        record.PendingCanonicalVersion = entity.CanonicalVersion;
        record.PendingHorizon = entity.Horizon;
        record.PendingPosition = entity.Position;
        record.PendingSimulationState = entity.Projected;
        GameInterestTier tier = DetermineTier(_recipients[recipientIndex].Anchor, entity, record.PendingTier);
        if (!sameKnown)
        {
            if (!record.Pending) CreatePending(ref record, now, true, entity, tier);
            else if (record.PendingWasSent) SupersedePending(ref record, now, true, entity, tier);
            else
            {
                record.PendingTier = tier;
                _unsentCoalesces++;
            }
        }
        else if (record.Pending && !record.PendingWasSent)
        {
            // A topology reconciliation can discover that a previously pending value is
            // already the newest truth; retaining the row is harmless, but never demote it.
            record.PendingTier = tier;
        }
        if (!wasDesired && desiredPresent) _entrantPresents++;
        return true;
    }

    private void CreatePending(
        ref DirtyRecord record,
        long now,
        bool present,
        in DirtyEntitySlot entity,
        GameInterestTier tier = GameInterestTier.Immediate)
    {
        if (!record.Pending) _pendingCount++;
        record.Pending = true;
        record.PendingPresent = present;
        record.PendingCanonicalVersion = entity.CanonicalVersion;
        record.PendingHorizon = present ? entity.Horizon : GameReplicationHorizon.None;
        record.PendingPosition = present ? entity.Position : default;
        record.PendingSimulationState = present ? entity.Projected : default;
        record.PendingTier = present ? tier : GameInterestTier.Immediate;
        record.EligibleAtMilliseconds = DueAt(now, record.PendingTier);
        record.PendingDeliveryId = 0;
        record.PendingWasSent = false;
    }

    private void SupersedePending(
        ref DirtyRecord record,
        long now,
        bool present,
        in DirtyEntitySlot entity,
        GameInterestTier tier = GameInterestTier.Immediate)
    {
        if (record.PendingWasSent) _sentSupersessions++; else _unsentCoalesces++;
        record.PendingPresent = present;
        record.PendingCanonicalVersion = entity.CanonicalVersion;
        record.PendingHorizon = present ? entity.Horizon : GameReplicationHorizon.None;
        record.PendingPosition = present ? entity.Position : default;
        record.PendingSimulationState = present ? entity.Projected : default;
        record.PendingTier = present ? tier : GameInterestTier.Immediate;
        record.EligibleAtMilliseconds = DueAt(now, record.PendingTier);
        record.PendingDeliveryId = 0;
        record.PendingWasSent = false;
    }

    private int SelectRows(int recipientIndex, long now, Span<int> destination)
    {
        int selected = 0;
        for (GameInterestTier tier = GameInterestTier.Immediate; tier >= GameInterestTier.Far; tier--)
        {
            int row = _recipients[recipientIndex].RowHead;
            while (row >= 0)
            {
                ref readonly DirtyRecord record = ref _rows[row];
                if (record.Active && record.Pending && !record.PendingWasSent &&
                    record.PendingTier == tier)
                {
                    if (record.EligibleAtMilliseconds > now)
                    {
                        _cadenceDeferrals++;
                    }
                    else if (selected < destination.Length)
                    {
                        destination[selected++] = row;
                    }
                }
                row = record.RecipientNext;
            }
        }
        return selected;
    }

    private void ExpireBundles(long now)
    {
        for (int index = 0; index < _bundles.Length; index++)
        {
            if (!_bundles[index].Active || now - _bundles[index].SentAtMilliseconds < _profile.ApplicationAckTimeoutMilliseconds)
                continue;
            _timedOutDeliveries++;
            ReleaseBundle(index, retry: true);
        }
    }

    private bool TryAllocateRecipient(out int index)
    {
        if (_freeRecipientCount == 0) { index = -1; return false; }
        index = _freeRecipients[--_freeRecipientCount];
        return true;
    }

    private void FreeRecipient(int index)
    {
        _recipients[index] = default;
        _freeRecipients[_freeRecipientCount++] = index;
    }

    private bool TryAllocateRow(int recipientIndex, int entityIndex, out int index)
    {
        if (_freeRowCount == 0) { index = -1; return false; }
        index = _freeRows[--_freeRowCount];
        ref DirtyRecord row = ref _rows[index];
        uint generation = _rowGenerations[index] == uint.MaxValue ? 0 : _rowGenerations[index] + 1;
        if (generation == 0) { _freeRows[_freeRowCount++] = index; return false; }
        _rowGenerations[index] = generation;
        row = new DirtyRecord { Active = true, Recipient = recipientIndex, EntityIndex = entityIndex, RecipientNext = -1 };
        row.RecipientNext = _recipients[recipientIndex].RowHead;
        _recipients[recipientIndex].RowHead = index;
        _rowMap.Add(new DirtyRowKey(recipientIndex, checked((uint)entityIndex)), index);
        _rowCount++;
        if (_attributionEnabled) _attributionRowsCreated++;
        return true;
    }

    private void RemoveRow(int index)
    {
        if ((uint)index >= (uint)_rows.Length || !_rows[index].Active) return;
        int recipient = _rows[index].Recipient;
        int previous = -1;
        int cursor = _recipients[recipient].RowHead;
        int visits = 0;
        while (cursor >= 0 && cursor != index)
        {
            if (++visits > _rows.Length)
                throw new InvalidOperationException("Dirty-entity recipient row list contains a cycle.");
            previous = cursor;
            cursor = _rows[cursor].RecipientNext;
        }
        if (cursor == index)
        {
            if (previous < 0) _recipients[recipient].RowHead = _rows[index].RecipientNext;
            else _rows[previous].RecipientNext = _rows[index].RecipientNext;
        }
        _rowMap.Remove(new DirtyRowKey(recipient, checked((uint)_rows[index].EntityIndex)));
        _rows[index] = default;
        _freeRows[_freeRowCount++] = index;
        _rowCount--;
        if (_attributionEnabled) _attributionRowsRemoved++;
    }

    private bool TryAllocateBundle(out int index)
    {
        if (_freeBundleCount == 0) { index = -1; return false; }
        index = _freeBundles[--_freeBundleCount];
        return true;
    }

    private void FreeBundle(int index)
    {
        _bundles[index] = default;
        _freeBundles[_freeBundleCount++] = index;
    }

    private void ReleaseBundle(int index, bool retry = false)
    {
        if (!_bundles[index].Active) return;
        int baseItem = index * _limits.MaximumRecordsPerBundle;
        ref DirtyBundleState bundle = ref _bundles[index];
        for (int itemIndex = 0; itemIndex < bundle.Count; itemIndex++)
        {
            DirtyBundleItem item = _bundleItems[baseItem + itemIndex];
            ref DirtyRecord row = ref _rows[item.RowIndex];
            if (row.Active && _rowGenerations[item.RowIndex] == item.RowGeneration &&
                row.Pending && row.PendingDeliveryId == item.DeliveryId && retry)
            {
                row.PendingDeliveryId = 0;
                row.PendingWasSent = false;
            }
            _bundleItems[baseItem + itemIndex] = default;
        }
        _bundleMap.Remove(bundle.BundleId);
        _recipients[bundle.Recipient].OutstandingBundleCount--;
        _bundles[index] = default;
        _freeBundles[_freeBundleCount++] = index;
        _bundleCount--;
    }

    private void LinkEntity(int entityIndex, int regionIndex)
    {
        ref DirtyEntitySlot entity = ref _entities[entityIndex];
        entity.Region = regionIndex;
        entity.RegionPrevious = -1;
        entity.RegionNext = _regions[regionIndex].EntityHead;
        if (entity.RegionNext >= 0) _entities[entity.RegionNext].RegionPrevious = entityIndex;
        _regions[regionIndex].EntityHead = entityIndex;
    }

    private void UnlinkEntity(int entityIndex)
    {
        ref DirtyEntitySlot entity = ref _entities[entityIndex];
        if (entity.Region < 0) return;
        if (entity.RegionPrevious >= 0) _entities[entity.RegionPrevious].RegionNext = entity.RegionNext;
        else _regions[entity.Region].EntityHead = entity.RegionNext;
        if (entity.RegionNext >= 0) _entities[entity.RegionNext].RegionPrevious = entity.RegionPrevious;
        int region = entity.Region;
        entity.Region = -1; entity.RegionPrevious = -1; entity.RegionNext = -1;
        PruneRegion(region);
    }

    private void LinkRecipient(int recipientIndex, int regionIndex)
    {
        ref RecipientSlot recipient = ref _recipients[recipientIndex];
        recipient.RegionPrevious = -1;
        recipient.RegionNext = _regions[regionIndex].RecipientHead;
        if (recipient.RegionNext >= 0) _recipients[recipient.RegionNext].RegionPrevious = recipientIndex;
        _regions[regionIndex].RecipientHead = recipientIndex;
    }

    private void UnlinkRecipient(int recipientIndex)
    {
        ref RecipientSlot recipient = ref _recipients[recipientIndex];
        int region = recipient.Region;
        if (recipient.RegionPrevious >= 0) _recipients[recipient.RegionPrevious].RegionNext = recipient.RegionNext;
        else _regions[region].RecipientHead = recipient.RegionNext;
        if (recipient.RegionNext >= 0) _recipients[recipient.RegionNext].RegionPrevious = recipient.RegionPrevious;
        recipient.RegionPrevious = -1; recipient.RegionNext = -1;
        PruneRegion(region);
    }

    private bool TryGetOrCreateRegion(ReplicationSpatialRegionId key, out int index)
    {
        if (_regionMap.TryGetValue(key, out index)) return true;
        if (_freeRegionCount == 0) { index = -1; return false; }
        index = _freeRegions[--_freeRegionCount];
        _regions[index] = new SpatialRegion { Active = true, Key = key, EntityHead = -1, RecipientHead = -1 };
        _regionMap.Add(key, index);
        _regionCount++;
        return true;
    }

    private int RegionIndex(ReplicationSpatialRegionId key) =>
        _regionMap.TryGetValue(key, out int index) ? index : -1;

    private void PruneRegion(int index)
    {
        if (index < 0 || !_regions[index].Active || _regions[index].EntityHead >= 0 || _regions[index].RecipientHead >= 0) return;
        _regionMap.Remove(_regions[index].Key);
        _regions[index] = default;
        _freeRegions[_freeRegionCount++] = index;
        _regionCount--;
    }

    private DirtyEntityUpdateStatus CapacityFailure()
    {
        _capacityFailures++;
        return DirtyEntityUpdateStatus.CapacityExceeded;
    }

    private void NextDiscoveryStamp()
    {
        if (_discoveryStamp == int.MaxValue) { Array.Clear(_recipientStamp); _discoveryStamp = 1; }
        else _discoveryStamp++;
    }

    private void NextEntityDiscoveryStamp()
    {
        if (_entityDiscoveryStamp == int.MaxValue) { Array.Clear(_entityStamp); _entityDiscoveryStamp = 1; }
        else _entityDiscoveryStamp++;
    }

    private ReplicationSpatialRegionId ToRegion(in ReplicationSpatialPoint point) => new(
        checked((int)FloorDivide(point.XMillimeters, _profile.RegionEdgeMillimeters)),
        checked((int)FloorDivide(point.ZMillimeters, _profile.RegionEdgeMillimeters)));

    private bool TryOffset(in ReplicationSpatialRegionId center, int dx, int dz, out ReplicationSpatialRegionId result)
    {
        long x = (long)center.X + dx;
        long z = (long)center.Z + dz;
        if (x < int.MinValue || x > int.MaxValue || z < int.MinValue || z > int.MaxValue)
        { result = default; return false; }
        result = new((int)x, (int)z);
        return true;
    }

    private int CellRadius(int radius) => checked((radius + _profile.RegionEdgeMillimeters - 1) / _profile.RegionEdgeMillimeters);

    private bool IsCurrentRelevant(
        int recipientIndex,
        in DirtyEntitySlot entity,
        in ReplicationSpatialPoint anchor)
    {
        if (!entity.Active) return false;
        bool existingSameGeneration = false;
        DirtyRowKey key = new(recipientIndex, checked((uint)entity.Entity.Index));
        if (_rowMap.TryGetValue(key, out int rowIndex))
        {
            ref readonly DirtyRecord row = ref _rows[rowIndex];
            existingSameGeneration = row.Active && row.Entity == entity.Entity &&
                (row.KnownPresent || row.DesiredPresent);
        }
        int radius = entity.Horizon switch
        {
            GameReplicationHorizon.Immediate => existingSameGeneration
                ? _profile.ImmediateLeaveRadiusMillimeters
                : _profile.ImmediateEnterRadiusMillimeters,
            GameReplicationHorizon.Near => _profile.NearRadiusMillimeters,
            GameReplicationHorizon.Far => _profile.FarRadiusMillimeters,
            _ => 0,
        };
        return radius > 0 && DistanceSquared(anchor, entity.Position) <= (Int128)radius * radius;
    }

    private bool IsWithinOld(in ReplicationSpatialPoint anchor, in ReplicationSpatialPoint entity, GameReplicationHorizon horizon)
    {
        int radius = horizon == GameReplicationHorizon.Immediate
            ? _profile.ImmediateLeaveRadiusMillimeters
            : HorizonRadius(horizon);
        return radius > 0 && DistanceSquared(anchor, entity) <= (Int128)radius * radius;
    }

    private GameInterestTier DetermineTier(
        in ReplicationSpatialPoint anchor,
        in DirtyEntitySlot entity,
        GameInterestTier previous)
    {
        if (!entity.Active) return GameInterestTier.Immediate;
        if (DistanceSquared(anchor, entity.Position) <= (Int128)_profile.ImmediateEnterRadiusMillimeters * _profile.ImmediateEnterRadiusMillimeters)
            return GameInterestTier.Immediate;
        if (previous == GameInterestTier.Immediate &&
            DistanceSquared(anchor, entity.Position) <= (Int128)_profile.ImmediateLeaveRadiusMillimeters * _profile.ImmediateLeaveRadiusMillimeters)
            return GameInterestTier.Immediate;
        if (DistanceSquared(anchor, entity.Position) <= (Int128)_profile.NearRadiusMillimeters * _profile.NearRadiusMillimeters)
            return GameInterestTier.Near;
        return GameInterestTier.Far;
    }

    private long DueAt(long now, GameInterestTier tier) => tier switch
    {
        GameInterestTier.Immediate => now,
        GameInterestTier.Near => checked(now + _profile.NearIntervalMilliseconds),
        GameInterestTier.Far => checked(now + _profile.FarIntervalMilliseconds),
        _ => now,
    };

    private int HorizonRadius(GameReplicationHorizon horizon) => horizon switch
    {
        GameReplicationHorizon.Immediate => _profile.ImmediateLeaveRadiusMillimeters,
        GameReplicationHorizon.Near => _profile.NearRadiusMillimeters,
        GameReplicationHorizon.Far => _profile.FarRadiusMillimeters,
        _ => 0,
    };

    private static Int128 DistanceSquared(in ReplicationSpatialPoint left, in ReplicationSpatialPoint right)
    {
        Int128 x = (Int128)left.XMillimeters - right.XMillimeters;
        Int128 z = (Int128)left.ZMillimeters - right.ZMillimeters;
        return x * x + z * z;
    }

    private static long FloorDivide(int value, int divisor)
    {
        long quotient = value / divisor;
        if (value < 0 && value % divisor != 0) quotient--;
        return quotient;
    }

    private long CalculateReservedBytes() => checked(
        (long)_entities.Length * Unsafe.SizeOf<DirtyEntitySlot>() +
        (long)_recipients.Length * Unsafe.SizeOf<RecipientSlot>() +
        (long)_regions.Length * Unsafe.SizeOf<SpatialRegion>() +
        (long)_rows.Length * Unsafe.SizeOf<DirtyRecord>() +
        (long)_rowGenerations.Length * sizeof(uint) +
        (long)_bundles.Length * Unsafe.SizeOf<DirtyBundleState>() +
        (long)_bundleItems.Length * Unsafe.SizeOf<DirtyBundleItem>() +
        (long)_selectionScratch.Length * sizeof(int) +
        (long)_dirtyEntities.Length * sizeof(int) +
        (long)_recipientStamp.Length * sizeof(int) +
        (long)_entityStamp.Length * sizeof(int) +
        (long)_freeRecipients.Length * sizeof(int) +
        (long)_freeRegions.Length * sizeof(int) +
        (long)_freeRows.Length * sizeof(int) +
        (long)_freeBundles.Length * sizeof(int) +
        DictionaryReservation(_recipientMapCapacity, Unsafe.SizeOf<ParticipantId>()) +
        DictionaryReservation(_regionMapCapacity, Unsafe.SizeOf<ReplicationSpatialRegionId>()) +
        DictionaryReservation(_rowMapCapacity, Unsafe.SizeOf<DirtyRowKey>()) +
        DictionaryReservation(_bundleMapCapacity, sizeof(ulong)));

    private static long DictionaryReservation(int capacity, int keySize) => checked(
        (long)capacity * (sizeof(int) * 3L + keySize + sizeof(int)));

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("DirtyEntitySpatialFanout is owner-thread-bound.");
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private static void SaturatingAdd(ref ulong value, ulong amount)
    {
        value = ulong.MaxValue - value < amount ? ulong.MaxValue : value + amount;
    }

    private readonly record struct DirtyRowKey(int Recipient, uint EntityIndex);

    private struct SpatialRegion
    {
        internal bool Active;
        internal ReplicationSpatialRegionId Key;
        internal int EntityHead;
        internal int RecipientHead;
    }

    private struct DirtyEntitySlot
    {
        internal bool Active;
        internal bool IsPublic;
        internal bool DirtyQueued;
        internal ReplicationEntityId Entity;
        internal ulong CanonicalVersion;
        internal GameReplicationHorizon Horizon;
        internal ReplicationSpatialPoint Position;
        internal GameSimulationState Projected;
        internal int Region;
        internal int RegionPrevious;
        internal int RegionNext;
        internal DirtyEntityLatestState DirtyOld;
    }

    private struct RecipientSlot
    {
        internal bool Active;
        internal ParticipantId Participant;
        internal ReplicationSpatialPoint Anchor;
        internal int Region;
        internal int RegionPrevious;
        internal int RegionNext;
        internal int RowHead;
        internal int OutstandingBundleCount;
    }

    private struct DirtyRecord
    {
        internal bool Active;
        internal int Recipient;
        internal int EntityIndex;
        internal int RecipientNext;
        internal ReplicationEntityId Entity;
        internal ReplicationEntityId KnownEntity;
        internal ulong KnownCanonicalVersion;
        internal bool KnownPresent;
        internal bool DesiredPresent;
        internal bool Pending;
        internal bool PendingPresent;
        internal ulong PendingCanonicalVersion;
        internal GameReplicationHorizon PendingHorizon;
        internal ReplicationSpatialPoint PendingPosition;
        internal GameSimulationState PendingSimulationState;
        internal GameInterestTier PendingTier;
        internal long EligibleAtMilliseconds;
        internal bool PendingWasSent;
        internal ulong PendingDeliveryId;
        internal long PendingSentAtMilliseconds;
    }

    private struct DirtyBundleState
    {
        internal bool Active;
        internal int Recipient;
        internal ulong BundleId;
        internal int Count;
        internal long SentAtMilliseconds;
    }

    private readonly record struct DirtyBundleItem(
        int RowIndex,
        uint RowGeneration,
        ulong DeliveryId,
        ReplicationEntityId Entity,
        ulong CanonicalVersion,
        bool IsPresent);
}
