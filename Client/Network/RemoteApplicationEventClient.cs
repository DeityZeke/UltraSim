using UltraSim.Game.Protocol;
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
public sealed class RemoteApplicationEventClient : IRemoteGameProtocolHandler
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private readonly RemoteApplicationEventSnapshot[] _events;
    private readonly byte[] _ackScratch = new byte[RemoteApplicationEventAppliedMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private StateEpoch _stateEpoch;
    private ulong _nextSequence;
    private int _count;
    private bool _nonviable;
    private ulong _applied;
    private ulong _duplicates;
    private ulong _rejected;

    public RemoteApplicationEventClient(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        StateEpoch stateEpoch,
        ulong eventFloor,
        int maximumRetainedEvents = 64)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        if (applicationSessionEpoch == 0)
            throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (!stateEpoch.IsValid) throw new ArgumentException("A valid StateEpoch is required.", nameof(stateEpoch));
        if (maximumRetainedEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedEvents));
        _applicationSessionEpoch = applicationSessionEpoch;
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
            value.StateEpoch != _stateEpoch.Value || value.ParticipantId == 0 || value.Kind == 0)
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
        if (value.Sequence != _nextSequence)
        {
            SaturatingIncrement(ref _rejected);
            return true;
        }
        if (_count == _events.Length)
        {
            _nonviable = true;
            QueueAcknowledgement(RemoteApplicationEventApplyStatus.CapacityExceeded, _nextSequence);
            return true;
        }
        _events[_count++] = new RemoteApplicationEventSnapshot(
            value.Sequence, _stateEpoch, value.ParticipantId,
            value.Kind, value.Subject, value.PayloadHash);
        _nextSequence++;
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
            _count = 0;
            _nonviable = false;
        }
        _stateEpoch = stateEpoch;
        _nextSequence = eventFloor;
    }

    public int CopyEvents(Span<RemoteApplicationEventSnapshot> destination)
    {
        EnsureOwnerThread();
        int count = Math.Min(destination.Length, _count);
        _events.AsSpan(0, count).CopyTo(destination);
        return count;
    }

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
        if (!RemoteApplicationEventAppliedMessageCodec.TryWriteExact(_ackScratch, in acknowledgement) ||
            _peer.TryQueue(RemoteApplicationEventAppliedMessageCodec.Descriptor.Id, _ackScratch) !=
                NetworkQueueStatus.Accepted)
            _nonviable = true;
    }

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
