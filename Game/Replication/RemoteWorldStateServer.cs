using System.Runtime.CompilerServices;
using UltraSim.Game.Admission;
using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Game.Replication;

public readonly record struct RemoteWorldStateServerSnapshot(
    ulong BundlesQueued,
    ulong ApplicationAcknowledgements,
    ulong ApplicationRejections,
    ulong ApplicationCapacityFailures,
    ulong MalformedAcknowledgements,
    ulong BaselinesStarted,
    ulong BaselinesApplied,
    ulong BaselinesRejected,
    ulong BaselinesTimedOut,
    ulong BaselinesAbandoned,
    uint ConsecutiveBaselineTimeouts,
    bool BaselineInProgress,
    ulong ActiveBaselineId,
    GameWorldStateBuildStatus LastBuildStatus,
    long ReservedBytes);

public enum RemoteWorldBaselineProgressStatus : byte
{
    Progressed = 0,
    CompleteAwaitingApplication = 1,
    NoBaseline = 2,
    AlreadyActive = 3,
    CaptureFailed = 4,
    TransportBackpressure = 5,
    SequenceExhausted = 6,
    AwaitingClientPreparation = 7,
}

/// <summary>
/// Connection-relative adapter over one game-owned maintained-interest recipient.
/// </summary>
/// <remarks>
/// The interest graph owns relevance, pending newest versions, and application
/// knowledge. This adapter performs only final bounded wire assembly and maps
/// application ACKs back to the graph; transport ACKs never advance knowledge.
/// WorldState messages are independent unreliable containers, so disjoint
/// bundles may be queued without waiting for an application round trip.
/// </remarks>
public sealed class RemoteWorldStateServer : IRemoteGameProtocolHandler
{
    private readonly NetworkProtocolPeer _peer;
    private GameInterestService _interest;
    private readonly ParticipantId _participant;
    private readonly ulong _applicationSessionEpoch;
    private readonly GameWorldStateRecord[] _records;
    private readonly RemoteWorldStateRecord[] _wireRecords;
    private readonly GameWorldStateRecord[] _baselineRecords;
    private readonly RemoteWorldStateRecord[] _baselineWireRecords;
    private readonly byte[] _messageScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _queued;
    private ulong _acknowledged;
    private ulong _rejected;
    private ulong _capacityFailures;
    private ulong _malformed;
    private ulong _baselinesStarted;
    private ulong _baselinesApplied;
    private ulong _baselinesRejected;
    private ulong _baselinesTimedOut;
    private ulong _baselinesAbandoned;
    private uint _consecutiveBaselineTimeouts;
    private readonly Func<long> _monotonicMilliseconds;
    private readonly long _baselineTimeoutMilliseconds;
    private readonly uint _maximumConsecutiveBaselineTimeouts;
    private ulong _nextBaselineId = 1;
    private ulong _lastBaselineRevision;
    private ulong _lastBaselineEventFloor;
    private ulong _baselineId;
    private ulong _baselineRevision;
    private ulong _baselineEventFloor;
    private ulong _baselineRelationFloor;
    private ulong _baselineHash;
    private int _baselineRecordCount;
    private ushort _baselineSegmentCount;
    private ushort _baselineRecordsPerSegment;
    private ushort _nextBaselineSegment;
    private bool _baselineBeginQueued;
    private bool _baselinePrepared;
    private bool _baselineCommitQueued;
    private long _baselineLastProgressAtMilliseconds;
    private StateEpoch _cancelStateEpoch;
    private ulong _cancelBaselineId;
    private GameWorldStateBuildStatus _lastBuild;

