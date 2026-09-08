using System.Security.Cryptography;

namespace UltraSim.Network;

public readonly record struct ProtocolHandshakeSnapshot(
    bool Established,
    int Steps,
    ulong ClientFlights,
    ulong ServerFlights,
    ulong InvalidDatagrams,
    ulong CookieChallenges,
    ulong CookieValidations,
    ulong KeyExchanges,
    ConnectionId Connection,
    SecurityEpoch SecurityEpoch);

internal static class ProtocolLoopbackHandshake
{
    internal const byte ClientHello = 1;
    internal const byte CookieChallenge = 2;
    internal const byte ClientKeyExchange = 3;
    internal const byte ServerKeyExchange = 4;
    internal const byte ClientFinish = 5;
    internal const byte ServerFinish = 6;
    internal const int NonceBytes = 32;
    internal const int CookieBytes = 16;
    internal const int MaximumClientPublicKeyBytes = 512;
    internal const int MasterSecretBytes = 32;
    internal const int ProofBytes = 32;
    internal const int SignatureBytes = 64;
    internal const int MaximumTranscriptBytes =
        2 + 8 + NonceBytes + 2 + MaximumClientPublicKeyBytes + 2 +
        MaximumClientPublicKeyBytes + 4 + 4 + 4;

    internal static int WriteClientHello(Span<byte> destination, NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> publicKey) =>
        WriteClientKey(destination, ClientHello, manifest, nonce, publicKey, default);

    internal static int WriteClientKeyExchange(Span<byte> destination, NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> cookie) =>
        WriteClientKey(destination, ClientKeyExchange, manifest, nonce, publicKey, cookie);

    private static int WriteClientKey(Span<byte> destination, byte kind, NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> cookie)
    {
        var writer = new NetworkPacketWriter(destination);
        if (!writer.TryWriteByte(kind) || !writer.TryWriteUInt16(manifest.Version) ||
            !writer.TryWriteUInt64(manifest.Fingerprint) || !writer.TryWriteBytes(nonce) ||
            publicKey.Length > ushort.MaxValue || !writer.TryWriteUInt16((ushort)publicKey.Length) ||
            !writer.TryWriteBytes(publicKey) || (kind == ClientKeyExchange && !writer.TryWriteBytes(cookie)))
            throw new InvalidOperationException("The handshake datagram limit is too small.");
        return writer.WrittenCount;
    }

    internal static bool TryReadClientHello(ReadOnlySpan<byte> source, NetworkProtocolManifest manifest,
        out ReadOnlySpan<byte> nonce, out ReadOnlySpan<byte> publicKey) =>
        TryReadClientKey(source, ClientHello, manifest, out nonce, out publicKey, out _);

    internal static bool TryReadClientKeyExchange(ReadOnlySpan<byte> source, NetworkProtocolManifest manifest,
        out ReadOnlySpan<byte> nonce, out ReadOnlySpan<byte> publicKey, out ReadOnlySpan<byte> cookie) =>
        TryReadClientKey(source, ClientKeyExchange, manifest, out nonce, out publicKey, out cookie);

    private static bool TryReadClientKey(ReadOnlySpan<byte> source, byte expectedKind, NetworkProtocolManifest manifest,
        out ReadOnlySpan<byte> nonce, out ReadOnlySpan<byte> publicKey, out ReadOnlySpan<byte> cookie)
    {
        cookie = default;
        var reader = new NetworkPacketReader(source);
        if (!reader.TryReadByte(out byte kind) || kind != expectedKind ||
            !reader.TryReadUInt16(out ushort version) || version != manifest.Version ||
            !reader.TryReadUInt64(out ulong fingerprint) || fingerprint != manifest.Fingerprint ||
            !reader.TryReadBytes(NonceBytes, out nonce) || !reader.TryReadUInt16(out ushort publicLength) ||
            publicLength is < 64 or > MaximumClientPublicKeyBytes || !reader.TryReadBytes(publicLength, out publicKey) ||
            (expectedKind == ClientKeyExchange && !reader.TryReadBytes(CookieBytes, out cookie)) || !reader.IsComplete)
        {
            nonce = publicKey = cookie = default;
            return false;
        }
        return true;
    }

    internal static int WriteCookieChallenge(Span<byte> destination, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cookie)
    {
        var writer = new NetworkPacketWriter(destination);
        if (!writer.TryWriteByte(CookieChallenge) || !writer.TryWriteBytes(nonce) || !writer.TryWriteBytes(cookie))
            throw new InvalidOperationException("The handshake datagram limit is too small.");
        return writer.WrittenCount;
    }

