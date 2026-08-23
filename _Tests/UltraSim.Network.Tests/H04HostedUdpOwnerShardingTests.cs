using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using UltraSim.Network;

internal static class H04HostedUdpOwnerShardingTests
{
    private static readonly NetworkMessageId s_messageId = new(1);
    private static readonly NetworkProtocolManifest s_manifest = NetworkProtocolManifest.Create(
        1,
        [new NetworkMessageDescriptor(s_messageId,
            "ultrasim.test.owner-shard", 1, NetworkLane.Control, 32, 0xA041)]);
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
    private static readonly HostedUdpOwnerShardingLimits s_limits = new(
        MaximumOwners: 2,
        MaximumConnections: 8,
        MaximumPacketSlots: 16,
        MaximumQueuedPacketsPerOwner: 4,
        MaximumDatagramBytes: 512);

    internal static void RunAll()
    {
        Run("HostedOwnerAndPacketIdentitiesAreGenerationFenced", HostedOwnerAndPacketIdentitiesAreGenerationFenced);
        Run("HostedOwnerIngressEgressTransfersAreExclusive", HostedOwnerIngressEgressTransfersAreExclusive);
        Run("HostedOwnerQueuesAndPacketCapacityFailTransactionally", HostedOwnerQueuesAndPacketCapacityFailTransactionally);
        Run("HostedOwnerFailureReclaimsAndFencesSlotReuse", HostedOwnerFailureReclaimsAndFencesSlotReuse);
        Run("HostedConnectionMigrationAndReuseAreFenced", HostedConnectionMigrationAndReuseAreFenced);
        Run("HostedClearPathConnectionRoutesButDoesNotAuthenticate", HostedClearPathConnectionRoutesButDoesNotAuthenticate);
        Run("HostedSeparateConnectionOwnerProcessesSecureProtocol", HostedSeparateConnectionOwnerProcessesSecureProtocol);
        Run("WarmedHostedOwnerHandoffAllocatesZero", WarmedHostedOwnerHandoffAllocatesZero);
    }

    private static void HostedOwnerAndPacketIdentitiesAreGenerationFenced()
    {
        Equal(8, Unsafe.SizeOf<HostedConnectionOwnerId>());
        Equal(8, Unsafe.SizeOf<HostedPacketTransferId>());
        False(default(HostedConnectionOwnerId).IsValid);
        False(default(HostedPacketTransferId).IsValid);
        False(new HostedConnectionOwnerId(1, 1) == new HostedConnectionOwnerId(1, 2));
        False(new HostedPacketTransferId(2, 1) == new HostedPacketTransferId(2, 2));
        Throws<ArgumentOutOfRangeException>(() => _ = new HostedConnectionOwnerId(0, 0));
        Throws<ArgumentOutOfRangeException>(() => _ = new HostedPacketTransferId(0, 0));
        True(s_limits.EstimateReservedBytes() >
            (long)s_limits.MaximumPacketSlots * s_limits.MaximumDatagramBytes);
    }

