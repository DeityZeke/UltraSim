using System.Diagnostics;
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
        Run("HostedShardedRetirementTimeoutRetainsOwnershipUntilQuiescent",
            HostedShardedRetirementTimeoutRetainsOwnershipUntilQuiescent);
        Run("HostedLifetimeIgnoresActivityNewerThanOwnerSample",
            HostedLifetimeIgnoresActivityNewerThanOwnerSample);
        Run("HostedShardedPathRebindCommitsThroughOwnerHandoff", HostedShardedPathRebindCommitsThroughOwnerHandoff);
        Run("HostedShardedLostCommitRecoversThroughOwnerHandoff", HostedShardedLostCommitRecoversThroughOwnerHandoff);
        Run("HostedShardedApplicationRetirementReclaimsAdmission", HostedShardedApplicationRetirementReclaimsAdmission);
        Run("HostedShardedIdleClosureReclaimsAdmission", HostedShardedIdleClosureReclaimsAdmission);
        Run("HostedShardedIngressBudgetPreservesApplicationProgress",
            HostedShardedIngressBudgetPreservesApplicationProgress);
        Run("HostedOwnerFailureReportsAfterCompleteHostCleanup", HostedOwnerFailureReportsAfterCompleteHostCleanup);
        Run("HostedApplicationDisposeFailureCannotSkipIndependentCleanup",
            HostedApplicationDisposeFailureCannotSkipIndependentCleanup);
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

    private static void HostedShardedRetirementTimeoutRetainsOwnershipUntilQuiescent()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var factory = new BlockingProgressFactory();
        SharedUdpProtocolLimits oneSession = s_shared with { MaximumSessions = 1 };
        HostedUdpOwnerShardingLimits oneOwner = s_sharding with
        {
            MaximumOwners = 1,
            MaximumConnections = 1,
            TransportRetirementTimeoutMilliseconds = 100,
        };
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_protocol,
            s_udp, oneSession, s_admission, oneOwner, factory, s_manifest);
        using HostedUdpProtocolClient first = CreateClient(host, identity);
        PumpUntil(host, [first], () =>
            first.State == HostedUdpClientState.Established &&
            factory.Latest is not null);
        True(host.TryGetSession(first.Connection, out HostedUdpProtocolSession? session));
        ConnectionId retired = first.Connection;

        factory.BlockNextProgress();
        try
        {
            True(factory.WaitUntilBlocked(TimeSpan.FromSeconds(2)),
                "The owner application did not enter the controlled stalled callback.");
            long started = Stopwatch.GetTimestamp();
            Throws<AggregateException>(() => session!.Dispose());
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            True(elapsed >= TimeSpan.FromMilliseconds(75) &&
                elapsed < TimeSpan.FromSeconds(2),
                "The owner retirement timeout did not remain bounded.");
            Equal(UdpProtocolSessionState.Draining, session!.State);
            Equal(1, host.SessionCount);
            Equal(1, host.GetSnapshot().Connections.Active);
            True(host.TryGetConnectionOwnerSnapshot(
                out HostedUdpConnectionOwnerRuntimeSnapshot retained));
            Equal(1, retained.ActiveTransports);
            Equal(1, retained.Handoff.BoundConnections);
            Equal(0, factory.DisposalCount);
        }
        finally
        {
            factory.Release();
        }

        PumpUntil(host, [first], () => host.SessionCount == 0);
        Equal(1, factory.DisposalCount);
        Equal(0, host.GetSnapshot().Connections.Active);
        True(host.TryGetConnectionOwnerSnapshot(
            out HostedUdpConnectionOwnerRuntimeSnapshot released));
        Equal(0, released.ActiveTransports);
        Equal(0, released.Handoff.BoundConnections);

        using HostedUdpProtocolClient replacement = CreateClient(host, identity);
        PumpUntil(host, [replacement], () =>
            replacement.State == HostedUdpClientState.Established);
        True(replacement.Connection != retired);
        Equal(1, host.GetSnapshot().Connections.Active);
    }

    private static void HostedLifetimeIgnoresActivityNewerThanOwnerSample()
    {
        Equal(HostedUdpSessionLifetimeDecision.Continue,
            HostedUdpSessionLifetimePolicy.Evaluate(
                UdpProtocolSessionState.Established,
                applicationRetirementRequested: false,
                now: 100,
                lastActivity: 101,
                drainDeadline: 0,
                reliableQueued: 0,
                s_udp));
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

    private static void HostedShardedApplicationRetirementReclaimsAdmission()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var clock = new ManualClock(100);
        var factory = new LifetimeFactory();
        using var host = CreateHost(identity, maximumOwners: 1, factory, clock);
        using HostedUdpProtocolClient client = CreateClient(host, identity, clock);
        PumpUntil(host, [client], () =>
            client.State == HostedUdpClientState.Established &&
            factory.Latest is not null);
        ConnectionId retired = client.Connection;
        factory.Latest!.RequestRetirement();
        PumpUntil(host, [client], () => host.SessionCount == 0);
        Equal(1, factory.DisposalCount);
        Equal(0, host.GetSnapshot().Connections.Active);
        True(host.TryGetConnectionOwnerSnapshot(
            out HostedUdpConnectionOwnerRuntimeSnapshot owners));
        Equal(0, owners.ActiveTransports);
        Equal(0, owners.Handoff.BoundConnections);

        using HostedUdpProtocolClient replacement = CreateClient(host, identity, clock);
        PumpUntil(host, [replacement], () =>
            replacement.State == HostedUdpClientState.Established);
        True(replacement.Connection != retired);
    }

    private static void HostedShardedIdleClosureReclaimsAdmission()
    {
        UdpProtocolLimits shortUdp = s_udp with
        {
            IdleTimeoutMilliseconds = 200,
            DrainTimeoutMilliseconds = 50,
            KeepAliveIntervalMilliseconds = 25,
        };
        HostedUdpAdmissionLimits shortAdmission = s_admission with
        {
            ValidatedCandidateTimeoutMilliseconds = 100,
        };
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var clock = new ManualClock(100);
        var factory = new LifetimeFactory();
        using var host = CreateHost(
            identity, maximumOwners: 1, factory, clock, shortUdp, shortAdmission);
        HostedUdpProtocolClient client = CreateClient(host, identity, clock, shortUdp);
        PumpUntil(host, [client], () =>
            client.State == HostedUdpClientState.Established &&
            factory.Latest is not null);
        client.Dispose();
        clock.Value = 301;
        PumpUntil(host, [], () => host.SessionCount == 0);
        Equal(1, factory.DisposalCount);
        Equal(0, host.GetSnapshot().Connections.Active);
        True(host.TryGetConnectionOwnerSnapshot(
            out HostedUdpConnectionOwnerRuntimeSnapshot owners));
        Equal(0, owners.ActiveTransports);
        Equal(0, owners.Handoff.BoundConnections);
    }

    private static void HostedShardedIngressBudgetPreservesApplicationProgress()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity, maximumOwners: 1, ingressTransfersPerPass: 1);
        using HostedUdpProtocolClient first = CreateClient(host, identity);
        using HostedUdpProtocolClient second = CreateClient(host, identity);
        PumpUntil(host, [first, second], () =>
            first.State == HostedUdpClientState.Established &&
            second.State == HostedUdpClientState.Established);

        Equal(NetworkQueueStatus.Accepted, first.Peer!.TryQueue(s_messageId, "budget-one"u8));
        Equal(NetworkQueueStatus.Accepted, second.Peer!.TryQueue(s_messageId, "budget-two"u8));
        bool firstReceived = false;
        bool secondReceived = false;
        PumpUntil(host, [first, second], () =>
        {
            firstReceived |= HasPayload(first, "budget-one"u8);
            secondReceived |= HasPayload(second, "budget-two"u8);
            return firstReceived && secondReceived;
        });

        True(host.TryGetConnectionOwnerSnapshot(
            out HostedUdpConnectionOwnerRuntimeSnapshot owners));
        Equal(1, owners.MaximumIngressTransfersPerPass);
        Equal(1, owners.PeakIngressTransfersPerPass);
        True(owners.ApplicationMessages >= 2);
    }

    private static void HostedOwnerFailureReportsAfterCompleteHostCleanup()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var factory = new FaultingFactory();
        var host = CreateHost(identity, maximumOwners: 2, factory);
        int port = host.LocalEndPoint.Port;
        using HostedUdpProtocolClient first = CreateClient(host, identity);
        using HostedUdpProtocolClient second = CreateClient(host, identity);
        try
        {
            PumpUntil(host, [first, second], () =>
                first.State == HostedUdpClientState.Established &&
                second.State == HostedUdpClientState.Established &&
                factory.Applications.Length == 2);
            factory.Applications[0].FailProgress();
            True(SpinWait.SpinUntil(
                () => factory.DisposalCount >= 1, TimeSpan.FromSeconds(5)),
                "The injected owner failure did not reach owner-side cleanup.");

            Throws<AggregateException>(() => host.Dispose());
            Equal(2, factory.DisposalCount,
                "A failure on one owner prevented independent owner disposal.");
            using var rebound = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);
            rebound.Bind(new IPEndPoint(IPAddress.Loopback, port));
            host.Dispose(); // Cleanup-complete failure reporting is idempotent.
        }
        finally
        {
            try { host.Dispose(); }
            catch (AggregateException) { }
        }
    }

    private static void HostedApplicationDisposeFailureCannotSkipIndependentCleanup()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var factory = new DisposalFaultFactory();
        var host = CreateHost(identity, maximumOwners: 2, factory);
        int port = host.LocalEndPoint.Port;
        using HostedUdpProtocolClient first = CreateClient(host, identity);
        using HostedUdpProtocolClient second = CreateClient(host, identity);
        PumpUntil(host, [first, second], () =>
            first.State == HostedUdpClientState.Established &&
            second.State == HostedUdpClientState.Established &&
            factory.Created == 2);

        Throws<AggregateException>(() => host.Dispose());
        Equal(2, factory.DisposalAttempts,
            "One failing application disposal skipped an independent application.");
        using var rebound = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram,
            System.Net.Sockets.ProtocolType.Udp);
        rebound.Bind(new IPEndPoint(IPAddress.Loopback, port));
        host.Dispose();
    }

    private static HostedUdpAdmissionHost CreateHost(
        NetworkServerIdentity identity,
        int maximumOwners = 2,
        IHostedUdpSessionApplicationFactory? factory = null,
        ManualClock? clock = null,
        UdpProtocolLimits? udp = null,
        HostedUdpAdmissionLimits? admission = null,
        int? ingressTransfersPerPass = null) =>
        clock is null ? new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0),
            identity,
            s_protocol,
            udp ?? s_udp,
            s_shared,
            admission ?? s_admission,
            s_sharding with
            {
                MaximumOwners = maximumOwners,
                MaximumIngressTransfersPerPass = ingressTransfersPerPass ??
                    s_sharding.MaximumIngressTransfersPerPass,
            },
            factory ?? new EchoFactory(),
            s_manifest) : new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0),
            identity,
            s_protocol,
            udp ?? s_udp,
            s_shared,
            admission ?? s_admission,
            s_sharding with
            {
                MaximumOwners = maximumOwners,
                MaximumIngressTransfersPerPass = ingressTransfersPerPass ??
                    s_sharding.MaximumIngressTransfersPerPass,
            },
            factory ?? new EchoFactory(),
            clock,
            s_manifest);

    private static HostedUdpProtocolClient CreateClient(
        HostedUdpAdmissionHost host,
        NetworkServerIdentity identity,
        ManualClock? clock = null,
        UdpProtocolLimits? udp = null)
    {
        bool created = clock is null
            ? HostedUdpProtocolClient.TryCreate(
                host.LocalEndPoint, identity.PublicKey, s_protocol, udp ?? s_udp,
                out HostedUdpProtocolClient? client, s_manifest,
                new IPEndPoint(IPAddress.Loopback, 0))
            : HostedUdpProtocolClient.TryCreateForTesting(
                host.LocalEndPoint, identity.PublicKey, s_protocol, udp ?? s_udp,
                clock, out client, s_manifest,
                new IPEndPoint(IPAddress.Loopback, 0));
        True(created);
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

    private sealed class LifetimeFactory : IHostedUdpSessionApplicationFactory
    {
        private LifetimeApplication? _latest;
        private int _disposalCount;

        internal LifetimeApplication? Latest => Volatile.Read(ref _latest);
        internal int DisposalCount => Volatile.Read(ref _disposalCount);

        public IHostedUdpSessionApplication Create(
            ConnectionId connection,
            NetworkProtocolPeer peer)
        {
            var application = new LifetimeApplication(this);
            Volatile.Write(ref _latest, application);
            return application;
        }

        internal void RecordDisposal() => Interlocked.Increment(ref _disposalCount);
    }

    private sealed class BlockingProgressFactory :
        IHostedUdpSessionApplicationFactory,
        IDisposable
    {
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _release = new(false);
        private BlockingProgressApplication? _latest;
        private int _blockNext;
        private int _disposalCount;

        internal BlockingProgressApplication? Latest => Volatile.Read(ref _latest);
        internal int DisposalCount => Volatile.Read(ref _disposalCount);

        public IHostedUdpSessionApplication Create(
            ConnectionId connection,
            NetworkProtocolPeer peer)
        {
            var application = new BlockingProgressApplication(this);
            Volatile.Write(ref _latest, application);
            return application;
        }

        internal void BlockNextProgress()
        {
            _entered.Reset();
            _release.Reset();
            Volatile.Write(ref _blockNext, 1);
        }

        internal bool WaitUntilBlocked(TimeSpan timeout) => _entered.Wait(timeout);

        internal void Progress()
        {
            if (Interlocked.Exchange(ref _blockNext, 0) == 0) return;
            _entered.Set();
            _release.Wait();
        }

        internal void RecordDisposal() => Interlocked.Increment(ref _disposalCount);

        internal void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _entered.Dispose();
            _release.Dispose();
        }
    }

    private sealed class BlockingProgressApplication(BlockingProgressFactory owner) :
        IHostedUdpSessionApplication
    {
        private int _disposed;

        public bool TryProcess(
            in NetworkDeliveredMessage message,
            ReadOnlySpan<byte> payload) => false;

        public bool TryProgress()
        {
            owner.Progress();
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.RecordDisposal();
        }
    }

    private sealed class LifetimeApplication(LifetimeFactory owner) :
        IHostedUdpSessionApplication,
        IHostedUdpSessionApplicationLifetime
    {
        private int _retirementRequested;
        private int _disposed;

        public bool RetirementRequested =>
            Volatile.Read(ref _retirementRequested) != 0;

        internal void RequestRetirement() =>
            Volatile.Write(ref _retirementRequested, 1);

        public bool TryProcess(
            in NetworkDeliveredMessage message,
            ReadOnlySpan<byte> payload) => false;

        public bool TryProgress() => true;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.RecordDisposal();
        }
    }

    private sealed class FaultingFactory : IHostedUdpSessionApplicationFactory
    {
        private readonly object _gate = new();
        private readonly List<FaultingApplication> _applications = [];
        private int _disposalCount;

        internal FaultingApplication[] Applications
        {
            get { lock (_gate) return _applications.ToArray(); }
        }

        internal int DisposalCount => Volatile.Read(ref _disposalCount);

        public IHostedUdpSessionApplication Create(
            ConnectionId connection,
            NetworkProtocolPeer peer)
        {
            var application = new FaultingApplication(this);
            lock (_gate) _applications.Add(application);
            return application;
        }

        internal void RecordDisposal() => Interlocked.Increment(ref _disposalCount);
    }

    private sealed class FaultingApplication(FaultingFactory owner) :
        IHostedUdpSessionApplication
    {
        private int _failProgress;
        private int _disposed;

        internal void FailProgress() => Volatile.Write(ref _failProgress, 1);

        public bool TryProcess(
            in NetworkDeliveredMessage message,
            ReadOnlySpan<byte> payload) => false;

        public bool TryProgress()
        {
            if (Volatile.Read(ref _failProgress) != 0)
                throw new InvalidOperationException("Injected owner progression failure.");
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.RecordDisposal();
        }
    }

    private sealed class DisposalFaultFactory : IHostedUdpSessionApplicationFactory
    {
        private int _created;
        private int _disposalAttempts;

        internal int Created => Volatile.Read(ref _created);
        internal int DisposalAttempts => Volatile.Read(ref _disposalAttempts);

        public IHostedUdpSessionApplication Create(
            ConnectionId connection,
            NetworkProtocolPeer peer)
        {
            int ordinal = Interlocked.Increment(ref _created);
            return new DisposalFaultApplication(this, fail: ordinal == 1);
        }

        internal void RecordDisposal() => Interlocked.Increment(ref _disposalAttempts);
    }

    private sealed class DisposalFaultApplication(
        DisposalFaultFactory owner,
        bool fail) : IHostedUdpSessionApplication
    {
        private int _disposed;

        public bool TryProcess(
            in NetworkDeliveredMessage message,
            ReadOnlySpan<byte> payload) => false;

        public bool TryProgress() => true;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            owner.RecordDisposal();
            if (fail)
                throw new InvalidOperationException("Injected application disposal failure.");
        }
    }

    private sealed class ManualClock(ulong milliseconds) : INetworkMonotonicClock
    {
        private long _milliseconds = checked((long)milliseconds);

        public ulong Milliseconds =>
            checked((ulong)Volatile.Read(ref _milliseconds));

        internal ulong Value
        {
            set => Volatile.Write(ref _milliseconds, checked((long)value));
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
