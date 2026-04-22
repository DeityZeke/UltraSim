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
        ushort LastProcessedInputSequence);

    private readonly World _world;
    private readonly INetworkTransport _transport;
    private readonly ServerEcsMovementInputApplier _applyInput;
    private readonly ServerEcsMovementStateProjector _projectState;
    private readonly Dictionary<ConnectionId, BindingState> _bindings = new();
    private readonly List<ConnectionId> _connectionScratch = new();

    public ServerEcsMovementNetworkBridge(
        World world,
        INetworkTransport transport,
        ServerEcsMovementInputApplier applyInput,
        ServerEcsMovementStateProjector projectState)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _applyInput = applyInput ?? throw new ArgumentNullException(nameof(applyInput));
        _projectState = projectState ?? throw new ArgumentNullException(nameof(projectState));
    }

    public int BindingCount => _bindings.Count;
    public int LastReceivedInputCount { get; private set; }
    public int LastIgnoredInputCount { get; private set; }
    public int LastSentStateCount { get; private set; }

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
            LastProcessedInputSequence: 0);
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

            for (int i = 0; i < count; i++)
            {
                ref readonly InboundDatagram datagram = ref inbound[i];
                if (datagram.Plane != NetworkPlane.Input)
                    continue;

                if (!PacketCodec.TryDecodeEnvelope(datagram.Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload))
                {
                    LastIgnoredInputCount++;
                    continue;
                }

                if (!_bindings.TryGetValue(datagram.ConnectionId, out _))
                {
                    LastIgnoredInputCount++;
                    continue;
                }

                if (header.PacketId != MovementInputPacket.PacketId)
                {
                    LastIgnoredInputCount++;
                    continue;
                }

                var reader = new UltraSim.Network.BitReader(payload);
                MovementInputPacket packet = MovementInputPacket.Deserialize(ref reader);
                OnMovementInput(datagram.ConnectionId, packet);
            }

            if (count < inbound.Length)
                break;
        }

        return LastReceivedInputCount;
    }

    public int AdvanceAndSend(float deltaSeconds, uint serverTick)
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
                ImmutablePayloadBuffer payload = PacketCodec.Encode(packet);
                _transport.EnqueueOutbound(new OutboundDatagram(
                    connectionId,
                    MovementStatePacket.Plane,
                    MovementStatePacket.PacketClass,
                    MovementStatePacket.Channel,
                    payload));
                sent++;
            }

            _bindings[connectionId] = state;
        }

        _transport.FlushOutbound();
        LastSentStateCount = sent;
        return sent;
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
}
