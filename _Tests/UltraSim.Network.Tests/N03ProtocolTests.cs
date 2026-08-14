using System.Security.Cryptography;
using UltraSim.Network;

internal static class N03ProtocolTests
{
    private static readonly NetworkProtocolLimits s_limits = new(
        16, 16, 4_096, 32, 256, 16, 2, 64, 4, 32);
    private static readonly DeterministicDatagramLimits s_datagrams = new(256, 512);
    private static readonly NetworkProtocolManifest s_manifest = CreateManifest();

    internal static void RunAll()
    {
        Run("HandshakeUsesCookiePinnedIdentityAndProtocolAgreement", HandshakeUsesCookiePinnedIdentityAndProtocolAgreement);
        Run("SecureOuterFramingIsCanonicalAndExact", SecureOuterFramingIsCanonicalAndExact);
        Run("ReliableLanesSurviveLossDuplicationCorruptionAndReorder", ReliableLanesSurviveImpairment);
        Run("UnreliableLanesApplySequencingAndCoalescing", UnreliableLanesApplySequencingAndCoalescing);
        Run("BulkFragmentationReassemblesExactlyAtSmallMtu", BulkFragmentationReassemblesExactly);
        Run("ReplayAndAuthenticationRejectBeforePublication", ReplayAndAuthenticationRejectBeforePublication);
        Run("AuthenticatedReplayIsIndependentFromDeliveryBackpressure", AuthenticatedReplayIsIndependentFromDeliveryBackpressure);
        Run("AuthenticatedRegionsRejectWithoutStatePublication", AuthenticatedRegionsRejectWithoutStatePublication);
        Run("ReceiveBackpressureRetainsReliableMessage", ReceiveBackpressureRetainsReliableMessage);
        Run("SecurityEpochRotationIsAuthenticatedAndFailClosed", SecurityEpochRotationIsAuthenticatedAndFailClosed);
        Run("ProtocolCapacitiesRejectTransactionally", ProtocolCapacitiesRejectTransactionally);
        Run("AbortiveShutdownIsBalancedAndIdempotent", AbortiveShutdownIsBalancedAndIdempotent);
        Run("N03WarmedSecureDeliveryAllocatesZero", WarmedSecureDeliveryAllocatesZero);
    }

    private static void SecureOuterFramingIsCanonicalAndExact()
    {
        var header = new NetworkSecureDatagramHeader(
            3, new ConnectionId(7, 2), new SecurityEpoch(4), new PacketNumber(9),
            NetworkSecureDatagramKind.Data, 3);
        Span<byte> datagram = stackalloc byte[
            NetworkSecureDatagramFraming.HeaderBytes + 3 + NetworkSecureDatagramFraming.TagBytes];
        var writer = new NetworkPacketWriter(datagram[..NetworkSecureDatagramFraming.HeaderBytes]);
        True(NetworkSecureDatagramFraming.TryWriteHeader(ref writer, header));
        datagram[NetworkSecureDatagramFraming.HeaderBytes] = 1;
        True(NetworkSecureDatagramFraming.TryReadExact(datagram, out NetworkSecureDatagramHeader decoded,
            out ReadOnlySpan<byte> associated, out ReadOnlySpan<byte> ciphertext, out ReadOnlySpan<byte> tag));
        Equal(header, decoded);
        Equal(NetworkSecureDatagramFraming.HeaderBytes, associated.Length);
        Equal(3, ciphertext.Length);
        Equal(NetworkSecureDatagramFraming.TagBytes, tag.Length);
        for (int length = 0; length < datagram.Length; length++)
            False(NetworkSecureDatagramFraming.TryReadExact(datagram[..length], out _, out _, out _, out _));
        Span<byte> trailing = stackalloc byte[datagram.Length + 1];
        datagram.CopyTo(trailing);
        False(NetworkSecureDatagramFraming.TryReadExact(trailing, out _, out _, out _, out _));
        datagram[25] = 1;
        False(NetworkSecureDatagramFraming.TryReadExact(datagram, out _, out _, out _, out _));
    }

