using System.Runtime.CompilerServices;
using UltraSim.Client.Core;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

namespace UltraSim.Client.Network;

public readonly record struct RemoteChunkWorldStateClientSnapshot(
    int PendingProofs,
    int PendingProofCapacity,
    ulong AppliedDeltas,
    ulong AppliedAbsences,
    ulong DuplicateDeliveries,
    ulong RejectedDeliveries,
    ulong CapacityFailures,
    ulong ContradictoryDeliveries,
    ulong MalformedDeliveries,
    ulong ProofsQueued,
    ulong ProofMessagesQueued,
    bool SnapshotCandidateActive,
    int SnapshotSegmentsReceived,
    ulong AppliedSnapshots,
    ulong RejectedSnapshots,
    ulong SupersededDeliveries,
    long ReservedBytes,
    RemoteChunkWorldStateClientTimingSnapshot Timing)
{
    public int RetiredInterestCount { get; init; }
    public ulong InterestRetirementsProcessed { get; init; }
    public ulong MissingResidentDeliveryAttempts { get; init; }
    public ulong MissingResidentPreparedChunkAttempts { get; init; }
    /// <summary>Subset of missing-resident attempts rejected at segmented Snapshot Begin,
    /// accounted in RejectedSnapshots rather than RejectedDeliveries.</summary>
    public ulong MissingResidentSnapshotBeginAttempts { get; init; }
}

