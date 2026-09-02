using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Network;

namespace UltraSim.Client.Network;

public readonly record struct RemoteApplicationEventClientSnapshot(
    StateEpoch StateEpoch,
    ulong NextSequence,
    int RetainedEvents,
    bool IsNonviable,
    ulong EventsApplied,
    ulong Duplicates,
    ulong Rejected);

public readonly record struct RemoteApplicationEventSnapshot(
    ulong Sequence,
    StateEpoch StateEpoch,
    ulong ParticipantId,
    ushort Kind,
    ulong Subject,
    ulong PayloadHash);

/// <summary>Bounded idempotent client consumer for retained application facts.</summary>
public sealed class RemoteApplicationEventClient : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private readonly ulong _participantId;
    private readonly RemoteApplicationEventSnapshot[] _events;
    private readonly byte[] _ackScratch = new byte[RemoteApplicationEventAppliedMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private StateEpoch _stateEpoch;
    private ulong _nextSequence;
    private int _count;
    private int _head;
    private RemoteApplicationEventAppliedMessage _pendingAcknowledgement;
    private bool _hasPendingAcknowledgement;
    private bool _nonviable;
    private ulong _applied;
    private ulong _duplicates;
    private ulong _rejected;

    public RemoteApplicationEventClient(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        ulong participantId,
        StateEpoch stateEpoch,
        ulong eventFloor,
        int maximumRetainedEvents = 64)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        if (applicationSessionEpoch == 0)
            throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (participantId == 0) throw new ArgumentOutOfRangeException(nameof(participantId));
        if (!stateEpoch.IsValid) throw new ArgumentException("A valid StateEpoch is required.", nameof(stateEpoch));
        if (maximumRetainedEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedEvents));
        _applicationSessionEpoch = applicationSessionEpoch;
        _participantId = participantId;
        _stateEpoch = stateEpoch;
        _nextSequence = eventFloor;
        _events = new RemoteApplicationEventSnapshot[maximumRetainedEvents];
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
        if (message.MessageId != RemoteApplicationEventMessageCodec.Descriptor.Id) return false;
        if (!RemoteApplicationEventMessageCodec.TryReadExact(payload, out RemoteApplicationEventMessage value) ||
            value.ApplicationSessionEpoch != _applicationSessionEpoch ||
            value.StateEpoch != _stateEpoch.Value || value.ParticipantId != _participantId || value.Kind == 0)
        {
            SaturatingIncrement(ref _rejected);
            return true;
        }
        if (value.Sequence < _nextSequence)
        {
            SaturatingIncrement(ref _duplicates);
            QueueAcknowledgement(RemoteApplicationEventApplyStatus.Applied, _nextSequence);
            return true;
        }
        if (_count == _events.Length)
        {
            _nonviable = true;
            QueueAcknowledgement(RemoteApplicationEventApplyStatus.CapacityExceeded, value.Sequence);
            return true;
        }
        int slot = (_head + _count) % _events.Length;
        _events[slot] = new RemoteApplicationEventSnapshot(
            value.Sequence, _stateEpoch, value.ParticipantId,
            value.Kind, value.Subject, value.PayloadHash);
        _count++;
        // The server is the trusted participant filter over one global Event
        // sequence. A gap therefore represents facts for other participants,
        // not missing facts for this consumer.
        _nextSequence = checked(value.Sequence + 1);
        SaturatingIncrement(ref _applied);
        QueueAcknowledgement(RemoteApplicationEventApplyStatus.Applied, _nextSequence);
        return true;
    }

    public void ApplyBaselineFloor(StateEpoch stateEpoch, ulong eventFloor)
    {
        EnsureOwnerThread();
        if (!stateEpoch.IsValid) throw new ArgumentException("A valid StateEpoch is required.", nameof(stateEpoch));
        if (stateEpoch.Value < _stateEpoch.Value ||
            (stateEpoch == _stateEpoch && eventFloor < _nextSequence))
            throw new ArgumentException("Application Event epochs and floors are monotonic.");
        if (stateEpoch != _stateEpoch)
        {
            Array.Clear(_events);
            _head = 0;
            _count = 0;
            _pendingAcknowledgement = default;
            _hasPendingAcknowledgement = false;
            _nonviable = false;
        }
        _stateEpoch = stateEpoch;
        _nextSequence = eventFloor;
    }

    public int CopyEvents(Span<RemoteApplicationEventSnapshot> destination)
    {
        EnsureOwnerThread();
        int count = Math.Min(destination.Length, _count);
        int first = Math.Min(count, _events.Length - _head);
        _events.AsSpan(_head, first).CopyTo(destination);
        if (first != count) _events.AsSpan(0, count - first).CopyTo(destination[first..]);
        return count;
    }

    /// <summary>Consumes the oldest already-applied fact and releases one bounded working slot.</summary>
    public bool TryConsumeNext(out RemoteApplicationEventSnapshot applicationEvent)
    {
        EnsureOwnerThread();
        if (_count == 0)
        {
            applicationEvent = default;
            return false;
        }
        applicationEvent = _events[_head];
        _events[_head] = default;
        _head = (_head + 1) % _events.Length;
        _count--;
        return true;
    }

    /// <summary>Retries the one bounded application acknowledgement after Control backpressure.</summary>
    public bool TryFlushAcknowledgement()
    {
        EnsureOwnerThread();
        if (!_hasPendingAcknowledgement) return true;
        if (!TryQueueAcknowledgement(in _pendingAcknowledgement)) return false;
        _pendingAcknowledgement = default;
        _hasPendingAcknowledgement = false;
        return true;
    }

    bool IRemoteGameProtocolProgressor.TryProgress() => TryFlushAcknowledgement();

    public RemoteApplicationEventClientSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(_stateEpoch, _nextSequence, _count, _nonviable,
            _applied, _duplicates, _rejected);
    }

    private void QueueAcknowledgement(RemoteApplicationEventApplyStatus status, ulong nextSequence)
    {
        var acknowledgement = new RemoteApplicationEventAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StateEpoch = _stateEpoch.Value,
            NextSequence = nextSequence,
            Status = status,
        };
        if (_hasPendingAcknowledgement)
        {
            if (!AcknowledgementsEqual(in _pendingAcknowledgement, in acknowledgement))
            {
                _nonviable = true;
                return;
            }
            if (TryQueueAcknowledgement(in _pendingAcknowledgement))
            {
                _pendingAcknowledgement = default;
                _hasPendingAcknowledgement = false;
            }
            return;
        }
        if (TryQueueAcknowledgement(in acknowledgement)) return;
        _pendingAcknowledgement = acknowledgement;
        _hasPendingAcknowledgement = true;
    }

    private bool TryQueueAcknowledgement(in RemoteApplicationEventAppliedMessage acknowledgement)
    {
        if (!RemoteApplicationEventAppliedMessageCodec.TryWriteExact(_ackScratch, in acknowledgement))
            throw new InvalidOperationException("A validated Event acknowledgement did not encode.");
        return _peer.TryQueue(RemoteApplicationEventAppliedMessageCodec.Descriptor.Id, _ackScratch) ==
            NetworkQueueStatus.Accepted;
    }

    private static bool AcknowledgementsEqual(
        in RemoteApplicationEventAppliedMessage left,
        in RemoteApplicationEventAppliedMessage right) =>
        left.ApplicationSessionEpoch == right.ApplicationSessionEpoch &&
        left.StateEpoch == right.StateEpoch &&
        left.NextSequence == right.NextSequence &&
        left.Status == right.Status;

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The application Event client is owner-thread-bound.");
    }
}
