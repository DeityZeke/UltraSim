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
        Run(nameof(ControlPlane_LoopbackHandshake_RoundTripsOverTransport), ControlPlane_LoopbackHandshake_RoundTripsOverTransport);
        Run(nameof(ControlPlane_LoopbackHandshake_RejectsInvalidHello), ControlPlane_LoopbackHandshake_RejectsInvalidHello);
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
            typeof(ImmutablePayloadBuffer),
            typeof(ImmutablePayloadBuffer),
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
