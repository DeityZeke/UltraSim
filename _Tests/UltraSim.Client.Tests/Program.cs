using UltraSim.Client;
using UltraSim.Network;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(ClientConnectionState_PreservesExpectedValues), ClientConnectionState_PreservesExpectedValues);
        Run(nameof(ClientInputSequencer_QueuesCopiesAndAcknowledgesInputs), ClientInputSequencer_QueuesCopiesAndAcknowledgesInputs);
        Run(nameof(ClientInputFocusState_BlocksMovementForTextMenusAndExplicitCapture), ClientInputFocusState_BlocksMovementForTextMenusAndExplicitCapture);
        Run(nameof(ClientAppShellNavigation_NavigatesBackAndTogglesSessionMenu), ClientAppShellNavigation_NavigatesBackAndTogglesSessionMenu);
        Run(nameof(ClientDirectConnectRequest_ValidatesAndTrimsEndpointFields), ClientDirectConnectRequest_ValidatesAndTrimsEndpointFields);
        Run(nameof(ClientPredictionStore_RecordsLooksUpAndRemovesAcknowledgedStates), ClientPredictionStore_RecordsLooksUpAndRemovesAcknowledgedStates);
        Run(nameof(ClientInterpolationBuffer_SamplesInterpolatedAndLatestState), ClientInterpolationBuffer_SamplesInterpolatedAndLatestState);
        Run(nameof(MovementPackets_RoundTripExpectedFields), MovementPackets_RoundTripExpectedFields);
        Run(nameof(ClientObserverStateBridge_AppliesNewestRealtimeFramesOnly), ClientObserverStateBridge_AppliesNewestRealtimeFramesOnly);
        Run(nameof(ClientObserverStateBridge_AppliesRealtimeStateBatchFrames), ClientObserverStateBridge_AppliesRealtimeStateBatchFrames);
        Run(nameof(ClientObserverStateBridge_AppliesFullBaselineAndResetsLocalState), ClientObserverStateBridge_AppliesFullBaselineAndResetsLocalState);
        Run(nameof(ClientObserverStateBridge_AppliesDeltaFramesAfterCompletedBaseline), ClientObserverStateBridge_AppliesDeltaFramesAfterCompletedBaseline);
        Run(nameof(ClientObserverEventBridge_AppliesIncreasingReliableEventFramesOnly), ClientObserverEventBridge_AppliesIncreasingReliableEventFramesOnly);
        Run(nameof(ClientObserverEventBridge_BuffersOutOfOrderFramesUntilGapCloses), ClientObserverEventBridge_BuffersOutOfOrderFramesUntilGapCloses);
        Run(nameof(ClientObserverEventBridge_CountsDroppedOutOfOrderFramesWhenBufferIsFull), ClientObserverEventBridge_CountsDroppedOutOfOrderFramesWhenBufferIsFull);
        Run(nameof(ClientObserverEventBridge_AllowSequenceGaps_AppliesScopedFanoutGaps), ClientObserverEventBridge_AllowSequenceGaps_AppliesScopedFanoutGaps);
        Run(nameof(ClientObserverEventBridge_RoutesNestedChatEventsBeforeGenericApply), ClientObserverEventBridge_RoutesNestedChatEventsBeforeGenericApply);
        Run(nameof(ClientMovementNetworkBridge_LoopbackRoundTripsMovementInputAndAuthoritativeState), ClientMovementNetworkBridge_LoopbackRoundTripsMovementInputAndAuthoritativeState);
        Run(nameof(ClientMovementSessionHost_Pump_EmitsReliableEventAckForContiguousObserverEvents), ClientMovementSessionHost_Pump_EmitsReliableEventAckForContiguousObserverEvents);
        Run(nameof(ClientMovementSessionHost_Pump_EmitsStateBaselineAckWhenBaselineCompletes), ClientMovementSessionHost_Pump_EmitsStateBaselineAckWhenBaselineCompletes);
        Run(nameof(ClientMovementSessionHost_BaselineCompletion_ResetsObserverEventSequenceFloor), ClientMovementSessionHost_BaselineCompletion_ResetsObserverEventSequenceFloor);
        Run(nameof(ClientMovementSessionHost_Pump_AggregatesObserverCountsAcrossPollBatches), ClientMovementSessionHost_Pump_AggregatesObserverCountsAcrossPollBatches);

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

    private static void ClientInputFocusState_BlocksMovementForTextMenusAndExplicitCapture()
    {
        Assert.False(new ClientInputFocusState(
            TextInputFocused: false,
            MenuOpen: false,
            ModalInputCaptured: false,
            ExplicitGameplayBlock: false).BlocksGameplayMovement);

        Assert.True(new ClientInputFocusState(
            TextInputFocused: true,
            MenuOpen: false,
            ModalInputCaptured: false,
            ExplicitGameplayBlock: false).BlocksGameplayMovement);

        Assert.True(new ClientInputFocusState(
            TextInputFocused: false,
            MenuOpen: true,
            ModalInputCaptured: false,
            ExplicitGameplayBlock: false).BlocksGameplayMovement);

        Assert.True(new ClientInputFocusState(
            TextInputFocused: false,
            MenuOpen: false,
            ModalInputCaptured: true,
            ExplicitGameplayBlock: false).BlocksGameplayMovement);

        Assert.True(new ClientInputFocusState(
            TextInputFocused: false,
            MenuOpen: false,
            ModalInputCaptured: false,
            ExplicitGameplayBlock: true).BlocksGameplayMovement);
    }

    private static void ClientAppShellNavigation_NavigatesBackAndTogglesSessionMenu()
    {
        var navigation = new ClientAppShellNavigation();

        Assert.Equal(ClientAppScreen.Start, navigation.CurrentScreen);
        Assert.True(navigation.IsMenuVisible);

        navigation.NavigateTo(ClientAppScreen.ServerBrowser);
        navigation.NavigateTo(ClientAppScreen.DirectConnect);
        Assert.Equal(ClientAppScreen.DirectConnect, navigation.CurrentScreen);
        Assert.Equal(2, navigation.BackStackCount);

        Assert.True(navigation.TryGoBack());
        Assert.Equal(ClientAppScreen.ServerBrowser, navigation.CurrentScreen);

        navigation.EnterSession();
        Assert.True(navigation.IsInSession);
        Assert.False(navigation.IsMenuVisible);
        Assert.Equal(ClientAppScreen.SessionStatus, navigation.CurrentScreen);

        navigation.ShowInSessionOptions();
        Assert.True(navigation.IsMenuVisible);
        Assert.Equal(ClientAppScreen.InSessionOptions, navigation.CurrentScreen);

        navigation.HideMenuIfInSession();
        Assert.False(navigation.IsMenuVisible);

        navigation.LeaveSession();
        Assert.False(navigation.IsInSession);
        Assert.True(navigation.IsMenuVisible);
        Assert.Equal(ClientAppScreen.Disconnected, navigation.CurrentScreen);
    }

    private static void ClientDirectConnectRequest_ValidatesAndTrimsEndpointFields()
    {
        Assert.True(ClientDirectConnectRequest.TryCreate(
            " 127.0.0.1 ",
            "42042",
            out ClientDirectConnectRequest request,
            out string error));
        Assert.Equal("127.0.0.1", request.Host);
        Assert.Equal(42042, request.Port);
        Assert.Equal(string.Empty, error);
        Assert.True(request.TryResolve(out System.Net.IPEndPoint endPoint, out error));
        Assert.Equal(42042, endPoint.Port);

        Assert.False(ClientDirectConnectRequest.TryCreate("", "42042", out _, out error));
        Assert.Equal("Host is required.", error);

        Assert.False(ClientDirectConnectRequest.TryCreate("127.0.0.1", "99999", out _, out error));
        Assert.Equal("Port must be between 0 and 65535.", error);
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

    private static void ClientObserverStateBridge_AppliesRealtimeStateBatchFrames()
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

        RealtimeStateBatchItem[] items =
        [
            new(10, 1001, ImmutablePayloadBuffer.CopyFrom([1, 2])),
            new(11, 1002, ImmutablePayloadBuffer.CopyFrom([3, 4]))
        ];
        ImmutablePayloadBuffer batch = PacketCodec.Encode(new RealtimeStateBatchPacket(items));
        InboundDatagram[] inbound =
        [
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Realtime, ChannelType.UnreliableSequenced, batch.Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(1, processed);
        Assert.Equal(2, bridge.LastReceivedFrameCount);
        Assert.Equal(2, bridge.LastAppliedFrameCount);
        Assert.Equal(2, applyCount);
        Assert.Equal((ulong)1002, lastItemKey);
        Assert.Equal((ulong)11, lastSequence);
        Assert.True(lastPayload is not null);
        Assert.Equal((byte)3, lastPayload![0]);
        Assert.Equal(2, bridge.LatestItemCount);
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
        Assert.True(bridge.TryDequeueAckPacket(out ReliableEventAckPacket ack));
        Assert.Equal((ulong)11, ack.Sequence);
        Assert.False(bridge.TryDequeueAckPacket(out _));
    }

    private static void ClientObserverStateBridge_AppliesFullBaselineAndResetsLocalState()
    {
        int applyCount = 0;
        int resetCount = 0;
        ulong lastItemKey = 0;
        ulong lastSequence = 0;
        var bridge = new ClientObserverStateBridge(
            new ConnectionId(7),
            (itemKey, sequence, _) =>
            {
                applyCount++;
                lastItemKey = itemKey;
                lastSequence = sequence;
            },
            () => resetCount++);

        InboundDatagram[] inbound =
        [
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineBeginPacket(new StateBaselineDescriptor(5, 22, 41, 2))).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineFramePacket(5, 1001, 21, ImmutablePayloadBuffer.CopyFrom([1]))).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineFramePacket(5, 1002, 22, ImmutablePayloadBuffer.CopyFrom([2]))).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineCompletePacket(5)).Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(4, processed);
        Assert.Equal(1, resetCount);
        Assert.Equal(2, bridge.LastReceivedBaselineFrameCount);
        Assert.Equal(2, bridge.LastAppliedBaselineFrameCount);
        Assert.Equal(2, applyCount);
        Assert.Equal((ulong)1002, lastItemKey);
        Assert.Equal((ulong)22, lastSequence);
        Assert.Equal(2, bridge.LatestItemCount);
        Assert.True(bridge.TryDequeueCompletedBaseline(out StateBaselineDescriptor baseline));
        Assert.Equal((uint)5, baseline.BaselineId);
        Assert.Equal((ulong)41, baseline.LastOrderedEventSequence);
        Assert.False(bridge.TryDequeueCompletedBaseline(out _));
    }

    private static void ClientObserverStateBridge_AppliesDeltaFramesAfterCompletedBaseline()
    {
        int applyCount = 0;
        ulong lastItemKey = 0;
        ulong lastSequence = 0;
        var bridge = new ClientObserverStateBridge(
            new ConnectionId(7),
            (itemKey, sequence, _) =>
            {
                applyCount++;
                lastItemKey = itemKey;
                lastSequence = sequence;
            });

        InboundDatagram[] inbound =
        [
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineBeginPacket(new StateBaselineDescriptor(5, 22, 41, 1))).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineFramePacket(5, 1001, 21, ImmutablePayloadBuffer.CopyFrom([1]))).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineCompletePacket(5)).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateDeltaFramePacket(5, 1001, 23, ImmutablePayloadBuffer.CopyFrom([2]))).Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(4, processed);
        Assert.Equal(1, bridge.LastReceivedBaselineFrameCount);
        Assert.Equal(1, bridge.LastAppliedBaselineFrameCount);
        Assert.Equal(1, bridge.LastReceivedDeltaFrameCount);
        Assert.Equal(1, bridge.LastAppliedDeltaFrameCount);
        Assert.Equal(2, applyCount);
        Assert.Equal((ulong)1001, lastItemKey);
        Assert.Equal((ulong)23, lastSequence);
        Assert.Equal(1, bridge.LatestItemCount);
    }

    private static void ClientObserverEventBridge_BuffersOutOfOrderFramesUntilGapCloses()
    {
        var applied = new List<ulong>();
        var bridge = new ClientObserverEventBridge(
            new ConnectionId(9),
            (sequence, _) => applied.Add(sequence));

        InboundDatagram[] inbound =
        [
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(10, ImmutablePayloadBuffer.CopyFrom([1]))).Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(12, ImmutablePayloadBuffer.CopyFrom([2]))).Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(11, ImmutablePayloadBuffer.CopyFrom([3]))).Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(3, processed);
        Assert.Equal(3, bridge.LastReceivedEventCount);
        Assert.Equal(3, bridge.LastAppliedEventCount);
        Assert.Equal(0, bridge.BufferedEventCount);
        Assert.SequenceEqual([10UL, 11UL, 12UL], applied);
        Assert.True(bridge.TryDequeueAckPacket(out ReliableEventAckPacket ack));
        Assert.Equal((ulong)12, ack.Sequence);
    }

    private static void ClientObserverEventBridge_CountsDroppedOutOfOrderFramesWhenBufferIsFull()
    {
        var bridge = new ClientObserverEventBridge(new ConnectionId(9), maxBufferedEvents: 1);

        InboundDatagram[] inbound =
        [
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(10, ImmutablePayloadBuffer.CopyFrom([1]))).Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(12, ImmutablePayloadBuffer.CopyFrom([2]))).Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(14, ImmutablePayloadBuffer.CopyFrom([3]))).Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(3, processed);
        Assert.Equal(1, bridge.BufferedEventCount);
        Assert.Equal(1L, bridge.DroppedBufferedEventCount);
    }

    private static void ClientObserverEventBridge_AllowSequenceGaps_AppliesScopedFanoutGaps()
    {
        var applied = new List<ulong>();
        var bridge = new ClientObserverEventBridge(
            new ConnectionId(9),
            (sequence, _) => applied.Add(sequence))
        {
            AllowSequenceGaps = true,
        };

        InboundDatagram[] inbound =
        [
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(10, ImmutablePayloadBuffer.CopyFrom([1]))).Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(12, ImmutablePayloadBuffer.CopyFrom([2]))).Memory),
        ];

        Assert.Equal(2, bridge.ProcessInbound(inbound));
        Assert.Equal(2, bridge.LastAppliedEventCount);
        Assert.Equal(0, bridge.BufferedEventCount);
        Assert.SequenceEqual([10UL, 12UL], applied);
    }

    private static void ClientObserverEventBridge_RoutesNestedChatEventsBeforeGenericApply()
    {
        ChatMessageEventPacket? applied = null;
        int genericApplyCount = 0;
        var chat = new ClientChatEventBridge(packet => applied = packet);
        var bridge = ClientObserverEventBridge.CreateSpanFastPath(
            new ConnectionId(9),
            (_, _) => genericApplyCount++);
        bridge.ChatEvents = chat;

        ImmutablePayloadBuffer chatPayload = PacketCodec.Encode(new ChatMessageEventPacket(
            MessageId: 7,
            ChannelId: 1,
            SenderEntityId: 22,
            SenderName: "Player 9",
            Text: "hello",
            ServerTick: 123,
            Flags: ChatMessageFlags.Local | ChatMessageFlags.Overhead));
        ImmutablePayloadBuffer frame = PacketCodec.Encode(new ReliableEventFramePacket(10, chatPayload));
        InboundDatagram[] inbound =
        [
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, frame.Memory),
        ];

        Assert.Equal(1, bridge.ProcessInbound(inbound));
        Assert.Equal(1, chat.LastReceivedMessageCount);
        Assert.Equal(1, chat.LastAppliedMessageCount);
        Assert.Equal(7UL, chat.LastMessageId);
        Assert.True(applied.HasValue);
        Assert.Equal("hello", applied.GetValueOrDefault().Text);
        Assert.Equal(0, genericApplyCount);
    }

    private static void ClientMovementSessionHost_Pump_EmitsReliableEventAckForContiguousObserverEvents()
    {
        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        var observerEvents = new ClientObserverEventBridge(new ConnectionId(1));
        var host = new ClientMovementSessionHost(
            pair.Client,
            new ConnectionId(1),
            default,
            observerEvents: observerEvents,
            maxPollBatch: 4);

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketCodec.Encode(new ReliableEventFramePacket(20, ImmutablePayloadBuffer.CopyFrom([5, 6])))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketCodec.Encode(new ReliableEventFramePacket(21, ImmutablePayloadBuffer.CopyFrom([7, 8])))));
        pair.Server.FlushOutbound();

        int inboundCount = host.Pump(1000);
        Assert.Equal(2, inboundCount);
        Assert.Equal((ulong)21, observerEvents.LastAppliedSequence);

        var ackInbound = new InboundDatagram[4];
        int ackCount = pair.Server.PollInbound(ackInbound);
        Assert.Equal(1, ackCount);
        Assert.True(PacketCodec.TryDecodeEnvelope(ackInbound[0].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(ReliableEventAckPacket.PacketId, header.PacketId);
        var reader = new BitReader(payload);
        ReliableEventAckPacket ack = ReliableEventAckPacket.Deserialize(ref reader);
        Assert.Equal((ulong)21, ack.Sequence);
    }

    private static void ClientMovementSessionHost_Pump_EmitsStateBaselineAckWhenBaselineCompletes()
    {
        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        var observerState = new ClientObserverStateBridge(new ConnectionId(1));
        var host = new ClientMovementSessionHost(
            pair.Client,
            new ConnectionId(1),
            default,
            observerState: observerState,
            maxPollBatch: 8);

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineBeginPacket.Plane,
            StateBaselineBeginPacket.PacketClass,
            StateBaselineBeginPacket.Channel,
            PacketCodec.Encode(new StateBaselineBeginPacket(new StateBaselineDescriptor(9, 50, 42, 1)))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineFramePacket.Plane,
            StateBaselineFramePacket.PacketClass,
            StateBaselineFramePacket.Channel,
            PacketCodec.Encode(new StateBaselineFramePacket(9, 1001, 50, ImmutablePayloadBuffer.CopyFrom([2])))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineCompletePacket.Plane,
            StateBaselineCompletePacket.PacketClass,
            StateBaselineCompletePacket.Channel,
            PacketCodec.Encode(new StateBaselineCompletePacket(9))));
        pair.Server.FlushOutbound();

        Assert.Equal(3, host.Pump(1000));

        var ackInbound = new InboundDatagram[4];
        int ackCount = pair.Server.PollInbound(ackInbound);
        Assert.Equal(1, ackCount);
        Assert.True(PacketCodec.TryDecodeEnvelope(ackInbound[0].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(StateBaselineAckPacket.PacketId, header.PacketId);
        var reader = new BitReader(payload);
        StateBaselineAckPacket ack = StateBaselineAckPacket.Deserialize(ref reader);
        Assert.Equal((uint)9, ack.Descriptor.BaselineId);
        Assert.Equal((ulong)50, ack.Descriptor.HighestLatestStateSequence);
        Assert.Equal((ulong)42, ack.Descriptor.LastOrderedEventSequence);
    }

    private static void ClientMovementSessionHost_BaselineCompletion_ResetsObserverEventSequenceFloor()
    {
        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        var appliedEvents = new List<ulong>();
        var observerState = new ClientObserverStateBridge(new ConnectionId(1));
        var observerEvents = new ClientObserverEventBridge(
            new ConnectionId(1),
            (sequence, _) => appliedEvents.Add(sequence));
        var host = new ClientMovementSessionHost(
            pair.Client,
            new ConnectionId(1),
            default,
            observerState: observerState,
            observerEvents: observerEvents,
            maxPollBatch: 8);

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketCodec.Encode(new ReliableEventFramePacket(10, ImmutablePayloadBuffer.CopyFrom([1])))));
        pair.Server.FlushOutbound();
        Assert.Equal(1, host.Pump(1000));
        Assert.Equal((ulong)10, observerEvents.LastAppliedSequence);

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineBeginPacket.Plane,
            StateBaselineBeginPacket.PacketClass,
            StateBaselineBeginPacket.Channel,
            PacketCodec.Encode(new StateBaselineBeginPacket(new StateBaselineDescriptor(9, 50, 42, 1)))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineFramePacket.Plane,
            StateBaselineFramePacket.PacketClass,
            StateBaselineFramePacket.Channel,
            PacketCodec.Encode(new StateBaselineFramePacket(9, 1001, 50, ImmutablePayloadBuffer.CopyFrom([2])))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineCompletePacket.Plane,
            StateBaselineCompletePacket.PacketClass,
            StateBaselineCompletePacket.Channel,
            PacketCodec.Encode(new StateBaselineCompletePacket(9))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketCodec.Encode(new ReliableEventFramePacket(43, ImmutablePayloadBuffer.CopyFrom([3])))));
        pair.Server.FlushOutbound();

        Assert.Equal(4, host.Pump(1002));
        Assert.Equal((ulong)43, observerEvents.LastAppliedSequence);
        Assert.SequenceEqual([10UL, 43UL], appliedEvents);
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

    private static void ClientMovementSessionHost_Pump_AggregatesObserverCountsAcrossPollBatches()
    {
        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        int latestApplyCount = 0;
        int eventApplyCount = 0;
        var observerState = new ClientObserverStateBridge(
            new ConnectionId(1),
            (_, _, _) => latestApplyCount++);
        var observerEvents = new ClientObserverEventBridge(
            new ConnectionId(1),
            (_, _) => eventApplyCount++);
        var host = new ClientMovementSessionHost(
            pair.Client,
            new ConnectionId(1),
            default,
            observerState: observerState,
            observerEvents: observerEvents,
            maxPollBatch: 2);

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            MovementStatePacket.Plane,
            MovementStatePacket.PacketClass,
            MovementStatePacket.Channel,
            PacketCodec.Encode(new MovementStatePacket(42, 100, 1, 1f, 2f, 3f, 4f))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            RealtimeStateFramePacket.Plane,
            RealtimeStateFramePacket.PacketClass,
            RealtimeStateFramePacket.Channel,
            PacketCodec.Encode(new RealtimeStateFramePacket(10, 1001, ImmutablePayloadBuffer.CopyFrom([1, 2])))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            RealtimeStateFramePacket.Plane,
            RealtimeStateFramePacket.PacketClass,
            RealtimeStateFramePacket.Channel,
            PacketCodec.Encode(new RealtimeStateFramePacket(11, 1002, ImmutablePayloadBuffer.CopyFrom([3, 4])))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketCodec.Encode(new ReliableEventFramePacket(20, ImmutablePayloadBuffer.CopyFrom([5, 6])))));
        pair.Server.FlushOutbound();

        int inboundCount = host.Pump(1000);

        Assert.Equal(4, inboundCount);
        Assert.Equal(2, observerState.LastReceivedFrameCount);
        Assert.Equal(2, observerState.LastAppliedFrameCount);
        Assert.Equal(2, latestApplyCount);
        Assert.Equal(1, observerEvents.LastReceivedEventCount);
        Assert.Equal(1, observerEvents.LastAppliedEventCount);
        Assert.Equal(1, eventApplyCount);
        Assert.Equal((ushort)1, host.Movement.LastReceiveResult.LastProcessedInputSequence);
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

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string? message = null)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(message ?? $"Expected sequence count {expected.Count}, got {actual.Count}.");

        for (int i = 0; i < expected.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                throw new InvalidOperationException(message ?? $"Sequence mismatch at {i}: expected '{expected[i]}', got '{actual[i]}'.");
        }
    }
}
