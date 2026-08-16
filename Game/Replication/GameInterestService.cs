using System.Diagnostics;
using System.Runtime.CompilerServices;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Network;

namespace UltraSim.Game.Replication;

/// <summary>
/// Single-owner bounded spatial broadphase, maintained relevance graph, and
/// newest-version recipient scheduler.
/// </summary>
/// <remarks>
/// Regions index space; compact recipient/entity relations own relevance and
/// delivery debt. Canonical state is supplied once by the game projection and
/// is never reread for each recipient. Application acknowledgement, rather than
/// packet transmission, advances recipient-known state.
/// </remarks>
public sealed class GameInterestService
{
    private const int BundleHeaderEstimate = sizeof(ulong) * 3 + sizeof(byte);

    private readonly GameInterestLimits _limits;
    private readonly GameInterestProfile _profile;
    private readonly int _immediateCellRadius;
    private readonly int _nearCellRadius;
    private readonly int _farCellRadius;
    private readonly int _maximumOutstandingBundlesPerRecipient;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly EntityState[] _entities;
    private readonly RecipientState[] _recipients;
    private readonly RegionState[] _regions;
    private readonly RegionInterestEdge[] _regionEdges;
    private readonly RelationState[] _relations;
    private readonly uint[] _relationGenerations;
    private readonly SentBundleState[] _bundles;
    private readonly SentBundleItem[] _bundleItems;
    private readonly int[] _freeRecipients;
    private readonly int[] _freeRegions;
    private readonly int[] _freeRegionEdges;
    private readonly int[] _freeRelations;
    private readonly int[] _freeBundles;
    private readonly Dictionary<ParticipantId, int> _recipientMap;
    private readonly Dictionary<ReplicationSpatialRegionId, int> _regionMap;
    private readonly Dictionary<RecipientRegionKey, int> _regionEdgeMap;
    private readonly Dictionary<RecipientEntityKey, int> _relationMap;
    private readonly Dictionary<ulong, int> _bundleMap;
    private readonly int _recipientMapCapacity;
    private readonly int _regionMapCapacity;
    private readonly int _regionEdgeMapCapacity;
    private readonly int _relationMapCapacity;
    private readonly int _bundleMapCapacity;

    private readonly StateEpoch _stateEpoch;
    private int _recipientHigh;
    private int _regionHigh;
    private int _regionEdgeHigh;
    private int _relationHigh;
    private int _bundleHigh;
    private int _freeRecipientCount;
    private int _freeRegionCount;
    private int _freeRegionEdgeCount;
    private int _freeRelationCount;
    private int _freeBundleCount;
    private int _entityCount;
    private int _recipientCount;
    private int _regionCount;
    private int _regionEdgeCount;
    private int _relationCount;
    private int _pendingCount;
    private int _bundleCount;
    private int _nonviableRecipients;
    private int _nextRecipientSchedule;
    private uint _regionUpdateStamp;
    private uint _buildStamp;
    private ulong _nextRelationSequence = 1;
    private ulong _nextBundleId = 1;
    private ulong _pendingWrites;
    private ulong _semanticConstructions;
    private ulong _semanticCopies;
    private ulong _semanticBytes;
    private ulong _bundlesBuilt;
    private ulong _bundlesApplied;
    private ulong _bundlesRejected;
    private ulong _timedOutBundles;
    private int _lastRegionEdgesAdded;
    private int _lastRegionEdgesRemoved;
    private int _lastRegionEdgesRetiered;
    private int _lastRelationsChanged;
    private int _lastRelationsExamined;
    private bool _attributionEnabled;
    private ulong _attributionEntityUpserts;
    private ulong _attributionExistingRelationVisits;
    private ulong _attributionReverseRegionEdgeVisits;
    private ulong _attributionRelationEnsureLookups;
    private ulong _attributionPendingRelationVisits;
    private ulong _attributionActualRelationChanges;
    private ulong _attributionPendingVersionRewrites;
    private ulong _attributionEntityCanonicalTicks;
    private ulong _attributionRelationMaintenanceTicks;
    private ulong _attributionBundleBuildCalls;
    private ulong _attributionBundleBuildSuccesses;
    private ulong _attributionBundleBuildNoData;
    private ulong _attributionBundleBuildOutstandingCapacity;
    private ulong _attributionBundleBuildOtherFailures;
    private ulong _attributionPendingImmediateExamined;
    private ulong _attributionPendingNearExamined;
    private ulong _attributionPendingFarExamined;
    private ulong _attributionPendingSelectedImmediate;
    private ulong _attributionPendingSelectedNear;
    private ulong _attributionPendingSelectedFar;
    private ulong _attributionPendingSkippedAlreadySelected;
    private ulong _attributionPendingSkippedCadence;
    private ulong _attributionPendingSkippedAwaitingRetry;
    private ulong _attributionBundleStopsNoEligibleDebt;
    private ulong _attributionBundleStopsRecordLimit;
    private ulong _attributionBundleStopsByteLimit;
    private ulong _attributionBundleRecords;
    private ulong _attributionBundleEstimatedBytes;
    private int _attributionMaximumBundleRecords;
    private int _attributionMaximumBundleEstimatedBytes;
    private ulong _attributionApplicationAcknowledgements;
    private ulong _attributionAcknowledgementRecordsExamined;
    private ulong _attributionAcknowledgementTicks;
    private readonly ulong[] _attributionBundleRecordHistogram;

    public GameInterestService(
        StateEpoch stateEpoch,
        GameInterestLimits limits = default,
        GameInterestProfile profile = default)
    {
        if (!stateEpoch.IsValid) throw new ArgumentException("A valid projection epoch is required.", nameof(stateEpoch));
        if (limits == default) limits = GameInterestLimits.Default;
        if (profile == default) profile = GameInterestProfile.Reference;
        limits.Validate();
        profile.Validate();
        _limits = limits;
        _profile = profile;
        _stateEpoch = stateEpoch;
        _immediateCellRadius = CandidateCellRadius(
            profile.ImmediateLeaveRadiusMillimeters, profile.RegionEdgeMillimeters);
        _nearCellRadius = CellRadius(profile.NearRadiusMillimeters, profile.RegionEdgeMillimeters);
        _farCellRadius = CellRadius(profile.FarRadiusMillimeters, profile.RegionEdgeMillimeters);
        _maximumOutstandingBundlesPerRecipient = Math.Max(
            1, limits.MaximumOutstandingBundles / limits.MaximumRecipients);
        if (_farCellRadius > 1_024)
            throw new ArgumentOutOfRangeException(nameof(profile), "The far region envelope exceeds 1,024 cells per axis.");

        _entities = new EntityState[limits.MaximumEntities];
        _recipients = new RecipientState[limits.MaximumRecipients];
        _regions = new RegionState[limits.MaximumRegions];
        _regionEdges = new RegionInterestEdge[limits.MaximumRegionInterestEdges];
        _relations = new RelationState[limits.MaximumRecipientEntityRelations];
        _relationGenerations = new uint[limits.MaximumRecipientEntityRelations];
        _bundles = new SentBundleState[limits.MaximumOutstandingBundles];
        _bundleItems = new SentBundleItem[checked(
            limits.MaximumOutstandingBundles * limits.MaximumRecordsPerBundle)];
        _freeRecipients = new int[limits.MaximumRecipients];
        _freeRegions = new int[limits.MaximumRegions];
        _freeRegionEdges = new int[limits.MaximumRegionInterestEdges];
        _freeRelations = new int[limits.MaximumRecipientEntityRelations];
        _freeBundles = new int[limits.MaximumOutstandingBundles];

        _recipientMap = new Dictionary<ParticipantId, int>(limits.MaximumRecipients);
        _regionMap = new Dictionary<ReplicationSpatialRegionId, int>(limits.MaximumRegions);
        _regionEdgeMap = new Dictionary<RecipientRegionKey, int>(limits.MaximumRegionInterestEdges);
        _relationMap = new Dictionary<RecipientEntityKey, int>(limits.MaximumRecipientEntityRelations);
        _bundleMap = new Dictionary<ulong, int>(limits.MaximumOutstandingBundles);
        _recipientMapCapacity = _recipientMap.EnsureCapacity(limits.MaximumRecipients);
        _regionMapCapacity = _regionMap.EnsureCapacity(limits.MaximumRegions);
        _regionEdgeMapCapacity = _regionEdgeMap.EnsureCapacity(limits.MaximumRegionInterestEdges);
        _relationMapCapacity = _relationMap.EnsureCapacity(limits.MaximumRecipientEntityRelations);
        _bundleMapCapacity = _bundleMap.EnsureCapacity(limits.MaximumOutstandingBundles);
        _attributionBundleRecordHistogram = new ulong[limits.MaximumRecordsPerBundle + 1];
    }

    public StateEpoch StateEpoch
    {
        get { EnsureOwnerThread(); return _stateEpoch; }
    }

    public long ApplicationAckRetryMilliseconds
    {
        get { EnsureOwnerThread(); return _profile.ApplicationAckRetryMilliseconds; }
    }

    public long ApplicationAckTimeoutMilliseconds
    {
        get { EnsureOwnerThread(); return _profile.ApplicationAckTimeoutMilliseconds; }
    }

    /// <summary>Latest globally issued recipient-relation sequence in this projection epoch.</summary>
    public ulong LatestRelationSequence
    {
        get { EnsureOwnerThread(); return _nextRelationSequence - 1; }
    }

