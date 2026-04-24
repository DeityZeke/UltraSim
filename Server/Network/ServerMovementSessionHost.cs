using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

/// <summary>
/// Narrow live server session host that shares one transport poll path between control-plane
/// session traffic and authoritative movement traffic.
/// </summary>
public sealed class ServerMovementSessionHost
{
    private readonly World _world;
    private readonly INetworkTransport _transport;
    private readonly NetworkControlPlaneHost _control;
    private readonly ServerEcsMovementNetworkBridge _movement;
    private readonly ServerEcsWorldNetworkPublisher _worldPublisher;
    private readonly ConnectionSubscriptionRegistry _subscriptions;
    private readonly ServerNetworkSessionRuntime _sessionRuntime;
    private readonly ServerMovementSessionSpawner _spawnSession;
    private readonly ServerMovementSessionDespawner? _despawnSession;
    private readonly InboundDatagram[] _inboundScratch;
    private readonly ControlPlaneEvent[] _controlEvents;
    private readonly SessionLifecycleEvent[] _sessionEvents;
    private readonly Dictionary<ConnectionId, Entity> _entityByConnection = new();

    public ServerMovementSessionHost(
        World world,
        INetworkTransport transport,
        ServerEcsMovementInputApplier applyInput,
        ServerEcsMovementStateProjector projectState,
        ServerMovementSessionSpawner spawnSession,
        ServerMovementSessionDespawner? despawnSession = null,
        HandshakePolicy handshakePolicy = default,
        SessionAdmissionPolicy sessionPolicy = default,
        ServerMovementOwnerCorrectionPolicy ownerCorrectionPolicy = default,
        ServerNetworkSessionRuntimeOptions runtimeOptions = default,
        int maxPollBatch = 64)
    {
        if (maxPollBatch <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPollBatch));

