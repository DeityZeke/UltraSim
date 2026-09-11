using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using UltraSim.Network;

internal static class N05UdpTransportTests
{
    private static readonly NetworkProtocolLimits s_protocol = new(
        16, 16, 4_096, 32, 256, 16, 16, 2, 64, 4, 32, 16);
    private static readonly UdpProtocolLimits s_udp = new(
        512, 64 * 1024, 64 * 1024, 4_096, 30_000, 5_000, 1_000, 1_000);
    private static readonly NetworkMessageId s_probe = new(1);

    internal static void RunAll()
    {
        Run("RealUdpHandshakeAndReliableDelivery", RealUdpHandshakeAndReliableDelivery);
        Run("UdpPinnedIdentityAndConnectedPathRejectSpoofing", UdpPinnedIdentityAndConnectedPathRejectSpoofing);
        Run("UdpDrainPreservesAdmittedReliableDelivery", UdpDrainPreservesAdmittedReliableDelivery);
        Run("UdpDrainRejectsNewApplicationWork", UdpDrainRejectsNewApplicationWork);
        Run("UdpTimersAreDeterministic", UdpTimersAreDeterministic);
        Run("UdpNonzeroPacingBranchDefersSecondPacket", UdpNonzeroPacingBranchDefersSecondPacket);
        Run("UdpCookieAcceptsPreviousBucketAndRejectsExpiredBucket", UdpCookieAcceptsPreviousBucketAndRejectsExpiredBucket);
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
        True(snapshot.ClientDatagrams.SentBytes > 0 && snapshot.ServerDatagrams.SentBytes > 0);
        True(snapshot.ClientDatagrams.ReceivedBytes > 0 && snapshot.ServerDatagrams.ReceivedBytes > 0);
        NetworkTrafficSnapshot clientTraffic = session.Client.GetTrafficSnapshot();
        NetworkTrafficSnapshot serverTraffic = session.Server.GetTrafficSnapshot();
        Equal(1UL, clientTraffic.SumMessages().MessagesSent);
        Equal(1UL, serverTraffic.SumMessages().MessagesSent);
        Equal(1UL, clientTraffic.SumMessages().MessagesReceived);
        Equal(1UL, serverTraffic.SumMessages().MessagesReceived);
        True(clientTraffic.Transport.AcknowledgementDatagramsSent > 0);
        True(serverTraffic.Transport.AcknowledgementDatagramsSent > 0);
        Equal(snapshot.ClientDatagrams.UdpPayload.PayloadBytesSent,
            snapshot.ClientDatagrams.SentBytes);
        True(snapshot.ClientDatagrams.UdpPayload.PayloadBytesSent >=
            clientTraffic.Transport.SecureUdpPayloadBytesSent);
        True(snapshot.ServerDatagrams.UdpPayload.PayloadBytesSent >=
            serverTraffic.Transport.SecureUdpPayloadBytesSent);
        Equal(UdpProtocolSessionState.Established, snapshot.State);
    }

