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

/// <summary>Long-lived server signing identity pinned by a ProtocolLoopback client.</summary>
public sealed class NetworkServerIdentity : IDisposable
{
    private readonly ECDsa _signingKey;
    private readonly byte[] _publicKey;
    private bool _disposed;

    private NetworkServerIdentity(ECDsa signingKey)
    {
        _signingKey = signingKey;
        _publicKey = signingKey.ExportSubjectPublicKeyInfo();
    }

    public static NetworkServerIdentity CreateEphemeral() =>
        new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public ReadOnlySpan<byte> PublicKey => _disposed
        ? throw new ObjectDisposedException(nameof(NetworkServerIdentity))
        : _publicKey;

    internal bool TrySign(ReadOnlySpan<byte> transcript, Span<byte> signature, out int written)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NetworkServerIdentity));
        return _signingKey.TrySignData(transcript, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation, out written);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _signingKey.Dispose();
        CryptographicOperations.ZeroMemory(_publicKey);
        _disposed = true;
    }
}

internal readonly record struct ProtocolHandshakeResult(
    byte[] MasterSecret,
    ConnectionId Connection,
    SecurityEpoch SecurityEpoch,
    NetworkConnectionTable Connections,
    ProtocolHandshakeSnapshot Snapshot);

internal static class ProtocolLoopbackHandshake
{
    private const byte ClientHello = 1;
    private const byte CookieChallenge = 2;
    private const byte ClientKeyExchange = 3;
    private const byte ServerKeyExchange = 4;
    private const byte ClientFinish = 5;
    private const byte ServerFinish = 6;
    private const int NonceBytes = 32;
    private const int CookieBytes = 16;
    private const int ProofBytes = 32;
    private const int SignatureBytes = 64;

    internal static bool TryEstablish(
        DeterministicDatagramLink link,
        NetworkProtocolManifest clientManifest,
        NetworkProtocolManifest serverManifest,
        NetworkServerIdentity serverIdentity,
        ReadOnlySpan<byte> trustedServerPublicKey,
        int maximumSteps,
        out ProtocolHandshakeResult result) =>
        TryEstablish(
            link.A,
            link.B,
            link.Limits.MaximumDatagramBytes,
            static () => 1,
            "protocol-loopback"u8.ToArray(),
            link.Advance,
            clientManifest,
            serverManifest,
            serverIdentity,
            trustedServerPublicKey,
            maximumSteps,
            out result);

    internal static bool TryEstablishUdp(
        INetworkDatagramEndpoint client,
        INetworkDatagramEndpoint server,
        int maximumDatagramBytes,
        Func<ulong> cookieTimeBucket,
        byte[] clientPathBinding,
        NetworkProtocolManifest clientManifest,
        NetworkProtocolManifest serverManifest,
        NetworkServerIdentity serverIdentity,
        ReadOnlySpan<byte> trustedServerPublicKey,
        int maximumPumps,
        out ProtocolHandshakeResult result) =>
        TryEstablish(
            client,
            server,
            maximumDatagramBytes,
            cookieTimeBucket,
            clientPathBinding,
            static () => Thread.Yield(),
            clientManifest,
            serverManifest,
            serverIdentity,
            trustedServerPublicKey,
            maximumPumps,
            out result);

    private static bool TryEstablish(
        INetworkDatagramEndpoint clientEndpoint,
        INetworkDatagramEndpoint serverEndpoint,
        int maximumDatagramBytes,
        Func<ulong> cookieTimeBucket,
        byte[] clientPathBinding,
        Action advance,
        NetworkProtocolManifest clientManifest,
        NetworkProtocolManifest serverManifest,
        NetworkServerIdentity serverIdentity,
        ReadOnlySpan<byte> trustedServerPublicKey,
        int maximumSteps,
        out ProtocolHandshakeResult result)
    {
        if (maximumSteps <= 0) throw new ArgumentOutOfRangeException(nameof(maximumSteps));
        if (maximumDatagramBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDatagramBytes));
        ArgumentNullException.ThrowIfNull(cookieTimeBucket);
        ArgumentNullException.ThrowIfNull(clientPathBinding);
        ArgumentNullException.ThrowIfNull(advance);
        if (!AesGcm.IsSupported) throw new PlatformNotSupportedException("AES-GCM is required by Network N0.3.");

