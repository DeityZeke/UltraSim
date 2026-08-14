using System.Security.Cryptography;

namespace UltraSim.Network;

public enum NetworkSecureDatagramKind : byte
{
    Data = 1,
    // Packet-level transport delivery evidence; never application/baseline proof.
    Acknowledgement = 2,
}

public readonly record struct NetworkSecureDatagramHeader(
    ushort ProtocolVersion,
    ConnectionId Connection,
    SecurityEpoch SecurityEpoch,
    PacketNumber PacketNumber,
    NetworkSecureDatagramKind Kind,
    ushort CiphertextLength);

/// <summary>Authenticated outer framing. The header is associated data and is never trusted before tag validation.</summary>
public static class NetworkSecureDatagramFraming
{
    public const ushort Magic = 0x5553;
    public const int HeaderBytes = 28;
    public const int TagBytes = 16;
    public const int NonceBytes = 12;

    public static int MaximumPlaintextBytes(int maximumDatagramBytes) =>
        maximumDatagramBytes - HeaderBytes - TagBytes;

    public static bool TryWriteHeader(ref NetworkPacketWriter writer, in NetworkSecureDatagramHeader header)
    {
        if (header.ProtocolVersion == 0 || !header.Connection.IsValid || !header.SecurityEpoch.IsValid ||
            header.Kind is not NetworkSecureDatagramKind.Data and not NetworkSecureDatagramKind.Acknowledgement)
            return false;
        NetworkPacketWriter staged = writer;
        if (!staged.TryWriteUInt16(Magic) ||
            !staged.TryWriteUInt16(header.ProtocolVersion) ||
            !staged.TryWriteUInt32(header.Connection.Slot) ||
            !staged.TryWriteUInt32(header.Connection.Generation) ||
            !staged.TryWriteUInt32(header.SecurityEpoch.Value) ||
            !staged.TryWriteUInt64(header.PacketNumber.Value) ||
            !staged.TryWriteByte((byte)header.Kind) ||
            !staged.TryWriteByte(0) ||
            !staged.TryWriteUInt16(header.CiphertextLength)) return false;
        writer = staged;
        return true;
    }

    public static bool TryReadExact(
        ReadOnlySpan<byte> datagram,
        out NetworkSecureDatagramHeader header,
        out ReadOnlySpan<byte> associatedData,
        out ReadOnlySpan<byte> ciphertext,
        out ReadOnlySpan<byte> tag)
    {
        if (datagram.Length < HeaderBytes + TagBytes)
        {
            header = default;
            associatedData = ciphertext = tag = default;
            return false;
        }
        var reader = new NetworkPacketReader(datagram[..HeaderBytes]);
        if (!reader.TryReadUInt16(out ushort magic) || magic != Magic ||
            !reader.TryReadUInt16(out ushort protocol) || protocol == 0 ||
            !reader.TryReadUInt32(out uint slot) ||
            !reader.TryReadUInt32(out uint generation) || generation == 0 ||
            !reader.TryReadUInt32(out uint epoch) || epoch == 0 ||
            !reader.TryReadUInt64(out ulong packet) ||
            !reader.TryReadByte(out byte kind) || kind is < 1 or > 2 ||
            !reader.TryReadByte(out byte reserved) || reserved != 0 ||
            !reader.TryReadUInt16(out ushort length) || !reader.IsComplete ||
            datagram.Length != HeaderBytes + length + TagBytes)
        {
            header = default;
            associatedData = ciphertext = tag = default;
            return false;
        }
        header = new(protocol, new ConnectionId(slot, generation), new SecurityEpoch(epoch),
            new PacketNumber(packet), (NetworkSecureDatagramKind)kind, length);
        associatedData = datagram[..HeaderBytes];
        ciphertext = datagram.Slice(HeaderBytes, length);
        tag = datagram[^TagBytes..];
        return true;
    }

