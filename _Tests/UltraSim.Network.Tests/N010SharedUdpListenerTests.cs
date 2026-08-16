using System.Net;
using System.Net.Sockets;
using UltraSim.Network;

internal static class N010SharedUdpListenerTests
{
    private static readonly NetworkMessageId s_reliable = new(1);
    private static readonly NetworkMessageId s_latest = new(2);
    private static readonly NetworkProtocolManifest s_manifest = NetworkProtocolManifest.Create(
        8,
        [
            new NetworkMessageDescriptor(s_reliable, "ultrasim.test.shared-control", 1,
                NetworkLane.Control, 32, 0x1010),
            new NetworkMessageDescriptor(s_latest, "ultrasim.test.shared-owner", 1,
                NetworkLane.OwnerState, 32, 0x2020),
        ]);
    private static readonly NetworkProtocolLimits s_protocol = new(
        8, 8, 64, 1, 64, 16, 16, 2, 64, 4, 32, 4);
    private static readonly UdpProtocolLimits s_udp = new(
        512, 16 * 1024, 16 * 1024, 4_096, 30_000, 10_000, 1_000, 5_000);

    internal static void RunAll()
    {
        Run("SharedUdpListenerRoutesIndependentSecureSessions", SharedUdpListenerRoutesIndependentSecureSessions);
        Run("SharedUdpListenerCapacityAndUnknownPathsAreBounded", SharedUdpListenerCapacityAndUnknownPathsAreBounded);
        Run("SharedUdpFailedHandshakeReleasesCapacity", SharedUdpFailedHandshakeReleasesCapacity);
        Run("SharedUdpSessionDrainDoesNotRetirePeerSession", SharedUdpSessionDrainDoesNotRetirePeerSession);
        Run("SharedUdpRetiredCapacityAdvancesGeneration", SharedUdpRetiredCapacityAdvancesGeneration);
        Run("SharedUdpAggregateListenerCapacityPreservesBurst", SharedUdpAggregateListenerCapacityPreservesBurst);
        Run("WarmedSharedUdpDeliveryAllocatesZero", WarmedSharedUdpDeliveryAllocatesZero);
    }