    private static void HostedOwnerIngressEgressTransfersAreExclusive()
    {
        using var handoff = new HostedUdpConnectionOwnerHandoff(s_limits);
        True(handoff.TryOpenOwner(
            Environment.CurrentManagedThreadId, out HostedConnectionOwnerId owner));
        var connection = new ConnectionId(1, 1);
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryBindConnection(connection, owner));
        ReadOnlySpan<byte> ingress = "listener-to-owner"u8;
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferIngress(connection, new HostedPathGeneration(1),
                HostedPacketTransferKind.SecureProtocol, ingress));
        True(handoff.TryAcquireIngress(owner, out HostedPacketTransfer inbound));
        Equal(connection, inbound.Connection);
        Equal(owner, inbound.Owner);
        Equal(HostedPacketTransferKind.SecureProtocol, inbound.Kind);
        True(handoff.GetIngressPayload(owner, inbound).SequenceEqual(ingress));
        True(handoff.TryReleaseIngress(owner, inbound.Id));
        False(handoff.TryReleaseIngress(owner, inbound.Id),
            "A released packet lease was released twice.");

        ReadOnlySpan<byte> egress = "owner-to-listener"u8;
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferEgress(owner, connection, new HostedPathGeneration(1),
                HostedPacketTransferKind.SecureProtocol, egress));
        True(handoff.TryAcquireEgress(owner, out HostedPacketTransfer outbound));
        True(handoff.GetEgressPayload(outbound).SequenceEqual(egress));
        True(handoff.TryReleaseEgress(outbound.Id));
        False(handoff.TryReleaseEgress(outbound.Id),
            "A returned listener packet lease was released twice.");

        HostedUdpOwnerHandoffSnapshot snapshot = handoff.GetSnapshot();
        Equal(0, snapshot.RentedPackets);
        Equal(1UL, snapshot.IngressAccepted);
        Equal(1UL, snapshot.EgressAccepted);
        Equal(2UL, snapshot.Released);
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryUnbindConnection(connection, owner));
        Equal(HostedPacketHandoffStatus.Accepted, handoff.TryCloseOwner(owner));
    }

    private static void HostedOwnerQueuesAndPacketCapacityFailTransactionally()
    {
        var limits = s_limits with
        {
            MaximumOwners = 1,
            MaximumConnections = 1,
            MaximumPacketSlots = 1,
            MaximumQueuedPacketsPerOwner = 1,
        };
        using var handoff = new HostedUdpConnectionOwnerHandoff(limits);
        True(handoff.TryOpenOwner(
            Environment.CurrentManagedThreadId, out HostedConnectionOwnerId owner));
        var connection = new ConnectionId(0, 1);
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryBindConnection(connection, owner));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferIngress(connection, default,
                HostedPacketTransferKind.SecureProtocol, "first"u8));
        Equal(HostedPacketHandoffStatus.DestinationFull,
            handoff.TryOfferIngress(connection, default,
                HostedPacketTransferKind.SecureProtocol, "second"u8));
        True(handoff.TryAcquireIngress(owner, out HostedPacketTransfer held));
        Equal(HostedPacketHandoffStatus.PacketCapacityExceeded,
            handoff.TryOfferEgress(owner, connection, default,
                HostedPacketTransferKind.SecureProtocol, "reply"u8));
        Equal(HostedPacketHandoffStatus.ConnectionBusy,
            handoff.TryMigrateConnection(connection, owner, owner));
        True(handoff.TryReleaseIngress(owner, held.Id));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferEgress(owner, connection, default,
                HostedPacketTransferKind.SecureProtocol, "first-reply"u8));
        Equal(HostedPacketHandoffStatus.DestinationFull,
            handoff.TryOfferEgress(owner, connection, default,
                HostedPacketTransferKind.SecureProtocol, "second-reply"u8));
        True(handoff.TryAcquireEgress(owner, out HostedPacketTransfer reply));
        True(handoff.TryReleaseEgress(reply.Id));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryUnbindConnection(connection, owner));
        Equal(HostedPacketHandoffStatus.Accepted, handoff.TryCloseOwner(owner));
        HostedUdpOwnerHandoffSnapshot snapshot = handoff.GetSnapshot();
        Equal(0, snapshot.RentedPackets);
        Equal(2UL, snapshot.DestinationFullDrops);
        Equal(1UL, snapshot.PacketCapacityDrops);
    }

    private static void HostedOwnerFailureReclaimsAndFencesSlotReuse()
    {
        var limits = s_limits with { MaximumOwners = 1 };
        using var handoff = new HostedUdpConnectionOwnerHandoff(limits);
        True(handoff.TryOpenOwner(
            Environment.CurrentManagedThreadId, out HostedConnectionOwnerId failed));
        var connection = new ConnectionId(2, 3);
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryBindConnection(connection, failed));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferIngress(connection, default,
                HostedPacketTransferKind.SecureProtocol, "orphan"u8));
        True(handoff.TryAcquireIngress(failed, out _));
        Equal(HostedPacketHandoffStatus.ConnectionBusy,
            handoff.TryCloseOwner(failed));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.ForceCloseFailedOwner(failed));
        HostedUdpOwnerHandoffSnapshot reclaimed = handoff.GetSnapshot();
        Equal(0, reclaimed.RentedPackets);
        Equal(0, reclaimed.BoundConnections);
        Equal(1UL, reclaimed.ForcedOwnerReclaims);
        Equal(HostedPacketHandoffStatus.DestinationClosed,
            handoff.TryBindConnection(new ConnectionId(1, 1), failed));

        True(handoff.TryOpenOwner(
            Environment.CurrentManagedThreadId, out HostedConnectionOwnerId replacement));
        Equal(failed.Slot, replacement.Slot);
        True(replacement.Generation > failed.Generation);
        Equal(HostedPacketHandoffStatus.StaleOwner,
            handoff.TryBindConnection(new ConnectionId(1, 1), failed));
        Equal(HostedPacketHandoffStatus.Accepted, handoff.TryCloseOwner(replacement));
    }

    private static void HostedConnectionMigrationAndReuseAreFenced()
    {
        using var handoff = new HostedUdpConnectionOwnerHandoff(s_limits);
        int thread = Environment.CurrentManagedThreadId;
        True(handoff.TryOpenOwner(thread, out HostedConnectionOwnerId first));
        True(handoff.TryOpenOwner(thread, out HostedConnectionOwnerId second));
        var original = new ConnectionId(3, 4);
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryBindConnection(original, first));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferIngress(original, default,
                HostedPacketTransferKind.SecureProtocol, "pending"u8));
        Equal(HostedPacketHandoffStatus.ConnectionBusy,
            handoff.TryMigrateConnection(original, first, second));
        True(handoff.TryAcquireIngress(first, out HostedPacketTransfer transfer));
        True(handoff.TryReleaseIngress(first, transfer.Id));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryMigrateConnection(original, first, second));
        Equal(HostedPacketHandoffStatus.StaleOwner,
            handoff.TryMigrateConnection(original, first, second));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferIngress(original, default,
                HostedPacketTransferKind.SecureProtocol, "moved"u8));
        False(handoff.TryAcquireIngress(first, out _));
        True(handoff.TryAcquireIngress(second, out transfer));
        True(handoff.TryReleaseIngress(second, transfer.Id));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryUnbindConnection(original, second));

        var reused = new ConnectionId(original.Slot, original.Generation + 1);
        Equal(HostedPacketHandoffStatus.UnknownConnection,
            handoff.TryOfferIngress(original, default,
                HostedPacketTransferKind.SecureProtocol, "stale"u8));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryBindConnection(reused, first));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryUnbindConnection(reused, first));
        Equal(HostedPacketHandoffStatus.Accepted, handoff.TryCloseOwner(first));
        Equal(HostedPacketHandoffStatus.Accepted, handoff.TryCloseOwner(second));
    }

    private static void HostedClearPathConnectionRoutesButDoesNotAuthenticate()
    {
        using var handoff = new HostedUdpConnectionOwnerHandoff(s_limits);
        True(handoff.TryOpenOwner(
            Environment.CurrentManagedThreadId, out HostedConnectionOwnerId owner));
        var connection = new ConnectionId(4, 7);
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryBindConnection(connection, owner));
        Span<byte> key = stackalloc byte[HostedUdpPathRebindCodec.KeyBytes];
        Span<byte> nonce = stackalloc byte[HostedUdpPathRebindCodec.NonceBytes];
        Span<byte> request = stackalloc byte[HostedUdpPathRebindCodec.RequestBytes];
        Span<byte> observedPath = stackalloc byte[HostedUdpPathRebindCodec.PathBindingBytes];
        Span<byte> challenge = stackalloc byte[HostedUdpPathRebindCodec.ChallengeBytes];
        Span<byte> challengeFlight = stackalloc byte[HostedUdpPathRebindCodec.PathFlightBytes];
        key.Fill(0x44);
        nonce.Fill(0x55);
        observedPath.Fill(0x66);
        challenge.Fill(0x77);
        _ = HostedUdpPathRebindCodec.WriteRequest(
            request, s_manifest, connection, 9, nonce, key);

        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferPathControl(s_manifest, request, observedPath));
        True(handoff.TryAcquireIngress(owner, out HostedPacketTransfer transfer));
        Equal(HostedPacketTransferKind.PathControl, transfer.Kind);
        Span<byte> copiedPath = stackalloc byte[HostedUdpPathRebindCodec.PathBindingBytes];
        True(handoff.TryCopyIngressPathBinding(owner, transfer, copiedPath));
        True(copiedPath.SequenceEqual(observedPath));
        ReadOnlySpan<byte> routed = handoff.GetIngressPayload(owner, transfer);
        True(HostedUdpPathRebindCodec.TryReadRequest(
            routed, s_manifest, connection, key, out ulong attempt, out _));
        _ = HostedUdpPathRebindCodec.WritePathFlight(
            challengeFlight, HostedPathDatagramKind.Challenge, s_manifest,
            connection, attempt, new HostedPathGeneration(2), nonce, challenge,
            copiedPath, key);
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferEgress(owner, connection, new HostedPathGeneration(1),
                HostedPacketTransferKind.PathControl, challengeFlight, copiedPath));
        True(handoff.TryReleaseIngress(owner, transfer.Id));
        True(handoff.TryAcquireEgress(owner, out HostedPacketTransfer outbound));
        Equal(HostedPacketTransferKind.PathControl, outbound.Kind);
        True(handoff.TryCopyEgressPathBinding(outbound, copiedPath));
        True(copiedPath.SequenceEqual(observedPath));
        True(HostedUdpPathRebindCodec.TryReadPathFlight(
            handoff.GetEgressPayload(outbound), HostedPathDatagramKind.Challenge,
            s_manifest, connection, key, out _, out _, out _, out _, out _));
        True(handoff.TryReleaseEgress(outbound.Id));

        request[^1] ^= 0x80;
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferPathControl(s_manifest, request, observedPath));
        True(handoff.TryAcquireIngress(owner, out transfer));
        False(HostedUdpPathRebindCodec.TryReadRequest(
            handoff.GetIngressPayload(owner, transfer),
            s_manifest, connection, key, out _, out _),
            "The listener-side clear ConnectionId was treated as path authentication.");
        True(handoff.TryReleaseIngress(owner, transfer.Id));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryUnbindConnection(connection, owner));
        Equal(HostedPacketHandoffStatus.Accepted, handoff.TryCloseOwner(owner));
    }

    private static void HostedSeparateConnectionOwnerProcessesSecureProtocol()
    {
        using var handoff = new HostedUdpConnectionOwnerHandoff(s_limits);
        var connection = new ConnectionId(5, 2);
        var epoch = new SecurityEpoch(3);
        var path = new HostedPathGeneration(1);
        byte[] master = new byte[ProtocolLoopbackHandshake.MasterSecretBytes];
        master.AsSpan().Fill(0x7A);
        var link = new DeterministicDatagramLink(32, 512);
        using var client = new NetworkProtocolPeer(
            link.A, s_manifest, s_protocol, new DeterministicDatagramLimits(32, 512),
            connection, epoch, master, sendsClientToServer: true);
        using var ownerKnown = new ManualResetEventSlim(false);
        using var startOwner = new ManualResetEventSlim(false);
        using var ownerReady = new ManualResetEventSlim(false);
        using var process = new ManualResetEventSlim(false);
        using var processed = new ManualResetEventSlim(false);
        using var stop = new ManualResetEventSlim(false);
        HostedConnectionOwnerId owner = default;
        int ownerThreadId = 0;
        Exception? ownerFailure = null;
        bool serverReceived = false;

        var thread = new Thread(() =>
        {
            try
            {
                ownerThreadId = Environment.CurrentManagedThreadId;
                ownerKnown.Set();
                startOwner.Wait();
                using var endpoint = new HostedUdpConnectionOwnerEndpoint(
                    handoff, owner, connection, path, 512);
                using var server = new NetworkProtocolPeer(
                    endpoint, s_manifest, s_protocol,
                    new DeterministicDatagramLimits(32, 512),
                    connection, epoch, master, sendsClientToServer: false);
                ownerReady.Set();
                process.Wait();
                _ = server.ProcessInbound(1);
                if (server.TryPeekReceived(
                        out NetworkDeliveredMessage delivered,
                        out ReadOnlySpan<byte> payload) &&
                    delivered.MessageId == s_messageId && payload.SequenceEqual("request"u8))
                {
                    serverReceived = server.TryConsumeReceived();
                    if (server.TryQueue(s_messageId, "response"u8) !=
                        NetworkQueueStatus.Accepted)
                        throw new InvalidOperationException("The owner could not queue its reply.");
                    server.Emit(2);
                }
                processed.Set();
                stop.Wait();
            }
            catch (Exception exception)
            {
                ownerFailure = exception;
                ownerReady.Set();
                processed.Set();
            }
        }) { IsBackground = true, Name = "UltraSim.H0.4.Owner" };
        thread.Start();
        True(ownerKnown.Wait(TimeSpan.FromSeconds(5)));
        True(handoff.TryOpenOwner(ownerThreadId, out owner));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryBindConnection(connection, owner));
        startOwner.Set();
        True(ownerReady.Wait(TimeSpan.FromSeconds(5)));
        if (ownerFailure is not null) throw ownerFailure;

        Equal(NetworkQueueStatus.Accepted, client.TryQueue(s_messageId, "request"u8));
        client.Emit(1);
        Span<byte> datagram = stackalloc byte[512];
        True(link.B.TryReceive(datagram, out int length));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferIngress(connection, path,
                HostedPacketTransferKind.SecureProtocol, datagram[..length]));
        process.Set();
        True(processed.Wait(TimeSpan.FromSeconds(5)));
        if (ownerFailure is not null) throw ownerFailure;
        True(serverReceived);
        int egressCount = 0;
        while (handoff.TryAcquireEgress(owner, out HostedPacketTransfer transfer))
        {
            Equal(DatagramSendStatus.Accepted,
                link.B.TrySend(handoff.GetEgressPayload(transfer)));
            True(handoff.TryReleaseEgress(transfer.Id));
            egressCount++;
        }
        True(egressCount != 0);
        _ = client.ProcessInbound(3);
        True(client.TryPeekReceived(
            out NetworkDeliveredMessage response, out ReadOnlySpan<byte> responsePayload));
        Equal(s_messageId, response.MessageId);
        True(responsePayload.SequenceEqual("response"u8));
        True(client.TryConsumeReceived());

        stop.Set();
        True(thread.Join(TimeSpan.FromSeconds(5)));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryUnbindConnection(connection, owner));
        Equal(HostedPacketHandoffStatus.Accepted, handoff.TryCloseOwner(owner));
        CryptographicOperations.ZeroMemory(master);
    }

    private static void WarmedHostedOwnerHandoffAllocatesZero()
    {
        using var handoff = new HostedUdpConnectionOwnerHandoff(s_limits);
        True(handoff.TryOpenOwner(
            Environment.CurrentManagedThreadId, out HostedConnectionOwnerId owner));
        var connection = new ConnectionId(6, 1);
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryBindConnection(connection, owner));
        ReadOnlySpan<byte> payload = "allocation-check"u8;
        for (int warm = 0; warm < 2_000; warm++)
            _ = TransferRoundTrip(handoff, owner, connection, payload);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (int prime = 0; prime < 1_000; prime++)
            _ = TransferRoundTrip(handoff, owner, connection, payload);
        long before = GC.GetAllocatedBytesForCurrentThread();
        ulong checksum = 0;
        for (int iteration = 0; iteration < 1_000; iteration++)
            checksum += TransferRoundTrip(handoff, owner, connection, payload);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated,
            $"The warmed bidirectional owner handoff allocated {allocated} managed bytes.");
        True(checksum != 0);
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryUnbindConnection(connection, owner));
        Equal(HostedPacketHandoffStatus.Accepted, handoff.TryCloseOwner(owner));
    }

    private static ulong TransferRoundTrip(
        HostedUdpConnectionOwnerHandoff handoff,
        HostedConnectionOwnerId owner,
        ConnectionId connection,
        ReadOnlySpan<byte> payload)
    {
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferIngress(connection, default,
                HostedPacketTransferKind.SecureProtocol, payload));
        True(handoff.TryAcquireIngress(owner, out HostedPacketTransfer inbound));
        ulong checksum = handoff.GetIngressPayload(owner, inbound)[0];
        True(handoff.TryReleaseIngress(owner, inbound.Id));
        Equal(HostedPacketHandoffStatus.Accepted,
            handoff.TryOfferEgress(owner, connection, default,
                HostedPacketTransferKind.SecureProtocol, payload));
        True(handoff.TryAcquireEgress(owner, out HostedPacketTransfer outbound));
        checksum += handoff.GetEgressPayload(outbound)[0];
        True(handoff.TryReleaseEgress(outbound.Id));
        return checksum;
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
}
