using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using UltraSim.Network;

internal static class H03HostedUdpPathRebindingTests
{
    private static readonly NetworkMessageId s_messageId = new(1);
    private static readonly NetworkProtocolManifest s_manifest = NetworkProtocolManifest.Create(
        1,
        [new NetworkMessageDescriptor(s_messageId,
            "ultrasim.test.hosted-rebind", 1, NetworkLane.Control, 32, 0xA031)]);
    private static readonly NetworkProtocolLimits s_protocol = new(
        MaximumReliableMessages: 8,
        MaximumReceivedMessages: 8,
        MaximumMessageBytes: 32,
        MaximumFragmentsPerMessage: 4,
        SentPacketCapacity: 64,
        MaximumInboundPacketsPerPump: 32,
        MaximumPacketsPerPump: 32,
        MinimumRetransmissionSteps: 2,
        MaximumRetransmissionSteps: 32,
        InitialCongestionWindowPackets: 4,
        MaximumCongestionWindowPackets: 32,
        MaximumIndependentUnreliableMessages: 8,
        ReliablePayloadByteCapacity: 256,
        ReceivedPayloadByteCapacity: 256,
        MaximumUnreliableFramesPerDatagram: 4);
    private static readonly UdpProtocolLimits s_udp = new(
        MaximumDatagramBytes: 512,
        SocketSendBufferBytes: 32 * 1024,
        SocketReceiveBufferBytes: 32 * 1024,
        MaximumHandshakePumps: 1_024,
        CookieBucketMilliseconds: 1_000,
        IdleTimeoutMilliseconds: 10_000,
        DrainTimeoutMilliseconds: 1_000,
        KeepAliveIntervalMilliseconds: 1_000);
    private static readonly SharedUdpProtocolLimits s_shared = new(
        MaximumSessions: 4,
        MaximumQueuedDatagramsPerSession: 8,
        MaximumInboundDatagramsPerPump: 256,
        SocketIoMode: SharedUdpSocketIoMode.Scalar,
        AggregateSocketSendBufferBytes: 64 * 1024,
        AggregateSocketReceiveBufferBytes: 64 * 1024);
    private static readonly HostedUdpAdmissionLimits s_admission = new(
        MaximumValidatedCandidates: 4,
        MaximumTrackedSourceAddresses: 16,
        MaximumChallengesPerAddressPerBucket: 64,
        MaximumValidationsPerAddressPerBucket: 64,
        ValidatedCandidateTimeoutMilliseconds: 2_000);

    internal static void RunAll()
    {
        Run("HostedPathGenerationIsFixedWidthAndMonotonic", HostedPathGenerationIsFixedWidthAndMonotonic);
        Run("HostedPathRebindPreservesSessionAndSecureTraffic", HostedPathRebindPreservesSessionAndSecureTraffic);
        Run("HostedPathCommitLossRecoversOnTheCommittedPath", HostedPathCommitLossRecoversOnTheCommittedPath);
        Run("HostedStaleOrWrongPathProofCannotReclaimOwnership", HostedStaleOrWrongPathProofCannotReclaimOwnership);
        Run("HostedPathRebindTimeoutPreservesTheCurrentPath", HostedPathRebindTimeoutPreservesTheCurrentPath);
        Run("HostedPostProofTimeoutFailsClosedInsteadOfReusingOldPath", HostedPostProofTimeoutFailsClosedInsteadOfReusingOldPath);
        Run("HostedPathRebindNativeReceiveMatchesScalarLifecycle", HostedPathRebindNativeReceiveMatchesScalarLifecycle);
        Run("WarmedHostedSecureDeliveryAfterPathRebindAllocatesZero", WarmedHostedSecureDeliveryAfterPathRebindAllocatesZero);
    }

    private static void HostedPathGenerationIsFixedWidthAndMonotonic()
    {
        Equal(4, Unsafe.SizeOf<HostedPathGeneration>());
        False(default(HostedPathGeneration).IsValid);
        var first = new HostedPathGeneration(1);
        var second = new HostedPathGeneration(2);
        True(first.CompareTo(second) < 0);
        False(first == second);
        Throws<ArgumentOutOfRangeException>(() => _ = new HostedPathGeneration(0));
        Equal(HostedUdpPathRebindCodec.RequestBytes, 91);
        Equal(HostedUdpPathRebindCodec.PathFlightBytes, 134);
    }

