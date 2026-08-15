using UltraSim.Game.Admission;
using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Game.Replication;

public enum RemoteApplicationEventScheduleStatus : byte
{
    Queued = 0,
    NoData = 1,
    AwaitingApplication = 2,
    BaselineRequired = 3,
    TransportBackpressure = 4,
    RecipientNonviable = 5,
}

public readonly record struct RemoteApplicationEventServerSnapshot(
    StateEpoch StateEpoch,
    ulong EventFloor,
    ulong NextQueuedSequence,
    bool AwaitingApplication,
    bool RecipientNonviable,
    ulong EventsQueued,
    ulong EventsApplied,
    ulong EventsRejected,
    ulong MalformedAcknowledgements);

/// <summary>
/// Connection-owned adapter from retained game application facts to reliable
/// Event messages and explicit application acknowledgement.
/// </summary>
public sealed class RemoteApplicationEventServer : IRemoteGameProtocolHandler
{
    private readonly NetworkProtocolPeer _peer;
    private GameProjectionService _projection;
    private readonly ulong _applicationSessionEpoch;
    private readonly GameApplicationEvent[] _scratch = new GameApplicationEvent[1];
    private readonly byte[] _messageScratch = new byte[RemoteApplicationEventMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private StateEpoch _stateEpoch;
    private ulong _nextSequence;
    private GameApplicationEvent _outstanding;
    private bool _awaitingApplication;
    private bool _nonviable;
    private ulong _queued;
    private ulong _applied;
    private ulong _rejected;
    private ulong _malformed;

    public RemoteApplicationEventServer(
        NetworkProtocolPeer peer,
        GameProjectionService projection,
        ulong applicationSessionEpoch,
        ulong eventFloor)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        if (applicationSessionEpoch == 0)
            throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        _stateEpoch = projection.StateEpoch;
        if (!_stateEpoch.IsValid) throw new ArgumentException("Projection has no active epoch.", nameof(projection));
        ValidateFloor(projection, eventFloor);
        _applicationSessionEpoch = applicationSessionEpoch;
        _nextSequence = eventFloor;
    }

    /// <summary>First event sequence still required by this recipient.</summary>
    public ulong EventFloor
    {
        get { EnsureOwnerThread(); return _nextSequence; }
    }

    public RemoteApplicationEventScheduleStatus TrySchedule()
    {
        EnsureOwnerThread();
        if (_nonviable) return RemoteApplicationEventScheduleStatus.RecipientNonviable;
        if (_awaitingApplication) return RemoteApplicationEventScheduleStatus.AwaitingApplication;
        GameProjectionHistoryReadResult read = _projection.ReadApplicationEvents(_nextSequence, _scratch);
        if (read.Status == GameProjectionHistoryReadStatus.NoData)
            return RemoteApplicationEventScheduleStatus.NoData;
        if (read.Status != GameProjectionHistoryReadStatus.Success || read.Count != 1)
            return RemoteApplicationEventScheduleStatus.BaselineRequired;
        GameApplicationEvent value = _scratch[0];
        var message = new RemoteApplicationEventMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StateEpoch = value.StateEpoch.Value,
            Sequence = value.Sequence,
            ParticipantId = value.Participant.Value,
            Kind = value.Kind,
            Subject = value.Subject,
            PayloadHash = value.PayloadHash,
        };
        if (!RemoteApplicationEventMessageCodec.TryWriteExact(_messageScratch, in message) ||
            _peer.TryQueue(RemoteApplicationEventMessageCodec.Descriptor.Id, _messageScratch) !=
                NetworkQueueStatus.Accepted)
            return RemoteApplicationEventScheduleStatus.TransportBackpressure;
        _outstanding = value;
        _awaitingApplication = true;
        SaturatingIncrement(ref _queued);
        return RemoteApplicationEventScheduleStatus.Queued;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
        if (message.MessageId != RemoteApplicationEventAppliedMessageCodec.Descriptor.Id) return false;
        if (!RemoteApplicationEventAppliedMessageCodec.TryReadExact(
                payload, out RemoteApplicationEventAppliedMessage acknowledgement) ||
            !_awaitingApplication || acknowledgement.ApplicationSessionEpoch != _applicationSessionEpoch ||
            acknowledgement.StateEpoch != _stateEpoch.Value ||
            (uint)acknowledgement.Status > (uint)RemoteApplicationEventApplyStatus.CapacityExceeded ||
            (acknowledgement.Status == RemoteApplicationEventApplyStatus.Applied
                ? acknowledgement.NextSequence != unchecked(_outstanding.Sequence + 1)
                : acknowledgement.NextSequence != _outstanding.Sequence))
        {
            SaturatingIncrement(ref _malformed);
            return true;
        }
        if (acknowledgement.Status == RemoteApplicationEventApplyStatus.Applied)
        {
            _nextSequence = acknowledgement.NextSequence;
            _projection.DiscardApplicationEventsThrough(_nextSequence);
            _outstanding = default;
            _awaitingApplication = false;
            SaturatingIncrement(ref _applied);
        }
        else if (acknowledgement.Status == RemoteApplicationEventApplyStatus.CapacityExceeded)
        {
            _nonviable = true;
            SaturatingIncrement(ref _rejected);
        }
        else
        {
            _outstanding = default;
            _awaitingApplication = false;
            SaturatingIncrement(ref _rejected);
        }
        return true;
    }

    /// <summary>Rebinds after a strictly newer authority epoch and declared fresh-client floor.</summary>
    public void ReplaceProjection(GameProjectionService projection, ulong eventFloor)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.StateEpoch.Value <= _stateEpoch.Value)
            throw new ArgumentException("Replacement projection must own a newer StateEpoch.", nameof(projection));
        ValidateFloor(projection, eventFloor);
        _projection = projection;
        _stateEpoch = projection.StateEpoch;
        _nextSequence = eventFloor;
        _outstanding = default;
        _awaitingApplication = false;
        _nonviable = false;
    }

    public RemoteApplicationEventServerSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(_stateEpoch, _nextSequence, _awaitingApplication
                ? _outstanding.Sequence : _nextSequence,
            _awaitingApplication, _nonviable, _queued, _applied, _rejected, _malformed);
    }

    private static void ValidateFloor(GameProjectionService projection, ulong floor)
    {
        Span<GameApplicationEvent> empty = [];
        GameProjectionHistoryReadResult result = projection.ReadApplicationEvents(floor, empty);
        if (result.Status is GameProjectionHistoryReadStatus.HistoryLost or
            GameProjectionHistoryReadStatus.FutureSequence)
            throw new ArgumentOutOfRangeException(nameof(floor));
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The application Event server is owner-thread-bound.");
    }
}