    private static void SharedUdpListenerRoutesIndependentSecureSessions()
    {
        using SharedUdpProtocolHost host = CreateHost(maximumSessions: 2);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? first, out ProtocolHandshakeSnapshot firstHandshake));
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? second, out ProtocolHandshakeSnapshot secondHandshake));
        True(first is not null && second is not null);
        SharedUdpProtocolSession firstSession = first!;
        SharedUdpProtocolSession secondSession = second!;
        True(firstHandshake.Established && secondHandshake.Established);
        False(firstSession.Connection == secondSession.Connection,
            "A shared server connection table reused one live identity.");

        byte[] firstPayload = [1, 2, 3];
        byte[] secondPayload = [9, 8, 7, 6];
        Equal(NetworkQueueStatus.Accepted, firstSession.Client.TryQueue(s_reliable, firstPayload));
        Equal(NetworkQueueStatus.Accepted, secondSession.Server.TryQueue(s_reliable, secondPayload));
        Drive(host, () => firstSession.Server.ReceivedQueued == 1 && secondSession.Client.ReceivedQueued == 1);
        AssertReceive(firstSession.Server, firstPayload);
        AssertReceive(secondSession.Client, secondPayload);
        Drive(host, () => firstSession.Client.ReliableQueued == 0 && secondSession.Server.ReliableQueued == 0);

        SharedUdpProtocolHostSnapshot snapshot = host.GetSnapshot();
        Equal(2, snapshot.Sessions);
        Equal(2, snapshot.Listener.RegisteredPaths);
        Equal(2, snapshot.Connections.Active);
        Equal(4L * (s_udp.SocketSendBufferBytes + s_udp.SocketReceiveBufferBytes),
            snapshot.ConfiguredSocketBufferBytes,
            "The listener must retain the aggregate configured socket capacity that the two server sockets replaced.");
        True(snapshot.Listener.Sent > 0 && snapshot.Listener.Received > 0);
        True(snapshot.Listener.SentBytes > 0 && snapshot.Listener.ReceivedBytes > 0);
    }

    private static void SharedUdpListenerCapacityAndUnknownPathsAreBounded()
    {
        using SharedUdpProtocolHost host = CreateHost(maximumSessions: 1);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? admitted, out _));
        True(admitted is not null);
        SharedUdpProtocolSession admittedSession = admitted!;
        False(host.TryAddLoopbackSession(out SharedUdpProtocolSession? rejected, out _));
        True(rejected is null);
        Equal(1, host.GetSnapshot().Connections.Active);

        using var hostile = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        hostile.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        _ = hostile.SendTo(new byte[96], host.ListenerLocalEndPoint);
        host.Pump();
        SharedUdpProtocolHostSnapshot snapshot = host.GetSnapshot();
        Equal(1UL, snapshot.Listener.UnknownPathDatagrams);
        Equal(0UL, admittedSession.GetSnapshot().Server.AuthenticationFailures,
            "An unknown source path entered an admitted secure peer.");
    }

    private static void SharedUdpSessionDrainDoesNotRetirePeerSession()
    {
        using SharedUdpProtocolHost host = CreateHost(maximumSessions: 2);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? first, out _));
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? second, out _));
        True(first is not null && second is not null);
        SharedUdpProtocolSession firstSession = first!;
        SharedUdpProtocolSession secondSession = second!;
        firstSession.BeginDrain();
        Equal(NetworkQueueStatus.NotAccepting,
            firstSession.Client.TryQueue(s_reliable, stackalloc byte[] { 1 }));
        Drive(host, () => firstSession.State == UdpProtocolSessionState.Closed);
        Equal(UdpProtocolSessionState.Established, secondSession.State);
        Equal(NetworkQueueStatus.Accepted,
            secondSession.Client.TryQueue(s_reliable, stackalloc byte[] { 4, 5 }));
        Drive(host, () => secondSession.Server.ReceivedQueued == 1);
        AssertReceive(secondSession.Server, stackalloc byte[] { 4, 5 });
        firstSession.Dispose();
        Equal(1, host.GetSnapshot().Connections.Active);
    }

    private static void SharedUdpFailedHandshakeReleasesCapacity()
    {
        using NetworkServerIdentity serverIdentity = NetworkServerIdentity.CreateEphemeral();
        using NetworkServerIdentity unrelatedIdentity = NetworkServerIdentity.CreateEphemeral();
        using var host = new SharedUdpProtocolHost(
            s_protocol,
            s_udp with { MaximumHandshakePumps = 64 },
            new SharedUdpProtocolLimits(1, 4, 16),
            s_manifest,
            serverIdentity,
            unrelatedIdentity.PublicKey.ToArray());

        False(host.TryAddLoopbackSession(out SharedUdpProtocolSession? rejected, out _));
        True(rejected is null);
        SharedUdpProtocolHostSnapshot snapshot = host.GetSnapshot();
        Equal(0, snapshot.Sessions);
        Equal(0, snapshot.Listener.RegisteredPaths);
        Equal(0, snapshot.Connections.Active);
        Equal(1, snapshot.Connections.Available);
    }

    private static void WarmedSharedUdpDeliveryAllocatesZero()
    {
        using SharedUdpProtocolHost host = CreateHost(maximumSessions: 4);
        var sessions = new SharedUdpProtocolSession[4];
        for (int index = 0; index < sessions.Length; index++)
        {
            True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _));
            sessions[index] = session!;
        }
        for (int iteration = 0; iteration < 32; iteration++)
            RoundTrip(iteration);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 128; iteration++)
            RoundTrip(iteration);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated,
            "The warmed shared-listener secure-delivery path allocated managed memory.");

        void RoundTrip(int value)
        {
            Span<byte> payload = stackalloc byte[4];
            BitConverter.TryWriteBytes(payload, value);
            for (int index = 0; index < sessions.Length; index++)
                Equal(NetworkQueueStatus.Accepted,
                    sessions[index].Server.TryQueue(s_latest, payload));
            int step = 0;
            while (!AllClientsReceived() && step++ < 50_000) host.Pump();
            True(AllClientsReceived(), "Shared UDP latest state did not converge.");
            for (int index = 0; index < sessions.Length; index++)
            {
                True(sessions[index].Client.TryPeekReceived(out NetworkDeliveredMessage message,
                    out ReadOnlySpan<byte> received));
                Equal(s_latest, message.MessageId);
                True(received.SequenceEqual(payload));
                True(sessions[index].Client.TryConsumeReceived());
            }

            bool AllClientsReceived()
            {
                for (int index = 0; index < sessions.Length; index++)
                    if (sessions[index].Client.ReceivedQueued != 1) return false;
                return true;
            }
        }
    }

    private static void SharedUdpRetiredCapacityAdvancesGeneration()
    {
        using SharedUdpProtocolHost host = CreateHost(maximumSessions: 1);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? first, out _));
        ConnectionId stale = first!.Connection;
        first.Dispose();
        Equal(0, host.SessionCount);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? second, out _));
        True(second is not null);
        Equal(stale.Slot, second!.Connection.Slot);
        True(second.Connection.Generation > stale.Generation);
        False(second.Connection == stale);
    }

    private static void SharedUdpAggregateListenerCapacityPreservesBurst()
    {
        const int sessionCount = 64;
        using SharedUdpProtocolHost host = CreateHost(sessionCount);
        var sessions = new SharedUdpProtocolSession[sessionCount];
        Span<byte> payload = stackalloc byte[2];
        for (int index = 0; index < sessions.Length; index++)
        {
            True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _));
            sessions[index] = session!;
            for (byte message = 0; message < 4; message++)
            {
                payload[0] = (byte)index;
                payload[1] = message;
                Equal(NetworkQueueStatus.Accepted,
                    session!.Client.TryQueue(s_reliable, payload));
            }
        }

        Drive(host, AllDelivered);
        for (int index = 0; index < sessions.Length; index++)
        {
            Equal(0UL, sessions[index].Client.GetSnapshot().ReliableRetransmissions,
                "The shared listener dropped an admitted initial burst that fit the aggregate socket contract.");
            for (byte message = 0; message < 4; message++)
            {
                payload[0] = (byte)index;
                payload[1] = message;
                AssertReceive(sessions[index].Server, payload);
            }
        }

        SharedUdpProtocolHostSnapshot snapshot = host.GetSnapshot();
        Equal(0UL, snapshot.Listener.CapacityDrops);
        Equal(2L * sessionCount *
            (s_udp.SocketSendBufferBytes + s_udp.SocketReceiveBufferBytes),
            snapshot.ConfiguredSocketBufferBytes);

        bool AllDelivered()
        {
            for (int index = 0; index < sessions.Length; index++)
                if (sessions[index].Server.ReceivedQueued != 4) return false;
            return true;
        }
    }

    private static SharedUdpProtocolHost CreateHost(int maximumSessions) => new(
        s_protocol,
        s_udp,
        new SharedUdpProtocolLimits(maximumSessions, 4, checked(maximumSessions * 16)),
        s_manifest);

    private static void Drive(SharedUdpProtocolHost host, Func<bool> done)
    {
        for (int step = 0; step < 50_000 && !done(); step++) host.Pump();
        True(done(), "Shared UDP protocol traffic did not converge.");
    }

    private static void AssertReceive(NetworkProtocolPeer peer, ReadOnlySpan<byte> expected)
    {
        True(peer.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload));
        Equal(s_reliable, message.MessageId);
        True(payload.SequenceEqual(expected));
        True(peer.TryConsumeReceived());
    }

    private static void Run(string name, Action body)
    {
        body();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
    }

    private static void False(bool condition, string? message = null) => True(!condition, message);

    private static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Expected {expected}, got {actual}.");
    }
}
