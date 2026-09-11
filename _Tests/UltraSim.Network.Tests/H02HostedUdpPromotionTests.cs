using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using UltraSim.Network;

internal static class H02HostedUdpPromotionTests
{
    private static readonly NetworkMessageId s_messageId = new(1);
    private static readonly NetworkProtocolManifest s_manifest = NetworkProtocolManifest.Create(
        1,
        [new NetworkMessageDescriptor(s_messageId,
            "ultrasim.test.hosted-control", 1, NetworkLane.Control, 32, 0xA011)]);
    private static readonly NetworkProtocolManifest s_otherManifest = NetworkProtocolManifest.Create(
        2,
        [new NetworkMessageDescriptor(s_messageId,
            "ultrasim.test.hosted-control", 1, NetworkLane.Control, 32, 0xA011)]);
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
        Run("HostedCandidateIdentityIsGenerationFencedAndFixedWidth", HostedCandidateIdentityIsGenerationFencedAndFixedWidth);
        Run("HostedPromotionPublishesOnlyAfterFinishAndCarriesSecureTraffic", HostedPromotionPublishesOnlyAfterFinishAndCarriesSecureTraffic);
        Run("HostedPromotionReusesImmutableCandidateAndRejectsCredentialReplacement", HostedPromotionReusesImmutableCandidateAndRejectsCredentialReplacement);
        Run("HostedPromotionFailureAndTimeoutReleaseAllConnectionState", HostedPromotionFailureAndTimeoutReleaseAllConnectionState);
        Run("HostedPromotionCapacityIsTransactionalAndReusable", HostedPromotionCapacityIsTransactionalAndReusable);
        Run("HostedDuplicateClientFinishRecoversWithoutSecondSession", HostedDuplicateClientFinishRecoversWithoutSecondSession);
        Run("HostedTerminalSendFailureKeepsOneRecoverableSession", HostedTerminalSendFailureKeepsOneRecoverableSession);
        Run("HostedFirstServerFinishIsValidWithoutClientRetry", HostedFirstServerFinishIsValidWithoutClientRetry);
        Run("HostedPromotionNativeReceiveMatchesScalarLifecycle", HostedPromotionNativeReceiveMatchesScalarLifecycle);
        Run("HostedPromotionResamplesMonotonicTimeAfterInboundPublication", HostedPromotionResamplesMonotonicTimeAfterInboundPublication);
        Run("WarmedHostedSecureDeliveryAllocatesZero", WarmedHostedSecureDeliveryAllocatesZero);
        Run("HostedManifestCookieBindingAndDeploymentSignerLifetimeAreExact", HostedManifestCookieBindingAndDeploymentSignerLifetimeAreExact);
        Run("HostedScalarIdleClosureRetiresAdmissionAndPeerState", HostedScalarIdleClosureRetiresAdmissionAndPeerState);
    }

    private static void HostedCandidateIdentityIsGenerationFencedAndFixedWidth()
    {
        Equal(8, Unsafe.SizeOf<HostedCandidateId>());
        False(default(HostedCandidateId).IsValid);
        var first = new HostedCandidateId(3, 1);
        var reused = new HostedCandidateId(3, 2);
        True(first.IsValid);
        False(first == reused);
        Equal("3:1", first.ToString());
        Throws<ArgumentOutOfRangeException>(() => _ = new HostedCandidateId(3, 0));

        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var clock = new ManualClock(100);
        using var host = CreateHost(identity, clock,
            s_admission with { ValidatedCandidateTimeoutMilliseconds = 500 });
        using Socket socket = CreateRawClient(host.LocalEndPoint);
        HostedCandidateId original = AdmitRawCandidate(host, socket, out _, out _);
        Equal(HostedCandidateState.Promoting, GetOnlyCandidate(host, out HostedCandidateId observed));
        Equal(original, observed);
        Equal(0, host.GetSnapshot().Listener.RegisteredPaths,
            "A key-exchange candidate acquired a registered path before ClientFinish.");

        clock.Milliseconds = 601;
        host.Pump();
        Equal(0, host.GetSnapshot().ValidatedCandidates);
        DrainRaw(socket);
        HostedCandidateId replacement = AdmitRawCandidate(host, socket, out _, out _);
        Equal(original.Slot, replacement.Slot);
        True(replacement.Generation > original.Generation,
            "Candidate slot reuse did not advance generation.");
        False(original == replacement);
    }

    private static void HostedPromotionPublishesOnlyAfterFinishAndCarriesSecureTraffic()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        True(HostedUdpProtocolClient.TryCreate(
            host.LocalEndPoint, identity.PublicKey, s_protocol, s_udp,
            out HostedUdpProtocolClient? client, s_manifest));
        using (HostedUdpProtocolClient connected = client!)
        {
            PumpUntilPromoting(host, connected);
            HostedUdpAdmissionSnapshot pending = host.GetSnapshot();
            Equal(1, pending.PromotingCandidates);
            Equal(0, pending.EstablishedSessions);
            Equal(0, pending.Listener.RegisteredPaths);
            Equal(1, pending.Connections.Active);

            PumpUntilEstablished(host, connected);
            HostedUdpAdmissionSnapshot established = host.GetSnapshot();
            Equal(0, established.ValidatedCandidates);
            Equal(1, established.EstablishedSessions);
            Equal(1, established.Listener.RegisteredPaths);
            Equal(1, established.Connections.Active);
            Equal(HostedUdpClientState.Established, connected.State);
            True(host.TryGetSession(connected.Connection, out HostedUdpProtocolSession? session));
            True(session is not null);
            Equal(connected.Connection, session!.Connection);

            ReadOnlySpan<byte> clientPayload = "from-client"u8;
            Equal(NetworkQueueStatus.Accepted,
                connected.Peer!.TryQueue(s_messageId, clientPayload));
            PumpUntilReceived(host, connected, session.Server);
            True(session.Server.TryPeekReceived(
                out NetworkDeliveredMessage delivered, out ReadOnlySpan<byte> received));
            Equal(s_messageId, delivered.MessageId);
            True(received.SequenceEqual(clientPayload));
            True(session.Server.TryConsumeReceived());

            ReadOnlySpan<byte> serverPayload = "from-server"u8;
            Equal(NetworkQueueStatus.Accepted,
                session.Server.TryQueue(s_messageId, serverPayload));
            PumpUntilReceived(host, connected, connected.Peer);
            True(connected.Peer.TryPeekReceived(out delivered, out received));
            Equal(s_messageId, delivered.MessageId);
            True(received.SequenceEqual(serverPayload));
            True(connected.Peer.TryConsumeReceived());
            Equal(0UL, connected.Peer.GetSnapshot().AuthenticationFailures,
                "Duplicate handshake residue reached secure protocol authentication.");
        }
    }

    private static void HostedPromotionReusesImmutableCandidateAndRejectsCredentialReplacement()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using Socket socket = CreateRawClient(host.LocalEndPoint);
        HostedCandidateId candidate = AdmitRawCandidate(
            host, socket, out byte[] nonce, out byte[] publicKey);
        HostedUdpAdmissionSnapshot first = host.GetSnapshot();
        Equal(1UL, first.ServerKeyExchangeFlights);

        byte[] cookie = RequestCookie(host, socket, nonce, publicKey);
        SendKeyExchange(socket, nonce, publicKey, cookie);
        host.Pump();
        Equal(candidate, GetCandidateId(host));
        True(host.GetSnapshot().ServerKeyExchangeFlights >= 2,
            "A duplicate same-credential exchange did not reuse the cached server flight.");

        DrainRaw(socket);
        using ECDiffieHellman replacementKey = ECDiffieHellman.Create(
            ECCurve.NamedCurves.nistP256);
        byte[] replacementPublic = replacementKey.ExportSubjectPublicKeyInfo();
        byte[] replacementNonce = RandomNumberGenerator.GetBytes(
            ProtocolLoopbackHandshake.NonceBytes);
        byte[] replacementCookie = RequestCookie(
            host, socket, replacementNonce, replacementPublic);
        SendKeyExchange(socket, replacementNonce, replacementPublic, replacementCookie);
        host.Pump();
        HostedUdpAdmissionSnapshot conflict = host.GetSnapshot();
        Equal(1UL, conflict.CandidateCredentialConflicts);
        Equal(candidate, GetCandidateId(host));
        Equal(HostedCandidateState.Promoting, GetOnlyCandidate(host, out _));
    }

    private static void HostedPromotionFailureAndTimeoutReleaseAllConnectionState()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var clock = new ManualClock(100);
        using var host = CreateHost(identity, clock,
            s_admission with { ValidatedCandidateTimeoutMilliseconds = 500 });
        using Socket raw = CreateRawClient(host.LocalEndPoint);
        byte[] malformedPublic = new byte[64];
        RandomNumberGenerator.Fill(malformedPublic);
        byte[] nonce = RandomNumberGenerator.GetBytes(
            ProtocolLoopbackHandshake.NonceBytes);
        byte[] cookie = RequestCookie(host, raw, nonce, malformedPublic);
        SendKeyExchange(raw, nonce, malformedPublic, cookie);
        host.Pump();
        HostedUdpAdmissionSnapshot malformed = host.GetSnapshot();
        Equal(0, malformed.ValidatedCandidates);
        Equal(0, malformed.Connections.Active);
        Equal(0, malformed.Listener.RegisteredPaths);
        True(malformed.PromotionFailures >= 1);

        DrainRaw(raw);
        _ = AdmitRawCandidate(host, raw, out _, out _);
        Span<byte> serverFlight = stackalloc byte[512];
        int serverFlightLength = ReceiveRaw(raw, serverFlight);
        True(ProtocolLoopbackHandshake.TryReadServerKeyExchange(
            serverFlight[..serverFlightLength], out ConnectionId connection,
            out SecurityEpoch epoch, out _, out _));
        Span<byte> falseFinish = stackalloc byte[64];
        Span<byte> falseMaster = stackalloc byte[ProtocolLoopbackHandshake.MasterSecretBytes];
        falseMaster.Fill(0x5A);
        int falseFinishLength = ProtocolLoopbackHandshake.WriteClientFinish(
            falseFinish, connection, epoch, falseMaster, "wrong-transcript"u8);
        Equal(falseFinishLength, raw.Send(falseFinish[..falseFinishLength]));
        host.Pump();
        HostedUdpAdmissionSnapshot rejectedFinish = host.GetSnapshot();
        Equal(1, rejectedFinish.PromotingCandidates);
        Equal(1, rejectedFinish.Connections.Active);
        Equal(0, rejectedFinish.EstablishedSessions);
        Equal(0, rejectedFinish.Listener.RegisteredPaths);

        clock.Milliseconds = 601;
        host.Pump();
        Equal(0, host.GetSnapshot().Connections.Active);

        using NetworkServerIdentity wrong = NetworkServerIdentity.CreateEphemeral();
        True(HostedUdpProtocolClient.TryCreateForTesting(
            host.LocalEndPoint, wrong.PublicKey, s_protocol,
            s_udp with { MaximumHandshakePumps = 12 }, clock,
            out HostedUdpProtocolClient? client, s_manifest,
            new IPEndPoint(IPAddress.Loopback, 0)));
        using (HostedUdpProtocolClient connected = client!)
        {
            for (int index = 0; index < 20; index++)
            {
                connected.Pump();
                host.Pump();
            }
            Equal(HostedUdpClientState.Failed, connected.State);
            Equal(0, host.GetSnapshot().Listener.RegisteredPaths);
            clock.Milliseconds = 1_102;
            host.Pump();
            HostedUdpAdmissionSnapshot expired = host.GetSnapshot();
            Equal(0, expired.ValidatedCandidates);
            Equal(0, expired.Connections.Active);
            Equal(0, expired.EstablishedSessions);
        }
    }

    private static void HostedPromotionCapacityIsTransactionalAndReusable()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        SharedUdpProtocolLimits oneSession = s_shared with { MaximumSessions = 1 };
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_protocol,
            s_udp, oneSession, s_admission, s_manifest);
        using HostedUdpProtocolClient first = CreateClient(host, identity);
        PumpUntilEstablished(host, first);
        True(host.TryGetSession(first.Connection, out HostedUdpProtocolSession? firstSession));

        using HostedUdpProtocolClient second = CreateClient(host, identity);
        for (int index = 0; index < 16; index++)
        {
            second.Pump();
            host.Pump();
        }
        HostedUdpAdmissionSnapshot full = host.GetSnapshot();
        Equal(1, full.EstablishedSessions);
        Equal(1, full.Listener.RegisteredPaths);
        Equal(1, full.Connections.Active);
        True(full.PromotionCapacityDrops >= 1);
        Equal(HostedUdpClientState.Connecting, second.State);

        firstSession!.Dispose();
        Equal(0, host.GetSnapshot().EstablishedSessions);
        PumpUntilEstablished(host, second);
        Equal(1, host.GetSnapshot().EstablishedSessions);
        Equal(1, host.GetSnapshot().Connections.Active);
    }

    private static void HostedDuplicateClientFinishRecoversWithoutSecondSession()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntilEstablished(host, client);
        HostedUdpAdmissionSnapshot snapshot = host.GetSnapshot();
        Equal(1, snapshot.EstablishedSessions);
        Equal(1UL, snapshot.EstablishedPromotions);
        True(snapshot.DuplicateClientFinishes >= 1,
            "The retransmitted ClientFinish was not handled idempotently.");
        True(snapshot.ServerFinishFlights >= 2,
            "The duplicate ClientFinish did not resend cached ServerFinish.");
        client.Pump();
        True(client.GetSnapshot().DuplicateServerFinishes >= 1,
            "The client did not consume a cached duplicate ServerFinish idempotently.");
        Equal(0UL, client.Peer!.GetSnapshot().AuthenticationFailures,
            "A duplicate ServerFinish was misclassified as a secure datagram.");

        using Socket unrelated = CreateRawClient(host.LocalEndPoint);
        Span<byte> hostile = stackalloc byte[45];
        hostile.Fill(0xA5);
        Equal(hostile.Length, unrelated.Send(hostile));
        host.Pump();
        Equal(1, host.GetSnapshot().EstablishedSessions);
        Equal(1, host.GetSnapshot().Listener.RegisteredPaths);
    }

    private static void HostedTerminalSendFailureKeepsOneRecoverableSession()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntilPromoting(host, client);

        // Receive the cached server key exchange so the next client pump emits
        // ClientFinish, then reject only the first terminal server flight.
        client.Pump();
        host.Pump();
        host.SuppressNextServerFinishForTesting();
        client.Pump();
        host.Pump();

        HostedUdpAdmissionSnapshot committed = host.GetSnapshot();
        Equal(HostedUdpClientState.Connecting, client.State);
        Equal(1, committed.EstablishedSessions);
        Equal(1, committed.Listener.RegisteredPaths);
        Equal(1, committed.Connections.Active);
        Equal(0, committed.ValidatedCandidates);
        Equal(1UL, committed.EstablishedPromotions);
        Equal(0UL, committed.ServerFinishFlights);
        True(committed.SendCapacityDrops >= 1);
        True(host.TryGetSession(client.Connection, out HostedUdpProtocolSession? original));
        ConnectionId connection = original!.Connection;

        PumpUntilEstablished(host, client);
        HostedUdpAdmissionSnapshot recovered = host.GetSnapshot();
        Equal(connection, client.Connection);
        Equal(1, recovered.EstablishedSessions);
        Equal(1, recovered.Listener.RegisteredPaths);
        Equal(1, recovered.Connections.Active);
        Equal(1UL, recovered.EstablishedPromotions);
        True(recovered.DuplicateClientFinishes >= 1);
        True(recovered.ServerFinishFlights >= 1);
        True(host.TryGetSession(connection, out HostedUdpProtocolSession? current));
        True(ReferenceEquals(original, current),
            "Terminal retry published a second hosted session.");
    }

    private static void HostedFirstServerFinishIsValidWithoutClientRetry()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using Socket socket = CreateRawClient(host.LocalEndPoint);
        using ECDiffieHellman clientKey = ECDiffieHellman.Create(
            ECCurve.NamedCurves.nistP256);
        byte[] clientPublic = clientKey.ExportSubjectPublicKeyInfo();
        byte[] nonce = RandomNumberGenerator.GetBytes(
            ProtocolLoopbackHandshake.NonceBytes);
        byte[] cookie = RequestCookie(host, socket, nonce, clientPublic);
        SendKeyExchange(socket, nonce, clientPublic, cookie);
        host.Pump();

        Span<byte> serverKeyExchange = stackalloc byte[512];
        int serverKeyExchangeLength = ReceiveRaw(socket, serverKeyExchange);
        True(ProtocolLoopbackHandshake.TryReadServerKeyExchange(
            serverKeyExchange[..serverKeyExchangeLength], out ConnectionId connection,
            out SecurityEpoch epoch, out ReadOnlySpan<byte> serverPublic,
            out _));
        byte[] transcript = ProtocolLoopbackHandshake.BuildTranscript(
            s_manifest, nonce, clientPublic, serverPublic, connection, epoch);
        using ECDiffieHellman importedServer = ECDiffieHellman.Create();
        importedServer.ImportSubjectPublicKeyInfo(serverPublic, out int read);
        Equal(serverPublic.Length, read);
        byte[] secret = clientKey.DeriveRawSecretAgreement(importedServer.PublicKey);
        byte[] master = ProtocolLoopbackHandshake.DeriveMaster(
            secret, nonce, cookie, transcript);
        CryptographicOperations.ZeroMemory(secret);

        Span<byte> clientFinish = stackalloc byte[64];
        int clientFinishLength = ProtocolLoopbackHandshake.WriteClientFinish(
            clientFinish, connection, epoch, master, transcript);
        Equal(clientFinishLength, socket.Send(clientFinish[..clientFinishLength]));
        host.Pump();

        Span<byte> firstTerminalFlight = stackalloc byte[64];
        int firstTerminalLength = ReceiveRaw(socket, firstTerminalFlight);
        True(ProtocolLoopbackHandshake.TryReadServerFinish(
            firstTerminalFlight[..firstTerminalLength], connection, epoch,
            out ReadOnlySpan<byte> proof),
            "The first terminal response was not a canonical ServerFinish.");
        Span<byte> expected = stackalloc byte[ProtocolLoopbackHandshake.ProofBytes];
        ProtocolLoopbackHandshake.ComputeServerProof(master, transcript, expected);
        True(CryptographicOperations.FixedTimeEquals(expected, proof),
            "The first ServerFinish carried the wrong proof.");

        HostedUdpAdmissionSnapshot snapshot = host.GetSnapshot();
        Equal(1, snapshot.EstablishedSessions);
        Equal(1UL, snapshot.EstablishedPromotions);
        Equal(1UL, snapshot.ServerFinishFlights);
        Equal(0UL, snapshot.DuplicateClientFinishes,
            "The immediate terminal-flight test accidentally used retry recovery.");
        CryptographicOperations.ZeroMemory(master);
        CryptographicOperations.ZeroMemory(transcript);
        CryptographicOperations.ZeroMemory(clientPublic);
        CryptographicOperations.ZeroMemory(nonce);
        CryptographicOperations.ZeroMemory(cookie);
    }

    private static void HostedPromotionNativeReceiveMatchesScalarLifecycle()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_protocol, s_udp,
            s_shared with { SocketIoMode = SharedUdpSocketIoMode.PlatformSendReceiveBatch },
            s_admission, s_manifest);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntilEstablished(host, client);
        HostedUdpAdmissionSnapshot snapshot = host.GetSnapshot();
        Equal(1, snapshot.EstablishedSessions);
        Equal(1, snapshot.Listener.RegisteredPaths);
        True(snapshot.Listener.ReceiveCensus.UsesNativeReceiveBatching ==
            LinuxUdpBatch.IsReceiveSupported);
    }

    private static void WarmedHostedSecureDeliveryAllocatesZero()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = CreateHost(identity);
        using HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntilEstablished(host, client);
        True(host.TryGetSession(client.Connection, out HostedUdpProtocolSession? session));
        ReadOnlySpan<byte> payload = "warm"u8;

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
            "The warmed hosted secure delivery path allocated managed memory.");
    }

    private static void HostedPromotionResamplesMonotonicTimeAfterInboundPublication()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var clock = new AdvancingClock(100);
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_protocol,
            s_udp, s_shared, s_admission, clock, s_manifest);
        True(HostedUdpProtocolClient.TryCreateForTesting(
            host.LocalEndPoint, identity.PublicKey, s_protocol, s_udp, clock,
            out HostedUdpProtocolClient? client, s_manifest,
            new IPEndPoint(IPAddress.Loopback, 0)));
        using (HostedUdpProtocolClient connected = client!)
            PumpUntilEstablished(host, connected);
        Equal(1, host.GetSnapshot().EstablishedSessions);
    }

    private static void HostedManifestCookieBindingAndDeploymentSignerLifetimeAreExact()
    {
        Span<byte> key = stackalloc byte[32];
        Span<byte> nonce = stackalloc byte[ProtocolLoopbackHandshake.NonceBytes];
        Span<byte> publicKey = stackalloc byte[91];
        Span<byte> path = stackalloc byte[7];
        Span<byte> first = stackalloc byte[ProtocolLoopbackHandshake.CookieBytes];
        Span<byte> second = stackalloc byte[ProtocolLoopbackHandshake.CookieBytes];
        key.Fill(1);
        nonce.Fill(2);
        publicKey.Fill(3);
        path.Fill(4);
        ProtocolLoopbackHandshake.ComputeCookie(
            key, s_manifest, nonce, publicKey, path, 5, first);
        ProtocolLoopbackHandshake.ComputeCookie(
            key, s_otherManifest, nonce, publicKey, path, 5, second);
        False(CryptographicOperations.FixedTimeEquals(first, second),
            "The stateless cookie was not literally bound to the manifest.");

        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        byte[] before = identity.PublicKey.ToArray();
        var host = CreateHost(identity);
        host.Dispose();
        True(identity.PublicKey.SequenceEqual(before),
            "The hosted listener disposed the deployment-owned signer.");
        using var secondHost = CreateHost(identity);
        True(secondHost.TryCopyServerPublicKey(
            new byte[before.Length], out int written));
        Equal(before.Length, written);
    }

    private static void HostedScalarIdleClosureRetiresAdmissionAndPeerState()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var clock = new ManualClock(100);
        using var host = CreateHost(identity, clock);
        HostedUdpProtocolClient client = CreateClient(host, identity);
        PumpUntilEstablished(host, client);
        ConnectionId retiredConnection = client.Connection;
        True(host.TryGetSession(retiredConnection, out HostedUdpProtocolSession? retained));
        HostedUdpProtocolSession serverSession = retained!;
        Equal(NetworkQueueStatus.Accepted,
            client.Peer!.TryQueue(s_messageId, stackalloc byte[] { 1, 2, 3 }));
        PumpUntilReceived(host, client, serverSession.Server);
        HostedUdpServerConnectionTrafficSnapshot activeTraffic =
            serverSession.GetTrafficSnapshot();
        True(activeTraffic.UdpPayload.PayloadBytesReceived > 0);
        True(activeTraffic.SecureProtocol.SumMessages().MessagesReceived > 0);
        False(activeTraffic.IsFinalized);
        client.Dispose(); // Abrupt peer loss: no semantic detach reaches the host.

        clock.Milliseconds = 10_101;
        host.Pump();
        Equal(UdpProtocolSessionState.Draining, serverSession.State);
        host.Pump();
        Equal(UdpProtocolSessionState.Closed, serverSession.State);
        Equal(0, host.SessionCount);
        HostedUdpAdmissionSnapshot retired = host.GetSnapshot();
        Equal(0, retired.Connections.Active);
        Equal(0, retired.Listener.RegisteredPaths);
        True(host.TryGetRetiredTraffic(0,
            out HostedUdpServerConnectionTrafficSnapshot finalizedTraffic));
        Equal(retiredConnection, finalizedTraffic.Connection);
        True(finalizedTraffic.IsFinalized);
        True(finalizedTraffic.UdpPayload.PayloadBytesReceived >=
            activeTraffic.UdpPayload.PayloadBytesReceived);
        UdpPayloadTrafficSnapshot frozenPhysical = finalizedTraffic.UdpPayload;
        NetworkTransportTrafficSnapshot frozenSecure = finalizedTraffic.SecureProtocol.Transport;

        using HostedUdpProtocolClient replacement = CreateClient(host, identity);
        PumpUntilEstablished(host, replacement);
        Equal(1, host.SessionCount);
        True(replacement.Connection != retiredConnection);
        True(host.TryGetRetiredTraffic(0, out HostedUdpServerConnectionTrafficSnapshot stillFrozen));
        Equal(frozenPhysical, stillFrozen.UdpPayload);
        Equal(frozenSecure, stillFrozen.SecureProtocol.Transport);
    }

    private static HostedUdpAdmissionHost CreateHost(
        NetworkServerIdentity identity,
        ManualClock? clock = null,
        HostedUdpAdmissionLimits? admission = null)
        => clock is null
            ? new HostedUdpAdmissionHost(
                new IPEndPoint(IPAddress.Loopback, 0), identity, s_protocol,
                s_udp, s_shared, admission ?? s_admission, s_manifest)
            : new HostedUdpAdmissionHost(
                new IPEndPoint(IPAddress.Loopback, 0), identity, s_protocol,
                s_udp, s_shared, admission ?? s_admission, clock, s_manifest);

    private static HostedUdpProtocolClient CreateClient(
        HostedUdpAdmissionHost host,
        NetworkServerIdentity identity)
    {
        True(HostedUdpProtocolClient.TryCreate(
            host.LocalEndPoint, identity.PublicKey, s_protocol, s_udp,
            out HostedUdpProtocolClient? client, s_manifest,
            new IPEndPoint(IPAddress.Loopback, 0)));
        return client!;
    }

    private static void PumpUntilPromoting(
        HostedUdpAdmissionHost host,
        HostedUdpProtocolClient client)
    {
        for (int index = 0; index < 1_000; index++)
        {
            client.Pump();
            host.Pump();
            if (host.GetSnapshot().PromotingCandidates != 0) return;
            Thread.Yield();
        }
        throw new InvalidOperationException("The hosted candidate did not enter promotion.");
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
            if (client.State == HostedUdpClientState.Failed)
                throw new InvalidOperationException("The hosted client handshake failed.");
            Thread.Yield();
        }
        throw new InvalidOperationException("The hosted client handshake did not converge.");
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

    private static HostedCandidateId AdmitRawCandidate(
        HostedUdpAdmissionHost host,
        Socket socket,
        out byte[] nonce,
        out byte[] publicKey)
    {
        using ECDiffieHellman key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        publicKey = key.ExportSubjectPublicKeyInfo();
        nonce = RandomNumberGenerator.GetBytes(ProtocolLoopbackHandshake.NonceBytes);
        byte[] cookie = RequestCookie(host, socket, nonce, publicKey);
        SendKeyExchange(socket, nonce, publicKey, cookie);
        host.Pump();
        return GetCandidateId(host);
    }

    private static HostedCandidateId GetCandidateId(HostedUdpAdmissionHost host)
    {
        True(host.TryGetCandidate(0, out HostedCandidateId candidate, out _));
        return candidate;
    }

    private static HostedCandidateState GetOnlyCandidate(
        HostedUdpAdmissionHost host,
        out HostedCandidateId candidate)
    {
        True(host.TryGetCandidate(0, out candidate, out HostedCandidateState state));
        False(host.TryGetCandidate(1, out _, out _));
        return state;
    }

    private static byte[] RequestCookie(
        HostedUdpAdmissionHost host,
        Socket socket,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey)
    {
        Span<byte> datagram = stackalloc byte[512];
        int length = ProtocolLoopbackHandshake.WriteClientHello(
            datagram, s_manifest, nonce, publicKey);
        Equal(length, socket.Send(datagram[..length]));
        Span<byte> response = stackalloc byte[512];
        for (int spins = 0; spins < 10_000; spins++)
        {
            host.Pump();
            if (!socket.Poll(0, SelectMode.SelectRead)) continue;
            int received = socket.Receive(response);
            if (ProtocolLoopbackHandshake.TryReadCookieChallenge(
                    response[..received], nonce, out ReadOnlySpan<byte> cookie))
                return cookie.ToArray();
        }
        throw new InvalidOperationException("The hosted listener did not return a cookie.");
    }

    private static void SendKeyExchange(
        Socket socket,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey,
        ReadOnlySpan<byte> cookie)
    {
        Span<byte> datagram = stackalloc byte[512];
        int length = ProtocolLoopbackHandshake.WriteClientKeyExchange(
            datagram, s_manifest, nonce, publicKey, cookie);
        Equal(length, socket.Send(datagram[..length]));
    }

    private static Socket CreateRawClient(IPEndPoint server)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Connect(server);
        socket.Blocking = false;
        return socket;
    }

    private static void DrainRaw(Socket socket)
    {
        Span<byte> scratch = stackalloc byte[512];
        while (socket.Poll(0, SelectMode.SelectRead)) _ = socket.Receive(scratch);
    }

    private static int ReceiveRaw(Socket socket, Span<byte> destination)
    {
        for (int spins = 0; spins < 10_000; spins++)
        {
            if (socket.Poll(0, SelectMode.SelectRead))
                return socket.Receive(destination);
            Thread.Yield();
        }
        throw new InvalidOperationException("The expected hosted datagram did not arrive.");
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

    private sealed class AdvancingClock : INetworkMonotonicClock
    {
        private ulong _milliseconds;
        internal AdvancingClock(ulong milliseconds) => _milliseconds = milliseconds;
        public ulong Milliseconds => _milliseconds++;
    }
}
