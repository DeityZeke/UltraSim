using System.Runtime.CompilerServices;
using UltraSim.Network;

internal static class N02FoundationTests
{
    internal static void RunAll()
    {
        Run("KernelLimitsAreExplicitAndBounded", KernelLimitsAreExplicitAndBounded);
        Run("RuntimeIdentitiesAreFixedWidthAndFailAtWrap", RuntimeIdentitiesAreFixedWidthAndFailAtWrap);
        Run("ConnectionTableIsGenerationalStatefulAndIsolated", ConnectionTableIsGenerationalStatefulAndIsolated);
        Run("PacketPoolOwnershipTokensRejectStaleDoubleAndForeignUse", PacketPoolOwnershipTokensRejectStaleDoubleAndForeignUse);
        Run("ProducerOwnedRingIsBoundedAndThreadOwned", ProducerOwnedRingIsBoundedAndThreadOwned);
        Run("PacketReaderWriterAreCanonicalAndTransactional", PacketReaderWriterAreCanonicalAndTransactional);
        Run("PacketFramingRejectsTruncationAndTrailingBytes", PacketFramingRejectsTruncationAndTrailingBytes);
        Run("GeneratedCodecMeasuresWritesAndReadsExactly", GeneratedCodecMeasuresWritesAndReadsExactly);
        Run("ProtocolManifestIsStableAndRejectsCollisions", ProtocolManifestIsStableAndRejectsCollisions);
        Run("DeterministicDatagramEndpointIsBoundedTransactionalAndReproducible", DatagramEndpointIsBoundedAndReproducible);
        Run("N02AdmittedHotPathsAllocateZero", AdmittedHotPathsAllocateZero);
    }

    private static void KernelLimitsAreExplicitAndBounded()
    {
        NetworkKernelLimits limits = NetworkKernelLimits.Default;
        True(limits.EstimateReservedPayloadBytes() > 0);
        Throws<ArgumentOutOfRangeException>(() => _ = (limits with { MaximumConnections = 0 }).EstimateReservedPayloadBytes());
        var datagrams = DeterministicDatagramLimits.Default;
        True(datagrams.EstimateReservedPayloadBytes() > 0);
        Throws<ArgumentOutOfRangeException>(() => _ = (datagrams with { MaximumDatagramBytes = 0 }).EstimateReservedPayloadBytes());

        var small = new NetworkKernelLimits(2, 4, 256, 4, 4, 2);
        using var first = new NetworkKernel(small);
        using var second = new NetworkKernel(small);
        True(first.Connections.TryOpenValidated(out ConnectionId firstConnection));
        True(second.Connections.TryOpenValidated(out ConnectionId secondConnection));
        Equal(firstConnection, secondConnection);
        Equal(1, first.GetSnapshot().ActiveConnections);
        Equal(1, second.GetSnapshot().ActiveConnections);
        True(first.GetSnapshot().ReservedBytes > 0);
        True(first.Connections.TryAdvance(firstConnection, NetworkConnectionState.Closed));
        True(first.Connections.TryReleaseClosed(firstConnection));
        True(second.Connections.TryAdvance(secondConnection, NetworkConnectionState.Closed));
        True(second.Connections.TryReleaseClosed(secondConnection));
    }

    private static void RuntimeIdentitiesAreFixedWidthAndFailAtWrap()
    {
        Equal(4, Unsafe.SizeOf<SecurityEpoch>());
        Equal(8, Unsafe.SizeOf<PacketNumber>());
        Equal(8, Unsafe.SizeOf<LaneSequence>());
        Equal(8, Unsafe.SizeOf<PacketBufferToken>());
        False(new SecurityEpoch(uint.MaxValue).TryNext(out _));
        False(new PacketNumber(ulong.MaxValue).TryNext(out _));
        False(new LaneSequence(ulong.MaxValue).TryNext(out _));
        True(new SecurityEpoch(1).TryNext(out SecurityEpoch epoch));
        Equal(new SecurityEpoch(2), epoch);
    }

