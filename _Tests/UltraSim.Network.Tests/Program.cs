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