/// <summary>
/// N1.2/N1.3 client bridge for ordinary unfragmented deltas/absences and exceptional atomic Bulk
/// snapshots. Decoding and semantic application complete before a proof becomes eligible.
/// </summary>
public sealed class RemoteChunkWorldStateClient : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ClientWorldSemanticCache _cache;
    private readonly ulong _applicationSessionEpoch;
    private readonly RemoteChunkDeltaRecord[] _wireDeltas;
    private readonly WorldPublicDeltaRecord[] _semanticDeltas;
    private readonly RemoteChunkPublicRecord[] _wireSnapshot;
    private readonly WorldPublicRecord[] _semanticSnapshot;
    private readonly RemoteDirtyChunkBundleEntry[] _bundleEntries;
    private readonly byte[] _compressedBundleScratch;
    private readonly byte[] _snapshotPayload;
    private readonly byte[] _snapshotSegmentScratch;
    private readonly bool[] _snapshotSegments;
    private readonly DeliveryLineage[] _deliveryLineage;
    // A fence belongs to a live semantic slot generation, not every chunk ever visited.
    // The session-wide cutoff initializes later residents without retaining travel history.
    private readonly ResidentInterestFence[] _residentInterestFences;
    private readonly ClientSemanticChunkInfo[] _residentFenceScratch;
    private PublicationEpoch _retirementEpoch;
    private ulong _retirementCutoff;
    private ulong _interestRetirementsProcessed;
    private readonly PendingProof[] _proofs;
    private readonly RemoteChunkAppliedEnvelopeEntry[] _proofEnvelopeEntries;
    private readonly byte[] _proofScratch;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _proofHead;
    private int _proofCount;
    private ulong _appliedDeltas;
    private ulong _appliedAbsences;
    private ulong _duplicateDeliveries;
    private ulong _rejectedDeliveries;
    private ulong _missingResidentDeliveryAttempts;
    private ulong _missingResidentPreparedChunkAttempts;
    private ulong _missingResidentSnapshotBeginAttempts;
    private ulong _capacityFailures;
    private ulong _contradictoryDeliveries;
    private ulong _malformedDeliveries;
    private ulong _proofsQueued;
    private ulong _proofMessagesQueued;
    private RemoteChunkSnapshotBeginMessage _snapshotBegin;
    private ClientSemanticChunkHandle _snapshotHandle;
    private int _snapshotSegmentsReceived;
    private bool _snapshotCommitReceived;
    private ulong _appliedSnapshots;
    private ulong _rejectedSnapshots;
    private ulong _supersededDeliveries;
    private NetworkTelemetryLevel _telemetryLevel;
    private readonly ClientTimingWindow _decodeAndApplyTiming = new();
    private readonly ClientTimingWindow _proofDrainTiming = new();
    private readonly ClientTimingWindow _proofQueueLatency = new();
    private readonly ClientValueWindow _proofEnvelopeOccupancy = new();
    private readonly ClientValueWindow _bundleEnvelopeOccupancy = new();
    private readonly ClientTimingWindow _semanticApplyIntervals = new();
    private readonly ClientTimingWindow _decompressionTiming = new();
    private long _lastSemanticApplyTimestamp;
    private ulong _timedBundleEnvelopes;
    private ulong _timedBundleEntries;
    private ulong _timedBundleBytes;
    private ulong _timedCompressedMessages;
    private ulong _timedCompressedBytes;
    private ulong _timedCompressedDecodedBytes;
    private ulong _timedCompressedMalformed;
    private ulong _timedProofsGenerated;
    private ulong _timedProofsQueued;
    private ulong _timedProofEnvelopesQueued;
    private ulong _timedProofEnvelopeBytes;
    private ulong _timedProofQueueDeferrals;
    private ulong _timedProofApplied;
    private ulong _timedProofRejected;
    private ulong _timedProofCapacityExceeded;
    private ulong _timedProofContradictory;
    private ulong _timedProofDelta;
    private ulong _timedProofSnapshot;
    private ulong _timedProofAbsence;
    private ulong _timedPositiveSubsumptionOpportunities;
    private ulong _timedFullProofEnvelopes;
    private ulong _timedSingleProofEnvelopes;
    private int _timedProofQueueHighWater;
    private RemoteChunkWorldStateClientSnapshot _timedDeliveryStart;

    public RemoteChunkWorldStateClient(
        NetworkProtocolPeer peer,
        ClientWorldSemanticCache cache,
        ulong applicationSessionEpoch,
        int maximumDeltaRecords = UltraSimGameProtocol.MaximumChunkPublicationRecords,
        int maximumPendingProofs = 128)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        if (maximumDeltaRecords is <= 0 or > UltraSimGameProtocol.MaximumChunkPublicationRecords)
            throw new ArgumentOutOfRangeException(nameof(maximumDeltaRecords));
        if (maximumDeltaRecords > cache.Limits.MaximumPublicRecordsPerChunk)
            throw new ArgumentOutOfRangeException(nameof(maximumDeltaRecords));
        if (cache.Limits.MaximumPublicRecordsPerChunk >
            UltraSimGameProtocol.MaximumChunkPublicationRecords)
            throw new ArgumentOutOfRangeException(nameof(cache));
        if (maximumPendingProofs <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingProofs));
        _applicationSessionEpoch = applicationSessionEpoch;
        _wireDeltas = new RemoteChunkDeltaRecord[maximumDeltaRecords];
        _semanticDeltas = new WorldPublicDeltaRecord[maximumDeltaRecords];
        _wireSnapshot = new RemoteChunkPublicRecord[cache.Limits.MaximumPublicRecordsPerChunk];
        _semanticSnapshot = new WorldPublicRecord[cache.Limits.MaximumPublicRecordsPerChunk];
        _bundleEntries = new RemoteDirtyChunkBundleEntry[
            UltraSimGameProtocol.MaximumDirtyChunkBundleEntries];
        _compressedBundleScratch = new byte[
            UltraSimGameProtocol.MaximumCompressedDirtyChunkBundleDecodedBytes];
        _snapshotPayload = new byte[UltraSimGameProtocol.GetChunkSnapshotEncodedBytes(
            cache.Limits.MaximumPublicRecordsPerChunk)];
        int segmentPayload = peer.MaximumUnfragmentedMessageBytes -
            RemoteChunkSnapshotSegmentHeaderCodec.EncodedSize;
        if (segmentPayload is <= 0 or > ushort.MaxValue ||
            (_snapshotPayload.Length + segmentPayload - 1) / segmentPayload >
                UltraSimGameProtocol.MaximumChunkSnapshotSegments)
            throw new ArgumentOutOfRangeException(nameof(peer));
        _snapshotSegmentScratch = new byte[segmentPayload];
        _snapshotSegments = new bool[UltraSimGameProtocol.MaximumChunkSnapshotSegments];
        _deliveryLineage = new DeliveryLineage[cache.Limits.MaximumResidentChunks];
        _residentInterestFences = new ResidentInterestFence[cache.Limits.MaximumResidentChunks];
        _residentFenceScratch = new ClientSemanticChunkInfo[cache.Limits.MaximumResidentChunks];
        _proofs = new PendingProof[maximumPendingProofs];
        int maximumEnvelopeEntries = UltraSimGameProtocol.
            GetMaximumChunkAppliedEnvelopeEntriesForPayload(peer.MaximumUnfragmentedMessageBytes);
        if (maximumEnvelopeEntries == 0)
            throw new ArgumentOutOfRangeException(nameof(peer));
        _proofEnvelopeEntries = new RemoteChunkAppliedEnvelopeEntry[maximumEnvelopeEntries];
        _proofScratch = new byte[UltraSimGameProtocol.GetChunkAppliedEnvelopeEncodedBytes(
            maximumEnvelopeEntries)];
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwner();
        long timing = _telemetryLevel == NetworkTelemetryLevel.Detailed
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0;
        try { return TryProcessCore(in message, payload); }
        finally
        {
            if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
                _decodeAndApplyTiming.RecordElapsed(timing);
        }
    }

    private bool TryProcessCore(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        if (message.MessageId == RemoteChunkInterestRetiredMessageCodec.Descriptor.Id)
        {
            ProcessInterestRetired(payload);
            return true;
        }
        if (message.MessageId == RemoteChunkDeltaHeaderCodec.Descriptor.Id)
        {
            ProcessDelta(payload);
            return true;
        }
        if (message.MessageId == RemoteChunkAbsenceMessageCodec.Descriptor.Id)
        {
            ProcessAbsence(payload);
            return true;
        }
        if (message.MessageId == RemoteDirtyChunkBundleHeaderCodec.Descriptor.Id)
        {
            ProcessDirtyChunkBundle(payload);
            return true;
        }
        if (message.MessageId == RemoteCompressedDirtyChunkBundleHeaderCodec.Descriptor.Id)
        {
            ProcessCompressedDirtyChunkBundle(payload);
            return true;
        }
        if (message.MessageId == RemoteChunkSnapshotBeginMessageCodec.Descriptor.Id)
        {
            ProcessSnapshotBegin(payload);
            return true;
        }
        if (message.MessageId == RemoteChunkSnapshotSegmentHeaderCodec.Descriptor.Id)
        {
            ProcessSnapshotSegment(payload);
            return true;
        }
        if (message.MessageId == RemoteChunkSnapshotCommitMessageCodec.Descriptor.Id)
        {
            ProcessSnapshotCommit(payload);
            return true;
        }
        if (message.MessageId == RemoteChunkSnapshotCancelMessageCodec.Descriptor.Id)
        {
            ProcessSnapshotCancel(payload);
            return true;
        }
        return false;
    }

    public bool TryProgress()
    {
        EnsureOwner();
        long timing = _telemetryLevel == NetworkTelemetryLevel.Detailed
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0;
        try { return TryProgressCore(); }
        finally
        {
            if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
                _proofDrainTiming.RecordElapsed(timing);
        }
    }

    private bool TryProgressCore()
    {
        if (!FlushProofs()) return false;
        TryCommitSnapshotCandidate();
        return FlushProofs();
    }

    private bool FlushProofs()
    {
        while (_proofCount != 0)
        {
            RemoteChunkAppliedMessage first = _proofs[_proofHead].Message;
            int count = 0;
            while (count < _proofCount && count < _proofEnvelopeEntries.Length)
            {
                RemoteChunkAppliedMessage proof =
                    _proofs[(_proofHead + count) % _proofs.Length].Message;
                if (proof.ApplicationSessionEpoch != first.ApplicationSessionEpoch ||
                    proof.PublicationEpoch != first.PublicationEpoch)
                    break;
                _proofEnvelopeEntries[count] =
                    RemoteChunkAppliedEnvelopeEntry.FromMessage(in proof);
                count++;
            }
            if (!UltraSimGameProtocol.TryWriteChunkAppliedEnvelope(
                    _proofScratch, first.ApplicationSessionEpoch, first.PublicationEpoch,
                    _proofEnvelopeEntries.AsSpan(0, count), out int written))
                throw new InvalidOperationException("Retained chunk proofs became non-canonical.");
            if (_peer.TryQueue(RemoteChunkAppliedEnvelopeHeaderCodec.Descriptor.Id,
                    _proofScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            {
                if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
                    SaturatingIncrement(ref _timedProofQueueDeferrals);
                _proofEnvelopeEntries.AsSpan(0, count).Clear();
                return false;
            }
            for (int index = 0; index < count; index++)
            {
                PendingProof pending = _proofs[_proofHead];
                if (_telemetryLevel == NetworkTelemetryLevel.Detailed &&
                    pending.GeneratedTimestamp != 0)
                    _proofQueueLatency.RecordElapsed(pending.GeneratedTimestamp);
                _proofs[_proofHead] = default;
                _proofHead = (_proofHead + 1) % _proofs.Length;
            }
            _proofCount -= count;
            _proofEnvelopeEntries.AsSpan(0, count).Clear();
            SaturatingAdd(ref _proofsQueued, checked((ulong)count));
            SaturatingIncrement(ref _proofMessagesQueued);
            if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
            {
                SaturatingAdd(ref _timedProofsQueued, checked((ulong)count));
                SaturatingIncrement(ref _timedProofEnvelopesQueued);
                SaturatingAdd(ref _timedProofEnvelopeBytes, checked((ulong)written));
                _proofEnvelopeOccupancy.Record(checked((ulong)count));
                if (count == 1) SaturatingIncrement(ref _timedSingleProofEnvelopes);
                if (count == _proofEnvelopeEntries.Length)
                    SaturatingIncrement(ref _timedFullProofEnvelopes);
            }
        }
        return true;
    }

    public RemoteChunkWorldStateClientSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new RemoteChunkWorldStateClientSnapshot(
            _proofCount,
            _proofs.Length,
            _appliedDeltas,
            _appliedAbsences,
            _duplicateDeliveries,
            _rejectedDeliveries,
            _capacityFailures,
            _contradictoryDeliveries,
            _malformedDeliveries,
            _proofsQueued,
            _proofMessagesQueued,
            _snapshotBegin.DeliveryId != 0,
            _snapshotSegmentsReceived,
            _appliedSnapshots,
            _rejectedSnapshots,
            _supersededDeliveries,
            checked((long)_wireDeltas.Length * Unsafe.SizeOf<RemoteChunkDeltaRecord>() +
                (long)_semanticDeltas.Length * Unsafe.SizeOf<WorldPublicDeltaRecord>() +
                (long)_wireSnapshot.Length * Unsafe.SizeOf<RemoteChunkPublicRecord>() +
                (long)_semanticSnapshot.Length * Unsafe.SizeOf<WorldPublicRecord>() +
                (long)_bundleEntries.Length * Unsafe.SizeOf<RemoteDirtyChunkBundleEntry>() +
                _compressedBundleScratch.Length +
                _snapshotPayload.Length + _snapshotSegmentScratch.Length +
                _snapshotSegments.Length +
                (long)_deliveryLineage.Length * Unsafe.SizeOf<DeliveryLineage>() +
                (long)_residentInterestFences.Length * Unsafe.SizeOf<ResidentInterestFence>() +
                (long)_residentFenceScratch.Length * Unsafe.SizeOf<ClientSemanticChunkInfo>() +
                (long)_proofs.Length * Unsafe.SizeOf<PendingProof>() +
                (long)_proofEnvelopeEntries.Length *
                    Unsafe.SizeOf<RemoteChunkAppliedEnvelopeEntry>() +
                _proofScratch.Length),
            GetTimingSnapshot())
        {
            RetiredInterestCount = CountResidentInterestFences(),
            InterestRetirementsProcessed = _interestRetirementsProcessed,
            MissingResidentDeliveryAttempts = _missingResidentDeliveryAttempts,
            MissingResidentPreparedChunkAttempts = _missingResidentPreparedChunkAttempts,
            MissingResidentSnapshotBeginAttempts = _missingResidentSnapshotBeginAttempts,
        };
    }

    public void ConfigureTelemetry(NetworkTelemetryLevel level)
    {
        EnsureOwner();
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        _telemetryLevel = level;
        _decodeAndApplyTiming.Clear();
        _proofDrainTiming.Clear();
        _proofQueueLatency.Clear();
        _proofEnvelopeOccupancy.Clear();
        _bundleEnvelopeOccupancy.Clear();
        _semanticApplyIntervals.Clear();
        _decompressionTiming.Clear();
        _lastSemanticApplyTimestamp = 0;
        _timedBundleEnvelopes = _timedBundleEntries = _timedBundleBytes = 0;
        _timedCompressedMessages = _timedCompressedBytes = 0;
        _timedCompressedDecodedBytes = _timedCompressedMalformed = 0;
        _timedProofsGenerated = _timedProofsQueued = 0;
        _timedProofEnvelopesQueued = _timedProofEnvelopeBytes = 0;
        _timedProofQueueDeferrals = 0;
        _timedProofApplied = _timedProofRejected = 0;
        _timedProofCapacityExceeded = _timedProofContradictory = 0;
        _timedProofDelta = _timedProofSnapshot = _timedProofAbsence = 0;
        _timedPositiveSubsumptionOpportunities = 0;
        _timedFullProofEnvelopes = _timedSingleProofEnvelopes = 0;
        _timedProofQueueHighWater = _proofCount;
        _timedDeliveryStart = GetSnapshotWithoutTiming();
    }

    public RemoteChunkWorldStateClientTimingSnapshot GetTimingSnapshot()
    {
        EnsureOwner();
        RemoteChunkWorldStateClientSnapshot current = GetSnapshotWithoutTiming();
        return new(
            _telemetryLevel,
            _decodeAndApplyTiming.Capture(),
            _proofDrainTiming.Capture(),
            new RemoteChunkProofCensusSnapshot(
                _timedProofsGenerated,
                _timedProofsQueued,
                _timedProofEnvelopesQueued,
                _timedProofEnvelopeBytes,
                _timedProofQueueDeferrals,
                _timedProofApplied,
                _timedProofRejected,
                _timedProofCapacityExceeded,
                _timedProofContradictory,
                _timedProofDelta,
                _timedProofSnapshot,
                _timedProofAbsence,
                _timedPositiveSubsumptionOpportunities,
                _timedFullProofEnvelopes,
                _timedSingleProofEnvelopes,
                _proofCount,
                _timedProofQueueHighWater,
                _proofs.Length,
                _proofEnvelopeEntries.Length,
                _proofEnvelopeOccupancy.Capture(),
                _proofQueueLatency.Capture()),
            new RemoteChunkDeliveryCensusSnapshot(
                Difference(_timedDeliveryStart.AppliedDeltas, current.AppliedDeltas),
                Difference(_timedDeliveryStart.AppliedSnapshots, current.AppliedSnapshots),
                Difference(_timedDeliveryStart.AppliedAbsences, current.AppliedAbsences),
                Difference(_timedDeliveryStart.DuplicateDeliveries, current.DuplicateDeliveries),
                Difference(_timedDeliveryStart.SupersededDeliveries, current.SupersededDeliveries),
                Difference(_timedDeliveryStart.RejectedDeliveries, current.RejectedDeliveries),
                Difference(_timedDeliveryStart.CapacityFailures, current.CapacityFailures),
                Difference(_timedDeliveryStart.ContradictoryDeliveries,
                    current.ContradictoryDeliveries),
                Difference(_timedDeliveryStart.MalformedDeliveries, current.MalformedDeliveries)),
            new RemoteChunkBundleReceiveCensusSnapshot(
                _timedBundleEnvelopes,
                _timedBundleEntries,
                _timedBundleBytes,
                _bundleEnvelopeOccupancy.Capture()),
            new RemoteChunkCompressionReceiveCensusSnapshot(
                _timedCompressedMessages,
                _timedCompressedBytes,
                _timedCompressedDecodedBytes,
                _timedCompressedMalformed,
                _decompressionTiming.Capture()),
            _semanticApplyIntervals.Capture());
    }

    private RemoteChunkWorldStateClientSnapshot GetSnapshotWithoutTiming() => new(
        _proofCount,
        _proofs.Length,
        _appliedDeltas,
        _appliedAbsences,
        _duplicateDeliveries,
        _rejectedDeliveries,
        _capacityFailures,
        _contradictoryDeliveries,
        _malformedDeliveries,
        _proofsQueued,
        _proofMessagesQueued,
        _snapshotBegin.DeliveryId != 0,
        _snapshotSegmentsReceived,
        _appliedSnapshots,
        _rejectedSnapshots,
        _supersededDeliveries,
        0,
        default)
    {
        RetiredInterestCount = CountResidentInterestFences(),
        InterestRetirementsProcessed = _interestRetirementsProcessed,
        MissingResidentDeliveryAttempts = _missingResidentDeliveryAttempts,
        MissingResidentPreparedChunkAttempts = _missingResidentPreparedChunkAttempts,
        MissingResidentSnapshotBeginAttempts = _missingResidentSnapshotBeginAttempts,
    };

    private static ulong Difference(ulong before, ulong after) =>
        after >= before ? after - before : 0;

    private void ProcessInterestRetired(ReadOnlySpan<byte> payload)
    {
        if (!RemoteChunkInterestRetiredMessageCodec.TryReadExact(payload, out var retired) ||
            retired.ApplicationSessionEpoch != _applicationSessionEpoch ||
            retired.PublicationEpoch == 0 || retired.SpatialSpaceId == 0)
        {
            SaturatingIncrement(ref _rejectedDeliveries);
            return;
        }
        var epoch = new PublicationEpoch(retired.PublicationEpoch);
        var key = new SpatialChunkKey(new SpatialSpaceId(retired.SpatialSpaceId),
            retired.ChunkX, retired.ChunkZ);
        PublicationEpoch cacheEpoch = _cache.GetSnapshot().PublicationEpoch;
        if (cacheEpoch.IsValid && cacheEpoch != epoch) return;
        if (_retirementEpoch != epoch)
        {
            Array.Clear(_residentInterestFences);
            _retirementEpoch = epoch;
            _retirementCutoff = 0;
        }
        // Existing residents keep the floor from their own admission. Capture them before
        // advancing the global floor, which applies only to future slot generations.
        SynchronizeResidentInterestFences(epoch);
        if (_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle retiredHandle))
        {
            ref ResidentInterestFence fence = ref _residentInterestFences[checked((int)retiredHandle.Index)];
            if (fence.Cutoff >= retired.DeliveryCutoff) return;
            fence = fence with { Cutoff = retired.DeliveryCutoff };
        }
        if (retired.DeliveryCutoff > _retirementCutoff)
            _retirementCutoff = retired.DeliveryCutoff;
        SaturatingIncrement(ref _interestRetirementsProcessed);

        if (_snapshotBegin.DeliveryId != 0 &&
            _snapshotBegin.PublicationEpoch == retired.PublicationEpoch &&
            _snapshotBegin.SpatialSpaceId == retired.SpatialSpaceId &&
            _snapshotBegin.ChunkX == retired.ChunkX &&
            _snapshotBegin.ChunkZ == retired.ChunkZ &&
            _snapshotBegin.DeliveryId <= retired.DeliveryCutoff)
            ClearSnapshotCandidate();
        if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle)) return;
        DeliveryLineage lineage = _deliveryLineage[checked((int)handle.Index)];
        if (lineage.IsValid && lineage.HandleGeneration == handle.Generation &&
            lineage.Key == key && lineage.Epoch == epoch &&
            lineage.DeliveryId > retired.DeliveryCutoff)
            return;
        if (!_cache.TryRetirePublicInterest(handle, epoch, out _))
            throw new InvalidOperationException(
                "An authenticated public-interest retirement could not clear client knowledge.");
    }

    private bool IsRetiredDelivery(SpatialChunkKey key, PublicationEpoch epoch, ulong deliveryId)
    {
        if (_retirementEpoch != epoch) return false;
        if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle))
            return false; // Keep the ordinary explicit rejection/proof path for absent residency.
        ref ResidentInterestFence fence = ref EnsureResidentInterestFence(handle, key, epoch);
        return deliveryId <= fence.Cutoff;
    }

    private ref ResidentInterestFence EnsureResidentInterestFence(
        ClientSemanticChunkHandle handle, SpatialChunkKey key, PublicationEpoch epoch)
    {
        ref ResidentInterestFence fence = ref _residentInterestFences[checked((int)handle.Index)];
        if (fence.HandleGeneration != handle.Generation || fence.Key != key ||
            fence.Epoch != epoch)
            fence = new ResidentInterestFence(key, handle.Generation, epoch, _retirementCutoff);
        return ref fence;
    }

    private void SynchronizeResidentInterestFences(PublicationEpoch epoch)
    {
        if (!_cache.TryCopyResidentInfo(_residentFenceScratch, out int count, out _))
            throw new InvalidOperationException("The resident interest fence scratch is undersized.");
        for (int index = 0; index < count; index++)
        {
            ClientSemanticChunkInfo info = _residentFenceScratch[index];
            _ = EnsureResidentInterestFence(info.Handle, info.Key, epoch);
        }
    }

    private int CountResidentInterestFences()
    {
        int count = 0;
        for (int index = 0; index < _residentInterestFences.Length; index++)
        {
            ref readonly ResidentInterestFence fence = ref _residentInterestFences[index];
            if (fence.Cutoff != 0 &&
                _cache.TryGetResidentHandle(fence.Key, out ClientSemanticChunkHandle handle) &&
                handle.Index == index && handle.Generation == fence.HandleGeneration)
                count++;
        }
        return count;
    }

    private void ProcessDirtyChunkBundle(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadDirtyChunkBundle(
                payload, out RemoteDirtyChunkBundleHeader header, _bundleEntries,
                _wireSnapshot, _wireDeltas, out int entryCount,
                out int snapshotCount, out int deltaCount))
        {
            SaturatingIncrement(ref _malformedDeliveries);
            return;
        }
        if (header.ApplicationSessionEpoch != _applicationSessionEpoch ||
            entryCount > _proofs.Length - _proofCount)
        {
            SaturatingIncrement(ref _rejectedDeliveries);
            ClearBundleScratch(entryCount, snapshotCount, deltaCount);
            return;
        }
        if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
        {
            SaturatingIncrement(ref _timedBundleEnvelopes);
            SaturatingAdd(ref _timedBundleEntries, checked((ulong)entryCount));
            SaturatingAdd(ref _timedBundleBytes, checked((ulong)payload.Length));
            _bundleEnvelopeOccupancy.Record(checked((ulong)entryCount));
        }
        for (int index = 0; index < entryCount; index++)
        {
            ref readonly RemoteDirtyChunkBundleEntry entry = ref _bundleEntries[index];
            switch (entry.Kind)
            {
                case RemoteChunkPublicationKind.Snapshot:
                    ApplyBundledSnapshot(header, entry,
                        _wireSnapshot.AsSpan(entry.RecordOffset, entry.RecordCount));
                    break;
                case RemoteChunkPublicationKind.Delta:
                    ApplyBundledDelta(header, entry,
                        _wireDeltas.AsSpan(entry.RecordOffset, entry.RecordCount));
                    break;
                case RemoteChunkPublicationKind.Absence:
                    ApplyBundledAbsence(header, entry);
                    break;
                default:
                    throw new InvalidOperationException("A validated dirty-chunk bundle changed kind.");
            }
        }
        ClearBundleScratch(entryCount, snapshotCount, deltaCount);
    }

    private void ProcessCompressedDirtyChunkBundle(ReadOnlySpan<byte> payload)
    {
        long started = _telemetryLevel == NetworkTelemetryLevel.Detailed
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0;
        bool decoded = UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            payload, _compressedBundleScratch, out int decodedBytes);
        if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
            _decompressionTiming.RecordElapsed(started);
        if (!decoded)
        {
            SaturatingIncrement(ref _malformedDeliveries);
            if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
                SaturatingIncrement(ref _timedCompressedMalformed);
            return;
        }
        if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
        {
            SaturatingIncrement(ref _timedCompressedMessages);
            SaturatingAdd(ref _timedCompressedBytes, checked((ulong)payload.Length));
            SaturatingAdd(ref _timedCompressedDecodedBytes, checked((ulong)decodedBytes));
        }
        ProcessDirtyChunkBundle(_compressedBundleScratch.AsSpan(0, decodedBytes));
        _compressedBundleScratch.AsSpan(0, decodedBytes).Clear();
    }

    private void ApplyBundledDelta(
        in RemoteDirtyChunkBundleHeader header,
        in RemoteDirtyChunkBundleEntry entry,
        ReadOnlySpan<RemoteChunkDeltaRecord> records)
    {
        var epoch = new PublicationEpoch(header.PublicationEpoch);
        var key = new SpatialChunkKey(
            new SpatialSpaceId(entry.SpatialSpaceId), entry.ChunkX, entry.ChunkZ);
        if (IsRetiredDelivery(key, epoch, entry.DeliveryId)) return;
        RemoteChunkPublicationApplyStatus status;
        bool duplicate = false;
        DeliveryLineageDecision lineage = DeliveryLineageDecision.New;
        if (!TryGetPublicationHandle(key, out ClientSemanticChunkHandle handle) ||
            !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo info) ||
            !info.PublicationCursor.IsValid || info.PublicationCursor.Epoch != epoch)
            status = RemoteChunkPublicationApplyStatus.Rejected;
        else if ((lineage = CompareLineage(handle, key, epoch, entry.DeliveryId,
                RemoteChunkPublicationKind.Delta, entry.CurrentRevision, entry.ContentHash)) is
            DeliveryLineageDecision.Superseded or DeliveryLineageDecision.Duplicate)
        {
            status = RemoteChunkPublicationApplyStatus.Applied;
            duplicate = true;
        }
        else if (lineage == DeliveryLineageDecision.Contradictory)
            status = RemoteChunkPublicationApplyStatus.Contradictory;
        else if (info.PublicationCursor.Revision.Value >= entry.CurrentRevision)
        {
            status = RemoteChunkPublicationApplyStatus.Applied;
            duplicate = true;
        }
        // A later coalesced delivery may still start at the server's older applied cursor while
        // this client has locally applied an unproved intermediate delivery. The delta contains
        // latest operations for the complete base..current interval, and the semantic cache
        // independently rejects conflicting identity/version rows, so an overlapping base is
        // valid. Only a base ahead of local truth represents a gap.
        else if (info.PublicationCursor.Revision.Value < entry.BaseRevision ||
                 !FitsWorldProfile(records))
            status = RemoteChunkPublicationApplyStatus.Rejected;
        else
        {
            for (int index = 0; index < records.Length; index++)
                _semanticDeltas[index] = ToSemantic(key, records[index]);
            var cursor = new ChunkPublicationCursor(
                epoch, key, new ChunkPublicationRevision(entry.CurrentRevision));
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.Delta, key, cursor,
                new ChunkPublicationRevision(entry.HistoryFloor), records.Length, records.Length);
            status = _cache.TryApplyDelta(handle, batch,
                    _semanticDeltas.AsSpan(0, records.Length), out ClientSemanticChunkError error)
                ? RemoteChunkPublicationApplyStatus.Applied
                : Map(error);
        }
        if (status == RemoteChunkPublicationApplyStatus.Applied && !duplicate &&
            lineage == DeliveryLineageDecision.New)
            CommitLineage(handle, key, epoch, entry.DeliveryId,
                RemoteChunkPublicationKind.Delta, entry.CurrentRevision, entry.ContentHash);
        QueueProof(header.ApplicationSessionEpoch, header.PublicationEpoch, entry.DeliveryId,
            entry.SpatialSpaceId, entry.ChunkX, entry.ChunkZ, entry.CurrentRevision,
            RemoteChunkPublicationKind.Delta, status, handle);
        if (status == RemoteChunkPublicationApplyStatus.Applied)
        {
            if (duplicate) SaturatingIncrement(ref _duplicateDeliveries);
            else
            {
                SaturatingIncrement(ref _appliedDeltas);
                RecordSemanticApply();
            }
        }
        else RecordFailure(status);
        _semanticDeltas.AsSpan(0, records.Length).Clear();
    }

    private void ApplyBundledSnapshot(
        in RemoteDirtyChunkBundleHeader header,
        in RemoteDirtyChunkBundleEntry entry,
        ReadOnlySpan<RemoteChunkPublicRecord> records)
    {
        var epoch = new PublicationEpoch(header.PublicationEpoch);
        var key = new SpatialChunkKey(
            new SpatialSpaceId(entry.SpatialSpaceId), entry.ChunkX, entry.ChunkZ);
        if (IsRetiredDelivery(key, epoch, entry.DeliveryId)) return;
        RemoteChunkPublicationApplyStatus status;
        DeliveryLineageDecision lineage = DeliveryLineageDecision.New;
        if (!TryGetPublicationHandle(key, out ClientSemanticChunkHandle handle)
            || !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo info) || info.Key != key)
            status = RemoteChunkPublicationApplyStatus.Rejected;
        else if ((lineage = CompareLineage(handle, key, epoch, entry.DeliveryId,
                RemoteChunkPublicationKind.Snapshot, entry.CurrentRevision, entry.ContentHash)) is
            DeliveryLineageDecision.Superseded or DeliveryLineageDecision.Duplicate)
        {
            status = RemoteChunkPublicationApplyStatus.Applied;
            SaturatingIncrement(ref _supersededDeliveries);
        }
        else if (lineage == DeliveryLineageDecision.Contradictory)
            status = RemoteChunkPublicationApplyStatus.Contradictory;
        else if (!FitsWorldProfile(records))
            status = RemoteChunkPublicationApplyStatus.Rejected;
        else
        {
            for (int index = 0; index < records.Length; index++)
                _semanticSnapshot[index] = ToSemantic(key, records[index]);
            var cursor = new ChunkPublicationCursor(
                epoch, key, new ChunkPublicationRevision(entry.CurrentRevision));
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.Snapshot, key, cursor,
                new ChunkPublicationRevision(entry.HistoryFloor), records.Length, records.Length);
            status = _cache.TryApplySnapshot(handle, batch,
                    _semanticSnapshot.AsSpan(0, records.Length), out ClientSemanticChunkError error)
                ? RemoteChunkPublicationApplyStatus.Applied
                : Map(error);
        }
        if (status == RemoteChunkPublicationApplyStatus.Applied &&
            lineage == DeliveryLineageDecision.New)
        {
            CommitLineage(handle, key, epoch, entry.DeliveryId,
                RemoteChunkPublicationKind.Snapshot, entry.CurrentRevision, entry.ContentHash);
            SaturatingIncrement(ref _appliedSnapshots);
            RecordSemanticApply();
        }
        else if (status != RemoteChunkPublicationApplyStatus.Applied)
        {
            SaturatingIncrement(ref _rejectedSnapshots);
            RecordFailure(status);
        }
        QueueProof(header.ApplicationSessionEpoch, header.PublicationEpoch, entry.DeliveryId,
            entry.SpatialSpaceId, entry.ChunkX, entry.ChunkZ, entry.CurrentRevision,
            RemoteChunkPublicationKind.Snapshot, status, handle);
        _semanticSnapshot.AsSpan(0, records.Length).Clear();
    }

    private void ApplyBundledAbsence(
        in RemoteDirtyChunkBundleHeader header,
        in RemoteDirtyChunkBundleEntry entry)
    {
        var epoch = new PublicationEpoch(header.PublicationEpoch);
        var key = new SpatialChunkKey(
            new SpatialSpaceId(entry.SpatialSpaceId), entry.ChunkX, entry.ChunkZ);
        if (IsRetiredDelivery(key, epoch, entry.DeliveryId)) return;
        RemoteChunkPublicationApplyStatus status;
        DeliveryLineageDecision lineage = DeliveryLineageDecision.New;
        if (!TryGetPublicationHandle(key, out ClientSemanticChunkHandle handle))
            status = RemoteChunkPublicationApplyStatus.Rejected;
        else if ((lineage = CompareLineage(handle, key, epoch, entry.DeliveryId,
                RemoteChunkPublicationKind.Absence, 0, 0)) is
            DeliveryLineageDecision.Superseded or DeliveryLineageDecision.Duplicate)
        {
            status = RemoteChunkPublicationApplyStatus.Applied;
            SaturatingIncrement(ref _supersededDeliveries);
        }
        else if (lineage == DeliveryLineageDecision.Contradictory)
            status = RemoteChunkPublicationApplyStatus.Contradictory;
        else
        {
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.ChunkAbsent, key, default, default, 0, 0);
            status = _cache.TryApplyChunkAbsent(handle, epoch, batch,
                    out ClientSemanticChunkError error)
                ? RemoteChunkPublicationApplyStatus.Applied
                : Map(error);
        }
        if (status == RemoteChunkPublicationApplyStatus.Applied &&
            lineage == DeliveryLineageDecision.New)
        {
            CommitLineage(handle, key, epoch, entry.DeliveryId,
                RemoteChunkPublicationKind.Absence, 0, 0);
            RecordSemanticApply();
        }
        QueueProof(header.ApplicationSessionEpoch, header.PublicationEpoch, entry.DeliveryId,
            entry.SpatialSpaceId, entry.ChunkX, entry.ChunkZ, 0,
            RemoteChunkPublicationKind.Absence, status, handle);
        if (status == RemoteChunkPublicationApplyStatus.Applied)
            SaturatingIncrement(ref _appliedAbsences);
        else RecordFailure(status);
    }

    private void ClearBundleScratch(int entries, int snapshots, int deltas)
    {
        _bundleEntries.AsSpan(0, entries).Clear();
        _wireSnapshot.AsSpan(0, snapshots).Clear();
        _wireDeltas.AsSpan(0, deltas).Clear();
    }

    private void ProcessDelta(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadChunkDelta(
                payload, out RemoteChunkDeltaHeader header, _wireDeltas, out int count))
        {
            SaturatingIncrement(ref _malformedDeliveries);
            return;
        }
        if (header.ApplicationSessionEpoch != _applicationSessionEpoch || _proofCount == _proofs.Length)
        {
            SaturatingIncrement(ref _rejectedDeliveries);
            _wireDeltas.AsSpan(0, count).Clear();
            return;
        }

        var epoch = new PublicationEpoch(header.PublicationEpoch);
        var key = new SpatialChunkKey(new SpatialSpaceId(header.SpatialSpaceId), header.ChunkX, header.ChunkZ);
        if (IsRetiredDelivery(key, epoch, header.DeliveryId))
        {
            _wireDeltas.AsSpan(0, count).Clear();
            return;
        }
        RemoteChunkPublicationApplyStatus status;
        bool duplicate = false;
        DeliveryLineageDecision lineage = DeliveryLineageDecision.New;
        if (!TryGetPublicationHandle(key, out ClientSemanticChunkHandle handle) ||
            !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo info) ||
            !info.PublicationCursor.IsValid || info.PublicationCursor.Epoch != epoch)
        {
            status = RemoteChunkPublicationApplyStatus.Rejected;
        }
        else if ((lineage = CompareLineage(handle, key, epoch, header.DeliveryId,
                RemoteChunkPublicationKind.Delta, header.CurrentRevision, header.ContentHash)) is
            DeliveryLineageDecision.Superseded or DeliveryLineageDecision.Duplicate)
        {
            status = RemoteChunkPublicationApplyStatus.Applied;
            duplicate = true;
        }
        else if (lineage == DeliveryLineageDecision.Contradictory)
        {
            status = RemoteChunkPublicationApplyStatus.Contradictory;
        }
        else if (info.PublicationCursor.Revision.Value >= header.CurrentRevision)
        {
            status = RemoteChunkPublicationApplyStatus.Applied;
            duplicate = true;
        }
        else if (info.PublicationCursor.Revision.Value < header.BaseRevision)
        {
            status = RemoteChunkPublicationApplyStatus.Rejected;
        }
        else if (!FitsWorldProfile(_wireDeltas.AsSpan(0, count)))
        {
            status = RemoteChunkPublicationApplyStatus.Rejected;
        }
        else
        {
            for (int index = 0; index < count; index++)
                _semanticDeltas[index] = ToSemantic(key, _wireDeltas[index]);
            var cursor = new ChunkPublicationCursor(
                epoch, key, new ChunkPublicationRevision(header.CurrentRevision));
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.Delta,
                key,
                cursor,
                new ChunkPublicationRevision(header.HistoryFloor),
                count,
                count);
            bool applied = _cache.TryApplyDelta(
                handle, batch, _semanticDeltas.AsSpan(0, count), out ClientSemanticChunkError error);
            status = applied ? RemoteChunkPublicationApplyStatus.Applied : Map(error);
        }

        if (status == RemoteChunkPublicationApplyStatus.Applied && !duplicate &&
            lineage == DeliveryLineageDecision.New)
            CommitLineage(handle, key, epoch, header.DeliveryId,
                RemoteChunkPublicationKind.Delta, header.CurrentRevision, header.ContentHash);

        QueueProof(header.ApplicationSessionEpoch, header.PublicationEpoch, header.DeliveryId,
            header.SpatialSpaceId, header.ChunkX, header.ChunkZ, header.CurrentRevision,
            RemoteChunkPublicationKind.Delta, status, handle);
        if (status == RemoteChunkPublicationApplyStatus.Applied)
        {
            if (duplicate) SaturatingIncrement(ref _duplicateDeliveries);
            else
            {
                SaturatingIncrement(ref _appliedDeltas);
                RecordSemanticApply();
            }
        }
        else RecordFailure(status);
        _wireDeltas.AsSpan(0, count).Clear();
        _semanticDeltas.AsSpan(0, count).Clear();
    }

    private void ProcessAbsence(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadChunkAbsence(
                payload, out RemoteChunkAbsenceMessage message))
        {
            SaturatingIncrement(ref _malformedDeliveries);
            return;
        }
        if (message.ApplicationSessionEpoch != _applicationSessionEpoch || _proofCount == _proofs.Length)
        {
            SaturatingIncrement(ref _rejectedDeliveries);
            return;
        }
        var epoch = new PublicationEpoch(message.PublicationEpoch);
        var key = new SpatialChunkKey(
            new SpatialSpaceId(message.SpatialSpaceId), message.ChunkX, message.ChunkZ);
        if (IsRetiredDelivery(key, epoch, message.DeliveryId)) return;
        RemoteChunkPublicationApplyStatus status;
        DeliveryLineageDecision lineage = DeliveryLineageDecision.New;
        if (!TryGetPublicationHandle(key, out ClientSemanticChunkHandle handle))
        {
            status = RemoteChunkPublicationApplyStatus.Rejected;
        }
        else if ((lineage = CompareLineage(handle, key, epoch, message.DeliveryId,
                RemoteChunkPublicationKind.Absence, 0, 0)) is
            DeliveryLineageDecision.Superseded or DeliveryLineageDecision.Duplicate)
        {
            status = RemoteChunkPublicationApplyStatus.Applied;
            SaturatingIncrement(ref _supersededDeliveries);
        }
        else if (lineage == DeliveryLineageDecision.Contradictory)
        {
            status = RemoteChunkPublicationApplyStatus.Contradictory;
        }
        else
        {
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.ChunkAbsent, key, default, default, 0, 0);
            bool applied = _cache.TryApplyChunkAbsent(handle, epoch, batch,
                out ClientSemanticChunkError error);
            status = applied ? RemoteChunkPublicationApplyStatus.Applied : Map(error);
        }
        if (status == RemoteChunkPublicationApplyStatus.Applied &&
            lineage == DeliveryLineageDecision.New)
        {
            CommitLineage(handle, key, epoch, message.DeliveryId,
                RemoteChunkPublicationKind.Absence, 0, 0);
            RecordSemanticApply();
        }
        QueueProof(message.ApplicationSessionEpoch, message.PublicationEpoch, message.DeliveryId,
            message.SpatialSpaceId, message.ChunkX, message.ChunkZ, 0,
            RemoteChunkPublicationKind.Absence, status, handle);
        if (status == RemoteChunkPublicationApplyStatus.Applied)
            SaturatingIncrement(ref _appliedAbsences);
        else
            RecordFailure(status);
    }

    private void ProcessSnapshotBegin(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadChunkSnapshotBegin(payload, out var begin))
        {
            SaturatingIncrement(ref _malformedDeliveries);
            return;
        }
        if (begin.ApplicationSessionEpoch != _applicationSessionEpoch)
        {
            SaturatingIncrement(ref _rejectedSnapshots);
            return;
        }
        if (begin.SegmentPayloadBytes != _snapshotSegmentScratch.Length)
        {
            QueueSnapshotProof(in begin, RemoteChunkPublicationApplyStatus.Rejected);
            SaturatingIncrement(ref _rejectedSnapshots);
            return;
        }
        if (begin.RecordCount > _wireSnapshot.Length || begin.EncodedBytes > _snapshotPayload.Length)
        {
            QueueSnapshotProof(in begin, RemoteChunkPublicationApplyStatus.CapacityExceeded);
            SaturatingIncrement(ref _rejectedSnapshots);
            return;
        }
        if (_snapshotBegin.DeliveryId != 0)
        {
            if (SameBegin(in _snapshotBegin, in begin)) return;
            if (begin.DeliveryId <= _snapshotBegin.DeliveryId)
            {
                SaturatingIncrement(ref _supersededDeliveries);
                return;
            }
            ClearSnapshotCandidate();
        }

        var epoch = new PublicationEpoch(begin.PublicationEpoch);
        var key = new SpatialChunkKey(
            new SpatialSpaceId(begin.SpatialSpaceId), begin.ChunkX, begin.ChunkZ);
        if (IsRetiredDelivery(key, epoch, begin.DeliveryId)) return;
        if (!TryGetPublicationHandle(key, out ClientSemanticChunkHandle handle))
        {
            QueueSnapshotProof(in begin, RemoteChunkPublicationApplyStatus.Rejected);
            SaturatingIncrement(ref _rejectedSnapshots);
            SaturatingIncrement(ref _missingResidentSnapshotBeginAttempts);
            return;
        }
        DeliveryLineageDecision lineage = CompareLineage(
            handle, key, epoch, begin.DeliveryId, RemoteChunkPublicationKind.Snapshot,
            begin.Revision, begin.ContentHash);
        if (lineage is DeliveryLineageDecision.Superseded or DeliveryLineageDecision.Duplicate)
        {
            QueueSnapshotProof(in begin, RemoteChunkPublicationApplyStatus.Applied, handle);
            SaturatingIncrement(ref _supersededDeliveries);
            return;
        }
        if (lineage == DeliveryLineageDecision.Contradictory)
        {
            QueueSnapshotProof(in begin, RemoteChunkPublicationApplyStatus.Contradictory);
            SaturatingIncrement(ref _rejectedSnapshots);
            return;
        }

        _snapshotBegin = begin;
        _snapshotHandle = handle;
    }

    private void ProcessSnapshotSegment(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadChunkSnapshotSegment(
                payload, out var header, _snapshotSegmentScratch, out int count))
        {
            SaturatingIncrement(ref _malformedDeliveries);
            return;
        }
        if (_snapshotBegin.DeliveryId == 0 ||
            header.ApplicationSessionEpoch != _snapshotBegin.ApplicationSessionEpoch ||
            header.PublicationEpoch != _snapshotBegin.PublicationEpoch ||
            header.DeliveryId != _snapshotBegin.DeliveryId ||
            header.SegmentCount != _snapshotBegin.SegmentCount)
        {
            SaturatingIncrement(ref _supersededDeliveries);
            _snapshotSegmentScratch.AsSpan(0, count).Clear();
            return;
        }
        int offset = checked(header.SegmentIndex * _snapshotBegin.SegmentPayloadBytes);
        int expected = Math.Min(_snapshotBegin.SegmentPayloadBytes,
            checked((int)_snapshotBegin.EncodedBytes) - offset);
        if (offset < 0 || expected != count)
        {
            _snapshotSegmentScratch.AsSpan(0, count).Clear();
            RejectSnapshotCandidate(RemoteChunkPublicationApplyStatus.Rejected);
            return;
        }
        if (_snapshotSegments[header.SegmentIndex])
        {
            if (!_snapshotSegmentScratch.AsSpan(0, count)
                    .SequenceEqual(_snapshotPayload.AsSpan(offset, count)))
                RejectSnapshotCandidate(RemoteChunkPublicationApplyStatus.Contradictory);
            _snapshotSegmentScratch.AsSpan(0, count).Clear();
            return;
        }
        _snapshotSegmentScratch.AsSpan(0, count)
            .CopyTo(_snapshotPayload.AsSpan(offset, count));
        _snapshotSegmentScratch.AsSpan(0, count).Clear();
        _snapshotSegments[header.SegmentIndex] = true;
        _snapshotSegmentsReceived++;
        TryCommitSnapshotCandidate();
    }

    private void ProcessSnapshotCommit(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadChunkSnapshotCommit(payload, out var commit))
        {
            SaturatingIncrement(ref _malformedDeliveries);
            return;
        }
        if (_snapshotBegin.DeliveryId == 0 ||
            commit.ApplicationSessionEpoch != _snapshotBegin.ApplicationSessionEpoch ||
            commit.PublicationEpoch != _snapshotBegin.PublicationEpoch ||
            commit.DeliveryId != _snapshotBegin.DeliveryId)
        {
            SaturatingIncrement(ref _supersededDeliveries);
            return;
        }
        if (commit.SpatialSpaceId != _snapshotBegin.SpatialSpaceId ||
            commit.ChunkX != _snapshotBegin.ChunkX || commit.ChunkZ != _snapshotBegin.ChunkZ ||
            commit.Revision != _snapshotBegin.Revision ||
            commit.ContentHash != _snapshotBegin.ContentHash)
        {
            RejectSnapshotCandidate(RemoteChunkPublicationApplyStatus.Contradictory);
            return;
        }
        _snapshotCommitReceived = true;
        TryCommitSnapshotCandidate();
    }

    private void ProcessSnapshotCancel(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadChunkSnapshotCancel(payload, out var cancel))
        {
            SaturatingIncrement(ref _malformedDeliveries);
            return;
        }
        if (_snapshotBegin.DeliveryId != 0 &&
            cancel.ApplicationSessionEpoch == _snapshotBegin.ApplicationSessionEpoch &&
            cancel.PublicationEpoch == _snapshotBegin.PublicationEpoch &&
            cancel.DeliveryId == _snapshotBegin.DeliveryId)
            ClearSnapshotCandidate();
    }

    private void TryCommitSnapshotCandidate()
    {
        if (_snapshotBegin.DeliveryId == 0 || !_snapshotCommitReceived ||
            _snapshotSegmentsReceived != _snapshotBegin.SegmentCount ||
            _proofCount == _proofs.Length)
            return;
        RemoteChunkSnapshotBeginMessage begin = _snapshotBegin;
        var epoch = new PublicationEpoch(begin.PublicationEpoch);
        var key = new SpatialChunkKey(
            new SpatialSpaceId(begin.SpatialSpaceId), begin.ChunkX, begin.ChunkZ);
        RemoteChunkPublicationApplyStatus status;
        DeliveryLineageDecision lineage = DeliveryLineageDecision.New;
        int count = 0;
        if (!_cache.TryGetInfo(_snapshotHandle, out ClientSemanticChunkInfo info) ||
            info.Key != key)
        {
            status = RemoteChunkPublicationApplyStatus.Rejected;
        }
        else if ((lineage = CompareLineage(_snapshotHandle, key, epoch, begin.DeliveryId,
                RemoteChunkPublicationKind.Snapshot, begin.Revision, begin.ContentHash)) is
            DeliveryLineageDecision.Superseded or DeliveryLineageDecision.Duplicate)
        {
            status = RemoteChunkPublicationApplyStatus.Applied;
            SaturatingIncrement(ref _supersededDeliveries);
        }
        else if (lineage == DeliveryLineageDecision.Contradictory)
        {
            status = RemoteChunkPublicationApplyStatus.Contradictory;
        }
        else if (!UltraSimGameProtocol.TryReadChunkSnapshot(
                _snapshotPayload.AsSpan(0, checked((int)begin.EncodedBytes)),
                out RemoteChunkSnapshotHeader inner, _wireSnapshot, out count) ||
            inner.ApplicationSessionEpoch != begin.ApplicationSessionEpoch ||
            inner.PublicationEpoch != begin.PublicationEpoch ||
            inner.DeliveryId != begin.DeliveryId || inner.SpatialSpaceId != begin.SpatialSpaceId ||
            inner.ChunkX != begin.ChunkX || inner.ChunkZ != begin.ChunkZ ||
            inner.SchemaFingerprint != begin.SchemaFingerprint || inner.Revision != begin.Revision ||
            inner.HistoryFloor != begin.HistoryFloor || inner.ContentHash != begin.ContentHash ||
            inner.RecordCount != begin.RecordCount ||
            !FitsWorldProfile(_wireSnapshot.AsSpan(0, count)))
        {
            status = RemoteChunkPublicationApplyStatus.Rejected;
        }
        else
        {
            for (int index = 0; index < count; index++)
                _semanticSnapshot[index] = ToSemantic(key, _wireSnapshot[index]);
            var cursor = new ChunkPublicationCursor(
                epoch, key, new ChunkPublicationRevision(begin.Revision));
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.Snapshot,
                key,
                cursor,
                new ChunkPublicationRevision(begin.HistoryFloor),
                count,
                count);
            bool applied = _cache.TryApplySnapshot(
                _snapshotHandle, batch, _semanticSnapshot.AsSpan(0, count),
                out ClientSemanticChunkError error);
            status = applied ? RemoteChunkPublicationApplyStatus.Applied : Map(error);
        }

        if (status == RemoteChunkPublicationApplyStatus.Applied &&
            lineage == DeliveryLineageDecision.New)
        {
            CommitLineage(_snapshotHandle, key, epoch, begin.DeliveryId,
                RemoteChunkPublicationKind.Snapshot, begin.Revision, begin.ContentHash);
            SaturatingIncrement(ref _appliedSnapshots);
            RecordSemanticApply();
        }
        else if (status != RemoteChunkPublicationApplyStatus.Applied)
        {
            SaturatingIncrement(ref _rejectedSnapshots);
            RecordFailure(status);
        }
        QueueSnapshotProof(in begin, status, _snapshotHandle);
        _wireSnapshot.AsSpan(0, count).Clear();
        _semanticSnapshot.AsSpan(0, count).Clear();
        ClearSnapshotCandidate();
    }

    private void RejectSnapshotCandidate(RemoteChunkPublicationApplyStatus status)
    {
        if (_snapshotBegin.DeliveryId != 0 && _proofCount < _proofs.Length)
            QueueSnapshotProof(in _snapshotBegin, status, _snapshotHandle);
        SaturatingIncrement(ref _rejectedSnapshots);
        RecordFailure(status);
        ClearSnapshotCandidate();
    }

    private void QueueSnapshotProof(
        in RemoteChunkSnapshotBeginMessage begin,
        RemoteChunkPublicationApplyStatus status,
        ClientSemanticChunkHandle handle = default)
    {
        if (_proofCount == _proofs.Length) return;
        QueueProof(begin.ApplicationSessionEpoch, begin.PublicationEpoch, begin.DeliveryId,
            begin.SpatialSpaceId, begin.ChunkX, begin.ChunkZ, begin.Revision,
            RemoteChunkPublicationKind.Snapshot, status, handle);
    }

    private void ClearSnapshotCandidate()
    {
        if (_snapshotBegin.EncodedBytes != 0)
            _snapshotPayload.AsSpan(0, checked((int)_snapshotBegin.EncodedBytes)).Clear();
        _snapshotSegments.AsSpan(0, _snapshotBegin.SegmentCount).Clear();
        _snapshotBegin = default;
        _snapshotHandle = default;
        _snapshotSegmentsReceived = 0;
        _snapshotCommitReceived = false;
    }

    private static bool SameBegin(
        in RemoteChunkSnapshotBeginMessage left,
        in RemoteChunkSnapshotBeginMessage right) =>
        left.ApplicationSessionEpoch == right.ApplicationSessionEpoch &&
        left.PublicationEpoch == right.PublicationEpoch && left.DeliveryId == right.DeliveryId &&
        left.SpatialSpaceId == right.SpatialSpaceId && left.ChunkX == right.ChunkX &&
        left.ChunkZ == right.ChunkZ && left.SchemaFingerprint == right.SchemaFingerprint &&
        left.Revision == right.Revision && left.HistoryFloor == right.HistoryFloor &&
        left.ContentHash == right.ContentHash && left.EncodedBytes == right.EncodedBytes &&
        left.RecordCount == right.RecordCount && left.SegmentCount == right.SegmentCount &&
        left.SegmentPayloadBytes == right.SegmentPayloadBytes;

    private void QueueProof(
        ulong applicationSessionEpoch,
        ulong publicationEpoch,
        ulong deliveryId,
        ulong spatialSpaceId,
        int chunkX,
        int chunkZ,
        ulong revision,
        RemoteChunkPublicationKind kind,
        RemoteChunkPublicationApplyStatus status,
        ClientSemanticChunkHandle handle)
    {
        int tail = (_proofHead + _proofCount) % _proofs.Length;
        var message = new RemoteChunkAppliedMessage
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            PublicationEpoch = publicationEpoch,
            DeliveryId = deliveryId,
            SpatialSpaceId = spatialSpaceId,
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Revision = revision,
            Kind = kind,
            Status = status,
        };
        if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
        {
            SaturatingIncrement(ref _timedProofsGenerated);
            switch (status)
            {
                case RemoteChunkPublicationApplyStatus.Applied:
                    SaturatingIncrement(ref _timedProofApplied);
                    CountPositiveSubsumptionOpportunities(in message, handle);
                    break;
                case RemoteChunkPublicationApplyStatus.Rejected:
                    SaturatingIncrement(ref _timedProofRejected);
                    break;
                case RemoteChunkPublicationApplyStatus.CapacityExceeded:
                    SaturatingIncrement(ref _timedProofCapacityExceeded);
                    break;
                case RemoteChunkPublicationApplyStatus.Contradictory:
                    SaturatingIncrement(ref _timedProofContradictory);
                    break;
            }
            switch (kind)
            {
                case RemoteChunkPublicationKind.Delta:
                    SaturatingIncrement(ref _timedProofDelta);
                    break;
                case RemoteChunkPublicationKind.Snapshot:
                    SaturatingIncrement(ref _timedProofSnapshot);
                    break;
                case RemoteChunkPublicationKind.Absence:
                    SaturatingIncrement(ref _timedProofAbsence);
                    break;
            }
        }
        _proofs[tail] = new PendingProof(
            message,
            handle,
            _telemetryLevel == NetworkTelemetryLevel.Detailed
                ? System.Diagnostics.Stopwatch.GetTimestamp()
                : 0);
        _proofCount++;
        if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
            _timedProofQueueHighWater = Math.Max(_timedProofQueueHighWater, _proofCount);
    }

    private void CountPositiveSubsumptionOpportunities(
        in RemoteChunkAppliedMessage newer,
        ClientSemanticChunkHandle handle)
    {
        if (handle.Generation == 0) return;
        for (int offset = 0; offset < _proofCount; offset++)
        {
            PendingProof pending = _proofs[(_proofHead + offset) % _proofs.Length];
            RemoteChunkAppliedMessage older = pending.Message;
            if (pending.Handle != handle ||
                older.Status != RemoteChunkPublicationApplyStatus.Applied ||
                older.ApplicationSessionEpoch != newer.ApplicationSessionEpoch ||
                older.PublicationEpoch != newer.PublicationEpoch ||
                older.SpatialSpaceId != newer.SpatialSpaceId ||
                older.ChunkX != newer.ChunkX || older.ChunkZ != newer.ChunkZ ||
                older.DeliveryId >= newer.DeliveryId)
                continue;
            // Same live semantic-cache handle is the client-side subscription-lifetime fence.
            // A later exact positive current-truth proof can establish at least the older truth;
            // negative outcomes intentionally never enter this census.
            SaturatingIncrement(ref _timedPositiveSubsumptionOpportunities);
        }
    }

    private static WorldPublicDeltaRecord ToSemantic(
        SpatialChunkKey key,
        in RemoteChunkDeltaRecord record)
    {
        var id = new WorldPublicEntityId(record.EntityIndex, record.EntityGeneration);
        WorldPublicOperation operation = (WorldPublicOperation)record.Operation;
        WorldPublicRecord current = operation == WorldPublicOperation.Present
            ? new WorldPublicRecord(
                id,
                record.EntityPublicVersion,
                record.PrefabId,
                new WorldPosition(
                    key.Coord,
                    new WorldLocalPosition(
                        record.LocalXMillimeters, record.LocalZMillimeters, record.YMillimeters)),
                new WorldYaw(record.OrientationUnits),
                (WorldPublicStateFlags)record.StateFlags,
                new ChunkPublicationRevision(record.LastChangedRevision))
            : default;
        return new WorldPublicDeltaRecord(
            operation,
            (WorldPublicFieldMask)record.Fields,
            id,
            record.EntityPublicVersion,
            current);
    }

    private void RecordSemanticApply()
    {
        if (_telemetryLevel != NetworkTelemetryLevel.Detailed) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastSemanticApplyTimestamp != 0)
            _semanticApplyIntervals.RecordElapsedAt(_lastSemanticApplyTimestamp, now);
        _lastSemanticApplyTimestamp = now;
    }

    private static WorldPublicRecord ToSemantic(
        SpatialChunkKey key,
        in RemoteChunkPublicRecord record) => new(
        new WorldPublicEntityId(record.EntityIndex, record.EntityGeneration),
        record.EntityPublicVersion,
        record.PrefabId,
        new WorldPosition(
            key.Coord,
            new WorldLocalPosition(
                record.LocalXMillimeters, record.LocalZMillimeters, record.YMillimeters)),
        new WorldYaw(record.OrientationUnits),
        (WorldPublicStateFlags)record.StateFlags,
        new ChunkPublicationRevision(record.LastChangedRevision));

    private static bool FitsWorldProfile(ReadOnlySpan<RemoteChunkDeltaRecord> records)
    {
        for (int index = 0; index < records.Length; index++)
        {
            ref readonly RemoteChunkDeltaRecord record = ref records[index];
            if (record.Operation == RemoteChunkPublicOperation.Present &&
                (record.LocalXMillimeters >= GameWorldSpatialProfile.WorldChunkEdgeUnits ||
                 record.LocalZMillimeters >= GameWorldSpatialProfile.WorldChunkEdgeUnits))
                return false;
        }
        return true;
    }

    private static bool FitsWorldProfile(ReadOnlySpan<RemoteChunkPublicRecord> records)
    {
        for (int index = 0; index < records.Length; index++)
            if (records[index].LocalXMillimeters >= GameWorldSpatialProfile.WorldChunkEdgeUnits ||
                records[index].LocalZMillimeters >= GameWorldSpatialProfile.WorldChunkEdgeUnits)
                return false;
        return true;
    }

    private DeliveryLineageDecision CompareLineage(
        ClientSemanticChunkHandle handle,
        SpatialChunkKey key,
        PublicationEpoch epoch,
        ulong deliveryId,
        RemoteChunkPublicationKind kind,
        ulong revision,
        ulong contentHash)
    {
        ref DeliveryLineage current = ref _deliveryLineage[checked((int)handle.Index)];
        if (!current.IsValid || current.Key != key || current.Epoch != epoch)
            return DeliveryLineageDecision.New;
        if (deliveryId < current.DeliveryId) return DeliveryLineageDecision.Superseded;
        if (deliveryId > current.DeliveryId) return DeliveryLineageDecision.New;
        return current.Kind == kind && current.Revision == revision && current.ContentHash == contentHash
            ? DeliveryLineageDecision.Duplicate
            : DeliveryLineageDecision.Contradictory;
    }

    private void CommitLineage(
        ClientSemanticChunkHandle handle,
        SpatialChunkKey key,
        PublicationEpoch epoch,
        ulong deliveryId,
        RemoteChunkPublicationKind kind,
        ulong revision,
        ulong contentHash) =>
        _deliveryLineage[checked((int)handle.Index)] = new DeliveryLineage(
            key, handle.Generation, epoch, deliveryId, kind, revision, contentHash);

    private static RemoteChunkPublicationApplyStatus Map(ClientSemanticChunkError error) => error switch
    {
        ClientSemanticChunkError.CapacityExceeded or
        ClientSemanticChunkError.PublicRecordCapacityExceeded or
        ClientSemanticChunkError.KnownIdentityCapacityExceeded =>
            RemoteChunkPublicationApplyStatus.CapacityExceeded,
        ClientSemanticChunkError.ContradictoryPublication =>
            RemoteChunkPublicationApplyStatus.Contradictory,
        _ => RemoteChunkPublicationApplyStatus.Rejected,
    };

    private bool TryGetPublicationHandle(SpatialChunkKey key,
        out ClientSemanticChunkHandle handle)
    {
        if (_cache.TryGetResidentHandle(key, out handle)) return true;
        SaturatingIncrement(ref _missingResidentDeliveryAttempts);
        if (_cache.TryProbeStructuralChunk(key, out WorldStructuralChunkProbe probe) &&
            probe.Status == WorldStructuralChunkStatus.PreparedChunk)
            SaturatingIncrement(ref _missingResidentPreparedChunkAttempts);
        return false;
    }

    private void RecordFailure(RemoteChunkPublicationApplyStatus status)
    {
        switch (status)
        {
            case RemoteChunkPublicationApplyStatus.CapacityExceeded:
                SaturatingIncrement(ref _capacityFailures);
                break;
            case RemoteChunkPublicationApplyStatus.Contradictory:
                SaturatingIncrement(ref _contradictoryDeliveries);
                break;
            default:
                SaturatingIncrement(ref _rejectedDeliveries);
                break;
        }
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private static void SaturatingAdd(ref ulong value, ulong amount)
    {
        value = ulong.MaxValue - value < amount ? ulong.MaxValue : value + amount;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The remote chunk WorldState client is owner-thread bound.");
    }

    private enum DeliveryLineageDecision : byte
    {
        New = 1,
        Duplicate = 2,
        Superseded = 3,
        Contradictory = 4,
    }

    private readonly record struct PendingProof(
        RemoteChunkAppliedMessage Message,
        ClientSemanticChunkHandle Handle,
        long GeneratedTimestamp);

    private readonly record struct DeliveryLineage(
        SpatialChunkKey Key,
        uint HandleGeneration,
        PublicationEpoch Epoch,
        ulong DeliveryId,
        RemoteChunkPublicationKind Kind,
        ulong Revision,
        ulong ContentHash)
    {
        internal bool IsValid => Key.Space.IsValid && HandleGeneration != 0 && Epoch.IsValid &&
            DeliveryId != 0;
    }

    private readonly record struct ResidentInterestFence(
        SpatialChunkKey Key, uint HandleGeneration, PublicationEpoch Epoch, ulong Cutoff);
}