    private static void HandshakeUsesCookiePinnedIdentityAndProtocolAgreement()
    {
        using var server = NetworkServerIdentity.CreateEphemeral();
        True(ProtocolLoopback.TryCreate(s_limits, s_datagrams,
            new NetworkImpairmentOptions(0xC001, 200, 100, 75, 3),
            out ProtocolLoopback? loopback, out ProtocolHandshakeSnapshot handshake,
            s_manifest, server, server.PublicKey.ToArray()));
        using (loopback!)
        {
            True(handshake.Established);
            True(handshake.CookieChallenges > 0);
            True(handshake.CookieValidations > 0);
            Equal(1UL, handshake.KeyExchanges);
            True(handshake.Connection.IsValid);
            Equal(new SecurityEpoch(1), handshake.SecurityEpoch);
        }

        using var untrusted = NetworkServerIdentity.CreateEphemeral();
        False(ProtocolLoopback.TryCreate(s_limits, s_datagrams, default,
            out ProtocolLoopback? rejected, out _, s_manifest, server,
            untrusted.PublicKey.ToArray(), maximumHandshakeSteps: 64));
        True(rejected is null);

        NetworkMessageDescriptor[] descriptors = s_manifest.Messages.ToArray();
        NetworkProtocolManifest incompatible = NetworkProtocolManifest.Create(4, descriptors);
        False(incompatible.Fingerprint == s_manifest.Fingerprint);
        False(ProtocolLoopback.TryCreate(s_limits, s_datagrams, default,
            out ProtocolLoopback? mismatch, out _, s_manifest,
            maximumHandshakeSteps: 64, serverManifest: incompatible));
        True(mismatch is null);
    }