    internal static void WriteNonce(SecurityEpoch epoch, PacketNumber packet, Span<byte> nonce)
    {
        if (nonce.Length != NonceBytes) throw new ArgumentException("The AES-GCM nonce must be exactly 12 bytes.", nameof(nonce));
        var writer = new NetworkPacketWriter(nonce);
        if (!writer.TryWriteUInt32(epoch.Value) || !writer.TryWriteUInt64(packet.Value))
            throw new InvalidOperationException("The fixed nonce buffer is invalid.");
    }
}

internal sealed class NetworkDirectionalCryptography : IDisposable
{
    private readonly byte[] _masterSecret;
    private readonly bool _clientToServer;
    private AesGcm _cipher;
    private uint _epoch;

    internal NetworkDirectionalCryptography(ReadOnlySpan<byte> masterSecret, bool clientToServer, SecurityEpoch epoch)
    {
        if (masterSecret.Length != 32) throw new ArgumentException("A 256-bit master secret is required.", nameof(masterSecret));
        _masterSecret = masterSecret.ToArray();
        _clientToServer = clientToServer;
        _epoch = epoch.Value;
        _cipher = CreateCipher(_masterSecret, _clientToServer, epoch);
    }

    internal SecurityEpoch Epoch => new(_epoch);

    internal bool TryAdvanceTo(SecurityEpoch epoch)
    {
        if (epoch.Value != _epoch + 1 || _epoch == uint.MaxValue) return false;
        AesGcm replacement = CreateCipher(_masterSecret, _clientToServer, epoch);
        _cipher.Dispose();
        _cipher = replacement;
        _epoch = epoch.Value;
        return true;
    }

    internal void Encrypt(PacketNumber packet, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext,
        Span<byte> tag, ReadOnlySpan<byte> associatedData)
    {
        Span<byte> nonce = stackalloc byte[NetworkSecureDatagramFraming.NonceBytes];
        NetworkSecureDatagramFraming.WriteNonce(Epoch, packet, nonce);
        _cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
    }

    internal bool TryDecrypt(PacketNumber packet, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag,
        Span<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        Span<byte> nonce = stackalloc byte[NetworkSecureDatagramFraming.NonceBytes];
        NetworkSecureDatagramFraming.WriteNonce(Epoch, packet, nonce);
        try
        {
            _cipher.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            plaintext.Clear();
            return false;
        }
    }

    internal bool TryDecryptAtEpoch(SecurityEpoch epoch, PacketNumber packet, ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag, Span<byte> plaintext, ReadOnlySpan<byte> associatedData, out bool advanced)
    {
        advanced = false;
        if (epoch.Value == _epoch)
            return TryDecrypt(packet, ciphertext, tag, plaintext, associatedData);
        if (_epoch == uint.MaxValue || epoch.Value != _epoch + 1) return false;

        using AesGcm candidate = CreateCipher(_masterSecret, _clientToServer, epoch);
        Span<byte> nonce = stackalloc byte[NetworkSecureDatagramFraming.NonceBytes];
        NetworkSecureDatagramFraming.WriteNonce(epoch, packet, nonce);
        try
        {
            candidate.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        }
        catch (AuthenticationTagMismatchException)
        {
            plaintext.Clear();
            return false;
        }
        advanced = true;
        return true;
    }

    internal bool CommitAdvance(SecurityEpoch epoch) => TryAdvanceTo(epoch);

    public void Dispose()
    {
        _cipher.Dispose();
        CryptographicOperations.ZeroMemory(_masterSecret);
    }

    private static AesGcm CreateCipher(ReadOnlySpan<byte> masterSecret, bool clientToServer, SecurityEpoch epoch)
    {
        Span<byte> key = stackalloc byte[32];
        Span<byte> info = stackalloc byte[9];
        info[0] = clientToServer ? (byte)1 : (byte)2;
        var writer = new NetworkPacketWriter(info[1..]);
        if (!writer.TryWriteUInt32(epoch.Value) || !writer.TryWriteUInt32(0x4E3033))
            throw new InvalidOperationException("The key label buffer is invalid.");
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterSecret, key, default, info);
        var cipher = new AesGcm(key, NetworkSecureDatagramFraming.TagBytes);
        CryptographicOperations.ZeroMemory(key);
        return cipher;
    }
}