    private static void HostedPathRebindPreservesSessionAndSecureTraffic()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntilEstablished(host, client);
        True(host.TryGetSession(client.Connection, out HostedUdpProtocolSession? session));
        NetworkProtocolPeer clientPeer = client.Peer!;
        NetworkProtocolPeer serverPeer = session!.Server;
        ConnectionId connection = client.Connection;
        IPEndPoint oldClientPath = client.LocalEndPoint;

        True(client.TryBeginPathRebind(new IPEndPoint(IPAddress.Loopback, 0)));
        PumpUntilRebound(host, client, new HostedPathGeneration(2));

        False(client.LocalEndPoint.Port == oldClientPath.Port);
        Equal(connection, client.Connection);
        True(ReferenceEquals(clientPeer, client.Peer));
        True(host.TryGetSession(connection, out HostedUdpProtocolSession? current));
        True(ReferenceEquals(session, current));
        True(ReferenceEquals(serverPeer, current!.Server));
        Equal(new HostedPathGeneration(2), current.GetSnapshot().PathGeneration);
        Equal(client.LocalEndPoint.Port, current.RemoteEndPoint.Port);
        Equal(1, host.GetSnapshot().Listener.RegisteredPaths);
        Equal(0, host.GetSnapshot().PendingPathRebinds);

