using System.Net;
using System.Net.Sockets;
using UltraSim.Network;

internal static class N05UdpTransportTests
{
    private static readonly NetworkProtocolLimits s_protocol = new(
        16, 16, 4_096, 32, 256, 16, 16, 2, 64, 4, 32);
    private static readonly UdpProtocolLimits s_udp = new(
        512, 64 * 1024, 64 * 1024, 4_096, 30_000, 5_000, 1_000, 1_000);
    private static readonly NetworkMessageId s_probe = new(1);

    internal static void RunAll()
    {
        Run("RealUdpHandshakeAndReliableDelivery", RealUdpHandshakeAndReliableDelivery);
        Run("UdpPinnedIdentityAndConnectedPathRejectSpoofing", UdpPinnedIdentityAndConnectedPathRejectSpoofing);
        Run("UdpDrainPreservesAdmittedReliableDelivery", UdpDrainPreservesAdmittedReliableDelivery);
        Run("UdpAbruptPeerLossAndOutstandingShutdownAreBalanced", UdpAbruptPeerLossAndOutstandingShutdownAreBalanced);
        Run("FreshUdpReconnectOwnsFreshPathAndSecurity", FreshUdpReconnectOwnsFreshPathAndSecurity);
        Run("UdpPreAuthenticationFailureDoesNotLeakCapacity", UdpPreAuthenticationFailureDoesNotLeakCapacity);
        Run("WarmedUdpSecureDeliveryAllocatesZero", WarmedUdpSecureDeliveryAllocatesZero);
    }

    private static void RealUdpHandshakeAndReliableDelivery()
    {
        using UdpProtocolSession session = Create();
        True(session.Handshake.Established);
        True(session.Handshake.CookieChallenges > 0);
        True(session.Handshake.CookieValidations > 0);
        byte[] clientPayload = { 1, 2, 3, 4 };
        byte[] serverPayload = { 9, 8, 7 };
        Equal(NetworkQueueStatus.Accepted, session.Client.TryQueue(s_probe, clientPayload));
        Equal(NetworkQueueStatus.Accepted, session.Server.TryQueue(s_probe, serverPayload));
        Drive(session, () => session.Client.ReceivedQueued == 1 && session.Server.ReceivedQueued == 1);
        AssertReceive(session.Server, clientPayload);
        AssertReceive(session.Client, serverPayload);
        Drive(session, () => session.Client.ReliableQueued == 0 && session.Server.ReliableQueued == 0);
        UdpProtocolSessionSnapshot snapshot = session.GetSnapshot();
        True(snapshot.ClientDatagrams.Sent > 0 && snapshot.ServerDatagrams.Sent > 0);
        True(snapshot.ClientDatagrams.Received > 0 && snapshot.ServerDatagrams.Received > 0);
        Equal(UdpProtocolSessionState.Established, snapshot.State);
    }

    private static void UdpPinnedIdentityAndConnectedPathRejectSpoofing()
    {
        using var serverIdentity = NetworkServerIdentity.CreateEphemeral();
        using var wrongIdentity = NetworkServerIdentity.CreateEphemeral();
        False(UdpProtocolSession.TryCreate(
            s_protocol, s_udp, out UdpProtocolSession? rejected, out _,
            CoreNetworkProtocol.Manifest, serverIdentity,
            wrongIdentity.PublicKey.ToArray()));
        True(rejected is null);

        using UdpProtocolSession session = Create();
        UdpDatagramEndpointSnapshot before = session.GetSnapshot().ServerDatagrams;
        using var spoof = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        spoof.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        byte[] hostile = Enumerable.Repeat((byte)0xA5, 96).ToArray();
        _ = spoof.SendTo(hostile, session.ServerLocalEndPoint);
        for (int index = 0; index < 32; index++) session.Pump();
        UdpDatagramEndpointSnapshot after = session.GetSnapshot().ServerDatagrams;
        Equal(before.Received, after.Received,
            "A datagram outside the connected client path entered protocol receive state.");
        Equal(0UL, session.GetSnapshot().Server.AuthenticationFailures);
    }

    private static void UdpDrainPreservesAdmittedReliableDelivery()
    {
        using UdpProtocolSession session = Create();
        byte[] payload = { 4, 5, 6 };
        Equal(NetworkQueueStatus.Accepted, session.Server.TryQueue(s_probe, payload));
        session.BeginDrain();
        Drive(session, () => session.State == UdpProtocolSessionState.Closed);
        AssertReceive(session.Client, payload);
        Equal(0, session.Server.ReliableQueued);
    }

