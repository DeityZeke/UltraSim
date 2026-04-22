using System.Reflection;
using UltraSim.Network;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(NetworkPlane_PreservesExpectedValues), NetworkPlane_PreservesExpectedValues);
        Run(nameof(PacketClass_PreservesExpectedValues), PacketClass_PreservesExpectedValues);
        Run(nameof(NetworkServiceClass_PreservesExpectedValues), NetworkServiceClass_PreservesExpectedValues);
        Run(nameof(ChannelType_PreservesExpectedValues), ChannelType_PreservesExpectedValues);
        Run(nameof(ImmutablePayloadBuffer_CopyFrom_CreatesImmutableSnapshot), ImmutablePayloadBuffer_CopyFrom_CreatesImmutableSnapshot);
        Run(nameof(OutboundTransformContext_PreservesSuppliedFlags), OutboundTransformContext_PreservesSuppliedFlags);
        Run(nameof(INetworkTransport_ExposesExpectedSurface), INetworkTransport_ExposesExpectedSurface);
        Run(nameof(IOutboundTransform_ExposesExplicitTransformHook), IOutboundTransform_ExposesExplicitTransformHook);
        Run(nameof(PacketEnvelopeHeader_RoundTripsExpectedFields), PacketEnvelopeHeader_RoundTripsExpectedFields);
        Run(nameof(PacketCodec_EncodesAndDecodesHandshakeHello), PacketCodec_EncodesAndDecodesHandshakeHello);
        Run(nameof(PacketRegistry_DispatchesTypedPacketAndMetadata), PacketRegistry_DispatchesTypedPacketAndMetadata);
        Run(nameof(HandshakeGate_AcceptsAndRejectsExpectedReasons), HandshakeGate_AcceptsAndRejectsExpectedReasons);
        Run(nameof(NetworkSessionManager_CreatesTransitionsAndDrainsLifecycleEvents), NetworkSessionManager_CreatesTransitionsAndDrainsLifecycleEvents);
        Run(nameof(NetworkSessionManager_AdmissionPolicy_RejectsAndReplacesDuplicates), NetworkSessionManager_AdmissionPolicy_RejectsAndReplacesDuplicates);
        Run(nameof(NetworkSessionManager_SweepTimeouts_DisconnectsExpiredSessions), NetworkSessionManager_SweepTimeouts_DisconnectsExpiredSessions);
        Run(nameof(NetworkSessionManager_MetricsSnapshot_TracksRejectsTimeoutsAndPeak), NetworkSessionManager_MetricsSnapshot_TracksRejectsTimeoutsAndPeak);
        Run(nameof(ControlPlane_LoopbackHandshake_RoundTripsOverTransport), ControlPlane_LoopbackHandshake_RoundTripsOverTransport);
        Run(nameof(ControlPlane_LoopbackHandshake_RejectsInvalidHello), ControlPlane_LoopbackHandshake_RejectsInvalidHello);
        Run(nameof(ControlPlane_AdmissionReject_ReportsReasonAndMetrics), ControlPlane_AdmissionReject_ReportsReasonAndMetrics);
        Run(nameof(ControlPlane_PingProbe_RoundTripsAfterReadyHandshake), ControlPlane_PingProbe_RoundTripsAfterReadyHandshake);
        Run(nameof(LatestStateStream_CoalescesAndDrainsByLatestVersion), LatestStateStream_CoalescesAndDrainsByLatestVersion);
        Run(nameof(LatestStateStream_PartialDrain_AdvancesCursor), LatestStateStream_PartialDrain_AdvancesCursor);
        Run(nameof(OrderedEventStream_DrainsInOrderAndAdvancesCursor), OrderedEventStream_DrainsInOrderAndAdvancesCursor);
        Run(nameof(OrderedEventStream_RequiresRecoveryWhenCursorFallsBehind), OrderedEventStream_RequiresRecoveryWhenCursorFallsBehind);
        Run(nameof(ConnectionStreamScheduler_PrioritizesRealtimeThenReliableWithinBudget), ConnectionStreamScheduler_PrioritizesRealtimeThenReliableWithinBudget);
        Run(nameof(ConnectionStreamScheduler_LeavesCursorsOnUnscheduledWork), ConnectionStreamScheduler_LeavesCursorsOnUnscheduledWork);
        Run(nameof(ConnectionStreamScheduler_SignalsOrderedEventRecovery), ConnectionStreamScheduler_SignalsOrderedEventRecovery);
        Run(nameof(OutboundDatagramAssembler_EncodesScheduledWorkIntoTypedFrames), OutboundDatagramAssembler_EncodesScheduledWorkIntoTypedFrames);
        Run(nameof(OutboundDatagramAssembler_AppliesTransformsAndHeaderFlags), OutboundDatagramAssembler_AppliesTransformsAndHeaderFlags);
        Run(nameof(LoopbackTransportPair_RoundTripsDatagrams), LoopbackTransportPair_RoundTripsDatagrams);
        Run(nameof(LoopbackTransport_Stop_BlocksFurtherPollOutput), LoopbackTransport_Stop_BlocksFurtherPollOutput);

        Console.WriteLine("All Network scaffold tests passed.");
    }

    private static void NetworkPlane_PreservesExpectedValues()
    {
        Assert.Equal(0, (int)NetworkPlane.Control);
        Assert.Equal(1, (int)NetworkPlane.Input);
        Assert.Equal(2, (int)NetworkPlane.State);
    }

    private static void PacketClass_PreservesExpectedValues()
    {
        Assert.Equal(0, (int)PacketClass.Control);
        Assert.Equal(1, (int)PacketClass.Realtime);
        Assert.Equal(2, (int)PacketClass.ReliableEvent);
        Assert.Equal(3, (int)PacketClass.Bulk);
    }

    private static void NetworkServiceClass_PreservesExpectedValues()
    {
        Assert.Equal(0, (int)NetworkServiceClass.A);
        Assert.Equal(1, (int)NetworkServiceClass.B);
        Assert.Equal(2, (int)NetworkServiceClass.C);
        Assert.Equal(3, (int)NetworkServiceClass.D);
    }

    private static void ChannelType_PreservesExpectedValues()
    {
        Assert.Equal(0, (int)ChannelType.ReliableOrdered);
        Assert.Equal(1, (int)ChannelType.ReliableUnordered);
        Assert.Equal(2, (int)ChannelType.UnreliableSequenced);
        Assert.Equal(3, (int)ChannelType.Unreliable);
    }

    private static void ImmutablePayloadBuffer_CopyFrom_CreatesImmutableSnapshot()
    {
        byte[] source = [1, 2, 3];
        ImmutablePayloadBuffer payload = ImmutablePayloadBuffer.CopyFrom(source);

        source[0] = 99;

        Assert.Equal(3, payload.Length);
        Assert.Equal((byte)1, payload.Span[0]);
        Assert.Equal((byte)2, payload.Span[1]);
        Assert.Equal((byte)3, payload.Span[2]);
    }

    private static void OutboundTransformContext_PreservesSuppliedFlags()
    {
        var context = new OutboundTransformContext(
            new ConnectionId(7),
            NetworkPlane.State,
            PacketClass.Bulk,
            ChannelType.ReliableOrdered,
            AllowCompression: true,
            AllowSegmentation: false);

        Assert.Equal(7, context.ConnectionId.Value);
        Assert.Equal(NetworkPlane.State, context.Plane);
        Assert.Equal(PacketClass.Bulk, context.PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, context.Channel);
        Assert.True(context.AllowCompression);
        Assert.False(context.AllowSegmentation);
    }

    private static void INetworkTransport_ExposesExpectedSurface()
    {
        Type contract = typeof(INetworkTransport);

        Assert.True(contract.IsInterface);
        AssertMethod(contract, nameof(INetworkTransport.Start), typeof(void));
        AssertMethod(contract, nameof(INetworkTransport.Stop), typeof(void));
        AssertMethod(contract, nameof(INetworkTransport.PollInbound), typeof(int), typeof(Span<InboundDatagram>));
        AssertMethod(contract, nameof(INetworkTransport.EnqueueOutbound), typeof(void), typeof(OutboundDatagram).MakeByRefType());
        AssertMethod(contract, nameof(INetworkTransport.FlushOutbound), typeof(void));
    }

    private static void IOutboundTransform_ExposesExplicitTransformHook()
    {
        Type contract = typeof(IOutboundTransform);
        Assert.True(contract.IsInterface);
        AssertMethod(
            contract,
            nameof(IOutboundTransform.Transform),
            typeof(OutboundTransformResult),
            typeof(ImmutablePayloadBuffer),
            typeof(PacketHeaderFlags),
            typeof(OutboundTransformContext).MakeByRefType());
    }

    private static void LoopbackTransportPair_RoundTripsDatagrams()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        ImmutablePayloadBuffer payload = ImmutablePayloadBuffer.CopyFrom([7, 8, 9]);
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(42),
            NetworkPlane.Control,
            PacketClass.Control,
            ChannelType.ReliableOrdered,
            payload));
        pair.Server.FlushOutbound();

        var buffer = new InboundDatagram[4];
        int count = pair.Client.PollInbound(buffer);

        Assert.Equal(1, count);
        Assert.Equal(42, buffer[0].ConnectionId.Value);
        Assert.Equal(NetworkPlane.Control, buffer[0].Plane);
        Assert.Equal(PacketClass.Control, buffer[0].PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, buffer[0].Channel);
        Assert.True(buffer[0].Payload.Span.SequenceEqual(payload.Span));
    }

    private static void LoopbackTransport_Stop_BlocksFurtherPollOutput()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(9),
            NetworkPlane.State,
            PacketClass.Realtime,
            ChannelType.UnreliableSequenced,
            ImmutablePayloadBuffer.CopyFrom([1, 2])));
        pair.Server.FlushOutbound();
        pair.Client.Stop();

        var buffer = new InboundDatagram[2];
        Assert.Equal(0, pair.Client.PollInbound(buffer));
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{name} failed: {ex}");
            Environment.Exit(1);
        }
    }

    private static void AssertMethod(Type contract, string name, Type returnType, params Type[] parameterTypes)
    {
        MethodInfo? method = contract.GetMethod(name, parameterTypes);
        Assert.NotNull(method);
        Assert.Equal(returnType, method!.ReturnType);
    }

    private static void PacketEnvelopeHeader_RoundTripsExpectedFields()
    {
        byte[] bytes = new byte[PacketEnvelopeHeader.SizeInBytes];
        var writer = new BitWriter(bytes);
        var header = new PacketEnvelopeHeader(0x1234, PacketHeaderFlags.Compressed | PacketHeaderFlags.HighPriorityHint, 77);
        header.Serialize(ref writer);

        var reader = new BitReader(bytes);
        PacketEnvelopeHeader decoded = PacketEnvelopeHeader.Deserialize(ref reader);

        Assert.Equal((ushort)0x1234, decoded.PacketId);
        Assert.Equal(PacketHeaderFlags.Compressed | PacketHeaderFlags.HighPriorityHint, decoded.Flags);
        Assert.Equal((ushort)77, decoded.PayloadLength);
    }

    private static void PacketCodec_EncodesAndDecodesHandshakeHello()
    {
        var hello = new HandshakeHelloPacket(
            protocolVersion: 7,
            schemaHash: 11,
            contentHash: 13,
            features: FeatureFlags.Compression | FeatureFlags.SharedPayloadFanout);
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(hello, PacketHeaderFlags.None);

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(HandshakeHelloPacket.PacketId, header.PacketId);
        Assert.Equal(PacketHeaderFlags.None, header.Flags);

        var reader = new BitReader(payload);
        HandshakeHelloPacket decoded = HandshakeHelloPacket.Deserialize(ref reader);
        Assert.Equal((uint)7, decoded.ProtocolVersion);
        Assert.Equal((ulong)11, decoded.SchemaHash);
        Assert.Equal((ulong)13, decoded.ContentHash);
        Assert.Equal(FeatureFlags.Compression | FeatureFlags.SharedPayloadFanout, decoded.Features);
    }

    private static void PacketRegistry_DispatchesTypedPacketAndMetadata()
    {
        var registry = new PacketRegistry<List<string>>();
        registry.Register<HandshakeHelloPacket>(static (List<string> messages, in HandshakeHelloPacket packet) =>
        {
            messages.Add($"hello:{packet.ProtocolVersion}:{packet.Features}");
        });
        registry.Seal();

        var hello = new HandshakeHelloPacket(3, 4, 5, FeatureFlags.BulkTransfer);
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(hello);
        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));

        var messages = new List<string>();
        Assert.True(registry.TryDispatch(messages, header, payload));
        Assert.Equal(ChannelType.ReliableOrdered, registry.GetChannel(HandshakeHelloPacket.PacketId));
        Assert.Equal(NetworkPlane.Control, registry.GetPlane(HandshakeHelloPacket.PacketId));
        Assert.Equal(PacketClass.Control, registry.GetPacketClass(HandshakeHelloPacket.PacketId));
        Assert.Equal("hello:3:BulkTransfer", messages[0]);
    }

    private static void HandshakeGate_AcceptsAndRejectsExpectedReasons()
    {
        var policy = new HandshakePolicy(
            ProtocolVersion: 8,
            SchemaHash: 100,
            ContentHash: 200,
            RequiredFeatures: FeatureFlags.SharedPayloadFanout);

        var acceptedHello = new HandshakeHelloPacket(8, 100, 200, FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression);
        HandshakeValidationResult accepted = HandshakeGate.Validate(acceptedHello, policy);
        Assert.True(accepted.Accepted);

        var rejectedHello = new HandshakeHelloPacket(8, 100, 201, FeatureFlags.None);
        HandshakeValidationResult rejected = HandshakeGate.Validate(rejectedHello, policy);
        Assert.False(rejected.Accepted);
        Assert.Equal(HandshakeRejectReason.ContentHashMismatch, rejected.RejectReason);
    }

    private static void NetworkSessionManager_CreatesTransitionsAndDrainsLifecycleEvents()
    {
        var sessions = new NetworkSessionManager();
        SessionId sessionId = sessions.CreateSession(new ConnectionId(5), nowTicks: 10);
        Assert.True(sessions.TryMarkHandshaking(sessionId, 11));
        Assert.True(sessions.TryMarkAuthenticated(sessionId, 12));
        Assert.True(sessions.TryMarkReady(sessionId, 13));
        Assert.True(sessions.TryBeginDisconnect(sessionId, SessionDisconnectReason.Closed, 14));
        Assert.True(sessions.TryCompleteDisconnect(sessionId, 15));

        Assert.True(sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, snapshot.State);
        Assert.Equal(SessionDisconnectReason.Closed, snapshot.DisconnectReason);

        SessionLifecycleEvent[] events = new SessionLifecycleEvent[8];
        int count = sessions.DrainLifecycleEvents(events);
        Assert.Equal(6, count);
        Assert.Equal(SessionLifecycleEventKind.Created, events[0].Kind);
        Assert.Equal(ConnectionSessionState.Connecting, events[0].CurrentState);
        Assert.Equal(ConnectionSessionState.Handshaking, events[1].CurrentState);
        Assert.Equal(ConnectionSessionState.Authenticated, events[2].CurrentState);
        Assert.Equal(ConnectionSessionState.Ready, events[3].CurrentState);
        Assert.Equal(ConnectionSessionState.Disconnecting, events[4].CurrentState);
        Assert.Equal(ConnectionSessionState.Disconnected, events[5].CurrentState);
    }

    private static void NetworkSessionManager_AdmissionPolicy_RejectsAndReplacesDuplicates()
    {
        var rejecting = new NetworkSessionManager(new SessionAdmissionPolicy(
            DuplicateSessionPolicy.RejectNew,
            MaxActiveSessions: 0,
            AuthTimeoutTicks: 0,
            KeepAliveTimeoutTicks: 0));
        SessionId first = rejecting.CreateSession(new ConnectionId(7), 1);
        Assert.False(rejecting.TryCreateSession(new ConnectionId(7), 2, out _, out SessionDisconnectReason duplicateReason));
        Assert.Equal(SessionDisconnectReason.DuplicateSession, duplicateReason);
        Assert.True(rejecting.TryGetSnapshot(first, out SessionSnapshot rejectSnapshot));
        Assert.Equal(ConnectionSessionState.Connecting, rejectSnapshot.State);

        var replacing = new NetworkSessionManager(new SessionAdmissionPolicy(
            DuplicateSessionPolicy.ReplaceExisting,
            MaxActiveSessions: 0,
            AuthTimeoutTicks: 0,
            KeepAliveTimeoutTicks: 0));
        SessionId replaceFirst = replacing.CreateSession(new ConnectionId(8), 1);
        Assert.True(replacing.TryCreateSession(new ConnectionId(8), 2, out SessionId replaceSecond, out SessionDisconnectReason replaceReason));
        Assert.Equal(SessionDisconnectReason.None, replaceReason);
        Assert.False(replacing.TryGetSnapshot(replaceFirst, out _));
        Assert.True(replacing.TryGetSnapshot(replaceSecond, out SessionSnapshot replaceSnapshot));
        Assert.Equal(ConnectionSessionState.Connecting, replaceSnapshot.State);
    }

    private static void NetworkSessionManager_SweepTimeouts_DisconnectsExpiredSessions()
    {
        var sessions = new NetworkSessionManager(new SessionAdmissionPolicy(
            DuplicateSessionPolicy.RejectNew,
            MaxActiveSessions: 0,
            AuthTimeoutTicks: 5,
            KeepAliveTimeoutTicks: 5));
        SessionId authSession = sessions.CreateSession(new ConnectionId(10), 100);
        Assert.True(sessions.TryMarkHandshaking(authSession, 101));

        Assert.Equal(1, sessions.SweepTimeouts(106));
        Assert.True(sessions.TryGetSnapshot(authSession, out SessionSnapshot authSnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, authSnapshot.State);
        Assert.Equal(SessionDisconnectReason.Timeout, authSnapshot.DisconnectReason);

        SessionId readySession = sessions.CreateSession(new ConnectionId(11), 200);
        Assert.True(sessions.TryMarkHandshaking(readySession, 201));
        Assert.True(sessions.TryMarkAuthenticated(readySession, 202));
        Assert.True(sessions.TryMarkReady(readySession, 203));
        Assert.True(sessions.TryTouchActivity(readySession, 204));

        Assert.Equal(1, sessions.SweepTimeouts(209));
        Assert.True(sessions.TryGetSnapshot(readySession, out SessionSnapshot readySnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, readySnapshot.State);
    }

    private static void NetworkSessionManager_MetricsSnapshot_TracksRejectsTimeoutsAndPeak()
    {
        var sessions = new NetworkSessionManager(new SessionAdmissionPolicy(
            DuplicateSessionPolicy.RejectNew,
            MaxActiveSessions: 1,
            AuthTimeoutTicks: 5,
            KeepAliveTimeoutTicks: 5));

        SessionId first = sessions.CreateSession(new ConnectionId(20), 100);
        Assert.False(sessions.TryCreateSession(new ConnectionId(20), 101, out _, out SessionDisconnectReason duplicateReason));
        Assert.Equal(SessionDisconnectReason.DuplicateSession, duplicateReason);
        Assert.True(sessions.TryMarkHandshaking(first, 102));
        Assert.Equal(1, sessions.SweepTimeouts(107));
        Assert.True(sessions.TryRemoveDisconnected(first, 108));

        SessionMetricsSnapshot metrics = sessions.GetMetricsSnapshot();
        Assert.Equal(0, metrics.TotalSessions);
        Assert.Equal(1, metrics.PeakConcurrentSessions);
        Assert.Equal(1, metrics.CreatedSessions);
        Assert.Equal(1, metrics.RejectedSessions);
        Assert.Equal(1, metrics.TimeoutDisconnects);
        Assert.Equal(1, metrics.RemovedSessions);
    }

    private static void ControlPlane_LoopbackHandshake_RoundTripsOverTransport()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var hello = new HandshakeHelloPacket(9, 10, 11, FeatureFlags.Compression);
        var server = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, pair.Server, handshakePolicy: policy);
        var client = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, pair.Client, clientHello: hello);
        server.Start();
        client.Start();

        SessionId clientSession = client.BeginClientHandshake(new ConnectionId(1), nowTicks: 100);
        server.Pump(101);
        client.Pump(102);

        Assert.True(server.Sessions.TryGetSessionIdByConnection(new ConnectionId(1), out SessionId serverSession));
        Assert.True(server.Sessions.TryGetSnapshot(serverSession, out SessionSnapshot serverSnapshot));
        Assert.Equal(ConnectionSessionState.Ready, serverSnapshot.State);

        Assert.True(client.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot clientSnapshot));
        Assert.Equal(ConnectionSessionState.Ready, clientSnapshot.State);
    }

    private static void ControlPlane_LoopbackHandshake_RejectsInvalidHello()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var badHello = new HandshakeHelloPacket(9, 10, 99, FeatureFlags.None);
        var server = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, pair.Server, handshakePolicy: policy);
        var client = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, pair.Client, clientHello: badHello);
        server.Start();
        client.Start();

        SessionId clientSession = client.BeginClientHandshake(new ConnectionId(2), nowTicks: 200);
        server.Pump(201);
        client.Pump(202);

        Assert.True(server.Sessions.TryGetSessionIdByConnection(new ConnectionId(2), out SessionId serverSession));
        Assert.True(server.Sessions.TryGetSnapshot(serverSession, out SessionSnapshot serverSnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, serverSnapshot.State);
        Assert.Equal(SessionDisconnectReason.Rejected, serverSnapshot.DisconnectReason);

        Assert.True(client.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot clientSnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, clientSnapshot.State);
        Assert.Equal(SessionDisconnectReason.Rejected, clientSnapshot.DisconnectReason);

        ControlPlaneEvent[] serverEvents = new ControlPlaneEvent[4];
        int serverEventCount = server.DrainEvents(serverEvents);
        Assert.Equal(1, serverEventCount);
        Assert.Equal(ControlPlaneEventKind.HandshakeRejected, serverEvents[0].Kind);
        Assert.Equal(HandshakeRejectReason.ContentHashMismatch, serverEvents[0].HandshakeRejectReason);
    }

    private static void ControlPlane_AdmissionReject_ReportsReasonAndMetrics()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var hello = new HandshakeHelloPacket(9, 10, 11, FeatureFlags.Compression);
        var sessionPolicy = new SessionAdmissionPolicy(
            DuplicateSessionPolicy.RejectNew,
            MaxActiveSessions: 1,
            AuthTimeoutTicks: 0,
            KeepAliveTimeoutTicks: 0);
        var server = new NetworkControlPlaneHost(
            NetworkControlPlaneRole.Server,
            pair.Server,
            handshakePolicy: policy,
            sessionPolicy: sessionPolicy);
        var client = new NetworkControlPlaneHost(
            NetworkControlPlaneRole.Client,
            pair.Client,
            clientHello: hello);
        server.Start();
        client.Start();

        client.BeginClientHandshake(new ConnectionId(30), 300);
        server.Pump(301);
        client.Pump(302);

        SessionId rejectedClientSession = client.BeginClientHandshake(new ConnectionId(31), 303);
        server.Pump(304);
        client.Pump(305);

        Assert.True(client.Sessions.TryGetSnapshot(rejectedClientSession, out SessionSnapshot rejectedSnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, rejectedSnapshot.State);
        Assert.Equal(SessionDisconnectReason.Rejected, rejectedSnapshot.DisconnectReason);

        ControlPlaneMetricsSnapshot serverMetrics = server.GetMetricsSnapshot();
        Assert.Equal(1, serverMetrics.HandshakeAccepts);
        Assert.Equal(1, serverMetrics.HandshakeRejects);
        Assert.Equal(1, serverMetrics.AdmissionRejects);

        ControlPlaneEvent[] events = new ControlPlaneEvent[4];
        int eventCount = server.DrainEvents(events);
        Assert.Equal(2, eventCount);
        Assert.Equal(ControlPlaneEventKind.HandshakeAccepted, events[0].Kind);
        Assert.Equal(ControlPlaneEventKind.SessionAdmissionRejected, events[1].Kind);
        Assert.Equal(HandshakeRejectReason.ServerBusy, events[1].HandshakeRejectReason);
        Assert.Equal(SessionDisconnectReason.ServerBusy, events[1].DisconnectReason);
    }

    private static void ControlPlane_PingProbe_RoundTripsAfterReadyHandshake()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var hello = new HandshakeHelloPacket(9, 10, 11, FeatureFlags.Compression);
        var server = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, pair.Server, handshakePolicy: policy);
        var client = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, pair.Client, clientHello: hello);
        server.Start();
        client.Start();

        SessionId clientSession = client.BeginClientHandshake(new ConnectionId(3), nowTicks: 300);
        server.Pump(301);
        client.Pump(302);
        Assert.True(client.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot snapshot));
        Assert.Equal(ConnectionSessionState.Ready, snapshot.State);

        Assert.True(client.SendPingProbe(new ConnectionId(3), sequence: 17, clientTick: 303));
        server.Pump(304);
        client.Pump(305);

        Assert.Equal(1, client.ReceivedPongs.Count);
        Assert.Equal((ushort)17, client.ReceivedPongs[0].Sequence);
        Assert.Equal((uint)303, client.ReceivedPongs[0].ClientTick);
        Assert.Equal((uint)304, client.ReceivedPongs[0].ServerTick);

        ControlPlaneMetricsSnapshot serverMetrics = server.GetMetricsSnapshot();
        ControlPlaneMetricsSnapshot clientMetrics = client.GetMetricsSnapshot();
        Assert.Equal(1, serverMetrics.PingProbesReceived);
        Assert.Equal(1, serverMetrics.PongProbesSent);
        Assert.Equal(1, clientMetrics.PingProbesSent);
        Assert.Equal(1, clientMetrics.PongProbesReceived);
    }

    private static void LatestStateStream_CoalescesAndDrainsByLatestVersion()
    {
        var stream = new LatestStateStream();
        stream.Publish(1, ImmutablePayloadBuffer.CopyFrom([1]));
        ulong newerSequence = stream.Publish(1, ImmutablePayloadBuffer.CopyFrom([2]));
        ulong otherSequence = stream.Publish(2, ImmutablePayloadBuffer.CopyFrom([3]));

        LatestStateSnapshot snapshot = stream.GetSnapshot();
        Assert.Equal(2, snapshot.ItemCount);
        Assert.Equal(otherSequence, snapshot.HighestPublishedSequence);

        var updates = new LatestStateUpdate[4];
        LatestStateDrainResult result = stream.DrainSince(default, updates);

        Assert.Equal(2, result.Count);
        Assert.Equal(newerSequence, updates[0].Sequence);
        Assert.Equal((byte)2, updates[0].Payload.Span[0]);
        Assert.Equal((ulong)2, updates[1].ItemKey);
        Assert.Equal((byte)3, updates[1].Payload.Span[0]);
        Assert.Equal(otherSequence, result.NextCursor.LastSeenSequence);
    }

    private static void LatestStateStream_PartialDrain_AdvancesCursor()
    {
        var stream = new LatestStateStream();
        ulong first = stream.Publish(10, ImmutablePayloadBuffer.CopyFrom([10]));
        ulong second = stream.Publish(20, ImmutablePayloadBuffer.CopyFrom([20]));
        ulong third = stream.Publish(30, ImmutablePayloadBuffer.CopyFrom([30]));

        var firstBatch = new LatestStateUpdate[2];
        LatestStateDrainResult firstResult = stream.DrainSince(default, firstBatch);
        Assert.Equal(2, firstResult.Count);
        Assert.Equal(first, firstBatch[0].Sequence);
        Assert.Equal(second, firstBatch[1].Sequence);

        var secondBatch = new LatestStateUpdate[2];
        LatestStateDrainResult secondResult = stream.DrainSince(firstResult.NextCursor, secondBatch);
        Assert.Equal(1, secondResult.Count);
        Assert.Equal(third, secondBatch[0].Sequence);
    }

    private static void OrderedEventStream_DrainsInOrderAndAdvancesCursor()
    {
        var stream = new OrderedEventStream(capacity: 4);
        ulong first = stream.Publish(ImmutablePayloadBuffer.CopyFrom([1]));
        ulong second = stream.Publish(ImmutablePayloadBuffer.CopyFrom([2]));
        ulong third = stream.Publish(ImmutablePayloadBuffer.CopyFrom([3]));

        OrderedEventSnapshot snapshot = stream.GetSnapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(first, snapshot.OldestAvailableSequence);
        Assert.Equal(third, snapshot.NewestAvailableSequence);

        var batch = new OrderedEventEntry[2];
        OrderedEventDrainResult firstDrain = stream.DrainSince(default, batch);
        Assert.Equal(OrderedEventDrainStatus.Success, firstDrain.Status);
        Assert.Equal(2, firstDrain.Count);
        Assert.Equal(first, batch[0].Sequence);
        Assert.Equal(second, batch[1].Sequence);

        var nextBatch = new OrderedEventEntry[2];
        OrderedEventDrainResult secondDrain = stream.DrainSince(firstDrain.NextCursor, nextBatch);
        Assert.Equal(1, secondDrain.Count);
        Assert.Equal(third, nextBatch[0].Sequence);
    }

    private static void OrderedEventStream_RequiresRecoveryWhenCursorFallsBehind()
    {
        var stream = new OrderedEventStream(capacity: 2);
        ulong first = stream.Publish(ImmutablePayloadBuffer.CopyFrom([1]));
        stream.Publish(ImmutablePayloadBuffer.CopyFrom([2]));
        ulong third = stream.Publish(ImmutablePayloadBuffer.CopyFrom([3]));

        var batch = new OrderedEventEntry[2];
        OrderedEventDrainResult drain = stream.DrainSince(new OrderedEventCursor(first), batch);
        Assert.Equal(OrderedEventDrainStatus.RecoveryRequired, drain.Status);
        Assert.Equal(0, drain.Count);
        Assert.Equal(third - 1, drain.NextCursor.NextSequence);
        Assert.Equal(third - 1, drain.OldestAvailableSequence);
        Assert.Equal(third, drain.NewestAvailableSequence);
    }

    private static void ConnectionStreamScheduler_PrioritizesRealtimeThenReliableWithinBudget()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        ulong realtimeSequence = latest.Publish(100, ImmutablePayloadBuffer.CopyFrom([1, 2]));
        ulong eventSequence = ordered.Publish(ImmutablePayloadBuffer.CopyFrom([9]));
        var scheduler = new ConnectionStreamScheduler(latest, ordered);

        var work = new ScheduledWorkItem[4];
        ConnectionScheduleResult result = scheduler.Schedule(
            default,
            new ConnectionSendBudget(
                new PacketClassBudget(MaxItems: 2, MaxPayloadBytes: 8),
                new PacketClassBudget(MaxItems: 2, MaxPayloadBytes: 8)),
            work);

        Assert.Equal(2, result.Count);
        Assert.False(result.OrderedEventRecoveryRequired);
        Assert.Equal(1, result.RealtimeItemsScheduled);
        Assert.Equal(1, result.ReliableEventItemsScheduled);
        Assert.Equal(ScheduledWorkKind.LatestState, work[0].Kind);
        Assert.Equal(PacketClass.Realtime, work[0].PacketClass);
        Assert.Equal(realtimeSequence, work[0].Sequence);
        Assert.Equal((ulong)100, work[0].ItemKey);
        Assert.Equal(ScheduledWorkKind.OrderedEvent, work[1].Kind);
        Assert.Equal(PacketClass.ReliableEvent, work[1].PacketClass);
        Assert.Equal(eventSequence, work[1].Sequence);
    }

    private static void ConnectionStreamScheduler_LeavesCursorsOnUnscheduledWork()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        ulong firstRealtime = latest.Publish(1, ImmutablePayloadBuffer.CopyFrom([1]));
        ulong secondRealtime = latest.Publish(2, ImmutablePayloadBuffer.CopyFrom([2]));
        ulong firstEvent = ordered.Publish(ImmutablePayloadBuffer.CopyFrom([3]));
        var scheduler = new ConnectionStreamScheduler(latest, ordered);

        var work = new ScheduledWorkItem[4];
        ConnectionScheduleResult firstResult = scheduler.Schedule(
            default,
            new ConnectionSendBudget(
                new PacketClassBudget(MaxItems: 1, MaxPayloadBytes: 16),
                new PacketClassBudget(MaxItems: 0, MaxPayloadBytes: 0)),
            work);

        Assert.Equal(1, firstResult.Count);
        Assert.Equal(firstRealtime, firstResult.NextState.LatestStateCursor.LastSeenSequence);
        Assert.Equal(0UL, firstResult.NextState.OrderedEventCursor.NextSequence);

        var nextWork = new ScheduledWorkItem[4];
        ConnectionScheduleResult secondResult = scheduler.Schedule(
            firstResult.NextState,
            new ConnectionSendBudget(
                new PacketClassBudget(MaxItems: 2, MaxPayloadBytes: 16),
                new PacketClassBudget(MaxItems: 1, MaxPayloadBytes: 16)),
            nextWork);

        Assert.Equal(2, secondResult.Count);
        Assert.Equal(secondRealtime, nextWork[0].Sequence);
        Assert.Equal(firstEvent, nextWork[1].Sequence);
    }

    private static void ConnectionStreamScheduler_SignalsOrderedEventRecovery()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 2);
        latest.Publish(10, ImmutablePayloadBuffer.CopyFrom([1]));
        ordered.Publish(ImmutablePayloadBuffer.CopyFrom([2]));
        ordered.Publish(ImmutablePayloadBuffer.CopyFrom([3]));
        ulong newestEvent = ordered.Publish(ImmutablePayloadBuffer.CopyFrom([4]));
        var scheduler = new ConnectionStreamScheduler(latest, ordered);

        var work = new ScheduledWorkItem[4];
        ConnectionScheduleResult result = scheduler.Schedule(
            new ConnectionScheduleState(
                default,
                new OrderedEventCursor(1)),
            ConnectionSendBudget.Unlimited,
            work);

        Assert.True(result.OrderedEventRecoveryRequired);
        Assert.Equal(1, result.Count);
        Assert.Equal(ScheduledWorkKind.LatestState, work[0].Kind);
        Assert.Equal(newestEvent - 1, result.NextState.OrderedEventCursor.NextSequence);
        Assert.Equal(0, result.ReliableEventItemsScheduled);
    }

    private static void OutboundDatagramAssembler_EncodesScheduledWorkIntoTypedFrames()
    {
        var assembler = new OutboundDatagramAssembler();
        ScheduledWorkItem[] items =
        [
            new(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: 10,
                ItemKey: 55,
                Payload: ImmutablePayloadBuffer.CopyFrom([1, 2, 3])),
            new(
                ScheduledWorkKind.OrderedEvent,
                PacketClass.ReliableEvent,
                Sequence: 20,
                ItemKey: 0,
                Payload: ImmutablePayloadBuffer.CopyFrom([9, 8]))
        ];

        var output = new OutboundDatagram[2];
        OutboundAssemblyResult result = assembler.Assemble(new ConnectionId(77), items, output);

        Assert.Equal(2, result.Count);
        Assert.Equal(NetworkPlane.State, output[0].Plane);
        Assert.Equal(PacketClass.Realtime, output[0].PacketClass);
        Assert.Equal(ChannelType.UnreliableSequenced, output[0].Channel);
        Assert.True(PacketCodec.TryDecodeEnvelope(output[0].Payload.Span, out PacketEnvelopeHeader realtimeHeader, out ReadOnlySpan<byte> realtimePayload));
        Assert.Equal(RealtimeStateFramePacket.PacketId, realtimeHeader.PacketId);
        Assert.Equal(PacketHeaderFlags.HighPriorityHint, realtimeHeader.Flags);
        var realtimeReader = new BitReader(realtimePayload);
        RealtimeStateFramePacket realtimeFrame = RealtimeStateFramePacket.Deserialize(ref realtimeReader);
        Assert.Equal((ulong)10, realtimeFrame.Sequence);
        Assert.Equal((ulong)55, realtimeFrame.ItemKey);
        Assert.True(realtimeFrame.Payload.Span.SequenceEqual(new byte[] { 1, 2, 3 }));

        Assert.Equal(PacketClass.ReliableEvent, output[1].PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, output[1].Channel);
        Assert.True(PacketCodec.TryDecodeEnvelope(output[1].Payload.Span, out PacketEnvelopeHeader eventHeader, out ReadOnlySpan<byte> eventPayload));
        Assert.Equal(ReliableEventFramePacket.PacketId, eventHeader.PacketId);
        Assert.Equal(PacketHeaderFlags.None, eventHeader.Flags);
        var eventReader = new BitReader(eventPayload);
        ReliableEventFramePacket eventFrame = ReliableEventFramePacket.Deserialize(ref eventReader);
        Assert.Equal((ulong)20, eventFrame.Sequence);
        Assert.True(eventFrame.Payload.Span.SequenceEqual(new byte[] { 9, 8 }));
    }

    private static void OutboundDatagramAssembler_AppliesTransformsAndHeaderFlags()
    {
        var assembler = new OutboundDatagramAssembler(new MarkCompressedTransform());
        ScheduledWorkItem[] items =
        [
            new(
                ScheduledWorkKind.OrderedEvent,
                PacketClass.ReliableEvent,
                Sequence: 30,
                ItemKey: 0,
                Payload: ImmutablePayloadBuffer.CopyFrom([4, 5]))
        ];

        var output = new OutboundDatagram[1];
        OutboundAssemblyResult result = assembler.Assemble(new ConnectionId(88), items, output);

        Assert.Equal(1, result.Count);
        Assert.Equal(1, result.CompressedCount);
        Assert.True(PacketCodec.TryDecodeEnvelope(output[0].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(PacketHeaderFlags.Compressed, header.Flags);
        var reader = new BitReader(payload);
        ReliableEventFramePacket frame = ReliableEventFramePacket.Deserialize(ref reader);
        Assert.True(frame.Payload.Span.SequenceEqual(new byte[] { 4, 5, 255 }));
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

    public static void NotNull(object? value, string? message = null)
    {
        if (value is null)
            throw new InvalidOperationException(message ?? "Expected value to be non-null.");
    }
}

internal sealed class MarkCompressedTransform : IOutboundTransform
{
    public OutboundTransformResult Transform(ImmutablePayloadBuffer payload, PacketHeaderFlags headerFlags, in OutboundTransformContext context)
    {
        byte[] data = new byte[payload.Length + 1];
        payload.Span.CopyTo(data);
        data[^1] = 255;
        return new OutboundTransformResult(
            ImmutablePayloadBuffer.FromOwned(data),
            headerFlags | PacketHeaderFlags.Compressed);
    }
}
