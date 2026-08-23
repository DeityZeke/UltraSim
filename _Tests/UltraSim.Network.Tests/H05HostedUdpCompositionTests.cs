using System.Net;
using UltraSim.Network;

internal static class H05HostedUdpCompositionTests
{
    private static readonly NetworkMessageId s_messageId = new(1);
    private static readonly NetworkProtocolManifest s_manifest = NetworkProtocolManifest.Create(
        1,
        [new NetworkMessageDescriptor(s_messageId,
            "ultrasim.test.hosted-h05-echo", 1, NetworkLane.Control, 32, 0xA051)]);
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
        MaximumHandshakePumps: 4_096,
        CookieBucketMilliseconds: 1_000,
        IdleTimeoutMilliseconds: 60_000,
        DrainTimeoutMilliseconds: 1_000,
        KeepAliveIntervalMilliseconds: 5_000);
    private static readonly SharedUdpProtocolLimits s_shared = new(
        MaximumSessions: 4,
        MaximumQueuedDatagramsPerSession: 16,
        MaximumInboundDatagramsPerPump: 256,
        SocketIoMode: SharedUdpSocketIoMode.Scalar,
        AggregateSocketSendBufferBytes: 64 * 1024,
        AggregateSocketReceiveBufferBytes: 64 * 1024);
    private static readonly HostedUdpAdmissionLimits s_admission = new(
        MaximumValidatedCandidates: 4,
        MaximumTrackedSourceAddresses: 16,
        MaximumChallengesPerAddressPerBucket: 64,
        MaximumValidationsPerAddressPerBucket: 64,
        ValidatedCandidateTimeoutMilliseconds: 5_000);
    private static readonly HostedUdpOwnerShardingLimits s_sharding = new(
        MaximumOwners: 2,
        MaximumConnections: 4,
        MaximumPacketSlots: 64,
        MaximumQueuedPacketsPerOwner: 16,
        MaximumDatagramBytes: 512);

    internal static void RunAll()
    {
        Run("HostedShardedListenerCarriesSecureApplications", HostedShardedListenerCarriesSecureApplications);
        Run("HostedShardedSessionRetirementIsQuiescentAndReusable", HostedShardedSessionRetirementIsQuiescentAndReusable);
        Run("HostedShardedPathRebindCommitsThroughOwnerHandoff", HostedShardedPathRebindCommitsThroughOwnerHandoff);
        Run("HostedShardedLostCommitRecoversThroughOwnerHandoff", HostedShardedLostCommitRecoversThroughOwnerHandoff);
    }

    private static void HostedShardedListenerCarriesSecureApplications()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity, maximumOwners: 1);
        using HostedUdpProtocolClient first = CreateClient(host, identity);
        using HostedUdpProtocolClient second = CreateClient(host, identity);
        PumpUntil(host, [first, second], () =>
            first.State == HostedUdpClientState.Established &&
            second.State == HostedUdpClientState.Established);

        Equal(NetworkQueueStatus.Accepted,
            first.Peer!.TryQueue(s_messageId, "first"u8));
        Equal(NetworkQueueStatus.Accepted,
            second.Peer!.TryQueue(s_messageId, "second"u8));
        bool firstReceived = false;
        bool secondReceived = false;
        PumpUntil(host, [first, second], () =>
        {
            firstReceived |= HasPayload(first, "first"u8);
            secondReceived |= HasPayload(second, "second"u8);
            return firstReceived && secondReceived;
        });

        True(host.TryGetConnectionOwnerSnapshot(
            out HostedUdpConnectionOwnerRuntimeSnapshot owners));
        Equal(1, owners.OwnerCount);
        Equal(2, owners.ActiveTransports);
        True(owners.ApplicationMessages >= 2);
        True(owners.Handoff.IngressAccepted != 0);
        True(owners.Handoff.EgressAccepted != 0);
        DrainHandoff(host, [first, second]);
        True(host.TryGetSession(first.Connection, out HostedUdpProtocolSession? session));
        Throws<InvalidOperationException>(() => _ = session!.Server.GetSnapshot());
    }

    private static void HostedShardedSessionRetirementIsQuiescentAndReusable()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity, maximumOwners: 1);
        using HostedUdpProtocolClient first = CreateClient(host, identity);
        PumpUntil(host, [first], () =>
            first.State == HostedUdpClientState.Established);
        True(host.TryGetSession(first.Connection, out HostedUdpProtocolSession? session));
        session!.Dispose();
        Equal(0, host.SessionCount);
        True(host.TryGetConnectionOwnerSnapshot(
            out HostedUdpConnectionOwnerRuntimeSnapshot retired));
        Equal(0, retired.ActiveTransports);
        Equal(0, retired.Handoff.BoundConnections);
        Equal(0, retired.Handoff.RentedPackets);

        using HostedUdpProtocolClient replacement = CreateClient(host, identity);
        PumpUntil(host, [replacement], () =>
            replacement.State == HostedUdpClientState.Established);
        Equal(1, host.SessionCount);
        True(host.TryGetConnectionOwnerSnapshot(
            out HostedUdpConnectionOwnerRuntimeSnapshot reused));
        Equal(1, reused.ActiveTransports);
        Equal(1, reused.Handoff.BoundConnections);
    }

    private static void HostedShardedPathRebindCommitsThroughOwnerHandoff()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntil(host, [client], () => client.State == HostedUdpClientState.Established);
        ConnectionId connection = client.Connection;
        True(client.TryBeginPathRebind(new IPEndPoint(IPAddress.Loopback, 0)));
        PumpUntil(host, [client], () =>
            client.PathGeneration.Value == 2 &&
            client.PathRebindState == HostedPathRebindState.None);
        Equal(connection, client.Connection);
        Equal(1, host.SessionCount);
        Equal(NetworkQueueStatus.Accepted,
            client.Peer!.TryQueue(s_messageId, "after-rebind"u8));
        PumpUntil(host, [client], () => HasPayload(client, "after-rebind"u8));
        True(host.TryGetConnectionOwnerSnapshot(
            out HostedUdpConnectionOwnerRuntimeSnapshot owners));
        True(owners.PathRequests >= 1);
        True(owners.PathChallenges >= 1);
        True(owners.PathProofs >= 1);
        True(owners.PathCommits >= 1);
        DrainHandoff(host, [client]);
    }

    private static void HostedShardedLostCommitRecoversThroughOwnerHandoff()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntil(host, [client], () => client.State == HostedUdpClientState.Established);
        host.SuppressNextPathCommitForTesting();
        True(client.TryBeginPathRebind(new IPEndPoint(IPAddress.Loopback, 0)));
        PumpUntil(host, [client], () =>
            client.PathGeneration.Value == 2 &&
            client.PathRebindState == HostedPathRebindState.None);
        Equal(1, host.SessionCount);
        Equal(NetworkQueueStatus.Accepted,
            client.Peer!.TryQueue(s_messageId, "lost-commit-recovered"u8));
        PumpUntil(host, [client], () =>
            HasPayload(client, "lost-commit-recovered"u8));
        True(client.GetSnapshot().PathRebindCommits >= 1);
        True(host.TryGetConnectionOwnerSnapshot(
            out HostedUdpConnectionOwnerRuntimeSnapshot owners));
        True(owners.ListenerPathDatagrams >= 2,
            "The lost Commit did not require an owner-authenticated duplicate recovery flight.");
        DrainHandoff(host, [client]);
    }

    private static HostedUdpAdmissionHost CreateHost(
        NetworkServerIdentity identity,
        int maximumOwners = 2) =>
        new(
            new IPEndPoint(IPAddress.Loopback, 0),
            identity,
            s_protocol,
            s_udp,
            s_shared,
            s_admission,
            s_sharding with { MaximumOwners = maximumOwners },
            new EchoFactory(),
            s_manifest);

    private static HostedUdpProtocolClient CreateClient(
        HostedUdpAdmissionHost host,
        NetworkServerIdentity identity)
    {
        True(HostedUdpProtocolClient.TryCreate(
            host.LocalEndPoint,
            identity.PublicKey,
            s_protocol,
            s_udp,
            out HostedUdpProtocolClient? client,
            s_manifest,
            new IPEndPoint(IPAddress.Loopback, 0)));
        return client!;
    }

    private static bool HasPayload(
        HostedUdpProtocolClient client,
        ReadOnlySpan<byte> expected)
    {
        if (!client.Peer!.TryPeekReceived(
                out NetworkDeliveredMessage message,
                out ReadOnlySpan<byte> payload)) return false;
        bool matches = message.MessageId == s_messageId && payload.SequenceEqual(expected);
        True(client.Peer.TryConsumeReceived());
        return matches;
    }

    private static void DrainHandoff(
        HostedUdpAdmissionHost host,
        HostedUdpProtocolClient[] clients)
    {
        PumpUntil(host, clients, () =>
        {
            True(host.TryGetConnectionOwnerSnapshot(
                out HostedUdpConnectionOwnerRuntimeSnapshot owners));
            return owners.Handoff.RentedPackets == 0 &&
                owners.Handoff.QueuedIngress == 0 &&
                owners.Handoff.QueuedEgress == 0;
        });
    }

    private static void PumpUntil(
        HostedUdpAdmissionHost host,
        HostedUdpProtocolClient[] clients,
        Func<bool> condition)
    {
        for (int iteration = 0; iteration < 20_000; iteration++)
        {
            for (int index = 0; index < clients.Length; index++) clients[index].Pump();
            host.Pump();
            if (condition()) return;
            Thread.Yield();
        }
        throw new InvalidOperationException("The sharded hosted operation did not converge.");
    }

    private sealed class EchoFactory : IHostedUdpSessionApplicationFactory
    {
        public IHostedUdpSessionApplication Create(
            ConnectionId connection,
            NetworkProtocolPeer peer) => new EchoApplication(peer);
    }

    private sealed class EchoApplication(NetworkProtocolPeer peer) : IHostedUdpSessionApplication
    {
        public bool TryProcess(
            in NetworkDeliveredMessage message,
            ReadOnlySpan<byte> payload) =>
            message.MessageId == s_messageId &&
            peer.TryQueue(s_messageId, payload) == NetworkQueueStatus.Accepted;

        public bool TryProgress() => true;

        public void Dispose()
        {
        }
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
}
