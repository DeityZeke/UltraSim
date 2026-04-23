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
    private readonly ReliableEventAckTracker _reliableEventAcks = new();
    private readonly StateBaselineAckTracker _stateBaselineAcks = new();

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
        _spawnSession = spawnSession ?? throw new ArgumentNullException(nameof(spawnSession));
        _despawnSession = despawnSession;
        _inboundScratch = new InboundDatagram[maxPollBatch];
        _controlEvents = new ControlPlaneEvent[maxPollBatch];
        _sessionEvents = new SessionLifecycleEvent[maxPollBatch];
    }

    public NetworkControlPlaneHost Control => _control;
    public ServerEcsMovementNetworkBridge Movement => _movement;
    public ReliableEventAckTracker ReliableEventAcks => _reliableEventAcks;
    public StateBaselineAckTracker StateBaselineAcks => _stateBaselineAcks;
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
            ProcessReliableEventAcks(inbound);
            ProcessStateBaselineAcks(inbound);
            _movement.ProcessInbound(inbound);
            totalInboundCount += inboundCount;

            if (inboundCount < _inboundScratch.Length)
                break;
        }

        if (totalInboundCount == 0)
        {
            _control.Sessions.SweepTimeouts(nowTicks);
        }

        HandleSessionLifecycleEvents(nowTicks);
        _movement.AdvanceAndSend(deltaSeconds, serverTick);
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
                _reliableEventAcks.Remove(evt.ConnectionId);
                _stateBaselineAcks.Remove(evt.ConnectionId);
                _despawnSession?.Invoke(_world, evt.ConnectionId, entity);
                if (_despawnSession is null && _world.Entities.IsAlive(entity))
                    _world.Destroy(entity);
            }
        }
    }

    public bool TryGetLastReliableEventAck(ConnectionId connectionId, out ulong sequence)
    {
        return _reliableEventAcks.TryGetLastAcknowledged(connectionId, out sequence);
    }

    public bool TryGetLastStateBaselineAck(ConnectionId connectionId, out StateBaselineDescriptor descriptor)
    {
        return _stateBaselineAcks.TryGetLastAcknowledged(connectionId, out descriptor);
    }

    private void ProcessReliableEventAcks(ReadOnlySpan<InboundDatagram> inbound)
    {
        for (int i = 0; i < inbound.Length; i++)
        {
            ref readonly InboundDatagram datagram = ref inbound[i];
            if (datagram.ConnectionId == ConnectionId.Invalid || datagram.Plane != NetworkPlane.State)
                continue;

            if (!PacketCodec.TryDecodeEnvelope(datagram.Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload))
                continue;
            if (header.PacketId != ReliableEventAckPacket.PacketId)
                continue;

            var reader = new UltraSim.Network.BitReader(payload);
            ReliableEventAckPacket ack = ReliableEventAckPacket.Deserialize(ref reader);
            _reliableEventAcks.TryAcknowledge(datagram.ConnectionId, ack.Sequence, out _);
        }
    }

    private void ProcessStateBaselineAcks(ReadOnlySpan<InboundDatagram> inbound)
    {
        for (int i = 0; i < inbound.Length; i++)
        {
            ref readonly InboundDatagram datagram = ref inbound[i];
            if (datagram.ConnectionId == ConnectionId.Invalid || datagram.Plane != NetworkPlane.State)
                continue;

            if (!PacketCodec.TryDecodeEnvelope(datagram.Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload))
                continue;
            if (header.PacketId != StateBaselineAckPacket.PacketId)
                continue;

            var reader = new UltraSim.Network.BitReader(payload);
            StateBaselineAckPacket ack = StateBaselineAckPacket.Deserialize(ref reader);
            _stateBaselineAcks.TryAcknowledge(datagram.ConnectionId, ack.Descriptor, out _);
        }
    }
}
