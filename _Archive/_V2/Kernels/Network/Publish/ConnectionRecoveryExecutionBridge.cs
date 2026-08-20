namespace UltraSim.Network;

/// <summary>
/// Executes bounded reliable-event resend work for one connection. It either:
/// 1. retransmits retained ordered events that are old enough to resend, or
/// 2. falls back to a full baseline when the replay window is no longer sufficient.
/// </summary>
public sealed class ConnectionRecoveryExecutionBridge
{
    private readonly ReliableEventRetransmitPlanner _retransmitPlanner;
    private readonly ReliableEventRetransmitBuilder _retransmitBuilder;
    private readonly ConnectionRecoveryPlanner _recoveryPlanner;

    public ConnectionRecoveryExecutionBridge(
        ReliableEventRetransmitPlanner retransmitPlanner,
        ConnectionRecoveryPlanner recoveryPlanner,
        ReliableEventRetransmitBuilder? retransmitBuilder = null)
    {
        _retransmitPlanner = retransmitPlanner ?? throw new ArgumentNullException(nameof(retransmitPlanner));
        _recoveryPlanner = recoveryPlanner ?? throw new ArgumentNullException(nameof(recoveryPlanner));
        _retransmitBuilder = retransmitBuilder ?? new ReliableEventRetransmitBuilder();
    }

    public ConnectionRecoveryExecutionResult Execute(
        ConnectionId connectionId,
        long nowTick,
        ReliableEventRetransmitPolicy retransmitPolicy,
        Span<ReliableEventReplayEntry> replayScratch,
        Span<LatestStateUpdate> snapshotScratch,
        Span<OutboundDatagram> destination)
    {
        ReliableEventRetransmitPlanResult retransmit = _retransmitPlanner.Plan(
            connectionId,
            nowTick,
            retransmitPolicy,
            replayScratch);

        if (retransmit.RecoveryRequired)
        {
            ConnectionRecoveryPlanResult baseline = _recoveryPlanner.PlanBaseline(
                connectionId,
                snapshotScratch,
                destination);
            return new ConnectionRecoveryExecutionResult(
                baseline.Mode,
                ReplayEntryCount: 0,
                DatagramCount: baseline.BaselineDatagramCount,
                CompressedCount: 0,
                EncryptedCount: 0,
                BaselineDescriptor: baseline.BaselineDescriptor,
                NextState: baseline.NextState);
        }

        if (retransmit.Count == 0)
        {
            return new ConnectionRecoveryExecutionResult(
                ConnectionRecoveryMode.Scheduled,
                ReplayEntryCount: 0,
                DatagramCount: 0,
                CompressedCount: 0,
                EncryptedCount: 0,
                BaselineDescriptor: default,
                NextState: default);
        }

        OutboundAssemblyResult assembly = _retransmitBuilder.Build(
            connectionId,
            replayScratch[..retransmit.Count],
            destination);
        return new ConnectionRecoveryExecutionResult(
            ConnectionRecoveryMode.Retransmit,
            ReplayEntryCount: retransmit.Count,
            DatagramCount: assembly.Count,
            CompressedCount: assembly.CompressedCount,
            EncryptedCount: assembly.EncryptedCount,
            BaselineDescriptor: default,
            NextState: default);
    }
}
