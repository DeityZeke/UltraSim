using UltraSim.Client;
using UltraSim.Network;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(ClientConnectionState_PreservesExpectedValues), ClientConnectionState_PreservesExpectedValues);
        Run(nameof(ClientInputSequencer_QueuesCopiesAndAcknowledgesInputs), ClientInputSequencer_QueuesCopiesAndAcknowledgesInputs);
        Run(nameof(ClientPredictionStore_RecordsLooksUpAndRemovesAcknowledgedStates), ClientPredictionStore_RecordsLooksUpAndRemovesAcknowledgedStates);
        Run(nameof(ClientInterpolationBuffer_SamplesInterpolatedAndLatestState), ClientInterpolationBuffer_SamplesInterpolatedAndLatestState);
        Run(nameof(MovementPackets_RoundTripExpectedFields), MovementPackets_RoundTripExpectedFields);
        Run(nameof(ClientObserverStateBridge_AppliesNewestRealtimeFramesOnly), ClientObserverStateBridge_AppliesNewestRealtimeFramesOnly);
        Run(nameof(ClientObserverEventBridge_AppliesIncreasingReliableEventFramesOnly), ClientObserverEventBridge_AppliesIncreasingReliableEventFramesOnly);
        Run(nameof(ClientMovementNetworkBridge_LoopbackRoundTripsMovementInputAndAuthoritativeState), ClientMovementNetworkBridge_LoopbackRoundTripsMovementInputAndAuthoritativeState);

        Console.WriteLine("All Client scaffold tests passed.");
    }

    private static void ClientConnectionState_PreservesExpectedValues()
    {
        Assert.Equal(0, (int)ClientConnectionState.Disconnected);
        Assert.Equal(1, (int)ClientConnectionState.Connecting);
        Assert.Equal(2, (int)ClientConnectionState.Handshaking);
        Assert.Equal(3, (int)ClientConnectionState.Ready);
        Assert.Equal(4, (int)ClientConnectionState.Faulted);
    }

    private static void ClientInputSequencer_QueuesCopiesAndAcknowledgesInputs()
    {
        var sequencer = new ClientInputSequencer<ClientMovementInput>();
        ClientInputSample<ClientMovementInput> first = sequencer.EnqueueInput(new ClientMovementInput(100, 0, 1), 10);
        ClientInputSample<ClientMovementInput> second = sequencer.EnqueueInput(new ClientMovementInput(0, 100, 2), 11);

        Assert.Equal((ushort)1, first.Sequence);
        Assert.Equal((ushort)2, second.Sequence);
        Assert.Equal(2, sequencer.PendingCount);

        var scratch = new ClientInputSample<ClientMovementInput>[4];
        int copied = sequencer.CopyPendingTo(scratch);
        Assert.Equal(2, copied);
        Assert.Equal(first, scratch[0]);
        Assert.Equal(second, scratch[1]);

        int removed = sequencer.AcknowledgeUpTo(1);
        Assert.Equal(1, removed);
        Assert.Equal(1, sequencer.PendingCount);
        copied = sequencer.CopyPendingTo(scratch);
        Assert.Equal(1, copied);
        Assert.Equal(second, scratch[0]);
    }

    private static void ClientPredictionStore_RecordsLooksUpAndRemovesAcknowledgedStates()
    {
        var store = new ClientPredictionStore<ClientMovementState>();
        store.Record(1, 10, new ClientMovementState(1, 2, 3, 4));
        store.Record(2, 11, new ClientMovementState(5, 6, 7, 8));

        Assert.Equal(2, store.Count);
        Assert.True(store.TryGet(2, out PredictedStateSample<ClientMovementState> second));
        Assert.Equal((ushort)2, second.Sequence);
        Assert.Equal(new ClientMovementState(5, 6, 7, 8), second.State);

        int removed = store.RemoveAcknowledgedUpTo(1);
        Assert.Equal(1, removed);
        Assert.Equal(1, store.Count);
        Assert.False(store.TryGet(1, out _));
        Assert.True(store.TryGet(2, out _));
    }

    private static void ClientInterpolationBuffer_SamplesInterpolatedAndLatestState()
    {
        var buffer = new ClientInterpolationBuffer<ClientMovementState>();
        buffer.AddSample(100, new ClientMovementState(0, 0, 0, 0));
        buffer.AddSample(110, new ClientMovementState(10, 20, 0, 0));

        Assert.True(buffer.TrySample(105, static (in ClientMovementState a, in ClientMovementState b, float alpha) =>
        {
            return new ClientMovementState(
                a.PositionX + (b.PositionX - a.PositionX) * alpha,
                a.PositionY + (b.PositionY - a.PositionY) * alpha,
                a.VelocityX + (b.VelocityX - a.VelocityX) * alpha,
                a.VelocityY + (b.VelocityY - a.VelocityY) * alpha);
        }, out ClientMovementState sampled));

        Assert.Equal(new ClientMovementState(5, 10, 0, 0), sampled);

        Assert.True(buffer.TrySample(999, static (in ClientMovementState a, in ClientMovementState b, float alpha) => a, out ClientMovementState latest));
        Assert.Equal(new ClientMovementState(10, 20, 0, 0), latest);
    }

    private static void MovementPackets_RoundTripExpectedFields()
    {
        var input = new MovementInputPacket(12, 345, -100, 200, 7);
        ImmutablePayloadBuffer encodedInput = PacketCodec.Encode(input);
        Assert.True(PacketCodec.TryDecodeEnvelope(encodedInput.Span, out PacketEnvelopeHeader inputHeader, out ReadOnlySpan<byte> inputPayload));
        Assert.Equal(MovementInputPacket.PacketId, inputHeader.PacketId);
        var inputReader = new BitReader(inputPayload);
        MovementInputPacket decodedInput = MovementInputPacket.Deserialize(ref inputReader);
        Assert.Equal(input, decodedInput);

        var state = new MovementStatePacket(77, 500, 12, 1.25f, 2.5f, 3.75f, 4.5f);
        ImmutablePayloadBuffer encodedState = PacketCodec.Encode(state);
        Assert.True(PacketCodec.TryDecodeEnvelope(encodedState.Span, out PacketEnvelopeHeader stateHeader, out ReadOnlySpan<byte> statePayload));
        Assert.Equal(MovementStatePacket.PacketId, stateHeader.PacketId);
        var stateReader = new BitReader(statePayload);
        MovementStatePacket decodedState = MovementStatePacket.Deserialize(ref stateReader);
        Assert.Equal(state, decodedState);
    }

    private static void ClientObserverStateBridge_AppliesNewestRealtimeFramesOnly()
    {
        int applyCount = 0;
        ulong lastItemKey = 0;
        ulong lastSequence = 0;
        byte[]? lastPayload = null;

        var bridge = new ClientObserverStateBridge(
            new ConnectionId(7),
            (itemKey, sequence, payload) =>
            {
                applyCount++;
                lastItemKey = itemKey;
                lastSequence = sequence;
                lastPayload = payload.Span.ToArray();
            });

        ImmutablePayloadBuffer frame1 = PacketCodec.Encode(new RealtimeStateFramePacket(10, 1001, ImmutablePayloadBuffer.CopyFrom([1, 2])));
        ImmutablePayloadBuffer frame2 = PacketCodec.Encode(new RealtimeStateFramePacket(9, 1001, ImmutablePayloadBuffer.CopyFrom([3, 4])));
        ImmutablePayloadBuffer frame3 = PacketCodec.Encode(new RealtimeStateFramePacket(11, 1001, ImmutablePayloadBuffer.CopyFrom([5, 6])));
        InboundDatagram[] inbound =
        [
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Realtime, ChannelType.UnreliableSequenced, frame1.Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Realtime, ChannelType.UnreliableSequenced, frame2.Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Realtime, ChannelType.UnreliableSequenced, frame3.Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(3, processed);
        Assert.Equal(3, bridge.LastReceivedFrameCount);
        Assert.Equal(2, bridge.LastAppliedFrameCount);
        Assert.Equal(2, applyCount);
        Assert.Equal((ulong)1001, lastItemKey);
        Assert.Equal((ulong)11, lastSequence);
        Assert.True(lastPayload is not null);
        Assert.Equal((byte)5, lastPayload![0]);
        Assert.Equal(1, bridge.LatestItemCount);
    }

    private static void ClientObserverEventBridge_AppliesIncreasingReliableEventFramesOnly()
    {
        int applyCount = 0;
        ulong lastSequence = 0;
        byte[]? lastPayload = null;

        var bridge = new ClientObserverEventBridge(
            new ConnectionId(9),
            (sequence, payload) =>
            {
                applyCount++;
                lastSequence = sequence;
                lastPayload = payload.Span.ToArray();
            });

        ImmutablePayloadBuffer frame1 = PacketCodec.Encode(new ReliableEventFramePacket(10, ImmutablePayloadBuffer.CopyFrom([1, 2])));
        ImmutablePayloadBuffer frame2 = PacketCodec.Encode(new ReliableEventFramePacket(9, ImmutablePayloadBuffer.CopyFrom([3, 4])));
        ImmutablePayloadBuffer frame3 = PacketCodec.Encode(new ReliableEventFramePacket(11, ImmutablePayloadBuffer.CopyFrom([5, 6])));
        InboundDatagram[] inbound =
        [
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, frame1.Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, frame2.Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, frame3.Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(3, processed);
        Assert.Equal(3, bridge.LastReceivedEventCount);
        Assert.Equal(2, bridge.LastAppliedEventCount);
        Assert.Equal(2, applyCount);
        Assert.Equal((ulong)11, lastSequence);
        Assert.True(lastPayload is not null);
        Assert.Equal((byte)5, lastPayload![0]);
    }

    private static void ClientMovementNetworkBridge_LoopbackRoundTripsMovementInputAndAuthoritativeState()
    {
        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        var bridge = new ClientMovementNetworkBridge(pair.Client, new ConnectionId(1));
        Assert.True(bridge.TrySendInput(
            new ClientMovementInput(300, -200, 3),
            clientTick: 100,
            predictedState: new ClientMovementState(1, 2, 3, 4),
            out ClientMovementInputSendResult sendResult));
        Assert.Equal((ushort)1, sendResult.Sequence);
        pair.Client.FlushOutbound();

        var inbound = new InboundDatagram[4];
        int received = pair.Server.PollInbound(inbound);
        Assert.Equal(1, received);
        Assert.True(PacketCodec.TryDecodeEnvelope(inbound[0].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(MovementInputPacket.PacketId, header.PacketId);
        var reader = new BitReader(payload);
        MovementInputPacket input = MovementInputPacket.Deserialize(ref reader);
        Assert.Equal((ushort)1, input.InputSequence);
        Assert.Equal((uint)100, input.ClientTick);
        Assert.Equal((short)300, input.MoveX);
        Assert.Equal((short)-200, input.MoveY);

        ImmutablePayloadBuffer statePayload = PacketCodec.Encode(new MovementStatePacket(
            NetworkEntityId: 42,
            ServerTick: 200,
            LastProcessedInputSequence: input.InputSequence,
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 1.5f,
            VelocityY: 2.5f));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            MovementStatePacket.Plane,
            MovementStatePacket.PacketClass,
            MovementStatePacket.Channel,
            statePayload));
        pair.Server.FlushOutbound();

        int inboundCount = bridge.PollInbound();
        Assert.Equal(1, inboundCount);
        Assert.Equal((ushort)1, bridge.LastReceiveResult.LastProcessedInputSequence);
        Assert.Equal(new ClientMovementState(10f, 20f, 1.5f, 2.5f), bridge.LastReceiveResult.AuthoritativeState);
        Assert.True(bridge.LastReceiveResult.HadPredictedState);
        Assert.Equal(1, bridge.LastReceiveResult.RemovedPendingInputs);
        Assert.Equal(1, bridge.LastReceiveResult.RemovedPredictions);
        Assert.Equal(0, bridge.Sequencer.PendingCount);
        Assert.Equal(0, bridge.PredictionStore.Count);
        Assert.True(bridge.LastReceiveResult.PositionErrorMagnitude > 0f);
        Assert.True(bridge.CorrectionStats.Snapshot().SampleCount > 0);

        bridge.Interpolation.AddSample(210, new ClientMovementState(20f, 30f, 3f, 4f));
        Assert.True(bridge.TrySampleInterpolatedState(205, static (in ClientMovementState a, in ClientMovementState b, float alpha) =>
        {
            return new ClientMovementState(
                a.PositionX + (b.PositionX - a.PositionX) * alpha,
                a.PositionY + (b.PositionY - a.PositionY) * alpha,
                a.VelocityX + (b.VelocityX - a.VelocityX) * alpha,
                a.VelocityY + (b.VelocityY - a.VelocityY) * alpha);
        }, out ClientMovementState interpolated));

        Assert.Equal(new ClientMovementState(15f, 25f, 2.25f, 3.25f), interpolated);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {name}: {ex}");
            Environment.Exit(1);
        }
    }
}

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
            throw new InvalidOperationException(message ?? "Expected condition to be false.");
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Expected '{expected}' but got '{actual}'.");
    }
}