    /// <summary>Adds or moves one game-owned 360-degree observation anchor.</summary>
    public GameInterestUpdateStatus SetRecipientAnchor(
        ParticipantId participant,
        in ReplicationSpatialPoint anchor,
        long nowMilliseconds)
    {
        EnsureOwnerThread();
        ResetLastMutationCounters();
        if (!participant.IsValid || nowMilliseconds < 0)
            return GameInterestUpdateStatus.InvalidInput;
        ReplicationSpatialRegionId region = ToRegion(anchor);
        int anchorMargin = Math.Max(_farCellRadius, _immediateCellRadius);
        if (region.X < int.MinValue + anchorMargin || region.X > int.MaxValue - anchorMargin ||
            region.Z < int.MinValue + anchorMargin || region.Z > int.MaxValue - anchorMargin)
            return GameInterestUpdateStatus.InvalidInput;

        bool created = false;
        if (!_recipientMap.TryGetValue(participant, out int recipientIndex))
        {
            if (!TryAllocateRecipient(out recipientIndex))
                return GameInterestUpdateStatus.CapacityExceeded;
            created = true;
            _recipients[recipientIndex] = new RecipientState
            {
                Active = true,
                Participant = participant,
                RegionEdgeHead = -1,
                RelationHead = -1,
                ImmediateRelationHead = -1,
                PendingImmediateHead = -1,
                PendingImmediateTail = -1,
                PendingNearHead = -1,
                PendingNearTail = -1,
                PendingFarHead = -1,
                PendingFarTail = -1,
            };
            _recipientMap.Add(participant, recipientIndex);
            _recipientCount++;
        }

        ref RecipientState recipient = ref _recipients[recipientIndex];
        if (recipient.Nonviable)
            return GameInterestUpdateStatus.RecipientNonviable;
        bool crossedRegion = !recipient.HasAnchor || recipient.AnchorRegion != region;
        bool moved = !recipient.HasAnchor || recipient.Anchor != anchor;
        if (!moved) return GameInterestUpdateStatus.NoChange;

        recipient.Anchor = anchor;
        recipient.AnchorRegion = region;
        recipient.HasAnchor = true;
        GameInterestUpdateStatus status = crossedRegion
            ? RebuildRecipientRegionEnvelope(recipientIndex, nowMilliseconds)
            : ReconcileRecipientImmediate(recipientIndex, nowMilliseconds);
        if (status != GameInterestUpdateStatus.Success && created)
            MarkRecipientNonviable(recipientIndex);
        return status;
    }

    /// <summary>Removes a disconnected recipient and all generation-local delivery state immediately.</summary>
    public bool RemoveRecipient(ParticipantId participant)
    {
        EnsureOwnerThread();
        if (!_recipientMap.TryGetValue(participant, out int recipientIndex)) return false;
        RemoveRecipientInternal(recipientIndex);
        return true;
    }

    /// <summary>
    /// Marks a recipient unable to represent its admitted projection scope.
    /// Further state scheduling stops until a later baseline/rescope mechanism
    /// replaces this generation-local recipient.
    /// </summary>
    public bool MarkRecipientProjectionNonviable(ParticipantId participant)
    {
        EnsureOwnerThread();
        if (!_recipientMap.TryGetValue(participant, out int recipientIndex)) return false;
        MarkRecipientNonviable(recipientIndex);
        return true;
    }

    /// <summary>
    /// Copies one complete, sorted present-state cutoff for a recipient.
    /// Existing pending sequences are reused so post-cutoff changes remain
    /// distinguishable and baseline acknowledgement cannot clear newer debt.
    /// </summary>
    public GameWorldBaselineCaptureStatus CopyRecipientBaseline(
        ParticipantId participant,
        Span<GameWorldStateRecord> destination,
        out int count)
    {
        EnsureOwnerThread();
        count = 0;
        if (!_recipientMap.TryGetValue(participant, out int recipientIndex))
            return GameWorldBaselineCaptureStatus.UnknownRecipient;
        if (_recipients[recipientIndex].Nonviable)
            return GameWorldBaselineCaptureStatus.RecipientNonviable;

        int relation = _recipients[recipientIndex].RelationHead;
        while (relation >= 0)
        {
            ref readonly RelationState state = ref _relations[relation];
            if (state.DesiredPresent)
            {
                if (count == destination.Length)
                {
                    destination[..count].Clear();
                    count = 0;
                    return GameWorldBaselineCaptureStatus.DestinationTooSmall;
                }
                ulong sequence = state.Pending && state.PendingPresent
                    ? state.PendingRelationSequence
                    : state.KnownRelationSequence;
                if (sequence == 0)
                {
                    destination[..count].Clear();
                    count = 0;
                    return GameWorldBaselineCaptureStatus.SequenceExhausted;
                }
                ref readonly EntityState entity = ref _entities[state.EntitySlot];
                if (!entity.Active || entity.Entity != state.Entity)
                    throw new InvalidOperationException("A desired relation lost its canonical entity.");
                destination[count++] = new(
                    sequence,
                    entity.Entity,
                    entity.CanonicalVersion,
                    true,
                    entity.Horizon,
                    entity.Position,
                    entity.Projected.SimulationState);
            }
            relation = state.RecipientNext;
        }
        destination[..count].Sort(static (left, right) =>
        {
            int index = left.Entity.Index.CompareTo(right.Entity.Index);
            return index != 0 ? index : left.Entity.Generation.CompareTo(right.Entity.Generation);
        });
        return GameWorldBaselineCaptureStatus.Success;
    }

    /// <summary>
    /// Advances recipient knowledge to an atomically applied baseline cutoff.
    /// Newer relation debt produced after capture is deliberately retained.
    /// </summary>
    public bool TryAcknowledgeBaseline(
        ParticipantId participant,
        ReadOnlySpan<GameWorldStateRecord> records,
        ulong relationSequenceFloor)
    {
        EnsureOwnerThread();
        if (!participant.IsValid ||
            !_recipientMap.TryGetValue(participant, out int recipientIndex))
            return false;
        for (int index = 0; index < records.Length; index++)
        {
            ref readonly GameWorldStateRecord item = ref records[index];
            if (!_relationMap.TryGetValue(
                    new RecipientEntityKey(recipientIndex, item.Entity.Index), out int relationIndex))
                continue;
            ref RelationState relation = ref _relations[relationIndex];
            if (!relation.Active || relation.Entity != item.Entity) continue;
            if (item.RelationSequence > relation.KnownRelationSequence)
            {
                relation.KnownRelationSequence = item.RelationSequence;
                relation.KnownCanonicalVersion = item.CanonicalVersion;
                relation.KnownPresent = true;
            }
            if (relation.Pending && relation.PendingPresent &&
                relation.PendingRelationSequence == item.RelationSequence)
                ClearPending(relationIndex);
        }

        // A complete-scope baseline also proves that every omitted identity was
        // absent at the cutoff. Consume tombstone debt at or below that cutoff;
        // anything published later remains pending.
        int relationIndexToInspect = _recipients[recipientIndex].RelationHead;
        while (relationIndexToInspect >= 0)
        {
            ref RelationState relation = ref _relations[relationIndexToInspect];
            int next = relation.RecipientNext;
            if (relation.Active && relation.Pending && !relation.PendingPresent &&
                relation.PendingRelationSequence <= relationSequenceFloor)
            {
                relation.KnownRelationSequence = Math.Max(
                    relation.KnownRelationSequence, relation.PendingRelationSequence);
                relation.KnownCanonicalVersion = relation.PendingCanonicalVersion;
                relation.KnownPresent = false;
                ClearPending(relationIndexToInspect);
                if (!relation.DesiredPresent)
                    RemoveRelation(relationIndexToInspect);
            }
            relationIndexToInspect = next;
        }
        return true;
    }

    /// <summary>Adds or replaces one complete canonical entity version.</summary>
    public GameInterestUpdateStatus UpsertEntity(
        in GameProjectedEntity entity,
        ulong canonicalVersion,
        in ReplicationSpatialPoint position,
        GameReplicationHorizon horizon,
        long nowMilliseconds)
    {
        EnsureOwnerThread();
        long attributionStarted = _attributionEnabled ? Stopwatch.GetTimestamp() : 0;
        if (_attributionEnabled) SaturatingIncrement(ref _attributionEntityUpserts);
        ResetLastMutationCounters();
        if (!entity.Entity.IsValid || entity.Entity.Index >= _entities.Length ||
            canonicalVersion == 0 || nowMilliseconds < 0 ||
            (uint)horizon > (uint)GameReplicationHorizon.Far)
            return GameInterestUpdateStatus.InvalidInput;

        int entityIndex = checked((int)entity.Entity.Index);
        ref EntityState state = ref _entities[entityIndex];
        if (!state.Active && state.Entity.IsValid &&
            entity.Entity.Generation <= state.Entity.Generation)
            return GameInterestUpdateStatus.StaleVersion;
        if (state.Active && state.Entity != entity.Entity)
        {
            if (entity.Entity.Generation <= state.Entity.Generation)
                return GameInterestUpdateStatus.StaleVersion;
            RetireEntity(entityIndex, nowMilliseconds);
        }
        else if (state.Active && canonicalVersion <= state.CanonicalVersion)
        {
            if (canonicalVersion == state.CanonicalVersion && state.Position == position &&
                state.Horizon == horizon && state.Projected == entity)
                return GameInterestUpdateStatus.NoChange;
            return GameInterestUpdateStatus.StaleVersion;
        }

        ReplicationSpatialRegionId nextRegionId = ToRegion(position);
        if (!TryGetOrCreateRegion(nextRegionId, out int nextRegion))
            return GameInterestUpdateStatus.CapacityExceeded;

        bool created = !state.Active;
        int previousRegion = state.Region;
        if (created)
        {
            state = new EntityState
            {
                Active = true,
                Entity = entity.Entity,
                Region = nextRegion,
                RegionPrevious = -1,
                RegionNext = -1,
                RelationHead = -1,
            };
            LinkEntityToRegion(entityIndex, nextRegion);
            _entityCount++;
        }
        else if (previousRegion != nextRegion)
        {
            UnlinkEntityFromRegion(entityIndex);
            state.Region = nextRegion;
            LinkEntityToRegion(entityIndex, nextRegion);
            PruneRegion(previousRegion);
        }

        state.Projected = entity;
        state.CanonicalVersion = canonicalVersion;
        state.Position = position;
        state.Horizon = horizon;
        state.CachedVersion = 0;

        long maintenanceStarted = _attributionEnabled ? Stopwatch.GetTimestamp() : 0;
        if (_attributionEnabled)
            SaturatingAdd(ref _attributionEntityCanonicalTicks,
                checked((ulong)(maintenanceStarted - attributionStarted)));

        if (created || previousRegion != nextRegion)
        {
            int relation = state.RelationHead;
            while (relation >= 0)
            {
                if (_attributionEnabled) SaturatingIncrement(ref _attributionExistingRelationVisits);
                int next = _relations[relation].EntityNext;
                int recipientIndex = _relations[relation].Recipient;
                if (!_regionEdgeMap.TryGetValue(new RecipientRegionKey(recipientIndex, nextRegion), out int edge))
                    SetRelationAbsent(relation, nowMilliseconds);
                else
                    SetRelationTierAndPresence(relation, _regionEdges[edge].Tier, nowMilliseconds);
                relation = next;
            }
        }
        else
        {
            int relation = state.RelationHead;
            while (relation >= 0)
            {
                if (_attributionEnabled) SaturatingIncrement(ref _attributionExistingRelationVisits);
                int next = _relations[relation].EntityNext;
                int recipientIndex = _relations[relation].Recipient;
                int edge = _regionEdgeMap[new RecipientRegionKey(recipientIndex, nextRegion)];
                SetRelationTierAndPresence(relation, _regionEdges[edge].Tier, nowMilliseconds);
                relation = next;
            }
        }

        // Always traverse current reverse region interest. This is required not
        // only for region entry, but when a replication-family horizon expands
        // while the entity remains in the same cell.
        int regionEdge = _regions[nextRegion].InterestHead;
        while (regionEdge >= 0)
        {
            if (_attributionEnabled) SaturatingIncrement(ref _attributionReverseRegionEdgeVisits);
            int next = _regionEdges[regionEdge].RegionNext;
            if (!EnsureDesiredRelation(
                    _regionEdges[regionEdge].Recipient,
                    entityIndex,
                    _regionEdges[regionEdge].Tier,
                    nowMilliseconds))
            {
                MarkRecipientNonviable(_regionEdges[regionEdge].Recipient);
            }
            regionEdge = next;
        }

        int pendingRelation = state.RelationHead;
        while (pendingRelation >= 0)
        {
            if (_attributionEnabled) SaturatingIncrement(ref _attributionPendingRelationVisits);
            int next = _relations[pendingRelation].EntityNext;
            if (_relations[pendingRelation].DesiredPresent &&
                (!_relations[pendingRelation].Pending ||
                 !_relations[pendingRelation].PendingPresent ||
                 _relations[pendingRelation].PendingCanonicalVersion != canonicalVersion))
                MarkRelationPending(pendingRelation, present: true, canonicalVersion, nowMilliseconds);
            pendingRelation = next;
        }

        if (_attributionEnabled)
        {
            SaturatingAdd(ref _attributionRelationMaintenanceTicks,
                checked((ulong)(Stopwatch.GetTimestamp() - maintenanceStarted)));
            SaturatingAdd(ref _attributionActualRelationChanges, checked((ulong)_lastRelationsChanged));
        }
        return AnyAffectedRecipientNonviable(state.Region)
            ? GameInterestUpdateStatus.RecipientNonviable
            : GameInterestUpdateStatus.Success;
    }

