using System.Security.Cryptography;
using static UltraSim.Network.ProtocolLoopbackHandshake;

namespace UltraSim.Network;

internal readonly record struct ProtocolHandshakeResult(
    byte[] MasterSecret,
    ConnectionId Connection,
    SecurityEpoch SecurityEpoch,
    NetworkConnectionTable Connections,
    ProtocolHandshakeSnapshot Snapshot);

internal static class ProtocolLoopbackHandshakeHarness
{
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
            null,
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
        out ProtocolHandshakeResult result,
        NetworkConnectionTable? sharedConnections = null) =>
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
            sharedConnections,
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
        NetworkConnectionTable? sharedConnections,
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
        NetworkConnectionTable connections = sharedConnections ?? new NetworkConnectionTable(1);

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
                    clientLength = WriteClientFinish(
                        clientBuffer, connection, epoch, clientMaster!, transcript!);
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
                        ComputeCookie(cookieKey, serverManifest, nonce, publicKey, clientPathBinding,
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
                        ComputeCookie(cookieKey, serverManifest, nonce, publicKey, clientPathBinding,
                            currentBucket, cookieScratch);
                        bool cookieValid = CryptographicOperations.FixedTimeEquals(cookieScratch, suppliedCookie);
                        if (!cookieValid && currentBucket != 0)
                        {
                            ComputeCookie(cookieKey, serverManifest, nonce, publicKey, clientPathBinding,
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
                    if (serverHasKey && TryReadClientFinish(
                            datagram, connection, epoch, out ReadOnlySpan<byte> proof))
                    {
                        ComputeClientProof(serverMaster!, transcript!, proofScratch);
                        if (!CryptographicOperations.FixedTimeEquals(proofScratch, proof)) { invalid++; continue; }
                        if (!serverEstablished)
                        {
                            if (!connections.TryAdvance(connection, NetworkConnectionState.ProtocolValidated) ||
                                !connections.TryAdvance(connection, NetworkConnectionState.Established))
                                throw new InvalidOperationException("The authenticated connection could not be established.");
                            serverEstablished = true;
                        }
                        int response = WriteServerFinish(
                            serverBuffer, connection, epoch, serverMaster!, transcript!);
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
                    if (clientHasServerKey && TryReadServerFinish(
                            datagram, connection, epoch, out ReadOnlySpan<byte> proof))
                    {
                        ComputeServerProof(clientMaster!, transcript!, proofScratch);
                        if (CryptographicOperations.FixedTimeEquals(proofScratch, proof)) clientEstablished = true;
                        else invalid++;
                        continue;
                    }
                    invalid++;
                }
            }
        }
        catch
        {
            if (connection.IsValid && connections.TryGetState(connection, out NetworkConnectionState state) &&
                state != NetworkConnectionState.Closed)
                _ = connections.TryAdvance(connection, NetworkConnectionState.Closed);
            if (connection.IsValid) _ = connections.TryReleaseClosed(connection);
            throw;
        }
        finally
        {
            serverEcdh?.Dispose();
            CryptographicOperations.ZeroMemory(cookieKey);
            if (clientMaster is not null) CryptographicOperations.ZeroMemory(clientMaster);
            if (serverMaster is not null) CryptographicOperations.ZeroMemory(serverMaster);
        }
    }

}
