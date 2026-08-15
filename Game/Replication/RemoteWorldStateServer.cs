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
    GameWorldStateBuildStatus LastBuildStatus);

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
    private readonly GameInterestService _interest;
    private readonly ParticipantId _participant;
    private readonly ulong _applicationSessionEpoch;
    private readonly GameWorldStateRecord[] _records;
    private readonly RemoteWorldStateRecord[] _wireRecords;
    private readonly byte[] _messageScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _queued;
    private ulong _acknowledged;
    private ulong _rejected;
    private ulong _capacityFailures;
    private ulong _malformed;
    private GameWorldStateBuildStatus _lastBuild;

    public RemoteWorldStateServer(
        NetworkProtocolPeer peer,
        GameInterestService interest,
        ParticipantId participant,
        ulong applicationSessionEpoch,
        int maximumRecordsPerBundle = 32)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _interest = interest ?? throw new ArgumentNullException(nameof(interest));
        if (!participant.IsValid) throw new ArgumentException("A valid participant is required.", nameof(participant));
        if (applicationSessionEpoch == 0) throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (maximumRecordsPerBundle <= 0 || maximumRecordsPerBundle > UltraSimGameProtocol.MaximumWorldStateRecords)
            throw new ArgumentOutOfRangeException(nameof(maximumRecordsPerBundle));
        _participant = participant;
        _applicationSessionEpoch = applicationSessionEpoch;
        _records = new GameWorldStateRecord[maximumRecordsPerBundle];
        _wireRecords = new RemoteWorldStateRecord[maximumRecordsPerBundle];
    }

    /// <summary>Schedules and queues one current recipient bundle if eligible debt exists.</summary>
    public GameWorldStateBuildStatus TrySchedule(long nowMilliseconds, int byteBudget)
    {
        EnsureOwnerThread();
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

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
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
            _malformed, _lastBuild);
    }

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