    /// <summary>Publishes recoverable absence for every recipient that knew or awaited the entity.</summary>
    public GameInterestUpdateStatus RemoveEntity(
        ReplicationEntityId entity,
        ulong canonicalVersion,
        long nowMilliseconds)
    {
        EnsureOwnerThread();
        ResetLastMutationCounters();
        if (!entity.IsValid || entity.Index >= _entities.Length || canonicalVersion == 0 || nowMilliseconds < 0)
            return GameInterestUpdateStatus.InvalidInput;
        int index = checked((int)entity.Index);
        ref EntityState state = ref _entities[index];
        if (!state.Active || state.Entity != entity) return GameInterestUpdateStatus.StaleVersion;
        if (canonicalVersion <= state.CanonicalVersion) return GameInterestUpdateStatus.StaleVersion;
        state.CanonicalVersion = canonicalVersion;
        RetireEntity(index, nowMilliseconds);
        return GameInterestUpdateStatus.Success;
    }

    /// <summary>Builds one fair recipient bundle from eligible newest-version debt.</summary>
    public GameWorldStateBuildStatus TryBuildNextBundle(
        long nowMilliseconds,
        int byteBudget,
        Span<GameWorldStateRecord> destination,
        out GameWorldStateBundle bundle)
    {
        EnsureOwnerThread();
        bundle = default;
        if (nowMilliseconds < 0) return GameWorldStateBuildStatus.NoData;
        ExpireBundles(nowMilliseconds);
        GameWorldStateBuildStatus deferred = GameWorldStateBuildStatus.NoData;
        for (int offset = 0; offset < _recipients.Length; offset++)
        {
            int index = (_nextRecipientSchedule + offset) % _recipients.Length;
            if (!_recipients[index].Active) continue;
            GameWorldStateBuildStatus status = TryBuildBundleCore(
                index, nowMilliseconds, byteBudget, destination, out bundle);
            if (status == GameWorldStateBuildStatus.NoData) continue;
            if (status == GameWorldStateBuildStatus.OutstandingCapacityExceeded)
            {
                deferred = status;
                continue;
            }
            _nextRecipientSchedule = (index + 1) % _recipients.Length;
            return status;
        }
        return deferred;
    }

    /// <summary>Builds one bundle for a declared connection/participant owner.</summary>
    public GameWorldStateBuildStatus TryBuildBundle(
        ParticipantId participant,
        long nowMilliseconds,
        int byteBudget,
        Span<GameWorldStateRecord> destination,
        out GameWorldStateBundle bundle)
    {
        EnsureOwnerThread();
        bundle = default;
        if (_attributionEnabled) SaturatingIncrement(ref _attributionBundleBuildCalls);
        if (!_recipientMap.TryGetValue(participant, out int recipientIndex))
            return RecordBuildStatus(GameWorldStateBuildStatus.UnknownRecipient);
        if (nowMilliseconds < 0) return RecordBuildStatus(GameWorldStateBuildStatus.NoData);
        ExpireBundles(nowMilliseconds);
        return RecordBuildStatus(TryBuildBundleCore(
            recipientIndex, nowMilliseconds, byteBudget, destination, out bundle));
    }

    /// <summary>Applies or rejects one application-level bundle acknowledgement.</summary>
    public bool TryAcknowledgeBundle(ParticipantId participant, ulong bundleId, bool applied)
    {
        EnsureOwnerThread();
        long attributionStarted = _attributionEnabled ? Stopwatch.GetTimestamp() : 0;
        if (!participant.IsValid || bundleId == 0 || !_bundleMap.TryGetValue(bundleId, out int bundleIndex))
            return false;
        ref SentBundleState bundle = ref _bundles[bundleIndex];
        if (!bundle.Active || _recipients[bundle.Recipient].Participant != participant)
            return false;

        int baseIndex = bundleIndex * _limits.MaximumRecordsPerBundle;
        if (_attributionEnabled)
        {
            SaturatingIncrement(ref _attributionApplicationAcknowledgements);
            SaturatingAdd(ref _attributionAcknowledgementRecordsExamined, checked((ulong)bundle.Count));
        }
        for (int index = 0; index < bundle.Count; index++)
        {
            SentBundleItem item = _bundleItems[baseIndex + index];
            if ((uint)item.Relation >= (uint)_relations.Length) continue;
            ref RelationState relation = ref _relations[item.Relation];
            if (!relation.Active || relation.Generation != item.RelationGeneration) continue;
            if (applied)
            {
                if (item.RelationSequence > relation.KnownRelationSequence)
                {
                    relation.KnownRelationSequence = item.RelationSequence;
                    relation.KnownCanonicalVersion = item.CanonicalVersion;
                    relation.KnownPresent = item.IsPresent;
                }
                if (relation.Pending && relation.PendingRelationSequence == item.RelationSequence)
                    ClearPending(item.Relation);
                if (!relation.DesiredPresent && !relation.Pending)
                    RemoveRelation(item.Relation);
            }
            else if (relation.Pending &&
                relation.PendingRelationSequence == item.RelationSequence)
            {
                relation.HasSent = false;
            }
        }
        if (applied) SaturatingIncrement(ref _bundlesApplied);
        else SaturatingIncrement(ref _bundlesRejected);
        ReleaseBundle(bundleIndex);
        if (_attributionEnabled)
            SaturatingAdd(ref _attributionAcknowledgementTicks,
                checked((ulong)(Stopwatch.GetTimestamp() - attributionStarted)));
        return true;
    }

    /// <summary>Releases failed transport handoff bookkeeping without advancing knowledge.</summary>
    public bool AbandonBundle(ulong bundleId)
    {
        EnsureOwnerThread();
        if (bundleId == 0 || !_bundleMap.TryGetValue(bundleId, out int bundleIndex)) return false;
        ref SentBundleState bundle = ref _bundles[bundleIndex];
        int baseIndex = bundleIndex * _limits.MaximumRecordsPerBundle;
        for (int index = 0; index < bundle.Count; index++)
        {
            SentBundleItem item = _bundleItems[baseIndex + index];
            if ((uint)item.Relation >= (uint)_relations.Length) continue;
            ref RelationState relation = ref _relations[item.Relation];
            if (relation.Active && relation.Generation == item.RelationGeneration && relation.Pending &&
                relation.PendingRelationSequence == item.RelationSequence)
                relation.HasSent = false;
        }
        ReleaseBundle(bundleIndex);
        return true;
    }

    public int CopyRelations(Span<GameRecipientEntityRelationSnapshot> destination)
    {
        EnsureOwnerThread();
        if (destination.Length < _relationCount)
            throw new ArgumentException("Destination cannot contain every maintained relation.", nameof(destination));
        int count = 0;
        for (int index = 0; index < _relations.Length; index++)
        {
            ref readonly RelationState relation = ref _relations[index];
            if (!relation.Active) continue;
            destination[count++] = new(
                _recipients[relation.Recipient].Participant,
                relation.Entity,
                relation.Tier,
                relation.DesiredPresent,
                relation.KnownCanonicalVersion,
                relation.Pending ? relation.PendingCanonicalVersion : 0,
                relation.Pending ? relation.PendingRelationSequence : 0,
                relation.EligibleAtMilliseconds);
        }
        destination[..count].Sort(static (left, right) =>
        {
            int participant = left.Participant.CompareTo(right.Participant);
            if (participant != 0) return participant;
            int entity = left.Entity.Index.CompareTo(right.Entity.Index);
            return entity != 0 ? entity : left.Entity.Generation.CompareTo(right.Entity.Generation);
        });
        return count;
    }

