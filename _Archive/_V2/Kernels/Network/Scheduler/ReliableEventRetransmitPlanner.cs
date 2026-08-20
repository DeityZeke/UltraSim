namespace UltraSim.Network;

/// <summary>
/// Plans bounded retransmit work from a per-connection replay window.
/// It only triggers once the oldest unacked event has aged past a threshold,
/// and it falls back to recovery when the requested sequence is no longer retained.
/// </summary>
public sealed class ReliableEventRetransmitPlanner
{
    private readonly ReliableEventReplayBuffer _replayBuffer;
    private readonly ReliableEventAckTracker _ackTracker;

    public ReliableEventRetransmitPlanner(
        ReliableEventReplayBuffer replayBuffer,
        ReliableEventAckTracker ackTracker)
    {
        _replayBuffer = replayBuffer ?? throw new ArgumentNullException(nameof(replayBuffer));
        _ackTracker = ackTracker ?? throw new ArgumentNullException(nameof(ackTracker));
    }

    public ReliableEventRetransmitPlanResult Plan(
        ConnectionId connectionId,
        long nowTick,
        ReliableEventRetransmitPolicy policy,
        Span<ReliableEventReplayEntry> destination)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (policy.RetransmitThresholdTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy));
        if (policy.MaxRetransmitItemsPerTick <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy));
        if (destination.Length == 0)
            return default;

        if (!_replayBuffer.TryGetOldestEntry(connectionId, out ReliableEventReplayEntry oldest))
            return default;

        long age = nowTick - oldest.SentTick;
        if (age < policy.RetransmitThresholdTicks)
        {
            return new ReliableEventRetransmitPlanResult(
                Count: 0,
                RecoveryRequired: false,
                NextSequence: oldest.Sequence,
                OldestAvailableSequence: oldest.Sequence);
        }

        ulong nextSequence = 1;
        if (_ackTracker.TryGetLastAcknowledged(connectionId, out ulong lastAcknowledged))
            nextSequence = lastAcknowledged + 1;
        else
            nextSequence = oldest.Sequence;

        int maxItems = Math.Min(destination.Length, policy.MaxRetransmitItemsPerTick);
        int copied = _replayBuffer.CopyPendingSince(connectionId, nextSequence, destination.Slice(0, maxItems), out bool recoveryRequired);

        return new ReliableEventRetransmitPlanResult(
            Count: copied,
            RecoveryRequired: recoveryRequired,
            NextSequence: nextSequence,
            OldestAvailableSequence: oldest.Sequence);
    }
}
