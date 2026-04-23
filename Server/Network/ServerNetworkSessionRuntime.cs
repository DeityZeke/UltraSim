using UltraSim.Network;

namespace UltraSim.Server.Network;

/// <summary>
/// First generic server-side runtime composition layer for the network kernel.
/// It owns per-connection schedule state, ack processing, baseline bootstrap,
/// bounded retransmit/recovery, ordered-event delivery, and latest-state delta delivery.
/// </summary>
public sealed class ServerNetworkSessionRuntime
{
    private readonly INetworkTransport _transport;
    private readonly LatestStateStream _latestState;
    private readonly OrderedEventStream _orderedEvents;
    private readonly ConnectionSubscriptionRegistry _subscriptions;
    private readonly ConnectionStreamScheduler _scheduler;
    private readonly OutboundDatagramAssembler _assembler;
    private readonly StateBaselineSnapshotBuilder _baselineBuilder;
    private readonly StateBaselineAckTracker _baselineAcks = new();
    private readonly ReliableEventAckTracker _reliableEventAcks = new();
    private readonly ReliableEventReplayBuffer _replayBuffer;
    private readonly ReliableEventRetransmitPlanner _retransmitPlanner;
    private readonly ReliableEventRetransmitBuilder _retransmitBuilder;
    private readonly ConnectionLatestStateSentTracker _latestSentTracker = new();
    private readonly ConnectionLatestStateDeltaExecutionBridge _deltaExecution;
    private readonly Dictionary<ConnectionId, ConnectionScheduleState> _stateByConnection = new();
    private readonly ConnectionId[] _connectionScratch;
    private readonly SubscriptionKey[] _subscriptionScratch;
    private readonly LatestStateUpdate[] _filteredSnapshotScratch;
    private readonly LatestStateUpdate[] _deltaScratch;
    private readonly ScheduledWorkItem[] _scheduledScratch;
    private readonly ScheduledWorkItem[] _orderedEventScratch;
    private readonly ReliableEventReplayEntry[] _replayScratch;
    private readonly OutboundDatagram[] _datagramScratch;
    private uint _nextBaselineId = 1;

    public ServerNetworkSessionRuntime(
        INetworkTransport transport,
        LatestStateStream latestState,
        OrderedEventStream orderedEvents,
        ConnectionSubscriptionRegistry subscriptions,
        ReliableEventReplayBuffer replayBuffer,
        ServerNetworkSessionRuntimeOptions options = default,
        OutboundDatagramAssembler? assembler = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _latestState = latestState ?? throw new ArgumentNullException(nameof(latestState));
        _orderedEvents = orderedEvents ?? throw new ArgumentNullException(nameof(orderedEvents));
        _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
        _replayBuffer = replayBuffer ?? throw new ArgumentNullException(nameof(replayBuffer));

        if (options == default)
            options = ServerNetworkSessionRuntimeOptions.Default;
        if (options.MaxConnections <= 0 || options.MaxSnapshotItems <= 0 || options.MaxScheduledItems <= 0 ||
            options.MaxReplayItems <= 0 || options.MaxDatagrams <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));