    internal static bool TryReadCookieChallenge(ReadOnlySpan<byte> source, ReadOnlySpan<byte> expectedNonce,
        out ReadOnlySpan<byte> cookie)
    {
        var reader = new NetworkPacketReader(source);
        if (!reader.TryReadByte(out byte kind) || kind != CookieChallenge ||
            !reader.TryReadBytes(NonceBytes, out ReadOnlySpan<byte> nonce) ||
            !nonce.SequenceEqual(expectedNonce) || !reader.TryReadBytes(CookieBytes, out cookie) || !reader.IsComplete)
        {
            cookie = default;
            return false;
        }
        return true;
    }

    internal static int WriteServerKeyExchange(Span<byte> destination, ConnectionId connection, SecurityEpoch epoch,
        ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> signature)
    {
        var writer = new NetworkPacketWriter(destination);
        if (!writer.TryWriteByte(ServerKeyExchange) || !writer.TryWriteUInt32(connection.Slot) ||
            !writer.TryWriteUInt32(connection.Generation) || !writer.TryWriteUInt32(epoch.Value) ||
            publicKey.Length > ushort.MaxValue || !writer.TryWriteUInt16((ushort)publicKey.Length) ||
            !writer.TryWriteBytes(publicKey) || !writer.TryWriteBytes(signature))
            throw new InvalidOperationException("The handshake datagram limit is too small.");
        return writer.WrittenCount;
    }

    internal static bool TryReadServerKeyExchange(ReadOnlySpan<byte> source, out ConnectionId connection,
        out SecurityEpoch epoch, out ReadOnlySpan<byte> publicKey, out ReadOnlySpan<byte> signature)
    {
        var reader = new NetworkPacketReader(source);
        if (!reader.TryReadByte(out byte kind) || kind != ServerKeyExchange ||
            !reader.TryReadUInt32(out uint slot) || !reader.TryReadUInt32(out uint generation) || generation == 0 ||
            !reader.TryReadUInt32(out uint epochValue) || epochValue == 0 ||
            !reader.TryReadUInt16(out ushort publicLength) || publicLength is < 64 or > 512 ||
            !reader.TryReadBytes(publicLength, out publicKey) ||
            !reader.TryReadBytes(SignatureBytes, out signature) || !reader.IsComplete)
        {
            connection = default;
            epoch = default;
            publicKey = signature = default;
            return false;
        }
        connection = new(slot, generation);
        epoch = new(epochValue);
        return true;
    }

    internal static int WriteClientFinish(Span<byte> destination, ConnectionId connection,
        SecurityEpoch epoch, ReadOnlySpan<byte> master, ReadOnlySpan<byte> transcript) =>
        WriteFinish(destination, ClientFinish, connection, epoch, master, transcript);

    internal static int WriteServerFinish(Span<byte> destination, ConnectionId connection,
        SecurityEpoch epoch, ReadOnlySpan<byte> master, ReadOnlySpan<byte> transcript) =>
        WriteFinish(destination, ServerFinish, connection, epoch, master, transcript);

    private static int WriteFinish(Span<byte> destination, byte kind, ConnectionId connection, SecurityEpoch epoch,
        ReadOnlySpan<byte> master, ReadOnlySpan<byte> transcript)
    {
        byte[] proof = new byte[ProofBytes];
        ComputeProof(master, transcript, kind, proof);
        var writer = new NetworkPacketWriter(destination);
        if (!writer.TryWriteByte(kind) || !writer.TryWriteUInt32(connection.Slot) ||
            !writer.TryWriteUInt32(connection.Generation) || !writer.TryWriteUInt32(epoch.Value) ||
            !writer.TryWriteBytes(proof)) throw new InvalidOperationException("The handshake datagram limit is too small.");
        return writer.WrittenCount;
    }

    internal static bool TryReadClientFinish(ReadOnlySpan<byte> source,
        ConnectionId expectedConnection, SecurityEpoch expectedEpoch,
        out ReadOnlySpan<byte> proof) =>
        TryReadFinish(source, ClientFinish, expectedConnection, expectedEpoch, out proof);

    internal static bool TryReadServerFinish(ReadOnlySpan<byte> source,
        ConnectionId expectedConnection, SecurityEpoch expectedEpoch,
        out ReadOnlySpan<byte> proof) =>
        TryReadFinish(source, ServerFinish, expectedConnection, expectedEpoch, out proof);

    private static bool TryReadFinish(ReadOnlySpan<byte> source, byte expectedKind, ConnectionId expectedConnection,
        SecurityEpoch expectedEpoch, out ReadOnlySpan<byte> proof)
    {
        var reader = new NetworkPacketReader(source);
        if (!reader.TryReadByte(out byte kind) || kind != expectedKind ||
            !reader.TryReadUInt32(out uint slot) || slot != expectedConnection.Slot ||
            !reader.TryReadUInt32(out uint generation) || generation != expectedConnection.Generation ||
            !reader.TryReadUInt32(out uint epoch) || epoch != expectedEpoch.Value ||
            !reader.TryReadBytes(ProofBytes, out proof) || !reader.IsComplete)
        {
            proof = default;
            return false;
        }
        return true;
    }

