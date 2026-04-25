using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

/// <summary>
/// Narrow authoritative server-side movement bridge for the first real client/server movement slice.
/// Inputs arrive over the network transport, update per-connection movement intent, and are then
/// applied to ECS-owned entities before authoritative owner state is sent back out.
/// </summary>
public sealed class ServerEcsMovementNetworkBridge
{
    private readonly record struct BindingState(
        Entity Entity,
        uint NetworkEntityId,
        MovementInputPacket CurrentInput,
        bool HasInput,
        ushort LastReceivedInputSequence,
        ushort LastProcessedInputSequence,
        MovementStatePacket LastSentState,
        bool HasLastSentState,
        uint LastSentServerTick);

    private readonly World _world;
    private readonly INetworkTransport _transport;
    private readonly ServerEcsMovementInputApplier _applyInput;
    private readonly ServerEcsMovementStateProjector _projectState;
    private readonly ServerMovementOwnerCorrectionPolicy _ownerCorrectionPolicy;
    private readonly Dictionary<ConnectionId, BindingState> _bindings = new();
    private readonly List<ConnectionId> _connectionScratch = new();

    public ServerEcsMovementNetworkBridge(
        World world,
        INetworkTransport transport,
        ServerEcsMovementInputApplier applyInput,
        ServerEcsMovementStateProjector projectState,
        ServerMovementOwnerCorrectionPolicy ownerCorrectionPolicy = default)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _applyInput = applyInput ?? throw new ArgumentNullException(nameof(applyInput));
        _projectState = projectState ?? throw new ArgumentNullException(nameof(projectState));
        _ownerCorrectionPolicy = ownerCorrectionPolicy == default ? ServerMovementOwnerCorrectionPolicy.Default : ownerCorrectionPolicy;
    }

    public int BindingCount => _bindings.Count;
    public int LastReceivedInputCount { get; private set; }
    public int LastIgnoredInputCount { get; private set; }
    public int LastSentStateCount { get; private set; }
    public int LastPublishedObserverStateCount { get; private set; }

    public void Bind(ConnectionId connectionId, Entity entity, uint networkEntityId)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (!_world.Entities.IsAlive(entity))
            throw new ArgumentOutOfRangeException(nameof(entity));
        if (networkEntityId == 0)
            throw new ArgumentOutOfRangeException(nameof(networkEntityId));

        _bindings[connectionId] = new BindingState(
            entity,
            networkEntityId,
            default,
            HasInput: false,
            LastReceivedInputSequence: 0,
            LastProcessedInputSequence: 0,
            LastSentState: default,
            HasLastSentState: false,
            LastSentServerTick: 0);
    }

    public bool Unbind(ConnectionId connectionId)
    {
        return _bindings.Remove(connectionId);
    }

    public int PollInputs()
    {
        LastReceivedInputCount = 0;
        LastIgnoredInputCount = 0;

        var inbound = new InboundDatagram[64];
        while (true)
        {
            int count = _transport.PollInbound(inbound);
            if (count == 0)
                break;

            ProcessInbound(inbound.AsSpan(0, count));
            DisposeInbound(inbound.AsSpan(0, count));

            if (count < inbound.Length)
                break;
        }

        return LastReceivedInputCount;
    }

    public int ProcessInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        int processed = 0;
        for (int i = 0; i < inbound.Length; i++)
        {
            ref readonly InboundDatagram datagram = ref inbound[i];
            if (datagram.Plane != NetworkPlane.Input)
                continue;

            processed++;
            if (!_bindings.TryGetValue(datagram.ConnectionId, out _))
            {
                LastIgnoredInputCount++;
                continue;
            }

            if (datagram.PacketId != MovementInputPacket.PacketId)
            {
                LastIgnoredInputCount++;
                continue;
            }

            var reader = new UltraSim.Network.BitReader(datagram.EnvelopePayloadSpan);
            MovementInputPacket packet = MovementInputPacket.Deserialize(ref reader);
            OnMovementInput(datagram.ConnectionId, packet);
        }

        return processed;
    }

    public int AdvanceAndSend(float deltaSeconds, uint serverTick, bool flushOutbound = true)
    {
        if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        _connectionScratch.Clear();
        foreach (ConnectionId connectionId in _bindings.Keys)
            _connectionScratch.Add(connectionId);

        int sent = 0;
        for (int i = 0; i < _connectionScratch.Count; i++)
        {
            ConnectionId connectionId = _connectionScratch[i];
            BindingState state = _bindings[connectionId];
            if (!_world.Entities.IsAlive(state.Entity))
                continue;

            if (state.HasInput)
            {
                _applyInput(_world, state.Entity, state.CurrentInput, deltaSeconds);
                state = state with { LastProcessedInputSequence = state.CurrentInput.InputSequence };
            }

            if (_projectState(
                _world,
                state.Entity,
                state.NetworkEntityId,
                state.LastProcessedInputSequence,
                serverTick,
                out MovementStatePacket packet))
            {
                if (!ShouldSendOwnerCorrection(state, packet, serverTick))
                {
                    _bindings[connectionId] = state;
                    continue;
                }

                ImmutablePayloadBuffer payload = PacketCodec.Encode(packet);
                _transport.EnqueueOutbound(new OutboundDatagram(
                    connectionId,
                    MovementStatePacket.Plane,
                    MovementStatePacket.PacketClass,
                    MovementStatePacket.Channel,
                    payload));
                state = state with
                {
                    LastSentState = packet,
                    HasLastSentState = true,
                    LastSentServerTick = serverTick,
                };
                sent++;
            }

            _bindings[connectionId] = state;
        }

        if (flushOutbound)
            _transport.FlushOutbound();
        LastSentStateCount = sent;
        return sent;
    }

    public int PublishObserverMovementStates(
        ServerEcsWorldNetworkPublisher publisher,
        SubscriptionKey scopeKey,
        uint serverTick)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        if (!scopeKey.IsValid)
            throw new ArgumentOutOfRangeException(nameof(scopeKey));

        _connectionScratch.Clear();
        foreach (ConnectionId connectionId in _bindings.Keys)
            _connectionScratch.Add(connectionId);

        int published = 0;
        for (int i = 0; i < _connectionScratch.Count; i++)
        {
            ConnectionId connectionId = _connectionScratch[i];
            BindingState state = _bindings[connectionId];
            if (!_world.Entities.IsAlive(state.Entity))
                continue;

            if (!_projectState(
                _world,
                state.Entity,
                state.NetworkEntityId,
                state.LastProcessedInputSequence,
                serverTick,
                out MovementStatePacket packet))
            {
                continue;
            }

            publisher.LatestStateStream.Publish(
                ServerEcsNetworkItemKeys.GetObservedMovementItemKey(state.NetworkEntityId),
                scopeKey,
                PacketCodec.EncodePayload(packet));
            published++;
        }

        LastPublishedObserverStateCount = published;
        return published;
    }

    private void OnMovementInput(ConnectionId connectionId, in MovementInputPacket packet)
    {
        if (!_bindings.TryGetValue(connectionId, out BindingState state))
        {
            LastIgnoredInputCount++;
            return;
        }

        if (state.LastReceivedInputSequence != 0 && !SequenceGt(packet.InputSequence, state.LastReceivedInputSequence))
        {
            LastIgnoredInputCount++;
            return;
        }

        _bindings[connectionId] = state with
        {
            CurrentInput = packet,
            HasInput = true,
            LastReceivedInputSequence = packet.InputSequence,
        };
        LastReceivedInputCount++;
    }

    private static bool SequenceGt(ushort left, ushort right)
    {
        return unchecked((short)(left - right)) > 0;
    }

    private bool ShouldSendOwnerCorrection(in BindingState state, in MovementStatePacket packet, uint serverTick)
    {
        if (!state.HasLastSentState)
            return true;

        uint silentTicks = serverTick - state.LastSentServerTick;
        if (silentTicks >= _ownerCorrectionPolicy.MaxSilentTicks)
            return true;

        float dx = packet.PositionX - state.LastSentState.PositionX;
        float dy = packet.PositionY - state.LastSentState.PositionY;
        float distanceSquared = (dx * dx) + (dy * dy);
        float deadband = _ownerCorrectionPolicy.PositionDeadband;
        return distanceSquared > (deadband * deadband);
    }

    private static void DisposeInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        for (int i = 0; i < inbound.Length; i++)
            inbound[i].Dispose();
    }
}