        _scheduler = new ConnectionStreamScheduler(_latestState, _orderedEvents);
        _assembler = assembler ?? new OutboundDatagramAssembler();
        _baselineBuilder = new StateBaselineSnapshotBuilder();
        _retransmitPlanner = new ReliableEventRetransmitPlanner(_replayBuffer, _reliableEventAcks);
        _retransmitBuilder = new ReliableEventRetransmitBuilder(_assembler);
        _deltaExecution = new ConnectionLatestStateDeltaExecutionBridge(
            new ConnectionLatestStateDeltaPlanner(_latestSentTracker),
            new StateDeltaSnapshotBuilder(_latestSentTracker));
        _connectionScratch = new ConnectionId[options.MaxConnections];
        _subscriptionScratch = new SubscriptionKey[options.MaxSnapshotItems];
        _filteredSnapshotScratch = new LatestStateUpdate[options.MaxSnapshotItems];
        _deltaScratch = new LatestStateUpdate[options.MaxSnapshotItems];
        _scheduledScratch = new ScheduledWorkItem[options.MaxScheduledItems];
        _orderedEventScratch = new ScheduledWorkItem[options.MaxScheduledItems];
        _replayScratch = new ReliableEventReplayEntry[options.MaxReplayItems];
        _datagramScratch = new OutboundDatagram[options.MaxDatagrams];
        Options = options;
    }

    public ServerNetworkSessionRuntimeOptions Options { get; }
    public StateBaselineAckTracker BaselineAcks => _baselineAcks;
    public ReliableEventAckTracker ReliableEventAcks => _reliableEventAcks;
    public ConnectionLatestStateSentTracker LatestStateSentTracker => _latestSentTracker;
    public int ConnectionCount => _stateByConnection.Count;

    public void BindConnection(ConnectionId connectionId)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        _stateByConnection.TryAdd(connectionId, default);
    }

    public bool UnbindConnection(ConnectionId connectionId)
    {
        _baselineAcks.Remove(connectionId);
        _reliableEventAcks.Remove(connectionId);
        _latestSentTracker.Remove(connectionId);
        _replayBuffer.AcknowledgeUpTo(connectionId, ulong.MaxValue);
        return _stateByConnection.Remove(connectionId);
    }

    public int ProcessInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        int processed = 0;
        for (int i = 0; i < inbound.Length; i++)
        {
            ref readonly InboundDatagram datagram = ref inbound[i];
            if (datagram.ConnectionId == ConnectionId.Invalid || datagram.Plane != NetworkPlane.State)
                continue;

            if (datagram.PacketId == ReliableEventAckPacket.PacketId)
            {
                var reader = new BitReader(datagram.EnvelopePayloadSpan);
                ReliableEventAckPacket ack = ReliableEventAckPacket.Deserialize(ref reader);
                if (_reliableEventAcks.TryAcknowledge(datagram.ConnectionId, ack.Sequence, out _))
                    _replayBuffer.AcknowledgeUpTo(datagram.ConnectionId, ack.Sequence);
                processed++;
                continue;
            }

            if (datagram.PacketId == StateBaselineAckPacket.PacketId)
            {
                var reader = new BitReader(datagram.EnvelopePayloadSpan);
                StateBaselineAckPacket ack = StateBaselineAckPacket.Deserialize(ref reader);
                if (_baselineAcks.TryAcknowledge(datagram.ConnectionId, ack.Descriptor, out _))
                {
                    _reliableEventAcks.TryAcknowledge(datagram.ConnectionId, ack.Descriptor.LastOrderedEventSequence, out _);
                    _replayBuffer.AcknowledgeUpTo(datagram.ConnectionId, ack.Descriptor.LastOrderedEventSequence);
                    if (_stateByConnection.ContainsKey(datagram.ConnectionId))
                    {
                        _stateByConnection[datagram.ConnectionId] = new ConnectionScheduleState(
                            new LatestStateCursor(ack.Descriptor.HighestLatestStateSequence),
                            new OrderedEventCursor(ack.Descriptor.LastOrderedEventSequence + 1));
                    }
                }

                processed++;
            }
        }

        return processed;
    }

    public ServerNetworkSessionRuntimeTickResult DispatchTick(long nowTick, ConnectionSendBudget budget, bool flushOutbound = true)
    {
        int connectionCount = CopyConnections(_connectionScratch);
        int baselineDatagrams = 0;
        int retransmitDatagrams = 0;
        int orderedEventDatagrams = 0;
        int deltaDatagrams = 0;

        for (int connectionIndex = 0; connectionIndex < connectionCount; connectionIndex++)
        {
            ConnectionId connectionId = _connectionScratch[connectionIndex];
            int subscriptionCount = _subscriptions.CopySubscriptions(connectionId, _subscriptionScratch);
            if (subscriptionCount == 0)
                continue;
            ReadOnlySpan<SubscriptionKey> subscriptions = _subscriptionScratch.AsSpan(0, subscriptionCount);

            if (!_baselineAcks.TryGetLastAcknowledged(connectionId, out StateBaselineDescriptor baselineDescriptor))
            {
                baselineDatagrams += SendFilteredBaseline(subscriptions, connectionId, out _);
                continue;
            }

            ReliableEventRetransmitPlanResult retransmitPlan = _retransmitPlanner.Plan(
                connectionId,
                nowTick,
                Options.RetransmitPolicy,
                _replayScratch);

            if (retransmitPlan.RecoveryRequired)
            {
                baselineDatagrams += SendFilteredBaseline(subscriptions, connectionId, out _);
                continue;
            }

            if (retransmitPlan.Count > 0)
            {
                OutboundAssemblyResult retransmitAssembly = _retransmitBuilder.Build(
                    connectionId,
                    _replayScratch.AsSpan(0, retransmitPlan.Count),
                    _datagramScratch);
                Enqueue(_datagramScratch.AsSpan(0, retransmitAssembly.Count));
                retransmitDatagrams += retransmitAssembly.Count;
            }

            if (!_stateByConnection.TryGetValue(connectionId, out ConnectionScheduleState state))
                continue;
            ConnectionScheduleResult schedule = _scheduler.Schedule(
                state,
                WithoutLatestStateBudget(budget),
                _scheduledScratch);
            _stateByConnection[connectionId] = schedule.NextState;

            int orderedCount = FilterOrderedEventsForConnection(subscriptions, _scheduledScratch.AsSpan(0, schedule.Count), _orderedEventScratch);
            if (orderedCount > 0)
            {
                OutboundAssemblyResult orderedAssembly = _assembler.Assemble(
                    connectionId,
                    _orderedEventScratch.AsSpan(0, orderedCount),
                    _datagramScratch);
                Enqueue(_datagramScratch.AsSpan(0, orderedAssembly.Count));
                orderedEventDatagrams += orderedAssembly.Count;

                for (int i = 0; i < orderedCount; i++)
                {
                    ScheduledWorkItem item = _orderedEventScratch[i];
                    _replayBuffer.Track(connectionId, nowTick, item.Sequence, item.ScopeKey, item.PacketClass, item.Payload);
                }
            }

            int filteredSnapshotCount = CopyFilteredLatestSnapshot(subscriptions, _filteredSnapshotScratch);
            if (filteredSnapshotCount > 0)
            {
                ConnectionLatestStateDeltaExecutionResult delta = _deltaExecution.Execute(
                    connectionId,
                    baselineDescriptor,
                    _filteredSnapshotScratch.AsSpan(0, filteredSnapshotCount),
                    _deltaScratch,
                    _datagramScratch);
                Enqueue(_datagramScratch.AsSpan(0, delta.DatagramCount));
                deltaDatagrams += delta.DatagramCount;
            }
        }

        if (flushOutbound)
            _transport.FlushOutbound();
        int totalDatagrams = baselineDatagrams + retransmitDatagrams + orderedEventDatagrams + deltaDatagrams;
        return new ServerNetworkSessionRuntimeTickResult(
            connectionCount,
            baselineDatagrams,
            retransmitDatagrams,
            orderedEventDatagrams,
            deltaDatagrams,
            totalDatagrams);
    }

    private int SendFilteredBaseline(ReadOnlySpan<SubscriptionKey> subscriptions, ConnectionId connectionId, out StateBaselineDescriptor descriptor)
    {
        int filteredSnapshotCount = CopyFilteredLatestSnapshot(subscriptions, _filteredSnapshotScratch);
        LatestStateSnapshot latestSnapshot = _latestState.GetSnapshot();
        OrderedEventSnapshot orderedSnapshot = _orderedEvents.GetSnapshot();
        ulong highestFilteredSequence = 0;
        for (int i = 0; i < filteredSnapshotCount; i++)
        {
            if (_filteredSnapshotScratch[i].Sequence > highestFilteredSequence)
                highestFilteredSequence = _filteredSnapshotScratch[i].Sequence;
        }

        descriptor = new StateBaselineDescriptor(
            BaselineId: _nextBaselineId++,
            HighestLatestStateSequence: highestFilteredSequence == 0 ? latestSnapshot.HighestPublishedSequence : highestFilteredSequence,
            LastOrderedEventSequence: orderedSnapshot.Count == 0 ? 0UL : orderedSnapshot.NewestAvailableSequence,
            ItemCount: (ushort)Math.Min(filteredSnapshotCount, ushort.MaxValue));

        int boundedItemCount = Math.Min(filteredSnapshotCount, ushort.MaxValue);
        int written = _baselineBuilder.Build(connectionId, descriptor, _filteredSnapshotScratch.AsSpan(0, boundedItemCount), _datagramScratch);
        Enqueue(_datagramScratch.AsSpan(0, written));
        _latestSentTracker.MarkSent(connectionId, _filteredSnapshotScratch.AsSpan(0, boundedItemCount));
        return written;
    }

    private int CopyConnections(Span<ConnectionId> destination)
    {
        int count = 0;
        foreach (ConnectionId connectionId in _stateByConnection.Keys)
        {
            if (count >= destination.Length)
                break;
            destination[count++] = connectionId;
        }

        return count;
    }

    private int CopyFilteredLatestSnapshot(ReadOnlySpan<SubscriptionKey> subscriptions, Span<LatestStateUpdate> destination)
    {
        return _latestState.CopySnapshotForScopes(subscriptions, destination);
    }

    private int FilterOrderedEventsForConnection(
        ReadOnlySpan<SubscriptionKey> subscriptions,
        ReadOnlySpan<ScheduledWorkItem> source,
        Span<ScheduledWorkItem> destination)
    {
        int count = 0;
        for (int i = 0; i < source.Length && count < destination.Length; i++)
        {
            ScheduledWorkItem item = source[i];
            if (item.Kind != ScheduledWorkKind.OrderedEvent)
                continue;
            if (!SubscriptionContains(subscriptions, item.ScopeKey))
                continue;

            destination[count++] = item;
        }

        return count;
    }

    private void Enqueue(ReadOnlySpan<OutboundDatagram> datagrams)
    {
        for (int i = 0; i < datagrams.Length; i++)
            _transport.EnqueueOutbound(datagrams[i]);
    }

    private static ConnectionSendBudget WithoutLatestStateBudget(ConnectionSendBudget budget)
    {
        return new ConnectionSendBudget(
            new PacketClassBudget(MaxItems: 0, MaxPayloadBytes: 0),
            budget.ReliableEvent,
            budget.Bulk);
    }

    private static bool SubscriptionContains(ReadOnlySpan<SubscriptionKey> keys, SubscriptionKey key)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == key)
                return true;
        }

        return false;
    }

}
