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
        _movement = new ServerEcsMovementNetworkBridge(world, transport, applyInput, projectState);
        _spawnSession = spawnSession ?? throw new ArgumentNullException(nameof(spawnSession));
        _despawnSession = despawnSession;
        _inboundScratch = new InboundDatagram[maxPollBatch];
        _controlEvents = new ControlPlaneEvent[maxPollBatch];
        _sessionEvents = new SessionLifecycleEvent[maxPollBatch];
    }

    public NetworkControlPlaneHost Control => _control;
    public ServerEcsMovementNetworkBridge Movement => _movement;
    public int BoundConnectionCount => _entityByConnection.Count;

    public void Start() => _control.Start();

    public void Stop() => _control.Stop();

    public int Pump(long nowTicks, float deltaSeconds, uint serverTick)
    {
        int inboundCount = _transport.PollInbound(_inboundScratch);
        if (inboundCount > 0)
        {
            ReadOnlySpan<InboundDatagram> inbound = _inboundScratch.AsSpan(0, inboundCount);
            _control.ProcessInbound(inbound, nowTicks);
            HandleControlPlaneEvents(nowTicks);
            _movement.ProcessInbound(inbound);
        }
        else
        {
            _control.Sessions.SweepTimeouts(nowTicks);
        }

        HandleSessionLifecycleEvents(nowTicks);
        _movement.AdvanceAndSend(deltaSeconds, serverTick);
        _control.FlushOutbound();
        return inboundCount;
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
                _entityByConnection.Remove(evt.ConnectionId);
                _despawnSession?.Invoke(_world, evt.ConnectionId, entity);
                if (_despawnSession is null && _world.Entities.IsAlive(entity))
                    _world.Destroy(entity);
            }
        }
    }
}