internal readonly record struct NetworkPacketWindowTransition(bool Initialized, ulong Largest, ulong Mask);

// The caller assigns the meaning: authenticated packet history or admitted-data ACK history.
internal struct NetworkPacketWindow
{
    private bool _initialized;
    private ulong _largest;
    private ulong _mask;

    internal bool TryPrepare(PacketNumber packet, out NetworkPacketWindowTransition transition)
    {
        ulong value = packet.Value;
        if (!_initialized)
        {
            transition = new(true, value, 1);
            return true;
        }
        if (value > _largest)
        {
            ulong distance = value - _largest;
            transition = new(true, value, distance >= 64 ? 1 : (_mask << (int)distance) | 1UL);
            return true;
        }
        ulong age = _largest - value;
        if (age >= 64 || (_mask & (1UL << (int)age)) != 0)
        {
            transition = default;
            return false;
        }
        transition = new(true, _largest, _mask | (1UL << (int)age));
        return true;
    }

    internal void Commit(in NetworkPacketWindowTransition transition) =>
        (_initialized, _largest, _mask) = (transition.Initialized, transition.Largest, transition.Mask);

    internal bool TryGetRange(out ulong largest, out ulong mask)
    {
        largest = _largest;
        mask = _mask;
        return _initialized;
    }

    internal void Reset() => this = default;
}

internal static class NetworkProtocolDataHeader
{
    internal const int EncodedBytes = 23;
}

internal readonly record struct NetworkDataFrameHeader(
    NetworkLane Lane,
    NetworkMessageId MessageId,
    ushort MessageVersion,
    LaneSequence LaneSequence,
    ushort FragmentIndex,
    ushort FragmentCount,
    int MessageLength,
    ushort FragmentLength);

internal static class NetworkDataFrameCodec
{
    internal static bool TryWrite(ref NetworkPacketWriter writer, in NetworkDataFrameHeader header)
    {
        if ((uint)header.Lane >= NetworkLaneContracts.Count || !header.MessageId.IsValid ||
            header.MessageVersion == 0 || header.FragmentCount == 0 ||
            header.FragmentIndex >= header.FragmentCount || header.MessageLength < 0 ||
            header.FragmentLength > header.MessageLength || header.LaneSequence.Value == ulong.MaxValue) return false;
        NetworkPacketWriter staged = writer;
        if (!staged.TryWriteByte((byte)header.Lane) || !staged.TryWriteUInt16(header.MessageId.Value) ||
            !staged.TryWriteUInt16(header.MessageVersion) || !staged.TryWriteUInt64(header.LaneSequence.Value) ||
            !staged.TryWriteUInt16(header.FragmentIndex) || !staged.TryWriteUInt16(header.FragmentCount) ||
            !staged.TryWriteUInt32((uint)header.MessageLength) || !staged.TryWriteUInt16(header.FragmentLength))
            return false;
        writer = staged;
        return true;
    }

    internal static bool TryRead(ref NetworkPacketReader reader, out NetworkDataFrameHeader header)
    {
        NetworkPacketReader staged = reader;
        if (!staged.TryReadByte(out byte lane) || lane >= NetworkLaneContracts.Count ||
            !staged.TryReadUInt16(out ushort messageId) || messageId == 0 ||
            !staged.TryReadUInt16(out ushort messageVersion) || messageVersion == 0 ||
            !staged.TryReadUInt64(out ulong laneSequence) || laneSequence == ulong.MaxValue ||
            !staged.TryReadUInt16(out ushort fragmentIndex) ||
            !staged.TryReadUInt16(out ushort fragmentCount) || fragmentCount == 0 || fragmentIndex >= fragmentCount ||
            !staged.TryReadUInt32(out uint messageLength) || messageLength > int.MaxValue ||
            !staged.TryReadUInt16(out ushort fragmentLength) || fragmentLength > messageLength)
        {
            header = default;
            return false;
        }
        header = new((NetworkLane)lane, new NetworkMessageId(messageId), messageVersion,
            new LaneSequence(laneSequence), fragmentIndex, fragmentCount, (int)messageLength, fragmentLength);
        reader = staged;
        return true;
    }
}