    private static void ConnectionTableIsGenerationalStatefulAndIsolated()
    {
        var first = new NetworkConnectionTable(1);
        var second = new NetworkConnectionTable(1);
        True(first.TryOpenValidated(out ConnectionId connection));
        True(second.TryOpenValidated(out ConnectionId isolated));
        Equal(connection, isolated);
        True(first.TryGetState(connection, out NetworkConnectionState state));
        Equal(NetworkConnectionState.CookieValidated, state);
        True(first.TryAdvance(connection, NetworkConnectionState.KeyExchange));
        True(first.TryAdvance(connection, NetworkConnectionState.ProtocolValidated));
        True(first.TryAdvance(connection, NetworkConnectionState.Established));
        False(first.TryAdvance(connection, NetworkConnectionState.KeyExchange));
        True(first.TryTakeLaneSequence(connection, NetworkLane.Control, out LaneSequence zero));
        True(first.TryTakeLaneSequence(connection, NetworkLane.Control, out LaneSequence one));
        Equal(0UL, zero.Value);
        Equal(1UL, one.Value);
        True(first.TryRotateSecurityEpoch(connection, out SecurityEpoch rotated));
        Equal(2U, rotated.Value);
        True(first.TryAdvance(connection, NetworkConnectionState.Draining));
        True(first.TryAdvance(connection, NetworkConnectionState.Closed));
        True(first.TryReleaseClosed(connection));
        False(first.TryGetState(connection, out _));
        True(first.TryOpenValidated(out ConnectionId reused));
        Equal(connection.Slot, reused.Slot);
        Equal(connection.Generation + 1, reused.Generation);
        False(first.TryGetState(connection, out _));
        True(second.TryGetState(isolated, out _));
    }

    private static void PacketPoolOwnershipTokensRejectStaleDoubleAndForeignUse()
    {
        var pool = new PacketBufferPool(1, 256);
        True(pool.TryRent(PacketBufferOwner.Application, out PacketBufferToken first));
        False(pool.TryRent(PacketBufferOwner.Application, out _));
        Span<byte> bytes = pool.GetWritableSpan(first, PacketBufferOwner.Application);
        bytes[0] = 91;
        True(pool.TryTransfer(first, PacketBufferOwner.Application, PacketBufferOwner.Assembly));
        False(pool.TryTransfer(first, PacketBufferOwner.Application, PacketBufferOwner.Transport));
        Throws<InvalidOperationException>(() => pool.GetWritableSpan(first, PacketBufferOwner.Application));
        Throws<InvalidOperationException>(() => pool.Dispose());
        True(pool.TryReturn(first, PacketBufferOwner.Assembly));
        False(pool.TryReturn(first, PacketBufferOwner.Assembly));
        True(pool.TryRent(PacketBufferOwner.Receive, out PacketBufferToken reused));
        False(first == reused);
        Equal((byte)0, pool.GetWritableSpan(reused, PacketBufferOwner.Receive)[0]);
        True(pool.TryReturn(reused, PacketBufferOwner.Receive));
        pool.Dispose();
        Throws<ObjectDisposedException>(() => pool.TryRent(PacketBufferOwner.Application, out _));
    }