        _world = world ?? throw new ArgumentNullException(nameof(world));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _control = new NetworkControlPlaneHost(
            NetworkControlPlaneRole.Server,
            transport,
            handshakePolicy: handshakePolicy,
            sessionPolicy: sessionPolicy);
        _movement = new ServerEcsMovementNetworkBridge(world, transport, applyInput, projectState, ownerCorrectionPolicy);
        var latestState = new LatestStateStream();
        if (runtimeOptions == default)
            runtimeOptions = ServerNetworkSessionRuntimeOptions.Default;
        var orderedEvents = new OrderedEventStream(capacity: runtimeOptions.OrderedEventCapacity);
        _worldPublisher = new ServerEcsWorldNetworkPublisher(world, latestState, orderedEvents);
        _subscriptions = new ConnectionSubscriptionRegistry();
        _sessionRuntime = new ServerNetworkSessionRuntime(
            transport,
            latestState,
            orderedEvents,
            _subscriptions,
            new ReliableEventReplayBuffer(capacityPerConnection: runtimeOptions.ReplayBufferCapacityPerConnection),
            runtimeOptions);
        _spawnSession = spawnSession ?? throw new ArgumentNullException(nameof(spawnSession));
        _despawnSession = despawnSession;
        _inboundScratch = new InboundDatagram[maxPollBatch];
        _controlEvents = new ControlPlaneEvent[maxPollBatch];
        _sessionEvents = new SessionLifecycleEvent[maxPollBatch];
    }

    public NetworkControlPlaneHost Control => _control;
    public ServerEcsMovementNetworkBridge Movement => _movement;
    public ServerEcsWorldNetworkPublisher WorldPublisher => _worldPublisher;
    public ConnectionSubscriptionRegistry Subscriptions => _subscriptions;
    public ServerNetworkSessionRuntime SessionRuntime => _sessionRuntime;
    public ReliableEventAckTracker ReliableEventAcks => _sessionRuntime.ReliableEventAcks;
    public StateBaselineAckTracker StateBaselineAcks => _sessionRuntime.BaselineAcks;
    public int BoundConnectionCount => _entityByConnection.Count;

    public int CopyBoundConnections(Span<ConnectionId> destination)
    {
        int count = 0;
        foreach (ConnectionId connectionId in _entityByConnection.Keys)
        {
            if (count >= destination.Length)
                break;
            destination[count++] = connectionId;
        }

        return count;
    }

    public bool TryGetBoundEntity(ConnectionId connectionId, out Entity entity)
    {
        return _entityByConnection.TryGetValue(connectionId, out entity);
    }

    public void Start() => _control.Start();

    public void Stop() => _control.Stop();

    public int Pump(long nowTicks, float deltaSeconds, uint serverTick)
    {
        int totalInboundCount = 0;
        while (true)
        {
            int inboundCount = _transport.PollInbound(_inboundScratch);
            if (inboundCount <= 0)
                break;

            ReadOnlySpan<InboundDatagram> inbound = _inboundScratch.AsSpan(0, inboundCount);
            _control.ProcessInbound(inbound, nowTicks);
            HandleControlPlaneEvents(nowTicks);
            _sessionRuntime.ProcessInbound(inbound);
            _movement.ProcessInbound(inbound);
            totalInboundCount += inboundCount;
            DisposeInbound(inbound);

            if (inboundCount < _inboundScratch.Length)
                break;
        }

        if (totalInboundCount == 0)
        {
            _control.Sessions.SweepTimeouts(nowTicks);
        }

        HandleSessionLifecycleEvents(nowTicks);
        _movement.AdvanceAndSend(deltaSeconds, serverTick, flushOutbound: false);
        _sessionRuntime.DispatchTick(nowTicks, ConnectionSendBudget.Unlimited, flushOutbound: false);
        _control.FlushOutbound();
        return totalInboundCount;
    }

    private void HandleControlPlaneEvents(long nowTicks)
    {
        while (true)
        {
            int count = _control.DrainEvents(_controlEvents);
            if (count == 0)
                break;

            for (int i = 0; i < count; i++)
            {
                ControlPlaneEvent evt = _controlEvents[i];
                if (evt.Kind != ControlPlaneEventKind.HandshakeAccepted)
                    continue;
                if (_entityByConnection.ContainsKey(evt.ConnectionId))
                    continue;

                ServerMovementSessionSpawn spawn = _spawnSession(_world, evt.ConnectionId);
                _movement.Bind(evt.ConnectionId, spawn.Entity, spawn.NetworkEntityId);
                _sessionRuntime.BindConnection(evt.ConnectionId);
                _entityByConnection[evt.ConnectionId] = spawn.Entity;
            }
        }
    }

    private void HandleSessionLifecycleEvents(long nowTicks)
    {
        while (true)
        {
            int count = _control.Sessions.DrainLifecycleEvents(_sessionEvents);
            if (count == 0)
                break;

            for (int i = 0; i < count; i++)
            {
                SessionLifecycleEvent evt = _sessionEvents[i];
                if (evt.CurrentState != ConnectionSessionState.Disconnected &&
                    evt.Kind != SessionLifecycleEventKind.Removed)
                {
                    continue;
                }

                if (!_entityByConnection.TryGetValue(evt.ConnectionId, out Entity entity))
                    continue;

                _movement.Unbind(evt.ConnectionId);
                _sessionRuntime.UnbindConnection(evt.ConnectionId);
                _subscriptions.RemoveConnection(evt.ConnectionId);
                _entityByConnection.Remove(evt.ConnectionId);
                _despawnSession?.Invoke(_world, evt.ConnectionId, entity);
                if (_despawnSession is null && _world.Entities.IsAlive(entity))
                    _world.Destroy(entity);
            }
        }
    }

    public bool TryGetLastReliableEventAck(ConnectionId connectionId, out ulong sequence)
    {
        return _sessionRuntime.ReliableEventAcks.TryGetLastAcknowledged(connectionId, out sequence);
    }

    public bool TryGetLastStateBaselineAck(ConnectionId connectionId, out StateBaselineDescriptor descriptor)
    {
        return _sessionRuntime.BaselineAcks.TryGetLastAcknowledged(connectionId, out descriptor);
    }

    private static void DisposeInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        for (int i = 0; i < inbound.Length; i++)
            inbound[i].Dispose();
    }
}