        byte[] clientNonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] cookieKey = RandomNumberGenerator.GetBytes(32);
        byte[] cookie = new byte[CookieBytes];
        byte[] clientBuffer = new byte[maximumDatagramBytes];
        byte[] serverBuffer = new byte[maximumDatagramBytes];
        byte[] receive = new byte[maximumDatagramBytes];
        byte[] cookieScratch = new byte[CookieBytes];
        byte[] signatureScratch = new byte[SignatureBytes];
        byte[] proofScratch = new byte[ProofBytes];
        byte[]? clientMaster = null;
        byte[]? serverMaster = null;
        byte[]? transcript = null;
        byte[]? serverPublic = null;
        byte[]? serverFlight = null;
        byte[]? clientPublic;
        ConnectionId connection = default;
        SecurityEpoch epoch = default;
        bool clientHasCookie = false;
        bool clientHasServerKey = false;
        bool serverHasKey = false;
        bool serverEstablished = false;
        bool clientEstablished = false;
        ulong clientFlights = 0;
        ulong serverFlights = 0;
        ulong invalid = 0;
        ulong challenges = 0;
        ulong validations = 0;
        ulong exchanges = 0;
        int executedSteps = 0;
        var connections = new NetworkConnectionTable(1);

        using ECDiffieHellman clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        clientPublic = clientEcdh.ExportSubjectPublicKeyInfo();
        using ECDsa trustedServer = ECDsa.Create();
        trustedServer.ImportSubjectPublicKeyInfo(trustedServerPublicKey, out int trustedRead);
        if (trustedRead != trustedServerPublicKey.Length)
            throw new CryptographicException("The server trust key contains trailing data.");
        ECDiffieHellman? serverEcdh = null;

        try
        {
            for (int step = 0; step < maximumSteps && (!clientEstablished || !serverEstablished); step++)
            {
                executedSteps = step + 1;
                int clientLength;
                if (!clientHasCookie)
                    clientLength = WriteClientHello(clientBuffer, clientManifest, clientNonce, clientPublic);
                else if (!clientHasServerKey)
                    clientLength = WriteClientKeyExchange(clientBuffer, clientManifest, clientNonce, clientPublic, cookie);
                else
                    clientLength = WriteFinish(clientBuffer, ClientFinish, connection, epoch, clientMaster!, transcript!);
                _ = clientEndpoint.TrySend(clientBuffer.AsSpan(0, clientLength));
                clientFlights++;

                DrainServer();
                DrainClient();
                advance();
            }

            if (!clientEstablished || !serverEstablished || clientMaster is null || serverMaster is null ||
                !CryptographicOperations.FixedTimeEquals(clientMaster, serverMaster))
            {
                if (connection.IsValid && connections.TryGetState(connection, out NetworkConnectionState state) &&
                    state != NetworkConnectionState.Closed)
                    _ = connections.TryAdvance(connection, NetworkConnectionState.Closed);
                if (connection.IsValid) _ = connections.TryReleaseClosed(connection);
                result = default;
                return false;
            }

            result = new(clientMaster, connection, epoch, connections, new(
                true, executedSteps, clientFlights, serverFlights, invalid,
                challenges, validations, exchanges, connection, epoch));
            clientMaster = null;
            return true;

            void DrainServer()
            {
                while (serverEndpoint.TryReceive(receive, out int length))
                {
                    ReadOnlySpan<byte> datagram = receive.AsSpan(0, length);
                    if (TryReadClientHello(datagram, serverManifest, out ReadOnlySpan<byte> nonce, out ReadOnlySpan<byte> publicKey))
                    {
                        ComputeCookie(cookieKey, nonce, publicKey, clientPathBinding,
                            cookieTimeBucket(), cookieScratch);
                        int response = WriteCookieChallenge(serverBuffer, nonce, cookieScratch);
                        _ = serverEndpoint.TrySend(serverBuffer.AsSpan(0, response));
                        serverFlights++;
                        challenges++;
                        continue;
                    }
                    if (TryReadClientKeyExchange(datagram, serverManifest, out nonce, out publicKey, out ReadOnlySpan<byte> suppliedCookie))
                    {
                        ulong currentBucket = cookieTimeBucket();
                        ComputeCookie(cookieKey, nonce, publicKey, clientPathBinding,
                            currentBucket, cookieScratch);
                        bool cookieValid = CryptographicOperations.FixedTimeEquals(cookieScratch, suppliedCookie);
                        if (!cookieValid && currentBucket != 0)
                        {
                            ComputeCookie(cookieKey, nonce, publicKey, clientPathBinding,
                                currentBucket - 1, cookieScratch);
                            cookieValid = CryptographicOperations.FixedTimeEquals(cookieScratch, suppliedCookie);
                        }
                        if (!cookieValid) { invalid++; continue; }
                        validations++;
                        if (!serverHasKey)
                        {
                            try
                            {
                                using ECDiffieHellman imported = ECDiffieHellman.Create();
                                imported.ImportSubjectPublicKeyInfo(publicKey, out int read);
                                if (read != publicKey.Length) { invalid++; continue; }
                                serverEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                                serverPublic = serverEcdh.ExportSubjectPublicKeyInfo();
                                byte[] secret = serverEcdh.DeriveRawSecretAgreement(imported.PublicKey);
                                if (!connections.TryOpenValidated(out connection) ||
                                    !connections.TryAdvance(connection, NetworkConnectionState.KeyExchange))
                                    throw new InvalidOperationException("The validated handshake connection could not be allocated.");
                                connections.TryGetSecurityEpoch(connection, out epoch);
                                transcript = BuildTranscript(serverManifest, nonce, publicKey, serverPublic, connection, epoch);
                                serverMaster = DeriveMaster(secret, nonce, suppliedCookie, transcript);
                                CryptographicOperations.ZeroMemory(secret);
                                if (!serverIdentity.TrySign(transcript, signatureScratch, out int signatureLength) ||
                                    signatureLength != SignatureBytes)
                                    throw new CryptographicException("The fixed server handshake signature could not be created.");
                                int response = WriteServerKeyExchange(serverBuffer, connection, epoch, serverPublic, signatureScratch);
                                serverFlight = serverBuffer.AsSpan(0, response).ToArray();
                                serverHasKey = true;
                                exchanges++;
                            }
                            catch (CryptographicException)
                            {
                                invalid++;
                                continue;
                            }
                        }
                        _ = serverEndpoint.TrySend(serverFlight);
                        serverFlights++;
                        continue;
                    }
                    if (serverHasKey && TryReadFinish(datagram, ClientFinish, connection, epoch, out ReadOnlySpan<byte> proof))
                    {
                        ComputeProof(serverMaster!, transcript!, ClientFinish, proofScratch);
                        if (!CryptographicOperations.FixedTimeEquals(proofScratch, proof)) { invalid++; continue; }
                        if (!serverEstablished)
                        {
                            if (!connections.TryAdvance(connection, NetworkConnectionState.ProtocolValidated) ||
                                !connections.TryAdvance(connection, NetworkConnectionState.Established))
                                throw new InvalidOperationException("The authenticated connection could not be established.");
                            serverEstablished = true;
                        }
                        int response = WriteFinish(serverBuffer, ServerFinish, connection, epoch, serverMaster!, transcript!);
                        _ = serverEndpoint.TrySend(serverBuffer.AsSpan(0, response));
                        serverFlights++;
                        continue;
                    }
                    invalid++;
                }
            }

            void DrainClient()
            {
                while (clientEndpoint.TryReceive(receive, out int length))
                {
                    ReadOnlySpan<byte> datagram = receive.AsSpan(0, length);
                    if (!clientHasServerKey && TryReadCookieChallenge(datagram, clientNonce, out ReadOnlySpan<byte> receivedCookie))
                    {
                        receivedCookie.CopyTo(cookie);
                        clientHasCookie = true;
                        continue;
                    }
                    if (!clientHasServerKey && clientHasCookie &&
                        TryReadServerKeyExchange(datagram, out ConnectionId receivedConnection,
                            out SecurityEpoch receivedEpoch, out ReadOnlySpan<byte> receivedPublic,
                            out ReadOnlySpan<byte> signature))
                    {
                        try
                        {
                            using ECDiffieHellman imported = ECDiffieHellman.Create();
                            imported.ImportSubjectPublicKeyInfo(receivedPublic, out int read);
                            if (read != receivedPublic.Length) { invalid++; continue; }
                            byte[] candidateTranscript = BuildTranscript(
                                clientManifest, clientNonce, clientPublic, receivedPublic, receivedConnection, receivedEpoch);
                            if (!trustedServer.VerifyData(candidateTranscript, signature, HashAlgorithmName.SHA256,
                                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                            {
                                invalid++;
                                continue;
                            }
                            byte[] secret = clientEcdh.DeriveRawSecretAgreement(imported.PublicKey);
                            clientMaster = DeriveMaster(secret, clientNonce, cookie, candidateTranscript);
                            CryptographicOperations.ZeroMemory(secret);
                            transcript = candidateTranscript;
                            connection = receivedConnection;
                            epoch = receivedEpoch;
                            clientHasServerKey = true;
                            continue;
                        }
                        catch (CryptographicException)
                        {
                            invalid++;
                            continue;
                        }
                    }
                    if (clientHasServerKey && TryReadFinish(datagram, ServerFinish, connection, epoch, out ReadOnlySpan<byte> proof))
                    {
                        ComputeProof(clientMaster!, transcript!, ServerFinish, proofScratch);
                        if (CryptographicOperations.FixedTimeEquals(proofScratch, proof)) clientEstablished = true;
                        else invalid++;
                        continue;
                    }
                    invalid++;
                }
            }
        }
        finally
        {
            serverEcdh?.Dispose();
            CryptographicOperations.ZeroMemory(cookieKey);
            if (clientMaster is not null) CryptographicOperations.ZeroMemory(clientMaster);
            if (serverMaster is not null) CryptographicOperations.ZeroMemory(serverMaster);
        }
    }

    private static int WriteClientHello(Span<byte> destination, NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> publicKey) =>
        WriteClientKey(destination, ClientHello, manifest, nonce, publicKey, default);

    private static int WriteClientKeyExchange(Span<byte> destination, NetworkProtocolManifest manifest,
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

    private static bool TryReadClientHello(ReadOnlySpan<byte> source, NetworkProtocolManifest manifest,
        out ReadOnlySpan<byte> nonce, out ReadOnlySpan<byte> publicKey) =>
        TryReadClientKey(source, ClientHello, manifest, out nonce, out publicKey, out _);

    private static bool TryReadClientKeyExchange(ReadOnlySpan<byte> source, NetworkProtocolManifest manifest,
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
            publicLength is < 64 or > 512 || !reader.TryReadBytes(publicLength, out publicKey) ||
            (expectedKind == ClientKeyExchange && !reader.TryReadBytes(CookieBytes, out cookie)) || !reader.IsComplete)
        {
            nonce = publicKey = cookie = default;
            return false;
        }
        return true;
    }

    private static int WriteCookieChallenge(Span<byte> destination, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cookie)
    {
        var writer = new NetworkPacketWriter(destination);
        if (!writer.TryWriteByte(CookieChallenge) || !writer.TryWriteBytes(nonce) || !writer.TryWriteBytes(cookie))
            throw new InvalidOperationException("The handshake datagram limit is too small.");
        return writer.WrittenCount;
    }

    private static bool TryReadCookieChallenge(ReadOnlySpan<byte> source, ReadOnlySpan<byte> expectedNonce,
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

    private static int WriteServerKeyExchange(Span<byte> destination, ConnectionId connection, SecurityEpoch epoch,
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

    private static bool TryReadServerKeyExchange(ReadOnlySpan<byte> source, out ConnectionId connection,
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

    private static void ComputeCookie(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> pathBinding, ulong timeBucket,
        Span<byte> cookie)
    {
        const int maximumPathBindingBytes = 256;
        if (pathBinding.Length > maximumPathBindingBytes)
            throw new ArgumentOutOfRangeException(nameof(pathBinding));
        Span<byte> hash = stackalloc byte[32];
        Span<byte> inputBuffer = stackalloc byte[NonceBytes + 32 + 2 + maximumPathBindingBytes + 8];
        Span<byte> input = inputBuffer[..checked(NonceBytes + 32 + 2 + pathBinding.Length + 8)];
        nonce.CopyTo(input);
        SHA256.HashData(publicKey, hash);
        hash.CopyTo(input[NonceBytes..]);
        var writer = new NetworkPacketWriter(input[(NonceBytes + 32)..]);
        if (pathBinding.Length > ushort.MaxValue ||
            !writer.TryWriteUInt16((ushort)pathBinding.Length) ||
            !writer.TryWriteBytes(pathBinding) || !writer.TryWriteUInt64(timeBucket))
            throw new InvalidOperationException("The handshake cookie input is invalid.");
        HMACSHA256.HashData(key, input, hash);
        hash[..CookieBytes].CopyTo(cookie);
        CryptographicOperations.ZeroMemory(inputBuffer);
        CryptographicOperations.ZeroMemory(hash);
    }

    private static byte[] BuildTranscript(NetworkProtocolManifest manifest, ReadOnlySpan<byte> nonce,
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

    private static byte[] DeriveMaster(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> cookie, ReadOnlySpan<byte> transcript)
    {
        Span<byte> salt = stackalloc byte[NonceBytes + CookieBytes];
        nonce.CopyTo(salt);
        cookie.CopyTo(salt[NonceBytes..]);
        Span<byte> transcriptHash = stackalloc byte[32];
        SHA256.HashData(transcript, transcriptHash);
        byte[] master = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, master, salt, transcriptHash);
        return master;
    }

    private static void ComputeProof(ReadOnlySpan<byte> master, ReadOnlySpan<byte> transcript, byte kind,
        Span<byte> proof)
    {
        byte[] input = new byte[checked(transcript.Length + 1)];
        transcript.CopyTo(input);
        input[^1] = kind;
        HMACSHA256.HashData(master, input, proof);
    }
}