    private static void ProducerOwnedRingIsBoundedAndThreadOwned()
    {
        int thread = Environment.CurrentManagedThreadId;
        var ring = new ProducerOwnedRing<uint>(2, thread, thread);
        True(ring.TryEnqueue(10));
        True(ring.TryEnqueue(20));
        False(ring.TryEnqueue(30));
        True(ring.TryDequeue(out uint first));
        True(ring.TryDequeue(out uint second));
        Equal(10U, first);
        Equal(20U, second);
        False(ring.TryDequeue(out _));
        Exception? failure = null;
        var foreign = new Thread(() =>
        {
            try { ring.TryEnqueue(1); }
            catch (Exception exception) { failure = exception; }
        });
        foreign.Start();
        foreign.Join();
        True(failure is InvalidOperationException);

        int producerId = 0;
        int consumerId = 0;
        Exception? producerFailure = null;
        Exception? consumerFailure = null;
        ProducerOwnedRing<uint>? concurrent = null;
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);
        var producer = new Thread(() =>
        {
            producerId = Environment.CurrentManagedThreadId;
            ready.Signal();
            start.Wait();
            try
            {
                for (uint value = 1; value <= 100_000; value++)
                {
                    var spin = new SpinWait();
                    while (!concurrent!.TryEnqueue(value)) spin.SpinOnce();
                }
            }
            catch (Exception exception) { producerFailure = exception; }
        });
        var consumer = new Thread(() =>
        {
            consumerId = Environment.CurrentManagedThreadId;
            ready.Signal();
            start.Wait();
            try
            {
                for (uint expected = 1; expected <= 100_000; expected++)
                {
                    var spin = new SpinWait();
                    uint actual;
                    while (!concurrent!.TryDequeue(out actual)) spin.SpinOnce();
                    Equal(expected, actual);
                }
            }
            catch (Exception exception) { consumerFailure = exception; }
        });
        producer.Start();
        consumer.Start();
        ready.Wait();
        concurrent = new ProducerOwnedRing<uint>(257, producerId, consumerId);
        start.Set();
        producer.Join();
        consumer.Join();
        True(producerFailure is null);
        True(consumerFailure is null);
        Equal(0, concurrent.Count);
    }

    private static void PacketReaderWriterAreCanonicalAndTransactional()
    {
        Span<byte> bytes = stackalloc byte[128];
        var writer = new NetworkPacketWriter(bytes);
        True(writer.TryWriteBoolean(true));
        True(writer.TryWriteUInt16(0x1234));
        True(writer.TryWriteUInt32(0x12345678));
        True(writer.TryWriteUInt64(0x0123456789ABCDEF));
        True(writer.TryWriteVarUInt64(300));
        True(writer.TryWriteBoundedUtf8("valid-✓", 32));
        var reader = new NetworkPacketReader(writer.WrittenSpan);
        True(reader.TryReadBoolean(out bool boolean));
        True(reader.TryReadUInt16(out ushort two));
        True(reader.TryReadUInt32(out uint four));
        True(reader.TryReadUInt64(out ulong eight));
        True(reader.TryReadVarUInt64(out ulong variable));
        True(reader.TryReadBoundedUtf8Bytes(32, out ReadOnlySpan<byte> text));
        True(reader.IsComplete);
        True(boolean);
        Equal((ushort)0x1234, two);
        Equal(0x12345678U, four);
        Equal(0x0123456789ABCDEFUL, eight);
        Equal(300UL, variable);
        True(NetworkCanonicalEncoding.IsCanonicalUtf8(text));

        var invalidBool = new NetworkPacketReader(new byte[] { 2 });
        False(invalidBool.TryReadBoolean(out _));
        Equal(0, invalidBool.ConsumedCount);
        var noncanonicalVarint = new NetworkPacketReader(new byte[] { 0x80, 0x00 });
        False(noncanonicalVarint.TryReadVarUInt64(out _));
        Equal(0, noncanonicalVarint.ConsumedCount);
        var invalidUtf8 = new NetworkPacketReader(new byte[] { 2, 0xC0, 0xAF });
        False(invalidUtf8.TryReadBoundedUtf8Bytes(8, out _));
        Equal(0, invalidUtf8.ConsumedCount);
        var invalidTextWriter = new NetworkPacketWriter(bytes);
        False(invalidTextWriter.TryWriteBoundedUtf8("\uD800", 8));
        Equal(0, invalidTextWriter.WrittenCount);
    }

    private static void PacketFramingRejectsTruncationAndTrailingBytes()
    {
        var header = new NetworkPacketHeader(
            1, new NetworkMessageId(1), 1, NetworkLane.Control, 0,
            new SecurityEpoch(1), new PacketNumber(9), new LaneSequence(3), 4);
        Span<byte> packet = stackalloc byte[NetworkPacketFraming.HeaderBytes + 4];
        var writer = new NetworkPacketWriter(packet);
        True(NetworkPacketFraming.TryWriteHeader(ref writer, header));
        True(writer.TryWriteUInt32(0xAABBCCDD));
        True(NetworkPacketFraming.TryReadExactPacket(packet, out NetworkPacketHeader decoded, out ReadOnlySpan<byte> payload));
        Equal(header, decoded);
        Equal(4, payload.Length);
        for (int length = 0; length < packet.Length; length++)
            False(NetworkPacketFraming.TryReadExactPacket(packet[..length], out _, out _));
        Span<byte> trailing = stackalloc byte[packet.Length + 1];
        packet.CopyTo(trailing);
        False(NetworkPacketFraming.TryReadExactPacket(trailing, out _, out _));
        trailing[0] = 0;
        False(NetworkPacketFraming.TryReadExactPacket(trailing[..packet.Length], out _, out _));
        var invalidFlags = header with { Flags = 1 };
        var invalidWriter = new NetworkPacketWriter(packet);
        False(NetworkPacketFraming.TryWriteHeader(ref invalidWriter, invalidFlags));
        Equal(0, invalidWriter.WrittenCount);
    }

    private static void GeneratedCodecMeasuresWritesAndReadsExactly()
    {
        var value = new ProtocolProbeMessage
        {
            Nonce = ulong.MaxValue,
            CapabilityHash = 0xAABBCCDD,
            RequestedVersion = 1,
            RequestManifest = true,
            PreferredLane = NetworkLane.Input,
            SignedDelta = sbyte.MinValue,
            SignedMode = SignedProtocolProbe.Negative,
        };
        True(ProtocolProbeMessageCodec.TryMeasure(in value, out int measured));
        Equal(ProtocolProbeMessageCodec.EncodedSize, measured);
        Span<byte> exact = stackalloc byte[ProtocolProbeMessageCodec.EncodedSize];
        True(ProtocolProbeMessageCodec.TryWriteExact(exact, in value));
        True(ProtocolProbeMessageCodec.TryReadExact(exact, out ProtocolProbeMessage decoded));
        Equal(value, decoded);
        Span<byte> truncatedStorage = stackalloc byte[ProtocolProbeMessageCodec.EncodedSize];
        for (int length = 0; length < exact.Length; length++)
        {
            Span<byte> truncated = truncatedStorage[..length];
            var writer = new NetworkPacketWriter(truncated);
            False(ProtocolProbeMessageCodec.TryWrite(ref writer, in value));
            Equal(0, writer.WrittenCount);
            var reader = new NetworkPacketReader(exact[..length]);
            False(ProtocolProbeMessageCodec.TryRead(ref reader, out _));
            Equal(0, reader.ConsumedCount);
        }
        Span<byte> trailing = stackalloc byte[ProtocolProbeMessageCodec.EncodedSize + 1];
        exact.CopyTo(trailing);
        False(ProtocolProbeMessageCodec.TryReadExact(trailing, out _));
        exact[14] = 2;
        False(ProtocolProbeMessageCodec.TryReadExact(exact, out _));
        exact[14] = 1;
        exact[15] = byte.MaxValue;
        False(ProtocolProbeMessageCodec.TryReadExact(exact, out _));
        exact[15] = (byte)NetworkLane.Input;
        exact[17] = 0;
        exact[18] = 0;
        False(ProtocolProbeMessageCodec.TryReadExact(exact, out _));
    }

    private static void ProtocolManifestIsStableAndRejectsCollisions()
    {
        NetworkProtocolManifest manifest = CoreNetworkProtocol.Manifest;
        Equal((ushort)1, manifest.Version);
        Equal(1, manifest.Count);
        True(manifest.TryGet(new NetworkMessageId(1), out NetworkMessageDescriptor descriptor));
        Equal(ProtocolProbeMessageCodec.Descriptor, descriptor);
        False(manifest.TryGet(new NetworkMessageId(2), out _));
        NetworkMessageDescriptor duplicateId = descriptor with { CanonicalKey = "different" };
        Throws<ArgumentException>(() => NetworkProtocolManifest.Create(1, new[] { descriptor, duplicateId }));
        NetworkMessageDescriptor duplicateKey = descriptor with { Id = new NetworkMessageId(2) };
        Throws<ArgumentException>(() => NetworkProtocolManifest.Create(1, new[] { descriptor, duplicateKey }));
        NetworkProtocolManifest nextVersion = NetworkProtocolManifest.Create(2, new[] { descriptor });
        False(manifest.Fingerprint == nextVersion.Fingerprint);
    }

    private static void DatagramEndpointIsBoundedAndReproducible()
    {
        var insufficient = new DeterministicDatagramLink(1, 32, new(7, 0, 1000, 0, 0));
        Equal(DatagramSendStatus.CapacityExceeded, insufficient.A.TrySend(new byte[] { 1 }));
        Equal(0UL, insufficient.GetSnapshot().Submitted);

        var first = new DeterministicDatagramLink(8, 32, new(7, 0, 1000, 1000, 0));
        var second = new DeterministicDatagramLink(8, 32, new(7, 0, 1000, 1000, 0));
        ReadOnlySpan<byte> input = new byte[] { 1, 2, 3 };
        Equal(DatagramSendStatus.Accepted, first.A.TrySend(input));
        Equal(DatagramSendStatus.Accepted, second.A.TrySend(input));
        Span<byte> firstOutput = stackalloc byte[32];
        Span<byte> secondOutput = stackalloc byte[32];
        for (int copy = 0; copy < 2; copy++)
        {
            True(first.B.TryReceive(firstOutput, out int firstLength));
            True(second.B.TryReceive(secondOutput, out int secondLength));
            Equal(firstLength, secondLength);
            True(firstOutput[..firstLength].SequenceEqual(secondOutput[..secondLength]));
            Equal((byte)0x83, firstOutput[firstLength - 1]);
        }
        False(first.B.TryReceive(firstOutput, out _));
        Equal(DatagramSendStatus.DatagramTooLarge, first.A.TrySend(new byte[33]));
        Equal(first.GetSnapshot().Duplicated, second.GetSnapshot().Duplicated);
    }

    private static void AdmittedHotPathsAllocateZero()
    {
        int thread = Environment.CurrentManagedThreadId;
        var pool = new PacketBufferPool(4, 256);
        var ring = new ProducerOwnedRing<PacketBufferToken>(4, thread, thread);
        var link = new DeterministicDatagramLink(4, 256);
        var message = new ProtocolProbeMessage
        {
            Nonce = 1,
            CapabilityHash = 2,
            RequestedVersion = 1,
            RequestManifest = true,
            PreferredLane = NetworkLane.Control,
            SignedDelta = -1,
            SignedMode = SignedProtocolProbe.Negative,
        };
        Span<byte> receive = stackalloc byte[256];
        True(pool.TryRent(PacketBufferOwner.Application, out PacketBufferToken warm));
        True(pool.TryReturn(warm, PacketBufferOwner.Application));

        long before = GC.GetAllocatedBytesForCurrentThread();
        ulong checksum = 0;
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            True(pool.TryRent(PacketBufferOwner.Application, out PacketBufferToken token));
            Span<byte> buffer = pool.GetWritableSpan(token, PacketBufferOwner.Application);
            True(ProtocolProbeMessageCodec.TryWriteExact(buffer[..ProtocolProbeMessageCodec.EncodedSize], in message));
            True(ring.TryEnqueue(token));
            True(ring.TryDequeue(out token));
            True(pool.TryTransfer(token, PacketBufferOwner.Application, PacketBufferOwner.Transport));
            ReadOnlySpan<byte> payload = pool.GetReadOnlySpan(token, PacketBufferOwner.Transport, ProtocolProbeMessageCodec.EncodedSize);
            Equal(DatagramSendStatus.Accepted, link.A.TrySend(payload));
            True(link.B.TryReceive(receive, out int length));
            True(ProtocolProbeMessageCodec.TryReadExact(receive[..length], out ProtocolProbeMessage decoded));
            checksum += decoded.Nonce;
            True(pool.TryReturn(token, PacketBufferOwner.Transport));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated);
        Equal(1_000UL, checksum);
        pool.Dispose();
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