    public RemoteWorldStateServer(
        NetworkProtocolPeer peer,
        GameInterestService interest,
        ParticipantId participant,
        ulong applicationSessionEpoch,
        int maximumRecordsPerBundle = 32,
        int maximumBaselineRecords = 4_096,
        long baselineTimeoutMilliseconds = 10_000,
        uint maximumConsecutiveBaselineTimeouts = 2,
        Func<long>? monotonicMilliseconds = null)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _interest = interest ?? throw new ArgumentNullException(nameof(interest));
        if (!participant.IsValid) throw new ArgumentException("A valid participant is required.", nameof(participant));
        if (applicationSessionEpoch == 0) throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (maximumRecordsPerBundle <= 0 || maximumRecordsPerBundle > UltraSimGameProtocol.MaximumWorldStateRecords)
            throw new ArgumentOutOfRangeException(nameof(maximumRecordsPerBundle));
        if (maximumBaselineRecords <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBaselineRecords));
        if (baselineTimeoutMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(baselineTimeoutMilliseconds));
        if (maximumConsecutiveBaselineTimeouts == 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConsecutiveBaselineTimeouts));
        _participant = participant;
        _applicationSessionEpoch = applicationSessionEpoch;
        _records = new GameWorldStateRecord[maximumRecordsPerBundle];
        _wireRecords = new RemoteWorldStateRecord[maximumRecordsPerBundle];
        _baselineRecords = new GameWorldStateRecord[maximumBaselineRecords];
        _baselineWireRecords = new RemoteWorldStateRecord[maximumBaselineRecords];
        _baselineTimeoutMilliseconds = baselineTimeoutMilliseconds;
        _maximumConsecutiveBaselineTimeouts = maximumConsecutiveBaselineTimeouts;
        _monotonicMilliseconds = monotonicMilliseconds ?? ReadEnvironmentTickCount64;
    }

    /// <summary>
    /// Rebinds the connection-owned adapter to a freshly rebuilt projection
    /// epoch. Old delayed traffic is fenced by StateEpoch; the connection and
    /// application session identities remain unchanged.
    /// </summary>
    public void ReplaceInterest(GameInterestService interest)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(interest);
        if (interest.StateEpoch.Value <= _interest.StateEpoch.Value)
            throw new ArgumentException(
                "Replacement interest must own a strictly newer StateEpoch.", nameof(interest));
        RequestBaselineCancellation();
        _consecutiveBaselineTimeouts = 0;
        _lastBaselineRevision = 0;
        _lastBaselineEventFloor = 0;
        _interest = interest;
        _lastBuild = GameWorldStateBuildStatus.NoData;
    }

    /// <summary>Schedules and queues one current recipient bundle if eligible debt exists.</summary>
    public GameWorldStateBuildStatus TrySchedule(long nowMilliseconds, int byteBudget)
    {
        EnsureOwnerThread();
        _ = TryExpireBaselineCore();
        if (!TryFlushBaselineCancellation() || _baselineId != 0)
            return _lastBuild = GameWorldStateBuildStatus.BaselineInProgress;
        if (nowMilliseconds < 0) return _lastBuild = GameWorldStateBuildStatus.NoData;
        _lastBuild = _interest.TryBuildBundle(
            _participant, nowMilliseconds, byteBudget, _records, out GameWorldStateBundle bundle);
        if (_lastBuild != GameWorldStateBuildStatus.Success) return _lastBuild;
        for (int index = 0; index < bundle.RecordCount; index++)
        {
            ref readonly GameWorldStateRecord record = ref _records[index];
            _wireRecords[index] = new(
                record.RelationSequence,
                record.Entity.Index,
                record.Entity.Generation,
                record.CanonicalVersion,
                record.IsPresent,
                (byte)record.Horizon,
                record.Position.XMillimeters,
                record.Position.ZMillimeters,
                record.SimulationState.TickMarker,
                record.SimulationState.InputVelocity);
        }
        if (!UltraSimGameProtocol.TryWriteWorldStateBundle(
                _messageScratch,
                _applicationSessionEpoch,
                bundle.StateEpoch.Value,
                bundle.BundleId,
                _wireRecords.AsSpan(0, bundle.RecordCount),
                out int written) ||
            _peer.TryQueue(RemoteWorldStateBundleHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
        {
            _interest.AbandonBundle(bundle.BundleId);
            _records.AsSpan(0, bundle.RecordCount).Clear();
            _wireRecords.AsSpan(0, bundle.RecordCount).Clear();
            return _lastBuild = GameWorldStateBuildStatus.TransportNotAccepting;
        }
        _records.AsSpan(0, bundle.RecordCount).Clear();
        _wireRecords.AsSpan(0, bundle.RecordCount).Clear();
        SaturatingIncrement(ref _queued);
        return _lastBuild;
    }

    /// <summary>Captures one complete recipient cutoff into the single bounded baseline slot.</summary>
    public RemoteWorldBaselineProgressStatus TryBeginBaseline(
        ulong baselineRevision,
        ulong eventFloor = 0,
        ushort recordsPerSegment = 32)
    {
        EnsureOwnerThread();
        if (!TryFlushBaselineCancellation() || _baselineId != 0)
            return RemoteWorldBaselineProgressStatus.AlreadyActive;
        if (baselineRevision == 0 || baselineRevision <= _lastBaselineRevision ||
            eventFloor < _lastBaselineEventFloor || recordsPerSegment == 0 ||
            recordsPerSegment > UltraSimGameProtocol.MaximumWorldStateRecords)
            return RemoteWorldBaselineProgressStatus.CaptureFailed;
        if (_nextBaselineId == ulong.MaxValue)
            return RemoteWorldBaselineProgressStatus.SequenceExhausted;
        GameWorldBaselineCaptureStatus captured = _interest.CopyRecipientBaseline(
            _participant, _baselineRecords, out _baselineRecordCount);
        if (captured != GameWorldBaselineCaptureStatus.Success)
            return RemoteWorldBaselineProgressStatus.CaptureFailed;
        for (int index = 0; index < _baselineRecordCount; index++)
            _baselineWireRecords[index] = ToWire(in _baselineRecords[index]);
        _baselineRecordsPerSegment = recordsPerSegment;
        _baselineSegmentCount = checked((ushort)((_baselineRecordCount + recordsPerSegment - 1) /
            recordsPerSegment));
        _baselineId = _nextBaselineId++;
        _baselineRevision = baselineRevision;
        _baselineEventFloor = eventFloor;
        _lastBaselineRevision = baselineRevision;
        _lastBaselineEventFloor = eventFloor;
        _baselineRelationFloor = _interest.LatestRelationSequence;
        _baselineHash = UltraSimGameProtocol.ComputeWorldBaselineHash(
            _baselineWireRecords.AsSpan(0, _baselineRecordCount));
        _nextBaselineSegment = 0;
        _baselineBeginQueued = false;
        _baselinePrepared = false;
        _baselineCommitQueued = false;
        _baselineLastProgressAtMilliseconds = ReadMonotonicMilliseconds();
        SaturatingIncrement(ref _baselinesStarted);
        return TryProgressBaseline();
    }

    /// <summary>Queues as much of the active reliable baseline transaction as current bounds admit.</summary>
    public RemoteWorldBaselineProgressStatus TryProgressBaseline()
    {
        EnsureOwnerThread();
        if (TryExpireBaselineCore()) return RemoteWorldBaselineProgressStatus.NoBaseline;
        if (!TryFlushBaselineCancellation())
            return RemoteWorldBaselineProgressStatus.TransportBackpressure;
        if (_baselineId == 0) return RemoteWorldBaselineProgressStatus.NoBaseline;
        if (!_baselineBeginQueued)
        {
            var begin = new RemoteWorldBaselineBeginMessage
            {
                ApplicationSessionEpoch = _applicationSessionEpoch,
                StateEpoch = _interest.StateEpoch.Value,
                BaselineId = _baselineId,
                BaselineRevision = _baselineRevision,
                EventFloor = _baselineEventFloor,
                RelationSequenceFloor = _baselineRelationFloor,
                SchemaFingerprint = UltraSimGameProtocol.WorldStateBaselineSchemaFingerprint,
                ContentHash = _baselineHash,
                RecordCount = checked((uint)_baselineRecordCount),
                SegmentCount = _baselineSegmentCount,
                RecordsPerSegment = _baselineRecordsPerSegment,
            };
            int bytes = RemoteWorldBaselineBeginMessageCodec.EncodedSize;
            if (!RemoteWorldBaselineBeginMessageCodec.TryWriteExact(
                    _messageScratch.AsSpan(0, bytes), in begin) ||
                _peer.TryQueue(RemoteWorldBaselineBeginMessageCodec.Descriptor.Id,
                    _messageScratch.AsSpan(0, bytes)) != NetworkQueueStatus.Accepted)
                return RemoteWorldBaselineProgressStatus.TransportBackpressure;
            _baselineBeginQueued = true;
            MarkBaselineProgress();
        }

        if (!_baselinePrepared)
            return RemoteWorldBaselineProgressStatus.AwaitingClientPreparation;

        while (_nextBaselineSegment < _baselineSegmentCount)
        {
            int offset = _nextBaselineSegment * _baselineRecordsPerSegment;
            int count = Math.Min(_baselineRecordsPerSegment, _baselineRecordCount - offset);
            if (!UltraSimGameProtocol.TryWriteWorldBaselineSegment(
                    _messageScratch,
                    _applicationSessionEpoch,
                    _interest.StateEpoch.Value,
                    _baselineId,
                    _nextBaselineSegment,
                    _baselineSegmentCount,
                    _baselineWireRecords.AsSpan(offset, count),
                    out int written))
                throw new InvalidOperationException("A validated baseline segment did not encode.");
            if (_peer.TryQueue(RemoteWorldBaselineSegmentHeaderCodec.Descriptor.Id,
                    _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
                return RemoteWorldBaselineProgressStatus.TransportBackpressure;
            _nextBaselineSegment++;
            MarkBaselineProgress();
        }

        if (!_baselineCommitQueued)
        {
            var commit = new RemoteWorldBaselineCommitMessage
            {
                ApplicationSessionEpoch = _applicationSessionEpoch,
                StateEpoch = _interest.StateEpoch.Value,
                BaselineId = _baselineId,
                BaselineRevision = _baselineRevision,
                ContentHash = _baselineHash,
            };
            int bytes = RemoteWorldBaselineCommitMessageCodec.EncodedSize;
            if (!RemoteWorldBaselineCommitMessageCodec.TryWriteExact(
                    _messageScratch.AsSpan(0, bytes), in commit) ||
                _peer.TryQueue(RemoteWorldBaselineCommitMessageCodec.Descriptor.Id,
                    _messageScratch.AsSpan(0, bytes)) != NetworkQueueStatus.Accepted)
                return RemoteWorldBaselineProgressStatus.TransportBackpressure;
            _baselineCommitQueued = true;
            MarkBaselineProgress();
        }
        return RemoteWorldBaselineProgressStatus.CompleteAwaitingApplication;
    }

    /// <summary>
    /// Expires an application-stalled baseline at its bounded monotonic deadline.
    /// No recipient knowledge is advanced. Repeated timeouts eventually mark the
    /// recipient nonviable rather than allowing infinite restart churn.
    /// </summary>
    public bool TryExpireBaseline()
    {
        EnsureOwnerThread();
        return TryExpireBaselineCore();
    }

    /// <summary>
    /// Explicitly abandons the active baseline without advancing recipient
    /// knowledge. The owner may subsequently start a newer recovery attempt.
    /// </summary>
    public bool AbandonBaseline()
    {
        EnsureOwnerThread();
        if (_baselineId == 0) return false;
        SaturatingIncrement(ref _baselinesAbandoned);
        RequestBaselineCancellation();
        return true;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
        if (message.MessageId == RemoteWorldBaselineAppliedMessageCodec.Descriptor.Id)
        {
            ProcessBaselineAcknowledgement(payload);
            return true;
        }
        if (message.MessageId != RemoteWorldStateAppliedMessageCodec.Descriptor.Id) return false;
        if (!RemoteWorldStateAppliedMessageCodec.TryReadExact(
                payload, out RemoteWorldStateAppliedMessage acknowledgement) ||
            acknowledgement.ApplicationSessionEpoch != _applicationSessionEpoch ||
            acknowledgement.StateEpoch != _interest.StateEpoch.Value ||
            (uint)acknowledgement.Status > (uint)RemoteWorldStateApplyStatus.CapacityExceeded ||
            !_interest.TryAcknowledgeBundle(
                _participant,
                acknowledgement.BundleId,
                acknowledgement.Status == RemoteWorldStateApplyStatus.Applied))
        {
            SaturatingIncrement(ref _malformed);
            return true;
        }
        if (acknowledgement.Status == RemoteWorldStateApplyStatus.Applied)
            SaturatingIncrement(ref _acknowledged);
        else if (acknowledgement.Status == RemoteWorldStateApplyStatus.CapacityExceeded)
        {
            SaturatingIncrement(ref _capacityFailures);
            _interest.MarkRecipientProjectionNonviable(_participant);
        }
        else
            SaturatingIncrement(ref _rejected);
        return true;
    }

    public RemoteWorldStateServerSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(_queued, _acknowledged, _rejected, _capacityFailures,
            _malformed, _baselinesStarted, _baselinesApplied, _baselinesRejected,
            _baselinesTimedOut, _baselinesAbandoned, _consecutiveBaselineTimeouts,
            _baselineId != 0 || _cancelBaselineId != 0,
            _baselineId != 0 ? _baselineId : _cancelBaselineId, _lastBuild,
            checked(
                (long)_records.Length * Unsafe.SizeOf<GameWorldStateRecord>() +
                (long)_wireRecords.Length * Unsafe.SizeOf<RemoteWorldStateRecord>() +
                (long)_baselineRecords.Length * Unsafe.SizeOf<GameWorldStateRecord>() +
                (long)_baselineWireRecords.Length * Unsafe.SizeOf<RemoteWorldStateRecord>() +
                _messageScratch.Length));
    }

    private void ProcessBaselineAcknowledgement(ReadOnlySpan<byte> payload)
    {
        if (!RemoteWorldBaselineAppliedMessageCodec.TryReadExact(
                payload, out RemoteWorldBaselineAppliedMessage acknowledgement) ||
            _baselineId == 0 || acknowledgement.ApplicationSessionEpoch != _applicationSessionEpoch ||
            acknowledgement.StateEpoch != _interest.StateEpoch.Value ||
            acknowledgement.BaselineId != _baselineId ||
            acknowledgement.BaselineRevision != _baselineRevision ||
            (uint)acknowledgement.Status > (uint)RemoteWorldBaselineApplyStatus.Prepared)
        {
            SaturatingIncrement(ref _malformed);
            return;
        }

        if (acknowledgement.Status == RemoteWorldBaselineApplyStatus.Prepared)
        {
            if (!_baselineBeginQueued || _baselinePrepared || _nextBaselineSegment != 0)
            {
                SaturatingIncrement(ref _malformed);
                return;
            }
            _baselinePrepared = true;
            MarkBaselineProgress();
            _ = TryProgressBaseline();
            return;
        }
        if (acknowledgement.Status == RemoteWorldBaselineApplyStatus.Applied)
        {
            if (!_interest.TryAcknowledgeBaseline(
                    _participant, _baselineRecords.AsSpan(0, _baselineRecordCount),
                    _baselineRelationFloor))
            {
                SaturatingIncrement(ref _malformed);
                return;
            }
            SaturatingIncrement(ref _baselinesApplied);
            _consecutiveBaselineTimeouts = 0;
        }
        else
        {
            SaturatingIncrement(ref _baselinesRejected);
            if (acknowledgement.Status == RemoteWorldBaselineApplyStatus.CapacityExceeded)
                _interest.MarkRecipientProjectionNonviable(_participant);
        }
        ClearBaseline();
    }

    private void ClearBaseline()
    {
        _baselineRecords.AsSpan(0, _baselineRecordCount).Clear();
        _baselineWireRecords.AsSpan(0, _baselineRecordCount).Clear();
        _baselineId = 0;
        _baselineRevision = 0;
        _baselineEventFloor = 0;
        _baselineRelationFloor = 0;
        _baselineHash = 0;
        _baselineRecordCount = 0;
        _baselineSegmentCount = 0;
        _baselineRecordsPerSegment = 0;
        _nextBaselineSegment = 0;
        _baselineBeginQueued = false;
        _baselinePrepared = false;
        _baselineCommitQueued = false;
        _baselineLastProgressAtMilliseconds = 0;
    }

    private bool TryExpireBaselineCore()
    {
        if (_baselineId == 0) return false;
        long now = ReadMonotonicMilliseconds();
        if (now - _baselineLastProgressAtMilliseconds < _baselineTimeoutMilliseconds)
            return false;
        SaturatingIncrement(ref _baselinesTimedOut);
        if (_consecutiveBaselineTimeouts != uint.MaxValue)
            _consecutiveBaselineTimeouts++;
        RequestBaselineCancellation();
        if (_consecutiveBaselineTimeouts >= _maximumConsecutiveBaselineTimeouts)
            _interest.MarkRecipientProjectionNonviable(_participant);
        return true;
    }

    private void RequestBaselineCancellation()
    {
        if (_baselineId == 0) return;
        if (_cancelBaselineId != 0)
            throw new InvalidOperationException("Only one bounded baseline cancellation may be pending.");
        _cancelStateEpoch = _interest.StateEpoch;
        _cancelBaselineId = _baselineId;
        ClearBaseline();
        _ = TryFlushBaselineCancellation();
    }

    private bool TryFlushBaselineCancellation()
    {
        if (_cancelBaselineId == 0) return true;
        var cancel = new RemoteWorldBaselineCancelMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StateEpoch = _cancelStateEpoch.Value,
            BaselineId = _cancelBaselineId,
        };
        int bytes = RemoteWorldBaselineCancelMessageCodec.EncodedSize;
        if (!RemoteWorldBaselineCancelMessageCodec.TryWriteExact(
                _messageScratch.AsSpan(0, bytes), in cancel) ||
            _peer.TryQueue(RemoteWorldBaselineCancelMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, bytes)) != NetworkQueueStatus.Accepted)
            return false;
        _cancelStateEpoch = default;
        _cancelBaselineId = 0;
        return true;
    }

    private void MarkBaselineProgress() =>
        _baselineLastProgressAtMilliseconds = ReadMonotonicMilliseconds();

    private long ReadMonotonicMilliseconds()
    {
        long now = _monotonicMilliseconds();
        if (now < 0 || (_baselineLastProgressAtMilliseconds != 0 &&
            now < _baselineLastProgressAtMilliseconds))
            throw new InvalidOperationException("The baseline monotonic clock moved backwards.");
        return now;
    }

    private static long ReadEnvironmentTickCount64() => Environment.TickCount64;

    private static RemoteWorldStateRecord ToWire(in GameWorldStateRecord record) => new(
        record.RelationSequence,
        record.Entity.Index,
        record.Entity.Generation,
        record.CanonicalVersion,
        record.IsPresent,
        (byte)record.Horizon,
        record.Position.XMillimeters,
        record.Position.ZMillimeters,
        record.SimulationState.TickMarker,
        record.SimulationState.InputVelocity);

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The remote WorldState server is owner-thread-bound.");
    }
}