    private static void UdpPinnedIdentityAndConnectedPathRejectSpoofing()
    {
        using var serverIdentity = NetworkServerIdentity.CreateEphemeral();
        using var wrongIdentity = NetworkServerIdentity.CreateEphemeral();
        False(UdpProtocolSession.TryCreate(
            s_protocol, s_udp, out UdpProtocolSession? unpinned, out _,
            CoreNetworkProtocol.Manifest, serverIdentity));
        True(unpinned is null);
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

    private static void UdpDrainRejectsNewApplicationWork()
    {
        using UdpProtocolSession session = Create();
        Equal(NetworkQueueStatus.Accepted,
            session.Server.TryQueue(s_probe, stackalloc byte[] { 1 }));
        session.BeginDrain();
        Equal(NetworkQueueStatus.NotAccepting,
            session.Server.TryQueue(s_probe, stackalloc byte[] { 2 }));
        Equal(NetworkQueueStatus.NotAccepting,
            session.Client.TryQueue(s_probe, stackalloc byte[] { 3 }));
        Drive(session, () => session.State == UdpProtocolSessionState.Closed);
        AssertReceive(session.Client, stackalloc byte[] { 1 });
        Equal(NetworkQueueStatus.NotAccepting,
            session.Server.TryQueue(s_probe, stackalloc byte[] { 4 }));
    }

    private static void UdpTimersAreDeterministic()
    {
        var clock = new ManualClock();
        UdpProtocolLimits limits = s_udp with
        {
            CookieBucketMilliseconds = 10,
            KeepAliveIntervalMilliseconds = 10,
            IdleTimeoutMilliseconds = 25,
            DrainTimeoutMilliseconds = 5,
        };
        using UdpProtocolSession session = Create(clock, limits);
        ulong received = session.GetSnapshot().Client.ReceivedPackets +
            session.GetSnapshot().Server.ReceivedPackets;
        for (int interval = 0; interval < 3; interval++)
        {
            clock.Advance(10);
            session.Pump();
            session.Pump();
        }
        UdpProtocolSessionSnapshot alive = session.GetSnapshot();
        Equal(UdpProtocolSessionState.Established, alive.State);
        True(alive.Client.ReceivedPackets + alive.Server.ReceivedPackets > received,
            "Authenticated keepalive traffic did not refresh the idle lifetime.");
        NetworkTransportTrafficSnapshot clientTraffic =
            session.Client.GetTrafficSnapshot().Transport;
        NetworkTransportTrafficSnapshot serverTraffic =
            session.Server.GetTrafficSnapshot().Transport;
        True(clientTraffic.KeepAliveDatagramsSent > 0);
        True(serverTraffic.KeepAliveDatagramsSent > 0);
        Equal(clientTraffic.KeepAliveDatagramsSent,
            serverTraffic.KeepAliveDatagramsReceived);
        Equal(serverTraffic.KeepAliveDatagramsSent,
            clientTraffic.KeepAliveDatagramsReceived);

        session.SuppressOutboundDatagramsForTesting(true);
        clock.Advance(25);
        session.Pump();
        Equal(UdpProtocolSessionState.Closed, session.State,
            "Lack of authenticated receive activity did not reach the idle/drain policy.");
    }

    private static void UdpNonzeroPacingBranchDefersSecondPacket()
    {
        var clock = new ManualClock();
        UdpProtocolLimits limits = s_udp with
        {
            KeepAliveIntervalMilliseconds = 1_000,
            IdleTimeoutMilliseconds = 5_000,
        };
        NetworkProtocolLimits protocol = s_protocol with
        {
            MinimumRetransmissionSteps = 100,
            MaximumRetransmissionSteps = 1_000,
        };
        using UdpProtocolSession session = Create(clock, limits, protocol);
        Equal(NetworkQueueStatus.Accepted,
            session.Client.TryQueue(s_probe, stackalloc byte[] { 7 }));
        session.Pump(); // emit at t=0
        clock.Advance(50); // below RTO, above CWND for nonzero pacing
        session.Pump(); // server admits and emits ACK
        session.Pump(); // client publishes the deterministic monotonic RTT sample
        NetworkProtocolSnapshot timed = session.GetSnapshot().Client;
        True(timed.SmoothedRoundTripSteps != 0,
            $"The timed acknowledgement did not publish an RTT sample: {timed}.");

        Equal(NetworkQueueStatus.Accepted,
            session.Client.TryQueue(s_probe, stackalloc byte[] { 8 }));
        Equal(NetworkQueueStatus.Accepted,
            session.Client.TryQueue(s_probe, stackalloc byte[] { 9 }));
        ulong before = session.GetSnapshot().ClientDatagrams.Sent;
        session.Pump();
        ulong afterFirst = session.GetSnapshot().ClientDatagrams.Sent;
        Equal(before + 1, afterFirst,
            "The nonzero pacing branch did not stop after one admitted data packet.");
        session.Pump();
        Equal(afterFirst, session.GetSnapshot().ClientDatagrams.Sent,
            "A second packet escaped before the monotonic pacing deadline.");
        clock.Advance(timed.SmoothedRoundTripSteps);
        session.Pump();
        True(session.GetSnapshot().ClientDatagrams.Sent > afterFirst,
            "The paced packet did not become eligible after its deadline.");
    }

    private static void UdpCookieAcceptsPreviousBucketAndRejectsExpiredBucket()
    {
        True(TryHandshakeWithBucketTransition(expired: false),
            "The immediately previous cookie bucket should remain valid.");
        False(TryHandshakeWithBucketTransition(expired: true),
            "A cookie older than the current/previous bucket was accepted.");
    }

    private static bool TryHandshakeWithBucketTransition(bool expired)
    {
        UdpDatagramEndpoint.CreateConnectedLoopbackPair(s_udp, out UdpDatagramEndpoint client,
            out UdpDatagramEndpoint server);
        using (client)
        using (server)
        using (var identity = NetworkServerIdentity.CreateEphemeral())
        {
            int calls = 0;
            ulong Bucket() => calls++ == 0 ? 10UL : expired ? 12UL : 11UL;
            bool established = ProtocolLoopbackHandshakeHarness.TryEstablishUdp(
                client, server, s_udp.MaximumDatagramBytes, Bucket,
                server.RemotePathBinding, CoreNetworkProtocol.Manifest,
                CoreNetworkProtocol.Manifest, identity, identity.PublicKey,
                maximumPumps: expired ? 64 : 4_096, out ProtocolHandshakeResult result);
            if (established) CryptographicOperations.ZeroMemory(result.MasterSecret);
            return established;
        }
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
        True(UdpProtocolSession.TryCreateSelfTrustedLoopback(
            s_protocol, s_udp, out UdpProtocolSession? session, out _));
        return session!;
    }

    private static UdpProtocolSession Create(
        ManualClock clock,
        UdpProtocolLimits limits,
        NetworkProtocolLimits? protocol = null)
    {
        True(UdpProtocolSession.TryCreateForTesting(
            protocol ?? s_protocol, limits, clock, out UdpProtocolSession? session, out _));
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

    private sealed class ManualClock : INetworkMonotonicClock
    {
        public ulong Milliseconds { get; private set; }

        internal void Advance(ulong milliseconds) =>
            Milliseconds = checked(Milliseconds + milliseconds);
    }
}