    internal static void ComputeCookie(ReadOnlySpan<byte> key, NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> pathBinding, ulong timeBucket,
        Span<byte> cookie)
    {
        const int maximumPathBindingBytes = 256;
        if (pathBinding.Length > maximumPathBindingBytes)
            throw new ArgumentOutOfRangeException(nameof(pathBinding));
        Span<byte> hash = stackalloc byte[32];
        Span<byte> inputBuffer = stackalloc byte[2 + 8 + NonceBytes + 32 + 2 + maximumPathBindingBytes + 8];
        Span<byte> input = inputBuffer[..checked(2 + 8 + NonceBytes + 32 + 2 + pathBinding.Length + 8)];
        var prefix = new NetworkPacketWriter(input);
        if (!prefix.TryWriteUInt16(manifest.Version) ||
            !prefix.TryWriteUInt64(manifest.Fingerprint))
            throw new InvalidOperationException("The handshake cookie manifest is invalid.");
        int offset = prefix.WrittenCount;
        nonce.CopyTo(input[offset..]);
        offset += NonceBytes;
        SHA256.HashData(publicKey, hash);
        hash.CopyTo(input[offset..]);
        offset += 32;
        var writer = new NetworkPacketWriter(input[offset..]);
        if (pathBinding.Length > ushort.MaxValue ||
            !writer.TryWriteUInt16((ushort)pathBinding.Length) ||
            !writer.TryWriteBytes(pathBinding) || !writer.TryWriteUInt64(timeBucket))
            throw new InvalidOperationException("The handshake cookie input is invalid.");
        HMACSHA256.HashData(key, input, hash);
        hash[..CookieBytes].CopyTo(cookie);
        CryptographicOperations.ZeroMemory(inputBuffer);
        CryptographicOperations.ZeroMemory(hash);
    }

    internal static byte[] BuildTranscript(NetworkProtocolManifest manifest, ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> clientPublic, ReadOnlySpan<byte> serverPublic, ConnectionId connection, SecurityEpoch epoch)
    {
        byte[] transcript = new byte[checked(2 + 8 + NonceBytes + 2 + clientPublic.Length + 2 +
            serverPublic.Length + 4 + 4 + 4)];
        var writer = new NetworkPacketWriter(transcript);
        if (!writer.TryWriteUInt16(manifest.Version) || !writer.TryWriteUInt64(manifest.Fingerprint) ||
            !writer.TryWriteBytes(nonce) || !writer.TryWriteUInt16((ushort)clientPublic.Length) ||
            !writer.TryWriteBytes(clientPublic) || !writer.TryWriteUInt16((ushort)serverPublic.Length) ||
            !writer.TryWriteBytes(serverPublic) || !writer.TryWriteUInt32(connection.Slot) ||
            !writer.TryWriteUInt32(connection.Generation) || !writer.TryWriteUInt32(epoch.Value) ||
            writer.WrittenCount != transcript.Length) throw new InvalidOperationException("The handshake transcript is invalid.");
        return transcript;
    }

    internal static byte[] DeriveMaster(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> cookie, ReadOnlySpan<byte> transcript)
    {
        Span<byte> salt = stackalloc byte[NonceBytes + CookieBytes];
        nonce.CopyTo(salt);
        cookie.CopyTo(salt[NonceBytes..]);
        Span<byte> transcriptHash = stackalloc byte[32];
        SHA256.HashData(transcript, transcriptHash);
        byte[] master = new byte[MasterSecretBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, master, salt, transcriptHash);
        return master;
    }

    internal static void ComputeClientProof(
        ReadOnlySpan<byte> master,
        ReadOnlySpan<byte> transcript,
        Span<byte> proof) => ComputeProof(master, transcript, ClientFinish, proof);

    internal static void ComputeServerProof(
        ReadOnlySpan<byte> master,
        ReadOnlySpan<byte> transcript,
        Span<byte> proof) => ComputeProof(master, transcript, ServerFinish, proof);

    private static void ComputeProof(ReadOnlySpan<byte> master, ReadOnlySpan<byte> transcript, byte kind,
        Span<byte> proof)
    {
        byte[] input = new byte[checked(transcript.Length + 1)];
        transcript.CopyTo(input);
        input[^1] = kind;
        HMACSHA256.HashData(master, input, proof);
    }
}