    public GameInterestSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(
            _stateEpoch,
            _entityCount,
            _recipientCount,
            _regionCount,
            _regionEdgeCount,
            _relationCount,
            _pendingCount,
            _bundleCount,
            _nonviableRecipients,
            _pendingWrites,
            _semanticConstructions,
            _semanticCopies,
            _semanticBytes,
            _bundlesBuilt,
            _bundlesApplied,
            _bundlesRejected,
            _timedOutBundles,
            _lastRegionEdgesAdded,
            _lastRegionEdgesRemoved,
            _lastRegionEdgesRetiered,
            _lastRelationsChanged,
            _lastRelationsExamined,
            CalculateReservedBytes());
    }

    /// <summary>Enables or disables cold diagnostic attribution and resets its cumulative epoch.</summary>
    public void ResetAttribution(bool enabled)
    {
        EnsureOwnerThread();
        _attributionEnabled = enabled;
        _attributionEntityUpserts = 0;
        _attributionExistingRelationVisits = 0;
        _attributionReverseRegionEdgeVisits = 0;
        _attributionRelationEnsureLookups = 0;
        _attributionPendingRelationVisits = 0;
        _attributionActualRelationChanges = 0;
        _attributionPendingVersionRewrites = 0;
        _attributionEntityCanonicalTicks = 0;
        _attributionRelationMaintenanceTicks = 0;
        _attributionBundleBuildCalls = 0;
        _attributionBundleBuildSuccesses = 0;
        _attributionBundleBuildNoData = 0;
        _attributionBundleBuildOutstandingCapacity = 0;
        _attributionBundleBuildOtherFailures = 0;
        _attributionPendingImmediateExamined = 0;
        _attributionPendingNearExamined = 0;
        _attributionPendingFarExamined = 0;
        _attributionPendingSelectedImmediate = 0;
        _attributionPendingSelectedNear = 0;
        _attributionPendingSelectedFar = 0;
        _attributionPendingSkippedAlreadySelected = 0;
        _attributionPendingSkippedCadence = 0;
        _attributionPendingSkippedAwaitingRetry = 0;
        _attributionBundleStopsNoEligibleDebt = 0;
        _attributionBundleStopsRecordLimit = 0;
        _attributionBundleStopsByteLimit = 0;
        _attributionBundleRecords = 0;
        _attributionBundleEstimatedBytes = 0;
        _attributionMaximumBundleRecords = 0;
        _attributionMaximumBundleEstimatedBytes = 0;
        _attributionApplicationAcknowledgements = 0;
        _attributionAcknowledgementRecordsExamined = 0;
        _attributionAcknowledgementTicks = 0;
        Array.Clear(_attributionBundleRecordHistogram);
    }

    public GameInterestAttributionSnapshot GetAttributionSnapshot()
    {
        EnsureOwnerThread();
        return new(
            _attributionEnabled,
            _attributionEntityUpserts,
            _attributionExistingRelationVisits,
            _attributionReverseRegionEdgeVisits,
            _attributionRelationEnsureLookups,
            _attributionPendingRelationVisits,
            _attributionActualRelationChanges,
            _attributionPendingVersionRewrites,
            _attributionEntityCanonicalTicks,
            _attributionRelationMaintenanceTicks,
            _attributionBundleBuildCalls,
            _attributionBundleBuildSuccesses,
            _attributionBundleBuildNoData,
            _attributionBundleBuildOutstandingCapacity,
            _attributionBundleBuildOtherFailures,
            _attributionPendingImmediateExamined,
            _attributionPendingNearExamined,
            _attributionPendingFarExamined,
            _attributionPendingSelectedImmediate,
            _attributionPendingSelectedNear,
            _attributionPendingSelectedFar,
            _attributionPendingSkippedAlreadySelected,
            _attributionPendingSkippedCadence,
            _attributionPendingSkippedAwaitingRetry,
            _attributionBundleStopsNoEligibleDebt,
            _attributionBundleStopsRecordLimit,
            _attributionBundleStopsByteLimit,
            _attributionBundleRecords,
            _attributionBundleEstimatedBytes,
            _attributionMaximumBundleRecords,
            _attributionMaximumBundleEstimatedBytes,
            _attributionApplicationAcknowledgements,
            _attributionAcknowledgementRecordsExamined,
            _attributionAcknowledgementTicks);
    }

    public int CopyBundleRecordHistogram(Span<ulong> destination)
    {
        EnsureOwnerThread();
        if (destination.Length < _attributionBundleRecordHistogram.Length)
            throw new ArgumentException("Destination is smaller than the bundle-record histogram.", nameof(destination));
        _attributionBundleRecordHistogram.CopyTo(destination);
        return _attributionBundleRecordHistogram.Length;
    }

    private GameInterestUpdateStatus RebuildRecipientRegionEnvelope(int recipientIndex, long now)
    {
        AdvanceRegionUpdateStamp();
        ref RecipientState recipient = ref _recipients[recipientIndex];
        ReplicationSpatialRegionId anchor = recipient.AnchorRegion;
        for (int dz = -_farCellRadius; dz <= _farCellRadius; dz++)
        {
            for (int dx = -_farCellRadius; dx <= _farCellRadius; dx++)
            {
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dz));
                if (distance > _farCellRadius) continue;
                GameInterestTier tier = distance <= _nearCellRadius
                    ? GameInterestTier.Near
                    : GameInterestTier.Far;
                var id = new ReplicationSpatialRegionId(
                    checked(anchor.X + dx),
                    checked(anchor.Z + dz));
                if (!TryGetOrCreateRegion(id, out int regionIndex) ||
                    !TryEnsureRegionEdge(recipientIndex, regionIndex, tier, now, out int edgeIndex))
                {
                    MarkRecipientNonviable(recipientIndex);
                    return GameInterestUpdateStatus.CapacityExceeded;
                }
                _regionEdges[edgeIndex].UpdateStamp = _regionUpdateStamp;
            }
        }

        int edge = recipient.RegionEdgeHead;
        while (edge >= 0)
        {
            int next = _regionEdges[edge].RecipientNext;
            if (_regionEdges[edge].UpdateStamp != _regionUpdateStamp)
                RemoveRegionEdge(edge, now);
            edge = next;
        }
        return ReconcileRecipientImmediate(recipientIndex, now);
    }

    private GameInterestUpdateStatus ReconcileRecipientImmediate(int recipientIndex, long now)
    {
        ref RecipientState recipient = ref _recipients[recipientIndex];

        // Only the exact R0 set needs exit reconciliation when an anchor moves;
        // coarse R1/R2 membership is unchanged unless region edges changed.
        int relation = recipient.ImmediateRelationHead;
        while (relation >= 0)
        {
            int next = _relations[relation].ImmediateNext;
            _lastRelationsExamined++;
            if (_relations[relation].EntityLinked)
            {
                int entityIndex = _relations[relation].EntitySlot;
                int region = _entities[entityIndex].Region;
                if (_regionEdgeMap.TryGetValue(new RecipientRegionKey(recipientIndex, region), out int edge))
                    SetRelationTierAndPresence(relation, _regionEdges[edge].Tier, now);
                else
                    SetRelationAbsent(relation, now);
            }
            relation = next;
        }

        // Reconcile possible R0 entries from only the small local cell
        // neighborhood. This deliberately does not scan unrelated Near/Far
        // relations owned by the recipient.
        ReplicationSpatialRegionId anchor = recipient.AnchorRegion;
        for (int dz = -_immediateCellRadius; dz <= _immediateCellRadius; dz++)
        {
            for (int dx = -_immediateCellRadius; dx <= _immediateCellRadius; dx++)
            {
                var id = new ReplicationSpatialRegionId(anchor.X + dx, anchor.Z + dz);
                if (!_regionMap.TryGetValue(id, out int regionIndex) ||
                    !_regionEdgeMap.TryGetValue(
                        new RecipientRegionKey(recipientIndex, regionIndex), out int edge))
                    continue;
                int entityIndex = _regions[regionIndex].EntityHead;
                while (entityIndex >= 0)
                {
                    int next = _entities[entityIndex].RegionNext;
                    _lastRelationsExamined++;
                    if (!EnsureDesiredRelation(
                            recipientIndex, entityIndex, _regionEdges[edge].Tier, now))
                    {
                        MarkRecipientNonviable(recipientIndex);
                        return GameInterestUpdateStatus.RecipientNonviable;
                    }
                    entityIndex = next;
                }
            }
        }
        return _recipients[recipientIndex].Nonviable
            ? GameInterestUpdateStatus.RecipientNonviable
            : GameInterestUpdateStatus.Success;
    }

    private bool TryEnsureRegionEdge(
        int recipientIndex,
        int regionIndex,
        GameInterestTier tier,
        long now,
        out int edgeIndex)
    {
        var key = new RecipientRegionKey(recipientIndex, regionIndex);
        if (_regionEdgeMap.TryGetValue(key, out edgeIndex))
        {
            ref RegionInterestEdge existing = ref _regionEdges[edgeIndex];
            if (existing.Tier != tier)
            {
                existing.Tier = tier;
                _lastRegionEdgesRetiered++;
                int entity = _regions[regionIndex].EntityHead;
                while (entity >= 0)
                {
                    int next = _entities[entity].RegionNext;
                    if (!EnsureDesiredRelation(recipientIndex, entity, tier, now)) return false;
                    entity = next;
                }
            }
            return true;
        }
        if (!TryAllocateRegionEdge(out edgeIndex)) return false;
        ref RegionInterestEdge edge = ref _regionEdges[edgeIndex];
        edge = new RegionInterestEdge
        {
            Active = true,
            Recipient = recipientIndex,
            Region = regionIndex,
            Tier = tier,
            RecipientPrevious = -1,
            RecipientNext = _recipients[recipientIndex].RegionEdgeHead,
            RegionPrevious = -1,
            RegionNext = _regions[regionIndex].InterestHead,
        };
        if (edge.RecipientNext >= 0) _regionEdges[edge.RecipientNext].RecipientPrevious = edgeIndex;
        if (edge.RegionNext >= 0) _regionEdges[edge.RegionNext].RegionPrevious = edgeIndex;
        _recipients[recipientIndex].RegionEdgeHead = edgeIndex;
        _regions[regionIndex].InterestHead = edgeIndex;
        _regionEdgeMap.Add(key, edgeIndex);
        _regionEdgeCount++;
        _lastRegionEdgesAdded++;

        int member = _regions[regionIndex].EntityHead;
        while (member >= 0)
        {
            int next = _entities[member].RegionNext;
            if (!EnsureDesiredRelation(recipientIndex, member, tier, now)) return false;
            member = next;
        }
        return true;
    }

    private void RemoveRegionEdge(int edgeIndex, long now)
    {
        ref RegionInterestEdge edge = ref _regionEdges[edgeIndex];
        int regionIndex = edge.Region;
        int member = _regions[regionIndex].EntityHead;
        while (member >= 0)
        {
            int next = _entities[member].RegionNext;
            var key = new RecipientEntityKey(edge.Recipient, _entities[member].Entity.Index);
            if (_relationMap.TryGetValue(key, out int relation))
                SetRelationAbsent(relation, now);
            member = next;
        }

        if (edge.RecipientPrevious >= 0)
            _regionEdges[edge.RecipientPrevious].RecipientNext = edge.RecipientNext;
        else
            _recipients[edge.Recipient].RegionEdgeHead = edge.RecipientNext;
        if (edge.RecipientNext >= 0)
            _regionEdges[edge.RecipientNext].RecipientPrevious = edge.RecipientPrevious;
        if (edge.RegionPrevious >= 0)
            _regionEdges[edge.RegionPrevious].RegionNext = edge.RegionNext;
        else
            _regions[edge.Region].InterestHead = edge.RegionNext;
        if (edge.RegionNext >= 0)
            _regionEdges[edge.RegionNext].RegionPrevious = edge.RegionPrevious;
        _regionEdgeMap.Remove(new RecipientRegionKey(edge.Recipient, edge.Region));
        edge = default;
        _freeRegionEdges[_freeRegionEdgeCount++] = edgeIndex;
        _regionEdgeCount--;
        _lastRegionEdgesRemoved++;
        PruneRegion(regionIndex);
    }

    private bool EnsureDesiredRelation(int recipientIndex, int entityIndex, GameInterestTier baseTier, long now)
    {
        if (_attributionEnabled) SaturatingIncrement(ref _attributionRelationEnsureLookups);
        ref EntityState entity = ref _entities[entityIndex];
        var key = new RecipientEntityKey(recipientIndex, entity.Entity.Index);
        if (_relationMap.TryGetValue(key, out int relationIndex))
        {
            // One recipient/canonical slot owns at most one unsatisfied
            // lifetime. A newer generation supersedes any pending tombstone or
            // present record for the older generation.
            if (_relations[relationIndex].Entity != entity.Entity)
            {
                // A newer lifetime only supersedes the old absence debt when
                // it can itself publish a complete present record to this
                // recipient. Otherwise retain the tombstone for the known old
                // lifetime until it is applied.
                if (DetermineTier(
                        recipientIndex, entityIndex, baseTier, GameInterestTier.None) ==
                    GameInterestTier.None)
                    return true;
                RemoveRelation(relationIndex);
            }
            else
            {
                if (!_relations[relationIndex].EntityLinked) LinkRelationToEntity(relationIndex, entityIndex);
                SetRelationTierAndPresence(relationIndex, baseTier, now);
                return !_recipients[recipientIndex].Nonviable;
            }
        }
        GameInterestTier tier = DetermineTier(recipientIndex, entityIndex, baseTier, GameInterestTier.None);
        if (tier == GameInterestTier.None) return true;
        if (!TryAllocateRelation(out relationIndex)) return false;
        ref RelationState relation = ref _relations[relationIndex];
        relation = new RelationState
        {
            Active = true,
            Generation = _relationGenerations[relationIndex],
            Recipient = recipientIndex,
            Entity = entity.Entity,
            EntitySlot = entityIndex,
            EntityLinked = true,
            Tier = tier,
            DesiredPresent = true,
            RecipientPrevious = -1,
            RecipientNext = _recipients[recipientIndex].RelationHead,
            EntityPrevious = -1,
            EntityNext = entity.RelationHead,
            ImmediatePrevious = -1,
            ImmediateNext = -1,
        };
        if (relation.RecipientNext >= 0)
            _relations[relation.RecipientNext].RecipientPrevious = relationIndex;
        if (relation.EntityNext >= 0)
            _relations[relation.EntityNext].EntityPrevious = relationIndex;
        _recipients[recipientIndex].RelationHead = relationIndex;
        entity.RelationHead = relationIndex;
        _relationMap.Add(key, relationIndex);
        if (tier == GameInterestTier.Immediate) LinkImmediateRelation(relationIndex);
        _relationCount++;
        MarkRelationPending(relationIndex, present: true, entity.CanonicalVersion, now);
        _lastRelationsChanged++;
        return true;
    }

    private void SetRelationTierAndPresence(int relationIndex, GameInterestTier baseTier, long now)
    {
        ref RelationState relation = ref _relations[relationIndex];
        int entityIndex = relation.EntitySlot;
        GameInterestTier tier = DetermineTier(relation.Recipient, entityIndex, baseTier, relation.Tier);
        if (tier == GameInterestTier.None)
        {
            SetRelationAbsent(relationIndex, now);
            return;
        }
        bool wasDesired = relation.DesiredPresent;
        GameInterestTier previous = relation.Tier;
        SetRelationTier(relationIndex, tier);
        relation.DesiredPresent = true;
        if (!wasDesired)
            MarkRelationPending(relationIndex, present: true, _entities[entityIndex].CanonicalVersion, now);
        else if (relation.Pending && tier != previous)
        {
            long retieredEligibility = ComputeEligibleAt(
                tier, now, relation.Recipient, relation.Entity);
            relation.EligibleAtMilliseconds = tier > previous
                ? Math.Min(relation.EligibleAtMilliseconds, retieredEligibility)
                : Math.Max(relation.EligibleAtMilliseconds, retieredEligibility);
            RequeuePending(relationIndex);
        }
        if (!wasDesired || tier != previous) _lastRelationsChanged++;
    }

    private void SetRelationAbsent(int relationIndex, long now)
    {
        ref RelationState relation = ref _relations[relationIndex];
        if (!relation.Active || !relation.DesiredPresent) return;
        ulong canonicalVersion = relation.EntityLinked
            ? _entities[relation.EntitySlot].CanonicalVersion
            : Math.Max(relation.PendingCanonicalVersion, relation.KnownCanonicalVersion);
        relation.DesiredPresent = false;
        SetRelationTier(relationIndex, GameInterestTier.None);
        if (relation.EntityLinked) UnlinkRelationFromEntity(relationIndex);
        MarkRelationPending(relationIndex, present: false, canonicalVersion, now);
        _lastRelationsChanged++;
    }

    private void MarkRelationPending(int relationIndex, bool present, ulong canonicalVersion, long now)
    {
        ref RelationState relation = ref _relations[relationIndex];
        if (_nextRelationSequence == ulong.MaxValue)
        {
            MarkRecipientNonviable(relation.Recipient);
            return;
        }
        if (relation.Pending)
        {
            if (_attributionEnabled) SaturatingIncrement(ref _attributionPendingVersionRewrites);
            UnlinkPending(relationIndex);
        }
        else _pendingCount++;
        relation.Pending = true;
        relation.PendingPresent = present;
        relation.PendingCanonicalVersion = canonicalVersion;
        relation.PendingRelationSequence = _nextRelationSequence++;
        relation.PendingSinceMilliseconds = now;
        relation.EligibleAtMilliseconds = ComputeEligibleAt(
            present ? relation.Tier : GameInterestTier.Immediate,
            now,
            relation.Recipient,
            relation.Entity);
        relation.HasSent = false;
        LinkPending(relationIndex, EffectivePendingTier(in relation));
        SaturatingIncrement(ref _pendingWrites);
    }

    private void ClearPending(int relationIndex)
    {
        ref RelationState relation = ref _relations[relationIndex];
        if (!relation.Pending) return;
        UnlinkPending(relationIndex);
        relation.Pending = false;
        relation.PendingPresent = false;
        relation.PendingCanonicalVersion = 0;
        relation.PendingRelationSequence = 0;
        relation.PendingSinceMilliseconds = 0;
        relation.EligibleAtMilliseconds = 0;
        relation.HasSent = false;
        _pendingCount--;
    }

    private static GameInterestTier EffectivePendingTier(in RelationState relation) =>
        relation.PendingPresent ? relation.Tier : GameInterestTier.Immediate;

    private void RequeuePending(int relationIndex)
    {
        ref RelationState relation = ref _relations[relationIndex];
        if (!relation.Pending) return;
        UnlinkPending(relationIndex);
        LinkPending(relationIndex, EffectivePendingTier(in relation));
    }

    private void LinkPending(int relationIndex, GameInterestTier tier)
    {
        ref RelationState relation = ref _relations[relationIndex];
        ref RecipientState recipient = ref _recipients[relation.Recipient];
        relation.PendingTier = tier;
        relation.PendingNext = -1;
        relation.PendingPrevious = tier switch
        {
            GameInterestTier.Immediate => recipient.PendingImmediateTail,
            GameInterestTier.Near => recipient.PendingNearTail,
            GameInterestTier.Far => recipient.PendingFarTail,
            _ => throw new InvalidOperationException("Pending state requires a delivery tier."),
        };
        if (relation.PendingPrevious >= 0)
            _relations[relation.PendingPrevious].PendingNext = relationIndex;
        else
        {
            switch (tier)
            {
                case GameInterestTier.Immediate: recipient.PendingImmediateHead = relationIndex; break;
                case GameInterestTier.Near: recipient.PendingNearHead = relationIndex; break;
                case GameInterestTier.Far: recipient.PendingFarHead = relationIndex; break;
            }
        }
        switch (tier)
        {
            case GameInterestTier.Immediate: recipient.PendingImmediateTail = relationIndex; break;
            case GameInterestTier.Near: recipient.PendingNearTail = relationIndex; break;
            case GameInterestTier.Far: recipient.PendingFarTail = relationIndex; break;
        }
    }

    private void UnlinkPending(int relationIndex)
    {
        ref RelationState relation = ref _relations[relationIndex];
        ref RecipientState recipient = ref _recipients[relation.Recipient];
        if (relation.PendingPrevious >= 0)
            _relations[relation.PendingPrevious].PendingNext = relation.PendingNext;
        else
        {
            switch (relation.PendingTier)
            {
                case GameInterestTier.Immediate: recipient.PendingImmediateHead = relation.PendingNext; break;
                case GameInterestTier.Near: recipient.PendingNearHead = relation.PendingNext; break;
                case GameInterestTier.Far: recipient.PendingFarHead = relation.PendingNext; break;
            }
        }
        if (relation.PendingNext >= 0)
            _relations[relation.PendingNext].PendingPrevious = relation.PendingPrevious;
        else
        {
            switch (relation.PendingTier)
            {
                case GameInterestTier.Immediate: recipient.PendingImmediateTail = relation.PendingPrevious; break;
                case GameInterestTier.Near: recipient.PendingNearTail = relation.PendingPrevious; break;
                case GameInterestTier.Far: recipient.PendingFarTail = relation.PendingPrevious; break;
            }
        }
        relation.PendingTier = GameInterestTier.None;
        relation.PendingPrevious = -1;
        relation.PendingNext = -1;
    }

    private GameWorldStateBuildStatus TryBuildBundleCore(
        int recipientIndex,
        long now,
        int byteBudget,
        Span<GameWorldStateRecord> destination,
        out GameWorldStateBundle bundle)
    {
        bundle = default;
        ref RecipientState recipient = ref _recipients[recipientIndex];
        if (recipient.Nonviable) return GameWorldStateBuildStatus.RecipientNonviable;
        if (recipient.OutstandingBundleCount >= _maximumOutstandingBundlesPerRecipient)
            return GameWorldStateBuildStatus.OutstandingCapacityExceeded;
        int maximum = Math.Min(destination.Length, _limits.MaximumRecordsPerBundle);
        if (maximum == 0) return GameWorldStateBuildStatus.DestinationTooSmall;
        if (byteBudget < BundleHeaderEstimate + GameWorldStateRecord.MaximumEncodedBytes)
            return GameWorldStateBuildStatus.ByteBudgetTooSmall;
        if (!TryPeekBundleSlot(out int bundleIndex))
            return GameWorldStateBuildStatus.OutstandingCapacityExceeded;
        if (_nextBundleId == ulong.MaxValue)
            return GameWorldStateBuildStatus.SequenceExhausted;

        AdvanceBuildStamp();
        int count = 0;
        int bytes = BundleHeaderEstimate;
        int immediateCursor = recipient.PendingImmediateHead;
        int nearCursor = recipient.PendingNearHead;
        int farCursor = recipient.PendingFarHead;
        while (count < maximum && bytes + GameWorldStateRecord.MaximumEncodedBytes <= byteBudget)
        {
            int selected = SelectEligibleRelation(
                recipientIndex, now, ref immediateCursor, ref nearCursor, ref farCursor);
            if (selected < 0) break;
            ref RelationState relation = ref _relations[selected];
            if (_attributionEnabled)
            {
                switch (relation.PendingTier)
                {
                    case GameInterestTier.Immediate: SaturatingIncrement(ref _attributionPendingSelectedImmediate); break;
                    case GameInterestTier.Near: SaturatingIncrement(ref _attributionPendingSelectedNear); break;
                    case GameInterestTier.Far: SaturatingIncrement(ref _attributionPendingSelectedFar); break;
                }
            }
            relation.BuildStamp = _buildStamp;
            GameWorldStateRecord record;
            if (relation.PendingPresent)
            {
                ref EntityState entity = ref _entities[relation.EntitySlot];
                if (!entity.Active || entity.Entity != relation.Entity ||
                    entity.CanonicalVersion != relation.PendingCanonicalVersion)
                {
                    MarkRecipientNonviable(recipientIndex);
                    return GameWorldStateBuildStatus.RecipientNonviable;
                }
                if (entity.CachedVersion != entity.CanonicalVersion)
                {
                    entity.CachedSemantic = new(
                        entity.Entity,
                        entity.CanonicalVersion,
                        entity.Horizon,
                        entity.Position,
                        entity.Projected.SimulationState);
                    entity.CachedVersion = entity.CanonicalVersion;
                    SaturatingIncrement(ref _semanticConstructions);
                }
                GameWorldStateSemantic semantic = entity.CachedSemantic;
                record = new(
                    relation.PendingRelationSequence,
                    semantic.Entity,
                    semantic.CanonicalVersion,
                    true,
                    semantic.Horizon,
                    semantic.Position,
                    semantic.SimulationState);
            }
            else
            {
                record = new(
                    relation.PendingRelationSequence,
                    relation.Entity,
                    relation.PendingCanonicalVersion,
                    false,
                    GameReplicationHorizon.None,
                    default,
                    default);
            }
            destination[count] = record;
            int itemIndex = bundleIndex * _limits.MaximumRecordsPerBundle + count;
            _bundleItems[itemIndex] = new(
                selected,
                relation.Generation,
                relation.PendingRelationSequence,
                relation.PendingCanonicalVersion,
                relation.PendingPresent);
            relation.LastSentMilliseconds = now;
            relation.HasSent = true;
            count++;
            bytes += GameWorldStateRecord.MaximumEncodedBytes;
            SaturatingIncrement(ref _semanticCopies);
            SaturatingAdd(ref _semanticBytes, GameWorldStateRecord.MaximumEncodedBytes);
        }
        if (_attributionEnabled)
        {
            if (count == maximum) SaturatingIncrement(ref _attributionBundleStopsRecordLimit);
            else if (bytes + GameWorldStateRecord.MaximumEncodedBytes > byteBudget)
                SaturatingIncrement(ref _attributionBundleStopsByteLimit);
            else SaturatingIncrement(ref _attributionBundleStopsNoEligibleDebt);
        }
        if (count == 0) return GameWorldStateBuildStatus.NoData;

        AllocatePeekedBundle(bundleIndex);
        ulong id = _nextBundleId++;
        _bundles[bundleIndex] = new SentBundleState
        {
            Active = true,
            BundleId = id,
            Recipient = recipientIndex,
            Count = count,
            SentAtMilliseconds = now,
        };
        _bundleMap.Add(id, bundleIndex);
        _bundleCount++;
        recipient.OutstandingBundleCount++;
        SaturatingIncrement(ref _bundlesBuilt);
        if (_attributionEnabled)
        {
            SaturatingAdd(ref _attributionBundleRecords, checked((ulong)count));
            SaturatingAdd(ref _attributionBundleEstimatedBytes, checked((ulong)bytes));
            _attributionMaximumBundleRecords = Math.Max(_attributionMaximumBundleRecords, count);
            _attributionMaximumBundleEstimatedBytes = Math.Max(_attributionMaximumBundleEstimatedBytes, bytes);
            SaturatingIncrement(ref _attributionBundleRecordHistogram[count]);
        }
        bundle = new(recipient.Participant, _stateEpoch, id, count, bytes);
        return GameWorldStateBuildStatus.Success;
    }

    private int SelectEligibleRelation(
        int recipientIndex,
        long now,
        ref int immediateCursor,
        ref int nearCursor,
        ref int farCursor)
    {
        ref RecipientState recipient = ref _recipients[recipientIndex];
        const int scheduleLength = 7;
        for (int offset = 0; offset < scheduleLength; offset++)
        {
            int scheduleIndex = (recipient.NextTierSchedule + offset) % scheduleLength;
            GameInterestTier tier = scheduleIndex switch
            {
                0 or 1 or 2 or 4 => GameInterestTier.Immediate,
                3 or 5 => GameInterestTier.Near,
                _ => GameInterestTier.Far,
            };
            int selected = tier switch
            {
                GameInterestTier.Immediate =>
                    SelectEligibleInPendingList(ref immediateCursor, tier, now),
                GameInterestTier.Near =>
                    SelectEligibleInPendingList(ref nearCursor, tier, now),
                GameInterestTier.Far =>
                    SelectEligibleInPendingList(ref farCursor, tier, now),
                _ => -1,
            };
            if (selected < 0) continue;
            recipient.NextTierSchedule = checked((byte)((scheduleIndex + 1) % scheduleLength));
            return selected;
        }
        return -1;
    }

    private int SelectEligibleInPendingList(ref int relation, GameInterestTier tier, long now)
    {
        while (relation >= 0)
        {
            int candidateIndex = relation;
            ref readonly RelationState candidate = ref _relations[candidateIndex];
            relation = candidate.PendingNext;
            if (_attributionEnabled)
            {
                switch (tier)
                {
                    case GameInterestTier.Immediate: SaturatingIncrement(ref _attributionPendingImmediateExamined); break;
                    case GameInterestTier.Near: SaturatingIncrement(ref _attributionPendingNearExamined); break;
                    case GameInterestTier.Far: SaturatingIncrement(ref _attributionPendingFarExamined); break;
                }
                if (candidate.BuildStamp == _buildStamp)
                    SaturatingIncrement(ref _attributionPendingSkippedAlreadySelected);
                else if (candidate.EligibleAtMilliseconds > now)
                    SaturatingIncrement(ref _attributionPendingSkippedCadence);
                else if (candidate.HasSent && now - candidate.LastSentMilliseconds <
                    _profile.ApplicationAckRetryMilliseconds)
                    SaturatingIncrement(ref _attributionPendingSkippedAwaitingRetry);
            }
            if (candidate.BuildStamp != _buildStamp &&
                candidate.EligibleAtMilliseconds <= now &&
                (!candidate.HasSent || now - candidate.LastSentMilliseconds >=
                    _profile.ApplicationAckRetryMilliseconds))
                return candidateIndex;
        }
        return -1;
    }

    private GameWorldStateBuildStatus RecordBuildStatus(GameWorldStateBuildStatus status)
    {
        if (!_attributionEnabled) return status;
        switch (status)
        {
            case GameWorldStateBuildStatus.Success:
                SaturatingIncrement(ref _attributionBundleBuildSuccesses);
                break;
            case GameWorldStateBuildStatus.NoData:
                SaturatingIncrement(ref _attributionBundleBuildNoData);
                break;
            case GameWorldStateBuildStatus.OutstandingCapacityExceeded:
                SaturatingIncrement(ref _attributionBundleBuildOutstandingCapacity);
                break;
            default:
                SaturatingIncrement(ref _attributionBundleBuildOtherFailures);
                break;
        }
        return status;
    }

    private GameInterestTier DetermineTier(
        int recipientIndex,
        int entityIndex,
        GameInterestTier baseTier,
        GameInterestTier previous)
    {
        ref readonly RecipientState recipient = ref _recipients[recipientIndex];
        ref readonly EntityState entity = ref _entities[entityIndex];
        int radius = previous == GameInterestTier.Immediate
            ? _profile.ImmediateLeaveRadiusMillimeters
            : _profile.ImmediateEnterRadiusMillimeters;
        if (WithinSquaredDistance(recipient.Anchor, entity.Position, radius) &&
            Allows(entity.Horizon, GameInterestTier.Immediate))
            return GameInterestTier.Immediate;
        return Allows(entity.Horizon, baseTier) ? baseTier : GameInterestTier.None;
    }

    private static bool Allows(GameReplicationHorizon horizon, GameInterestTier tier)
    {
        int required = tier switch
        {
            GameInterestTier.Immediate => 1,
            GameInterestTier.Near => 2,
            GameInterestTier.Far => 3,
            _ => int.MaxValue,
        };
        return required <= (int)horizon;
    }

    private long ComputeEligibleAt(
        GameInterestTier tier,
        long now,
        int recipientIndex,
        ReplicationEntityId entity)
    {
        if (tier == GameInterestTier.Immediate || tier == GameInterestTier.None) return now;
        long interval = tier == GameInterestTier.Near
            ? _profile.NearIntervalMilliseconds
            : _profile.FarIntervalMilliseconds;
        ulong mixed = _recipients[recipientIndex].Participant.Value;
        mixed ^= ((ulong)entity.Index << 32) | entity.Generation;
        // This is a bit-mixing permutation. Wraparound is the intended unsigned
        // wire-independent hash behavior even when the assembly checks arithmetic.
        mixed = unchecked((mixed ^ (mixed >> 33)) * 0xff51afd7ed558ccdUL);
        mixed ^= mixed >> 33;
        long phase = (long)(mixed % (ulong)interval);
        long remainder = now % interval;
        long delay = phase >= remainder ? phase - remainder : interval - (remainder - phase);
        return delay > long.MaxValue - now ? long.MaxValue : now + delay;
    }

    private void RetireEntity(int entityIndex, long now)
    {
        ref EntityState state = ref _entities[entityIndex];
        int region = state.Region;
        int relation = state.RelationHead;
        while (relation >= 0)
        {
            int next = _relations[relation].EntityNext;
            SetRelationAbsent(relation, now);
            relation = next;
        }
        UnlinkEntityFromRegion(entityIndex);
        state.Active = false;
        state.Projected = default;
        state.Position = default;
        state.Horizon = GameReplicationHorizon.None;
        state.CachedVersion = 0;
        state.CachedSemantic = default;
        state.Region = -1;
        state.RegionPrevious = -1;
        state.RegionNext = -1;
        state.RelationHead = -1;
        _entityCount--;
        PruneRegion(region);
    }

    private void RemoveRecipientInternal(int recipientIndex)
    {
        ref RecipientState recipient = ref _recipients[recipientIndex];
        int relation = recipient.RelationHead;
        while (relation >= 0)
        {
            int next = _relations[relation].RecipientNext;
            RemoveRelation(relation);
            relation = next;
        }
        int edge = recipient.RegionEdgeHead;
        while (edge >= 0)
        {
            int next = _regionEdges[edge].RecipientNext;
            RemoveRegionEdgeImmediate(edge);
            edge = next;
        }
        for (int index = 0; index < _bundles.Length; index++)
            if (_bundles[index].Active && _bundles[index].Recipient == recipientIndex)
                ReleaseBundle(index);
        _recipientMap.Remove(recipient.Participant);
        if (recipient.Nonviable) _nonviableRecipients--;
        recipient = default;
        _freeRecipients[_freeRecipientCount++] = recipientIndex;
        _recipientCount--;
    }

    private void RemoveRegionEdgeImmediate(int edgeIndex)
    {
        ref RegionInterestEdge edge = ref _regionEdges[edgeIndex];
        int region = edge.Region;
        if (edge.RecipientPrevious >= 0)
            _regionEdges[edge.RecipientPrevious].RecipientNext = edge.RecipientNext;
        else
            _recipients[edge.Recipient].RegionEdgeHead = edge.RecipientNext;
        if (edge.RecipientNext >= 0)
            _regionEdges[edge.RecipientNext].RecipientPrevious = edge.RecipientPrevious;
        if (edge.RegionPrevious >= 0)
            _regionEdges[edge.RegionPrevious].RegionNext = edge.RegionNext;
        else
            _regions[edge.Region].InterestHead = edge.RegionNext;
        if (edge.RegionNext >= 0)
            _regionEdges[edge.RegionNext].RegionPrevious = edge.RegionPrevious;
        _regionEdgeMap.Remove(new RecipientRegionKey(edge.Recipient, edge.Region));
        edge = default;
        _freeRegionEdges[_freeRegionEdgeCount++] = edgeIndex;
        _regionEdgeCount--;
        PruneRegion(region);
    }

    private void RemoveRelation(int relationIndex)
    {
        ref RelationState relation = ref _relations[relationIndex];
        if (!relation.Active) return;
        if (relation.Tier == GameInterestTier.Immediate)
            UnlinkImmediateRelation(relationIndex);
        if (relation.Pending)
        {
            UnlinkPending(relationIndex);
            _pendingCount--;
        }
        if (relation.EntityLinked) UnlinkRelationFromEntity(relationIndex);
        if (relation.RecipientPrevious >= 0)
            _relations[relation.RecipientPrevious].RecipientNext = relation.RecipientNext;
        else
            _recipients[relation.Recipient].RelationHead = relation.RecipientNext;
        if (relation.RecipientNext >= 0)
            _relations[relation.RecipientNext].RecipientPrevious = relation.RecipientPrevious;
        _relationMap.Remove(new RecipientEntityKey(relation.Recipient, relation.Entity.Index));
        uint generation = relation.Generation;
        relation = default;
        _relationCount--;
        if (generation != uint.MaxValue)
            _freeRelations[_freeRelationCount++] = relationIndex;
    }

    private void LinkRelationToEntity(int relationIndex, int entityIndex)
    {
        ref RelationState relation = ref _relations[relationIndex];
        ref EntityState entity = ref _entities[entityIndex];
        relation.EntitySlot = entityIndex;
        relation.EntityLinked = true;
        relation.EntityPrevious = -1;
        relation.EntityNext = entity.RelationHead;
        if (relation.EntityNext >= 0)
            _relations[relation.EntityNext].EntityPrevious = relationIndex;
        entity.RelationHead = relationIndex;
    }

    private void UnlinkRelationFromEntity(int relationIndex)
    {
        ref RelationState relation = ref _relations[relationIndex];
        if (!relation.EntityLinked) return;
        if (relation.EntityPrevious >= 0)
            _relations[relation.EntityPrevious].EntityNext = relation.EntityNext;
        else
            _entities[relation.EntitySlot].RelationHead = relation.EntityNext;
        if (relation.EntityNext >= 0)
            _relations[relation.EntityNext].EntityPrevious = relation.EntityPrevious;
        relation.EntityLinked = false;
        relation.EntitySlot = -1;
        relation.EntityPrevious = -1;
        relation.EntityNext = -1;
    }

    private void SetRelationTier(int relationIndex, GameInterestTier tier)
    {
        ref RelationState relation = ref _relations[relationIndex];
        if (relation.Tier == tier) return;
        if (relation.Tier == GameInterestTier.Immediate)
            UnlinkImmediateRelation(relationIndex);
        relation.Tier = tier;
        if (tier == GameInterestTier.Immediate)
            LinkImmediateRelation(relationIndex);
    }

    private void LinkImmediateRelation(int relationIndex)
    {
        ref RelationState relation = ref _relations[relationIndex];
        ref RecipientState recipient = ref _recipients[relation.Recipient];
        relation.ImmediatePrevious = -1;
        relation.ImmediateNext = recipient.ImmediateRelationHead;
        if (relation.ImmediateNext >= 0)
            _relations[relation.ImmediateNext].ImmediatePrevious = relationIndex;
        recipient.ImmediateRelationHead = relationIndex;
    }

    private void UnlinkImmediateRelation(int relationIndex)
    {
        ref RelationState relation = ref _relations[relationIndex];
        if (relation.ImmediatePrevious >= 0)
            _relations[relation.ImmediatePrevious].ImmediateNext = relation.ImmediateNext;
        else
            _recipients[relation.Recipient].ImmediateRelationHead = relation.ImmediateNext;
        if (relation.ImmediateNext >= 0)
            _relations[relation.ImmediateNext].ImmediatePrevious = relation.ImmediatePrevious;
        relation.ImmediatePrevious = -1;
        relation.ImmediateNext = -1;
    }

    private void LinkEntityToRegion(int entityIndex, int regionIndex)
    {
        ref EntityState entity = ref _entities[entityIndex];
        entity.Region = regionIndex;
        entity.RegionPrevious = -1;
        entity.RegionNext = _regions[regionIndex].EntityHead;
        if (entity.RegionNext >= 0) _entities[entity.RegionNext].RegionPrevious = entityIndex;
        _regions[regionIndex].EntityHead = entityIndex;
    }

    private void UnlinkEntityFromRegion(int entityIndex)
    {
        ref EntityState entity = ref _entities[entityIndex];
        if (entity.RegionPrevious >= 0)
            _entities[entity.RegionPrevious].RegionNext = entity.RegionNext;
        else
            _regions[entity.Region].EntityHead = entity.RegionNext;
        if (entity.RegionNext >= 0)
            _entities[entity.RegionNext].RegionPrevious = entity.RegionPrevious;
        entity.RegionPrevious = -1;
        entity.RegionNext = -1;
    }

    private void ExpireBundles(long now)
    {
        for (int index = 0; index < _bundles.Length; index++)
        {
            ref SentBundleState bundle = ref _bundles[index];
            if (!bundle.Active || now - bundle.SentAtMilliseconds < _profile.ApplicationAckTimeoutMilliseconds)
                continue;
            int baseIndex = index * _limits.MaximumRecordsPerBundle;
            for (int itemIndex = 0; itemIndex < bundle.Count; itemIndex++)
            {
                SentBundleItem item = _bundleItems[baseIndex + itemIndex];
                if ((uint)item.Relation >= (uint)_relations.Length) continue;
                ref RelationState relation = ref _relations[item.Relation];
                if (relation.Active && relation.Generation == item.RelationGeneration && relation.Pending &&
                    relation.PendingRelationSequence == item.RelationSequence)
                    relation.HasSent = false;
            }
            SaturatingIncrement(ref _timedOutBundles);
            ReleaseBundle(index);
        }
    }

    private void ReleaseBundle(int bundleIndex)
    {
        ref SentBundleState bundle = ref _bundles[bundleIndex];
        if (!bundle.Active) return;
        _recipients[bundle.Recipient].OutstandingBundleCount--;
        _bundleMap.Remove(bundle.BundleId);
        _bundleItems.AsSpan(
            bundleIndex * _limits.MaximumRecordsPerBundle,
            bundle.Count).Clear();
        bundle = default;
        _freeBundles[_freeBundleCount++] = bundleIndex;
        _bundleCount--;
    }

    private void MarkRecipientNonviable(int recipientIndex)
    {
        ref RecipientState recipient = ref _recipients[recipientIndex];
        if (recipient.Nonviable) return;
        recipient.Nonviable = true;
        _nonviableRecipients++;
    }

    private bool AnyAffectedRecipientNonviable(int regionIndex)
    {
        int edge = _regions[regionIndex].InterestHead;
        while (edge >= 0)
        {
            if (_recipients[_regionEdges[edge].Recipient].Nonviable) return true;
            edge = _regionEdges[edge].RegionNext;
        }
        return false;
    }

    private bool TryGetOrCreateRegion(ReplicationSpatialRegionId id, out int index)
    {
        if (_regionMap.TryGetValue(id, out index)) return true;
        if (!TryAllocateRegion(out index)) return false;
        _regions[index] = new RegionState
        {
            Active = true,
            Id = id,
            EntityHead = -1,
            InterestHead = -1,
        };
        _regionMap.Add(id, index);
        _regionCount++;
        return true;
    }

    private void PruneRegion(int regionIndex)
    {
        if (regionIndex < 0) return;
        ref RegionState region = ref _regions[regionIndex];
        if (!region.Active || region.EntityHead >= 0 || region.InterestHead >= 0) return;
        _regionMap.Remove(region.Id);
        region = default;
        _freeRegions[_freeRegionCount++] = regionIndex;
        _regionCount--;
    }

    private bool TryAllocateRecipient(out int index) =>
        TryAllocate(_freeRecipients, ref _freeRecipientCount, ref _recipientHigh, _recipients.Length, out index);

    private bool TryAllocateRegion(out int index) =>
        TryAllocate(_freeRegions, ref _freeRegionCount, ref _regionHigh, _regions.Length, out index);

    private bool TryAllocateRegionEdge(out int index) =>
        TryAllocate(_freeRegionEdges, ref _freeRegionEdgeCount, ref _regionEdgeHigh, _regionEdges.Length, out index);

    private bool TryAllocateRelation(out int index)
    {
        while (TryAllocate(_freeRelations, ref _freeRelationCount, ref _relationHigh, _relations.Length, out index))
        {
            if (_relationGenerations[index] == uint.MaxValue) continue;
            _relationGenerations[index]++;
            return true;
        }
        index = -1;
        return false;
    }

    private bool TryPeekBundleSlot(out int index)
    {
        if (_freeBundleCount != 0)
        {
            index = _freeBundles[_freeBundleCount - 1];
            return true;
        }
        if (_bundleHigh < _bundles.Length)
        {
            index = _bundleHigh;
            return true;
        }
        index = -1;
        return false;
    }

    private void AllocatePeekedBundle(int index)
    {
        if (_freeBundleCount != 0)
        {
            int popped = _freeBundles[--_freeBundleCount];
            if (popped != index) throw new InvalidOperationException("Bundle free-list ownership changed.");
        }
        else
        {
            if (_bundleHigh != index) throw new InvalidOperationException("Bundle high-water ownership changed.");
            _bundleHigh++;
        }
    }

    private static bool TryAllocate(
        int[] free,
        ref int freeCount,
        ref int high,
        int capacity,
        out int index)
    {
        if (freeCount != 0)
        {
            index = free[--freeCount];
            return true;
        }
        if (high < capacity)
        {
            index = high++;
            return true;
        }
        index = -1;
        return false;
    }

    private void AdvanceRegionUpdateStamp()
    {
        if (_regionUpdateStamp == uint.MaxValue)
        {
            for (int index = 0; index < _regionEdges.Length; index++)
                _regionEdges[index].UpdateStamp = 0;
            _regionUpdateStamp = 1;
        }
        else
        {
            _regionUpdateStamp++;
        }
    }

    private void AdvanceBuildStamp()
    {
        if (_buildStamp == uint.MaxValue)
        {
            for (int index = 0; index < _relations.Length; index++)
                _relations[index].BuildStamp = 0;
            _buildStamp = 1;
        }
        else
        {
            _buildStamp++;
        }
    }

    private ReplicationSpatialRegionId ToRegion(in ReplicationSpatialPoint point) => new(
        FloorDivide(point.XMillimeters, _profile.RegionEdgeMillimeters),
        FloorDivide(point.ZMillimeters, _profile.RegionEdgeMillimeters));

    private static int FloorDivide(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }

    private static int CellRadius(int radius, int edge) =>
        checked((int)(((long)radius + edge - 1L) / edge));

    // Exact local-distance candidates must include the cell just across a
    // boundary when radius is an exact multiple of edge length.
    private static int CandidateCellRadius(int radius, int edge) =>
        checked(radius / edge + 1);

    private static bool WithinSquaredDistance(
        in ReplicationSpatialPoint left,
        in ReplicationSpatialPoint right,
        int radius)
    {
        Int128 dx = (Int128)left.XMillimeters - right.XMillimeters;
        Int128 dz = (Int128)left.ZMillimeters - right.ZMillimeters;
        Int128 r = radius;
        return dx * dx + dz * dz <= r * r;
    }

    private void ResetLastMutationCounters()
    {
        _lastRegionEdgesAdded = 0;
        _lastRegionEdgesRemoved = 0;
        _lastRegionEdgesRetiered = 0;
        _lastRelationsChanged = 0;
        _lastRelationsExamined = 0;
    }

    private long CalculateReservedBytes()
    {
        long bytes = 0;
        bytes = checked(bytes + (long)_entities.Length * Unsafe.SizeOf<EntityState>());
        bytes = checked(bytes + (long)_recipients.Length * Unsafe.SizeOf<RecipientState>());
        bytes = checked(bytes + (long)_regions.Length * Unsafe.SizeOf<RegionState>());
        bytes = checked(bytes + (long)_regionEdges.Length * Unsafe.SizeOf<RegionInterestEdge>());
        bytes = checked(bytes + (long)_relations.Length * Unsafe.SizeOf<RelationState>());
        bytes = checked(bytes + (long)_relationGenerations.Length * sizeof(uint));
        bytes = checked(bytes + (long)_bundles.Length * Unsafe.SizeOf<SentBundleState>());
        bytes = checked(bytes + (long)_bundleItems.Length * Unsafe.SizeOf<SentBundleItem>());
        bytes = checked(bytes + (long)_attributionBundleRecordHistogram.Length * sizeof(ulong));
        bytes = checked(bytes + ((long)_freeRecipients.Length + _freeRegions.Length +
            _freeRegionEdges.Length + _freeRelations.Length + _freeBundles.Length) * sizeof(int));
        bytes = checked(bytes + DictionaryReservation(_recipientMapCapacity, Unsafe.SizeOf<ParticipantId>()));
        bytes = checked(bytes + DictionaryReservation(_regionMapCapacity, Unsafe.SizeOf<ReplicationSpatialRegionId>()));
        bytes = checked(bytes + DictionaryReservation(_regionEdgeMapCapacity, Unsafe.SizeOf<RecipientRegionKey>()));
        bytes = checked(bytes + DictionaryReservation(_relationMapCapacity, Unsafe.SizeOf<RecipientEntityKey>()));
        bytes = checked(bytes + DictionaryReservation(_bundleMapCapacity, sizeof(ulong)));
        return bytes;
    }

    private static long DictionaryReservation(int capacity, int keyBytes) =>
        checked((long)capacity * (sizeof(int) * 3L + keyBytes));

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private static void SaturatingAdd(ref ulong value, int amount)
    {
        ulong add = checked((ulong)amount);
        value = ulong.MaxValue - value < add ? ulong.MaxValue : value + add;
    }

    private static void SaturatingAdd(ref ulong value, ulong amount) =>
        value = ulong.MaxValue - value < amount ? ulong.MaxValue : value + amount;

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The maintained-interest graph is owner-thread-bound.");
    }

    private readonly record struct RecipientRegionKey(int Recipient, int Region);
    private readonly record struct RecipientEntityKey(int Recipient, uint EntityIndex);

    private struct EntityState
    {
        internal bool Active;
        internal ReplicationEntityId Entity;
        internal GameProjectedEntity Projected;
        internal ulong CanonicalVersion;
        internal ReplicationSpatialPoint Position;
        internal GameReplicationHorizon Horizon;
        internal int Region;
        internal int RegionPrevious;
        internal int RegionNext;
        internal int RelationHead;
        internal ulong CachedVersion;
        internal GameWorldStateSemantic CachedSemantic;
    }

    private struct RecipientState
    {
        internal bool Active;
        internal bool Nonviable;
        internal ParticipantId Participant;
        internal bool HasAnchor;
        internal ReplicationSpatialPoint Anchor;
        internal ReplicationSpatialRegionId AnchorRegion;
        internal int RegionEdgeHead;
        internal int RelationHead;
        internal int ImmediateRelationHead;
        internal int PendingImmediateHead;
        internal int PendingImmediateTail;
        internal int PendingNearHead;
        internal int PendingNearTail;
        internal int PendingFarHead;
        internal int PendingFarTail;
        internal int OutstandingBundleCount;
        internal byte NextTierSchedule;
    }

    private struct RegionState
    {
        internal bool Active;
        internal ReplicationSpatialRegionId Id;
        internal int EntityHead;
        internal int InterestHead;
    }

    private struct RegionInterestEdge
    {
        internal bool Active;
        internal int Recipient;
        internal int Region;
        internal GameInterestTier Tier;
        internal int RecipientPrevious;
        internal int RecipientNext;
        internal int RegionPrevious;
        internal int RegionNext;
        internal uint UpdateStamp;
    }

    private struct RelationState
    {
        internal bool Active;
        internal uint Generation;
        internal int Recipient;
        internal ReplicationEntityId Entity;
        internal int EntitySlot;
        internal bool EntityLinked;
        internal GameInterestTier Tier;
        internal bool DesiredPresent;
        internal ulong KnownRelationSequence;
        internal ulong KnownCanonicalVersion;
        internal bool KnownPresent;
        internal bool Pending;
        internal bool PendingPresent;
        internal ulong PendingRelationSequence;
        internal ulong PendingCanonicalVersion;
        internal long PendingSinceMilliseconds;
        internal long EligibleAtMilliseconds;
        internal bool HasSent;
        internal long LastSentMilliseconds;
        internal GameInterestTier PendingTier;
        internal int PendingPrevious;
        internal int PendingNext;
        internal int RecipientPrevious;
        internal int RecipientNext;
        internal int EntityPrevious;
        internal int EntityNext;
        internal int ImmediatePrevious;
        internal int ImmediateNext;
        internal uint BuildStamp;
    }

    private struct SentBundleState
    {
        internal bool Active;
        internal ulong BundleId;
        internal int Recipient;
        internal int Count;
        internal long SentAtMilliseconds;
    }

    private readonly record struct SentBundleItem(
        int Relation,
        uint RelationGeneration,
        ulong RelationSequence,
        ulong CanonicalVersion,
        bool IsPresent);
}