        AssertBidirectionalTraffic(host, client, current);
    }

    private static void HostedPathCommitLossRecoversOnTheCommittedPath()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntilEstablished(host, client);
        ConnectionId connection = client.Connection;
        True(host.TryGetSession(connection, out HostedUdpProtocolSession? session));

        True(client.TryBeginPathRebind(new IPEndPoint(IPAddress.Loopback, 0)));
        PumpUntilProving(host, client);
        host.SuppressNextPathCommitForTesting();
        client.Pump();
        host.Pump();

        HostedUdpAdmissionSnapshot committed = host.GetSnapshot();
        Equal(new HostedPathGeneration(2), session!.GetSnapshot().PathGeneration);
        Equal(HostedPathRebindState.Proving, client.PathRebindState);
        Equal(1, committed.EstablishedSessions);
        Equal(1, committed.Listener.RegisteredPaths);
        Equal(0, committed.PendingPathRebinds);
        Equal(0UL, committed.PathRebindCommits);
        True(committed.SendCapacityDrops >= 1);

        PumpUntilRebound(host, client, new HostedPathGeneration(2));
        HostedUdpAdmissionSnapshot recovered = host.GetSnapshot();
        Equal(connection, client.Connection);
        Equal(1, recovered.EstablishedSessions);
        True(recovered.DuplicatePathProofs >= 1);
        True(recovered.PathRebindCommits >= 1);
        AssertBidirectionalTraffic(host, client, session);
    }

    private static void HostedStaleOrWrongPathProofCannotReclaimOwnership()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntilEstablished(host, client);
        True(host.TryGetSession(client.Connection, out HostedUdpProtocolSession? session));

        True(client.TryBeginPathRebind(new IPEndPoint(IPAddress.Loopback, 0)));
        PumpUntilProving(host, client);
        byte[] staleProof = new byte[HostedUdpPathRebindCodec.PathFlightBytes];
        True(client.TryCopyPendingPathFlightForTesting(staleProof, out int proofLength));
        PumpUntilRebound(host, client, new HostedPathGeneration(2));

        True(client.TryBeginPathRebind(new IPEndPoint(IPAddress.Loopback, 0)));
        PumpUntilProving(host, client);
        IPEndPoint firstCommittedPath = session!.RemoteEndPoint;
        using Socket hostile = CreateRawClient(host.LocalEndPoint);
        Equal(proofLength, hostile.Send(staleProof.AsSpan(0, proofLength)));
        host.Pump();

        Equal(new HostedPathGeneration(2), session.GetSnapshot().PathGeneration);
        Equal(firstCommittedPath, session.RemoteEndPoint);
        Equal(1, host.GetSnapshot().PendingPathRebinds);
        PumpUntilRebound(host, client, new HostedPathGeneration(3));
        False(session.RemoteEndPoint.Equals(firstCommittedPath));
        Equal(1, host.GetSnapshot().Listener.RegisteredPaths);
        True(host.GetSnapshot().PathRebindRejections >= 1);
        AssertBidirectionalTraffic(host, client, session);
    }

    private static void HostedPathRebindTimeoutPreservesTheCurrentPath()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var clock = new ManualClock(100);
        using var host = CreateHost(identity, clock);
        using HostedUdpProtocolClient client = CreateClient(host, identity, clock);
        PumpUntilEstablished(host, client);
        True(host.TryGetSession(client.Connection, out HostedUdpProtocolSession? session));
        IPEndPoint original = session!.RemoteEndPoint;

        True(client.TryBeginPathRebind(new IPEndPoint(IPAddress.Loopback, 0)));
        client.Pump();
        host.Pump();
        Equal(1, host.GetSnapshot().PendingPathRebinds);
        clock.Milliseconds = 2_101;
        host.Pump();

        Equal(0, host.GetSnapshot().PendingPathRebinds);
        True(host.GetSnapshot().PathRebindTimeouts >= 1);
        Equal(original, session.RemoteEndPoint);
        Equal(new HostedPathGeneration(1), session.GetSnapshot().PathGeneration);
        True(client.TryCancelPathRebind());
        AssertBidirectionalTraffic(host, client, session);
    }

    private static void HostedPathRebindNativeReceiveMatchesScalarLifecycle()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_protocol, s_udp,
            s_shared with { SocketIoMode = SharedUdpSocketIoMode.PlatformSendReceiveBatch },
            s_admission, s_manifest);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntilEstablished(host, client);
        True(client.TryBeginPathRebind(new IPEndPoint(IPAddress.Loopback, 0)));
        PumpUntilRebound(host, client, new HostedPathGeneration(2));
        HostedUdpAdmissionSnapshot snapshot = host.GetSnapshot();
        Equal(1, snapshot.EstablishedSessions);
        Equal(1, snapshot.Listener.RegisteredPaths);
        True(snapshot.Listener.ReceiveCensus.UsesNativeReceiveBatching ==
            LinuxUdpBatch.IsReceiveSupported);
    }

    private static void HostedPostProofTimeoutFailsClosedInsteadOfReusingOldPath()
    {
        UdpProtocolLimits shortHandshake = s_udp with { MaximumHandshakePumps = 16 };
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_protocol,
            shortHandshake, s_shared, s_admission, s_manifest);
        True(HostedUdpProtocolClient.TryCreate(
            host.LocalEndPoint, identity.PublicKey, s_protocol, shortHandshake,
            out HostedUdpProtocolClient? created, s_manifest,
            new IPEndPoint(IPAddress.Loopback, 0)));
        using HostedUdpProtocolClient client = created!;
        PumpUntilEstablished(host, client);
        True(client.TryBeginPathRebind(new IPEndPoint(IPAddress.Loopback, 0)));
        PumpUntilProving(host, client);

        for (int index = 0; index < 24 && client.State != HostedUdpClientState.Failed; index++)
        {
            host.SuppressNextPathCommitForTesting();
            client.Pump();
            host.Pump();
        }

        Equal(HostedUdpClientState.Failed, client.State);
        False(client.TryCancelPathRebind(),
            "A post-Proof transaction incorrectly allowed fallback to the old path.");
        Equal(1, host.GetSnapshot().EstablishedSessions);
        Equal(new HostedPathGeneration(2),
            GetSession(host, client.Connection).GetSnapshot().PathGeneration);
    }

    private static void WarmedHostedSecureDeliveryAfterPathRebindAllocatesZero()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntilEstablished(host, client);
        True(host.TryGetSession(client.Connection, out HostedUdpProtocolSession? session));
        True(client.TryBeginPathRebind(new IPEndPoint(IPAddress.Loopback, 0)));
        PumpUntilRebound(host, client, new HostedPathGeneration(2));
        ReadOnlySpan<byte> payload = "warm-rebind"u8;

        for (int index = 0; index < 16; index++)
        {
            Equal(NetworkQueueStatus.Accepted,
                session!.Server.TryQueue(s_messageId, payload));
            PumpUntilReceived(host, client, client.Peer!);
            True(client.Peer!.TryConsumeReceived());
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        Equal(NetworkQueueStatus.Accepted,
            session!.Server.TryQueue(s_messageId, payload));
        PumpUntilReceived(host, client, client.Peer!);
        True(client.Peer!.TryConsumeReceived());
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated,
            "The warmed secure path allocated after endpoint ownership moved.");
    }

    private static void AssertBidirectionalTraffic(
        HostedUdpAdmissionHost host,
        HostedUdpProtocolClient client,
        HostedUdpProtocolSession session)
    {
        ReadOnlySpan<byte> outbound = "after-rebind"u8;
        Equal(NetworkQueueStatus.Accepted, client.Peer!.TryQueue(s_messageId, outbound));
        PumpUntilReceived(host, client, session.Server);
        True(session.Server.TryPeekReceived(
            out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload));
        Equal(s_messageId, message.MessageId);
        True(payload.SequenceEqual(outbound));
        True(session.Server.TryConsumeReceived());

        ReadOnlySpan<byte> response = "path-owned"u8;
        Equal(NetworkQueueStatus.Accepted, session.Server.TryQueue(s_messageId, response));
        PumpUntilReceived(host, client, client.Peer);
        True(client.Peer.TryPeekReceived(out message, out payload));
        True(payload.SequenceEqual(response));
        True(client.Peer.TryConsumeReceived());
    }

    private static HostedUdpAdmissionHost CreateHost(
        NetworkServerIdentity identity,
        ManualClock? clock = null) =>
        clock is null
            ? new HostedUdpAdmissionHost(
                new IPEndPoint(IPAddress.Loopback, 0), identity, s_protocol,
                s_udp, s_shared, s_admission, s_manifest)
            : new HostedUdpAdmissionHost(
                new IPEndPoint(IPAddress.Loopback, 0), identity, s_protocol,
                s_udp, s_shared, s_admission, clock, s_manifest);

    private static HostedUdpProtocolClient CreateClient(
        HostedUdpAdmissionHost host,
        NetworkServerIdentity identity,
        ManualClock? clock = null)
    {
        bool created = clock is null
            ? HostedUdpProtocolClient.TryCreate(
                host.LocalEndPoint, identity.PublicKey, s_protocol, s_udp,
                out HostedUdpProtocolClient? client, s_manifest,
                new IPEndPoint(IPAddress.Loopback, 0))
            : HostedUdpProtocolClient.TryCreateForTesting(
                host.LocalEndPoint, identity.PublicKey, s_protocol, s_udp, clock,
                out client, s_manifest, new IPEndPoint(IPAddress.Loopback, 0));
        True(created);
        return client!;
    }

    private static void PumpUntilEstablished(
        HostedUdpAdmissionHost host,
        HostedUdpProtocolClient client)
    {
        for (int index = 0; index < 10_000; index++)
        {
            client.Pump();
            host.Pump();
            if (client.State == HostedUdpClientState.Established) return;
            Thread.Yield();
        }
        throw new InvalidOperationException("The hosted client did not establish.");
    }

    private static void PumpUntilProving(
        HostedUdpAdmissionHost host,
        HostedUdpProtocolClient client)
    {
        for (int index = 0; index < 10_000; index++)
        {
            client.Pump();
            host.Pump();
            if (client.PathRebindState == HostedPathRebindState.Proving) return;
            Thread.Yield();
        }
        throw new InvalidOperationException("The hosted path did not reach proof.");
    }

    private static void PumpUntilRebound(
        HostedUdpAdmissionHost host,
        HostedUdpProtocolClient client,
        HostedPathGeneration expected)
    {
        for (int index = 0; index < 10_000; index++)
        {
            client.Pump();
            host.Pump();
            if (client.PathRebindState == HostedPathRebindState.None &&
                client.PathGeneration == expected) return;
            Thread.Yield();
        }
        throw new InvalidOperationException("The hosted path rebind did not converge.");
    }

    private static void PumpUntilReceived(
        HostedUdpAdmissionHost host,
        HostedUdpProtocolClient client,
        NetworkProtocolPeer recipient)
    {
        for (int index = 0; index < 10_000; index++)
        {
            host.Pump();
            client.Pump();
            if (recipient.ReceivedQueued != 0) return;
            Thread.Yield();
        }
        throw new InvalidOperationException("Hosted secure traffic did not converge.");
    }

    private static Socket CreateRawClient(IPEndPoint server)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Connect(server);
        socket.Blocking = false;
        return socket;
    }

    private static HostedUdpProtocolSession GetSession(
        HostedUdpAdmissionHost host,
        ConnectionId connection)
    {
        True(host.TryGetSession(connection, out HostedUdpProtocolSession? session));
        return session!;
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
    }

    private static void False(bool condition, string? message = null) =>
        True(!condition, message ?? "Expected false.");

    private static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                message ?? $"Expected {expected}, got {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }
        catch (T)
        {
        }
    }

    private sealed class ManualClock : INetworkMonotonicClock
    {
        internal ManualClock(ulong milliseconds) => Milliseconds = milliseconds;
        public ulong Milliseconds { get; set; }
    }
}
