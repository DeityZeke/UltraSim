using UltraSim.Network;

namespace UltraSim.Client;

/// <summary>
/// First real client/network movement bridge built on top of the new network kernel.
/// It keeps the old client concepts separate: input sequencing, prediction bookkeeping,
/// and interpolation-ready authoritative state buffering.
/// </summary>
public sealed class ClientMovementNetworkBridge
{
    private readonly INetworkTransport _transport;
    private readonly ConnectionId _connectionId;
    private readonly PacketRegistry<ClientMovementNetworkBridge> _registry;
    private readonly ClientInputSequencer<ClientMovementInput> _sequencer;
    private readonly ClientPredictionStore<ClientMovementState> _predictionStore;
    private readonly ClientInterpolationBuffer<ClientMovementState> _interpolation;
    private ClientInputSample<ClientMovementInput>[] _pendingScratch = new ClientInputSample<ClientMovementInput>[64];

    public ClientMovementNetworkBridge(
        INetworkTransport transport,
        ConnectionId connectionId,
        ClientInputSequencer<ClientMovementInput>? sequencer = null,
        ClientPredictionStore<ClientMovementState>? predictionStore = null,
        ClientInterpolationBuffer<ClientMovementState>? interpolation = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        _connectionId = connectionId;
        _sequencer = sequencer ?? new ClientInputSequencer<ClientMovementInput>();
        _predictionStore = predictionStore ?? new ClientPredictionStore<ClientMovementState>();
        _interpolation = interpolation ?? new ClientInterpolationBuffer<ClientMovementState>();
        _registry = new PacketRegistry<ClientMovementNetworkBridge>();
        _registry.Register<MovementStatePacket>(static (ClientMovementNetworkBridge bridge, in MovementStatePacket packet) => bridge.OnMovementState(packet));
        _registry.Seal();
    }

    public ConnectionId ConnectionId => _connectionId;
    public ClientInputSequencer<ClientMovementInput> Sequencer => _sequencer;
    public ClientPredictionStore<ClientMovementState> PredictionStore => _predictionStore;
    public ClientInterpolationBuffer<ClientMovementState> Interpolation => _interpolation;
    public ClientMovementStateReceiveResult LastReceiveResult { get; private set; }

    public ClientInputSample<ClientMovementInput> QueuePredictedInput(
        in ClientMovementInput input,
        uint clientTick,
        in ClientMovementState predictedState)
    {
        ClientInputSample<ClientMovementInput> sample = _sequencer.EnqueueInput(input, clientTick);
        _predictionStore.Record(sample.Sequence, clientTick, predictedState);
        return sample;
    }

    public bool TrySendInput(
        in ClientMovementInput input,
        uint clientTick,
        in ClientMovementState predictedState,
        out ClientMovementInputSendResult result)
    {
        ClientInputSample<ClientMovementInput> sample = QueuePredictedInput(input, clientTick, predictedState);
        return TrySendSample(sample, out result);
    }

    public int SendPending(int maxCount = int.MaxValue)
    {
        if (maxCount <= 0)
            return 0;

        int copied = CopyPendingToScratch();
        int sent = 0;
        for (int i = 0; i < copied && i < maxCount; i++)
        {
            if (TrySendSample(_pendingScratch[i], out _))
                sent++;
        }

        return sent;
    }

    public int PollInbound()
    {
        var inbound = new InboundDatagram[32];
        int count = _transport.PollInbound(inbound);
        for (int i = 0; i < count; i++)
        {
            if (inbound[i].ConnectionId != _connectionId)
                continue;

            if (!PacketCodec.TryDecodeEnvelope(inbound[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload))
                continue;

            _registry.TryDispatch(this, header, payload);
        }

        return count;
    }

    public bool TrySampleInterpolatedState(uint targetServerTick, InterpolationLerp<ClientMovementState> lerp, out ClientMovementState state)
    {
        return _interpolation.TrySample(targetServerTick, lerp, out state);
    }

    private bool TrySendSample(ClientInputSample<ClientMovementInput> sample, out ClientMovementInputSendResult result)
    {
        var packet = new MovementInputPacket(
            sample.Sequence,
            sample.ClientTick,
            sample.Input.MoveX,
            sample.Input.MoveY,
            sample.Input.Flags);
        ImmutablePayloadBuffer payload = PacketCodec.Encode(packet);
        _transport.EnqueueOutbound(new OutboundDatagram(
            _connectionId,
            MovementInputPacket.Plane,
            MovementInputPacket.PacketClass,
            MovementInputPacket.Channel,
            payload));
        result = new ClientMovementInputSendResult(sample.Sequence, payload.Length);
        return true;
    }

    private void OnMovementState(in MovementStatePacket packet)
    {
        int removedPending = _sequencer.AcknowledgeUpTo(packet.LastProcessedInputSequence);
        int removedPredictions = _predictionStore.RemoveAcknowledgedUpTo(packet.LastProcessedInputSequence);
        _interpolation.AddSample(
            packet.ServerTick,
            new ClientMovementState(packet.PositionX, packet.PositionY, packet.VelocityX, packet.VelocityY));
        LastReceiveResult = new ClientMovementStateReceiveResult(
            packet.NetworkEntityId,
            packet.ServerTick,
            packet.LastProcessedInputSequence,
            removedPending,
            removedPredictions);
    }

    private int CopyPendingToScratch()
    {
        while (true)
        {
            int copied = _sequencer.CopyPendingTo(_pendingScratch);
            if (copied < _pendingScratch.Length)
                return copied;

            int newSize = _pendingScratch.Length << 1;
            if (newSize < 64)
                newSize = 64;
            _pendingScratch = new ClientInputSample<ClientMovementInput>[newSize];
        }
    }
}
