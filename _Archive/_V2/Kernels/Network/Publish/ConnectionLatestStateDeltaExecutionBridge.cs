namespace UltraSim.Network;

/// <summary>
/// Executes incremental latest-state delta emission for one connection from a snapshot and
/// the connection's last-sent latest-state view.
/// </summary>
public sealed class ConnectionLatestStateDeltaExecutionBridge
{
    private readonly ConnectionLatestStateDeltaPlanner _planner;
    private readonly StateDeltaSnapshotBuilder _builder;

    public ConnectionLatestStateDeltaExecutionBridge(
        ConnectionLatestStateDeltaPlanner planner,
        StateDeltaSnapshotBuilder builder)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    public ConnectionLatestStateDeltaExecutionResult Execute(
        ConnectionId connectionId,
        StateBaselineDescriptor baseline,
        ReadOnlySpan<LatestStateUpdate> snapshot,
        Span<LatestStateUpdate> deltaScratch,
        Span<OutboundDatagram> destination)
    {
        if (baseline.BaselineId == 0)
            return default;

        ConnectionLatestStateDeltaPlanResult plan = _planner.Plan(connectionId, snapshot, deltaScratch);
        if (plan.Count == 0)
            return new ConnectionLatestStateDeltaExecutionResult(0, 0, baseline.BaselineId, 0);

        int written = _builder.Build(connectionId, baseline, deltaScratch[..plan.Count], destination);
        return new ConnectionLatestStateDeltaExecutionResult(
            DeltaItemCount: plan.Count,
            DatagramCount: written,
            BaselineId: baseline.BaselineId,
            HighestSequence: plan.HighestPlannedSequence);
    }
}