    private static void ReliableLanesSurviveImpairment()
    {
        using ProtocolLoopback loopback = Create(new(0xBEEF, 250, 200, 100, 5));
        byte[] control = { 1, 2, 3, 4 };
        byte[] events = { 5, 6, 7 };
        byte[] bulk = Enumerable.Range(0, 1_500).Select(static value => unchecked((byte)value)).ToArray();
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Control), control));
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), events));
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Bulk), bulk));

        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 3, 20_000);
        AssertReceive(loopback.Server, NetworkLane.Control, control);
        AssertReceive(loopback.Server, NetworkLane.Event, events);
        AssertReceive(loopback.Server, NetworkLane.Bulk, bulk);
        Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 20_000);
        NetworkProtocolSnapshot client = loopback.Client.GetSnapshot();
        True(client.ReliableRetransmissions > 0);
        True(client.ReliableAcknowledgements >= 3);
    }

    private static void UnreliableLanesApplySequencingAndCoalescing()
    {
        using ProtocolLoopback loopback = Create(new(77, 0, 500, 0, 7));
        Span<byte> payload = stackalloc byte[8];
        for (ulong value = 0; value < 10; value++)
        {
            WriteUInt64(payload, value);
            Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Input), payload));
            Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.OwnerState), payload));
            Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.WorldState), payload));
        }
        byte[] disposable = { 99 };
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Disposable), disposable));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued >= 4, 1_000);

        var seen = new Dictionary<NetworkLane, ulong>();
        while (loopback.Server.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> delivered))
        {
            seen[message.Lane] = message.Lane == NetworkLane.Disposable ? delivered[0] : ReadUInt64(delivered);
            True(loopback.Server.TryConsumeReceived());
        }
        Equal(9UL, seen[NetworkLane.Input]);
        Equal(9UL, seen[NetworkLane.OwnerState]);
        Equal(9UL, seen[NetworkLane.WorldState]);
        Equal(99UL, seen[NetworkLane.Disposable]);
    }

    private static void BulkFragmentationReassemblesExactly()
    {
        var smallDatagrams = new DeterministicDatagramLimits(256, 256);
        using ProtocolLoopback loopback = Create(default, smallDatagrams);
        byte[] payload = Enumerable.Range(0, 4_000).Select(static value => unchecked((byte)(value * 31))).ToArray();
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Bulk), payload));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 5_000);
        AssertReceive(loopback.Server, NetworkLane.Bulk, payload);
        Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 5_000);
        True(loopback.Client.GetSnapshot().SentPackets > 1);
    }

    private static void ReplayAndAuthenticationRejectBeforePublication()
    {
        using ProtocolLoopback loopback = Create(new(0xDEAD, 0, 400, 75, 3));
        byte[] payload = { 4, 8, 15, 16, 23, 42 };
        for (byte iteration = 0; iteration < 64; iteration++)
        {
            payload[0] = iteration;
            Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), payload));
            Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 10_000);
            AssertReceive(loopback.Server, NetworkLane.Event, payload);
            Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 10_000);
        }
        NetworkProtocolSnapshot server = loopback.Server.GetSnapshot();
        True(server.AuthenticationFailures > 0);
        True(server.ReplayRejections > 0);
        Equal(0, server.ReceivedQueued);
    }

    private static void AuthenticatedReplayIsIndependentFromDeliveryBackpressure()
    {
        NetworkProtocolLimits limits = s_limits with { MaximumReceivedMessages = 1 };
        using ProtocolLoopback loopback = Create(new(0xA11CE, 0, 1_000, 0, 0), s_datagrams, limits);
        byte[] first = { 1 };
        byte[] blocked = { 2 };

        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), first));
        Drive(loopback, () => loopback.Server.ReceivedQueued == 1, 1_000);
        Drive(loopback, () => loopback.Client.ReliableQueued == 0, 1_000);

        NetworkProtocolSnapshot before = loopback.Server.GetSnapshot();
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), blocked));
        loopback.Pump(); // Emit the packet and its forced duplicate.
        loopback.Pump(); // Authenticate one copy, defer delivery, and reject the duplicate as replay.

        NetworkProtocolSnapshot deferred = loopback.Server.GetSnapshot();
        Equal(before.CapacityDrops + 1, deferred.CapacityDrops);
        Equal(before.ReplayRejections + 1, deferred.ReplayRejections);
        Equal(1, loopback.Server.ReceivedQueued);
        Equal(1, loopback.Client.ReliableQueued);

        AssertReceive(loopback.Server, NetworkLane.Event, first);
        Drive(loopback, () => loopback.Server.ReceivedQueued == 1, 1_000);
        AssertReceive(loopback.Server, NetworkLane.Event, blocked);
        Drive(loopback, () => loopback.Client.ReliableQueued == 0, 1_000);
    }

    private static void AuthenticatedRegionsRejectWithoutStatePublication()
    {
        var link = new DeterministicDatagramLink(s_datagrams);
        byte[] masterSecret = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var connection = new ConnectionId(7, 3);
        var epoch = new SecurityEpoch(1);
        using var sender = new NetworkProtocolPeer(
            link.A, s_manifest, s_limits, s_datagrams, connection, epoch, masterSecret,
            sendsClientToServer: true);
        using var receiver = new NetworkProtocolPeer(
            link.B, s_manifest, s_limits, s_datagrams, connection, epoch, masterSecret,
            sendsClientToServer: false);
        byte[] authenticatedInvalid = CreateAuthenticatedDatagram(
            masterSecret, connection, epoch, new PacketNumber(1), stackalloc byte[] { 0xFF });
        CryptographicOperations.ZeroMemory(masterSecret);

        byte[] expected = { 4, 8, 15, 16, 23, 42 };
        Equal(NetworkQueueStatus.Accepted, sender.TryQueue(Id(NetworkLane.Event), expected));
        sender.Emit(0);
        byte[] original = new byte[s_datagrams.MaximumDatagramBytes];
        True(link.B.TryReceive(original, out int originalLength));
        Array.Resize(ref original, originalLength);
        True(NetworkSecureDatagramFraming.TryReadExact(original, out NetworkSecureDatagramHeader header,
            out _, out ReadOnlySpan<byte> ciphertext, out _));
        True(ciphertext.Length > 0);

        byte[] headerTamper = (byte[])original.Clone();
        var headerWriter = new NetworkPacketWriter(
            headerTamper.AsSpan(0, NetworkSecureDatagramFraming.HeaderBytes));
        True(NetworkSecureDatagramFraming.TryWriteHeader(ref headerWriter,
            header with { PacketNumber = new PacketNumber(header.PacketNumber.Value + 1) }));
        True(NetworkSecureDatagramFraming.TryReadExact(headerTamper, out _, out _, out _, out _));

        byte[] ciphertextTamper = (byte[])original.Clone();
        ciphertextTamper[NetworkSecureDatagramFraming.HeaderBytes] ^= 0x40;
        byte[] tagTamper = (byte[])original.Clone();
        tagTamper[^1] ^= 0x80;

        AssertAuthenticationFailureWithoutPublication(link, receiver, headerTamper, 1);
        AssertAuthenticationFailureWithoutPublication(link, receiver, ciphertextTamper, 2);
        AssertAuthenticationFailureWithoutPublication(link, receiver, tagTamper, 3);

        NetworkProtocolSnapshot beforeInvalid = receiver.GetSnapshot();
        Equal(DatagramSendStatus.Accepted, link.A.TrySend(authenticatedInvalid));
        Equal(DatagramSendStatus.Accepted, link.A.TrySend(authenticatedInvalid));
        receiver.ProcessInbound(0);
        NetworkProtocolSnapshot afterInvalid = receiver.GetSnapshot();
        Equal(beforeInvalid.AuthenticationFailures, afterInvalid.AuthenticationFailures);
        Equal(beforeInvalid.ReplayRejections + 1, afterInvalid.ReplayRejections);
        Equal(beforeInvalid.ReceivedPackets, afterInvalid.ReceivedPackets);
        Equal(beforeInvalid.ReceivedQueued, afterInvalid.ReceivedQueued);
        Equal(beforeInvalid.ReceiveSecurityEpoch, afterInvalid.ReceiveSecurityEpoch);

        Equal(DatagramSendStatus.Accepted, link.A.TrySend(original));
        receiver.ProcessInbound(0);
        AssertReceive(receiver, NetworkLane.Event, expected);
        NetworkProtocolSnapshot accepted = receiver.GetSnapshot();
        Equal(1UL, accepted.ReceivedPackets);
        Equal(3UL, accepted.AuthenticationFailures);
        Equal(1UL, accepted.ReplayRejections);
        Equal(epoch, accepted.ReceiveSecurityEpoch);
    }

    private static byte[] CreateAuthenticatedDatagram(
        ReadOnlySpan<byte> masterSecret,
        ConnectionId connection,
        SecurityEpoch epoch,
        PacketNumber packet,
        ReadOnlySpan<byte> plaintext)
    {
        byte[] datagram = new byte[
            NetworkSecureDatagramFraming.HeaderBytes + plaintext.Length + NetworkSecureDatagramFraming.TagBytes];
        var header = new NetworkSecureDatagramHeader(
            s_manifest.Version, connection, epoch, packet, NetworkSecureDatagramKind.Data,
            checked((ushort)plaintext.Length));
        var writer = new NetworkPacketWriter(datagram.AsSpan(0, NetworkSecureDatagramFraming.HeaderBytes));
        True(NetworkSecureDatagramFraming.TryWriteHeader(ref writer, header));
        using var cryptography = new NetworkDirectionalCryptography(
            masterSecret, clientToServer: true, epoch);
        cryptography.Encrypt(packet, plaintext,
            datagram.AsSpan(NetworkSecureDatagramFraming.HeaderBytes, plaintext.Length),
            datagram.AsSpan(NetworkSecureDatagramFraming.HeaderBytes + plaintext.Length,
                NetworkSecureDatagramFraming.TagBytes),
            datagram.AsSpan(0, NetworkSecureDatagramFraming.HeaderBytes));
        return datagram;
    }

    private static void AssertAuthenticationFailureWithoutPublication(
        DeterministicDatagramLink link,
        NetworkProtocolPeer receiver,
        ReadOnlySpan<byte> datagram,
        ulong expectedFailures)
    {
        NetworkProtocolSnapshot before = receiver.GetSnapshot();
        Equal(DatagramSendStatus.Accepted, link.A.TrySend(datagram));
        receiver.ProcessInbound(0);
        NetworkProtocolSnapshot after = receiver.GetSnapshot();
        Equal(expectedFailures, after.AuthenticationFailures);
        Equal(before.ReplayRejections, after.ReplayRejections);
        Equal(before.ReceivedPackets, after.ReceivedPackets);
        Equal(before.ReceivedQueued, after.ReceivedQueued);
        Equal(before.CapacityDrops, after.CapacityDrops);
        Equal(before.SequencedDrops, after.SequencedDrops);
        Equal(before.ReceiveSecurityEpoch, after.ReceiveSecurityEpoch);
    }

    private static void ReceiveBackpressureRetainsReliableMessage()
    {
        NetworkProtocolLimits limits = s_limits with { MaximumReceivedMessages = 1 };
        using ProtocolLoopback loopback = Create(default, s_datagrams, limits);
        byte[] first = { 1 };
        byte[] second = { 2 };
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), first));
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), second));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 1_000);
        for (int step = 0; step < 64; step++) loopback.Pump();
        Equal(1, loopback.Server.GetSnapshot().ReceivedQueued);
        AssertReceive(loopback.Server, NetworkLane.Event, first);
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 1_000);
        AssertReceive(loopback.Server, NetworkLane.Event, second);
        Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 1_000);
        True(loopback.Server.GetSnapshot().CapacityDrops > 0);
    }

    private static void SecurityEpochRotationIsAuthenticatedAndFailClosed()
    {
        using ProtocolLoopback loopback = Create();
        byte[] first = { 10 };
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Control), first));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 1_000);
        AssertReceive(loopback.Server, NetworkLane.Control, first);
        Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 1_000);
        True(loopback.TryRotateSecurityEpoch());

        byte[] second = { 20 };
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Control), second));
        Equal(NetworkQueueStatus.Accepted, loopback.Server.TryQueue(Id(NetworkLane.Control), second));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1 &&
            loopback.Client.GetSnapshot().ReceivedQueued == 1, 1_000);
        AssertReceive(loopback.Server, NetworkLane.Control, second);
        AssertReceive(loopback.Client, NetworkLane.Control, second);
        Equal(new SecurityEpoch(2), loopback.Client.GetSnapshot().SendSecurityEpoch);
        Equal(new SecurityEpoch(2), loopback.Client.GetSnapshot().ReceiveSecurityEpoch);
        Equal(new SecurityEpoch(2), loopback.Server.GetSnapshot().SendSecurityEpoch);
        Equal(new SecurityEpoch(2), loopback.Server.GetSnapshot().ReceiveSecurityEpoch);
    }

    private static void ProtocolCapacitiesRejectTransactionally()
    {
        NetworkProtocolLimits limits = s_limits with { MaximumReliableMessages = 1 };
        using ProtocolLoopback loopback = Create(default, s_datagrams, limits);
        byte[] payload = { 1, 2 };
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Control), payload));
        Equal(NetworkQueueStatus.CapacityExceeded, loopback.Client.TryQueue(Id(NetworkLane.Control), payload));
        Equal(1, loopback.Client.GetSnapshot().ReliableQueued);
        byte[] tooLarge = new byte[4_097];
        Equal(NetworkQueueStatus.MessageTooLarge, loopback.Client.TryQueue(Id(NetworkLane.Bulk), tooLarge));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 1_000);
        AssertReceive(loopback.Server, NetworkLane.Control, payload);
        Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 1_000);
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Control), payload));
    }

    private static void WarmedSecureDeliveryAllocatesZero()
    {
        using ProtocolLoopback loopback = Create();
        Span<byte> payload = stackalloc byte[8];
        WriteUInt64(payload, 1);
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.OwnerState), payload));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 100);
        True(loopback.Server.TryConsumeReceived());
        for (int warm = 0; warm < 16; warm++) loopback.Pump();

        long before = GC.GetAllocatedBytesForCurrentThread();
        ulong checksum = 0;
        for (ulong iteration = 0; iteration < 1_000; iteration++)
        {
            WriteUInt64(payload, iteration);
            Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.OwnerState), payload));
            for (int pump = 0; pump < 4 && loopback.Server.GetSnapshot().ReceivedQueued == 0; pump++) loopback.Pump();
            True(loopback.Server.TryPeekReceived(out _, out ReadOnlySpan<byte> delivered));
            checksum += ReadUInt64(delivered);
            True(loopback.Server.TryConsumeReceived());
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated);
        Equal(499_500UL, checksum);
    }

    private static void AbortiveShutdownIsBalancedAndIdempotent()
    {
        ProtocolLoopback loopback = Create();
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.Bulk), new byte[2_000]));
        loopback.Pump();
        loopback.Dispose();
        loopback.Dispose();
        Throws<ObjectDisposedException>(() => loopback.Pump());
        Throws<ObjectDisposedException>(() => loopback.Client.TryQueue(Id(NetworkLane.Control), default));
    }

    private static ProtocolLoopback Create(
        NetworkImpairmentOptions impairment = default,
        DeterministicDatagramLimits? datagrams = null,
        NetworkProtocolLimits? limits = null)
    {
        True(ProtocolLoopback.TryCreate(limits ?? s_limits, datagrams ?? s_datagrams, impairment,
            out ProtocolLoopback? loopback, out _, s_manifest));
        return loopback!;
    }

    private static NetworkProtocolManifest CreateManifest()
    {
        var descriptors = new NetworkMessageDescriptor[NetworkLaneContracts.Count];
        foreach (NetworkLane lane in Enum.GetValues<NetworkLane>())
            descriptors[(int)lane] = new(new NetworkMessageId((ushort)((int)lane + 1)),
                $"n0.3.test.{lane}", 1, lane, 4_096, (ulong)lane + 100);
        return NetworkProtocolManifest.Create(3, descriptors);
    }

    private static NetworkMessageId Id(NetworkLane lane) => new((ushort)((int)lane + 1));

    private static void Drive(ProtocolLoopback loopback, Func<bool> complete, int maximumSteps)
    {
        for (int step = 0; step < maximumSteps; step++)
        {
            if (complete()) return;
            loopback.Pump();
        }
        throw new InvalidOperationException($"Protocol did not converge in {maximumSteps} steps. Snapshot: {loopback.GetSnapshot()}.");
    }

    private static void AssertReceive(NetworkProtocolPeer peer, NetworkLane expectedLane, ReadOnlySpan<byte> expected)
    {
        True(peer.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload));
        Equal(expectedLane, message.Lane);
        True(payload.SequenceEqual(expected));
        True(peer.TryConsumeReceived());
    }

    private static void WriteUInt64(Span<byte> destination, ulong value)
    {
        var writer = new NetworkPacketWriter(destination);
        True(writer.TryWriteUInt64(value));
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> source)
    {
        var reader = new NetworkPacketReader(source);
        True(reader.TryReadUInt64(out ulong value));
        True(reader.IsComplete);
        return value;
    }

    private static void Run(string name, Action body)
    {
        body();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }

    private static void False(bool condition) => True(!condition);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