    private static void UdpAbruptPeerLossAndOutstandingShutdownAreBalanced()
    {
        UdpProtocolSession abrupt = Create();
        abrupt.AbortClientTransportForTesting();
        abrupt.Pump();
        Equal(UdpProtocolSessionState.Closed, abrupt.State);
        abrupt.Dispose();
        abrupt.Dispose();

        UdpProtocolSession serverLoss = Create();
        serverLoss.AbortServerTransportForTesting();
        serverLoss.Pump();
        Equal(UdpProtocolSessionState.Closed, serverLoss.State);
        serverLoss.Dispose();

        byte[] payload = { 1, 2, 3 };
        for (int iteration = 0; iteration < 32; iteration++)
        {
            UdpProtocolSession session = Create();
            Equal(NetworkQueueStatus.Accepted,
                session.Client.TryQueue(s_probe, payload));
            session.Dispose();
            session.Dispose();
        }
    }

    private static void FreshUdpReconnectOwnsFreshPathAndSecurity()
    {
        IPEndPoint oldClient;
        IPEndPoint oldServer;
        ConnectionId oldConnection;
        using (UdpProtocolSession first = Create())
        {
            oldClient = first.ClientLocalEndPoint;
            oldServer = first.ServerLocalEndPoint;
            oldConnection = first.Connection;
        }
        using UdpProtocolSession second = Create();
        True(second.ClientLocalEndPoint.Port != oldClient.Port ||
            second.ServerLocalEndPoint.Port != oldServer.Port,
            "A reconnect unexpectedly revived both retired UDP path endpoints.");
        // Connection IDs are topology-local and may share the same numeric slot;
        // fresh path binding and independently derived secrets fence the lifetime.
        True(oldConnection.IsValid && second.Connection.IsValid);
        Equal(NetworkQueueStatus.Accepted,
            second.Client.TryQueue(s_probe, stackalloc byte[] { 7 }));
        Drive(second, () => second.Server.ReceivedQueued == 1);
        AssertReceive(second.Server, stackalloc byte[] { 7 });
    }

    private static void UdpPreAuthenticationFailureDoesNotLeakCapacity()
    {
        using var serverIdentity = NetworkServerIdentity.CreateEphemeral();
        using var wrongIdentity = NetworkServerIdentity.CreateEphemeral();
        UdpProtocolLimits failureLimits = s_udp with { MaximumHandshakePumps = 64 };
        for (int attempt = 0; attempt < 8; attempt++)
        {
            False(UdpProtocolSession.TryCreate(
                s_protocol, failureLimits, out UdpProtocolSession? rejected, out _,
                CoreNetworkProtocol.Manifest, serverIdentity,
                wrongIdentity.PublicKey.ToArray()));
            True(rejected is null);
        }
        True(UdpProtocolSession.TryCreate(
            s_protocol, s_udp, out UdpProtocolSession? accepted, out _,
            CoreNetworkProtocol.Manifest, serverIdentity,
            serverIdentity.PublicKey.ToArray()));
        accepted!.Dispose();
    }

    private static void WarmedUdpSecureDeliveryAllocatesZero()
    {
        using UdpProtocolSession session = Create();
        for (byte value = 1; value < 17; value++) Exchange(session, value);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (byte value = 17; value < 49; value++) Exchange(session, value);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated);
    }

    private static void Exchange(UdpProtocolSession session, byte value)
    {
        Equal(NetworkQueueStatus.Accepted,
            session.Client.TryQueue(s_probe, stackalloc byte[] { value }));
        for (int pump = 0; session.Server.ReceivedQueued != 1 && pump < 10_000; pump++)
            session.Pump();
        Equal(1, session.Server.ReceivedQueued);
        True(session.Server.TryPeekReceived(out _, out ReadOnlySpan<byte> payload));
        Equal(value, payload[0]);
        True(session.Server.TryConsumeReceived());
        for (int pump = 0; session.Client.ReliableQueued != 0 && pump < 10_000; pump++)
            session.Pump();
        Equal(0, session.Client.ReliableQueued);
    }

    private static UdpProtocolSession Create()
    {
        True(UdpProtocolSession.TryCreate(
            s_protocol, s_udp, out UdpProtocolSession? session, out _));
        return session!;
    }

    private static void Drive(UdpProtocolSession session, Func<bool> complete, int maximumPumps = 10_000)
    {
        for (int pump = 0; pump < maximumPumps; pump++)
        {
            if (complete()) return;
            session.Pump();
        }
        throw new InvalidOperationException(
            $"UDP protocol did not converge in {maximumPumps} pumps: {session.GetSnapshot()}.");
    }

    private static void AssertReceive(NetworkProtocolPeer peer, ReadOnlySpan<byte> expected)
    {
        True(peer.TryPeekReceived(out _, out ReadOnlySpan<byte> actual));
        True(actual.SequenceEqual(expected));
        True(peer.TryConsumeReceived());
    }

    private static void Run(string name, Action body)
    {
        body();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool condition, string message = "Expected true.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void False(bool condition, string message = "Expected false.") => True(!condition, message);

    private static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Expected '{expected}', got '{actual}'.");
    }
}
