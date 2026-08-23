using System.Net;
using System.Security.Cryptography;

namespace UltraSim.Network;

/// <summary>Lifecycle of one externally connected hosted UDP client.</summary>
public enum HostedUdpClientState : byte
{
    Connecting = 0,
    Established = 1,
    Draining = 2,
    Closed = 3,
    Failed = 4,
}

/// <summary>Cold diagnostics for one hosted client connection.</summary>
public readonly record struct HostedUdpClientSnapshot(
    HostedUdpClientState State,
    int HandshakePumps,
    ulong ClientFlights,
    ulong ServerFlights,
    ulong InvalidDatagrams,
    ulong DuplicateServerFinishes,
    ConnectionId Connection,
    SecurityEpoch SecurityEpoch,
    UdpDatagramEndpointSnapshot Datagrams,
    NetworkProtocolSnapshot Protocol);

/// <summary>
/// Owner-thread remote client for the hosted UDP handshake and secure protocol.
/// </summary>
/// <remarks>
/// The server signing key is supplied out of band and pinned before any server
/// flight is trusted. This type never constructs a self-trusted server identity.
/// </remarks>
public sealed class HostedUdpProtocolClient : IDisposable, IUdpKnownDatagramHandler
{
    private enum HandshakeStage : byte
    {
        Hello = 0,
        KeyExchange = 1,
        Finish = 2,
        Complete = 3,
    }

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly NetworkProtocolLimits _protocolLimits;
    private readonly UdpProtocolLimits _udpLimits;
    private readonly NetworkProtocolManifest _manifest;
    private readonly INetworkMonotonicClock _clock;
    private readonly UdpDatagramEndpoint _endpoint;
    private ECDiffieHellman? _clientEcdh;
    private ECDsa? _trustedServer;
    private readonly byte[] _nonce;
    private readonly byte[] _clientPublic;
    private readonly byte[] _cookie = new byte[ProtocolLoopbackHandshake.CookieBytes];
    private readonly byte[] _sendScratch;
    private readonly byte[] _receiveScratch;
    private byte[]? _transcript;
    private byte[]? _master;
    private byte[]? _serverFinish;
    private NetworkProtocolPeer? _peer;
    private ConnectionId _connection;
    private SecurityEpoch _securityEpoch;
    private HandshakeStage _handshakeStage;
    private HostedUdpClientState _state;
    private ulong _lastActivity;
    private ulong _lastKeepAlive;
    private ulong _drainDeadline;
    private int _handshakePumps;
    private ulong _clientFlights;
    private ulong _serverFlights;
    private ulong _invalidDatagrams;
    private ulong _duplicateServerFinishes;
    private bool _disposed;

    private HostedUdpProtocolClient(
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        NetworkProtocolManifest manifest,
        INetworkMonotonicClock clock,
        UdpDatagramEndpoint endpoint,
        ECDiffieHellman clientEcdh,
        ECDsa trustedServer,
        byte[] nonce,
        byte[] clientPublic)
    {
        _protocolLimits = protocolLimits;
        _udpLimits = udpLimits;
        _manifest = manifest;
        _clock = clock;
        _endpoint = endpoint;
        _clientEcdh = clientEcdh;
        _trustedServer = trustedServer;
        _nonce = nonce;
        _clientPublic = clientPublic;
        _sendScratch = new byte[udpLimits.MaximumDatagramBytes];
        _receiveScratch = new byte[udpLimits.MaximumDatagramBytes];
        _state = HostedUdpClientState.Connecting;
        _lastActivity = clock.Milliseconds;
        _lastKeepAlive = _lastActivity;
    }

    public HostedUdpClientState State
    {
        get { EnsureOperational(); return _state; }
    }

    public ConnectionId Connection
    {
        get { EnsureOperational(); return _connection; }
    }

    public NetworkProtocolPeer? Peer
    {
        get { EnsureOperational(); return _peer; }
    }

    public IPEndPoint LocalEndPoint
    {
        get
        {
            EnsureOperational();
            return new IPEndPoint(_endpoint.LocalEndPoint.Address, _endpoint.LocalEndPoint.Port);
        }
    }

    public static bool TryCreate(
        IPEndPoint serverEndPoint,
        ReadOnlySpan<byte> pinnedServerPublicKey,
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        out HostedUdpProtocolClient? client,
        NetworkProtocolManifest? manifest = null,
        IPEndPoint? localEndPoint = null)
        => TryCreateCore(serverEndPoint, pinnedServerPublicKey, protocolLimits,
            udpLimits, StopwatchNetworkMonotonicClock.Instance, out client,
            manifest, localEndPoint);

    internal static bool TryCreateForTesting(
        IPEndPoint serverEndPoint,
        ReadOnlySpan<byte> pinnedServerPublicKey,
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        INetworkMonotonicClock clock,
        out HostedUdpProtocolClient? client,
        NetworkProtocolManifest? manifest = null,
        IPEndPoint? localEndPoint = null)
        => TryCreateCore(serverEndPoint, pinnedServerPublicKey, protocolLimits,
            udpLimits, clock, out client, manifest, localEndPoint);

    private static bool TryCreateCore(
        IPEndPoint serverEndPoint,
        ReadOnlySpan<byte> pinnedServerPublicKey,
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        INetworkMonotonicClock clock,
        out HostedUdpProtocolClient? client,
        NetworkProtocolManifest? manifest,
        IPEndPoint? localEndPoint)
    {
        ArgumentNullException.ThrowIfNull(serverEndPoint);
        ArgumentNullException.ThrowIfNull(clock);
        udpLimits.Validate();
        manifest ??= CoreNetworkProtocol.Manifest;
        protocolLimits.Validate(new DeterministicDatagramLimits(
            1, udpLimits.MaximumDatagramBytes));
        if (pinnedServerPublicKey.IsEmpty)
        {
            client = null;
            return false;
        }

        ECDiffieHellman? ecdh = null;
        ECDsa? trusted = null;
        UdpDatagramEndpoint? endpoint = null;
        byte[]? nonce = null;
        byte[]? publicKey = null;
        try
        {
            trusted = ECDsa.Create();
            trusted.ImportSubjectPublicKeyInfo(pinnedServerPublicKey, out int trustedRead);
            if (trustedRead != pinnedServerPublicKey.Length)
                throw new CryptographicException("The pinned server identity contains trailing data.");
            ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            publicKey = ecdh.ExportSubjectPublicKeyInfo();
            nonce = RandomNumberGenerator.GetBytes(ProtocolLoopbackHandshake.NonceBytes);
            endpoint = UdpDatagramEndpoint.CreateConnectedClient(
                udpLimits,
                localEndPoint ?? new IPEndPoint(IPAddress.Any, 0),
                serverEndPoint);
            client = new HostedUdpProtocolClient(
                protocolLimits, udpLimits, manifest, clock, endpoint,
                ecdh, trusted, nonce, publicKey);
            endpoint = null;
            ecdh = null;
            trusted = null;
            nonce = null;
            publicKey = null;
            return true;
        }
        catch (CryptographicException)
        {
            client = null;
            return false;
        }
        finally
        {
            endpoint?.Dispose();
            ecdh?.Dispose();
            trusted?.Dispose();
            if (nonce is not null) CryptographicOperations.ZeroMemory(nonce);
            if (publicKey is not null) CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    /// <summary>Runs one nonblocking handshake or secure-protocol pass.</summary>
    public void Pump()
    {
        EnsureOperational();
        if (_state is HostedUdpClientState.Closed or HostedUdpClientState.Failed) return;
        ulong now = _clock.Milliseconds;
        if (_state == HostedUdpClientState.Connecting)
        {
            PumpHandshake();
            return;
        }

        NetworkProtocolPeer peer = _peer!;
        ulong receivedBefore = peer.ReceivedPacketCount;
        _ = peer.ProcessInbound(now);
        if (peer.ReceivedPacketCount != receivedBefore) _lastActivity = now;
        if (_state == HostedUdpClientState.Established &&
            now - _lastKeepAlive >= (ulong)_udpLimits.KeepAliveIntervalMilliseconds &&
            peer.TryEmitKeepAlive())
            _lastKeepAlive = now;
        peer.Emit(now);
        if (_state == HostedUdpClientState.Established &&
            now - _lastActivity >= (ulong)_udpLimits.IdleTimeoutMilliseconds)
            BeginDrainCore(now);
        if (_state == HostedUdpClientState.Draining &&
            (peer.ReliableQueued == 0 || now >= _drainDeadline))
            CloseTransport();
    }

    public void BeginDrain()
    {
        EnsureOperational();
        if (_state == HostedUdpClientState.Established)
            BeginDrainCore(_clock.Milliseconds);
    }

    public HostedUdpClientSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new(
            _state,
            _handshakePumps,
            _clientFlights,
            _serverFlights,
            _invalidDatagrams,
            _duplicateServerFinishes,
            _connection,
            _securityEpoch,
            _endpoint.GetSnapshot(),
            _peer?.GetSnapshot() ?? default);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _peer?.Dispose();
        _peer = null;
        _endpoint.Dispose();
        ReleaseHandshakeSecrets();
        CryptographicOperations.ZeroMemory(_nonce);
        CryptographicOperations.ZeroMemory(_clientPublic);
        CryptographicOperations.ZeroMemory(_cookie);
        CryptographicOperations.ZeroMemory(_sendScratch);
        CryptographicOperations.ZeroMemory(_receiveScratch);
        if (_serverFinish is not null)
        {
            CryptographicOperations.ZeroMemory(_serverFinish);
            _serverFinish = null;
        }
        _state = HostedUdpClientState.Closed;
        _disposed = true;
    }

    internal void SuppressOutboundDatagramsForTesting(bool suppress)
    {
        EnsureOperational();
        _endpoint.SuppressSendsForTesting = suppress;
    }

    bool IUdpKnownDatagramHandler.TryHandleKnownDatagram(
        ReadOnlySpan<byte> datagram)
    {
        if (_serverFinish is null || datagram.Length != _serverFinish.Length ||
            !CryptographicOperations.FixedTimeEquals(datagram, _serverFinish))
            return false;
        _duplicateServerFinishes = SaturatingIncrement(_duplicateServerFinishes);
        return true;
    }

    private void PumpHandshake()
    {
        if (_handshakePumps >= _udpLimits.MaximumHandshakePumps)
        {
            FailHandshake();
            return;
        }
        _handshakePumps++;
        int length = _handshakeStage switch
        {
            HandshakeStage.Hello => ProtocolLoopbackHandshake.WriteClientHello(
                _sendScratch, _manifest, _nonce, _clientPublic),
            HandshakeStage.KeyExchange => ProtocolLoopbackHandshake.WriteClientKeyExchange(
                _sendScratch, _manifest, _nonce, _clientPublic, _cookie),
            HandshakeStage.Finish => ProtocolLoopbackHandshake.WriteClientFinish(
                _sendScratch, _connection, _securityEpoch, _master!, _transcript!),
            _ => 0,
        };
        if (length != 0)
        {
            _ = _endpoint.TrySend(_sendScratch.AsSpan(0, length));
            _clientFlights = SaturatingIncrement(_clientFlights);
        }

        while (_endpoint.TryReceive(_receiveScratch, out int received))
        {
            _serverFlights = SaturatingIncrement(_serverFlights);
            ReadOnlySpan<byte> datagram = _receiveScratch.AsSpan(0, received);
            if (_handshakeStage == HandshakeStage.Hello &&
                ProtocolLoopbackHandshake.TryReadCookieChallenge(
                    datagram, _nonce, out ReadOnlySpan<byte> cookie))
            {
                cookie.CopyTo(_cookie);
                _handshakeStage = HandshakeStage.KeyExchange;
                continue;
            }
            if (_handshakeStage == HandshakeStage.KeyExchange &&
                TryAcceptServerKeyExchange(datagram))
            {
                _handshakeStage = HandshakeStage.Finish;
                continue;
            }
            if (_handshakeStage == HandshakeStage.Finish &&
                TryAcceptServerFinish(datagram))
            {
                EstablishProtocol();
                break;
            }
            _invalidDatagrams = SaturatingIncrement(_invalidDatagrams);
        }
    }

    private bool TryAcceptServerKeyExchange(ReadOnlySpan<byte> datagram)
    {
        if (!ProtocolLoopbackHandshake.TryReadServerKeyExchange(
                datagram, out ConnectionId connection, out SecurityEpoch epoch,
                out ReadOnlySpan<byte> serverPublic, out ReadOnlySpan<byte> signature))
            return false;
        try
        {
            using ECDiffieHellman imported = ECDiffieHellman.Create();
            imported.ImportSubjectPublicKeyInfo(serverPublic, out int read);
            if (read != serverPublic.Length) return false;
            byte[] transcript = ProtocolLoopbackHandshake.BuildTranscript(
                _manifest, _nonce, _clientPublic, serverPublic, connection, epoch);
            if (!_trustedServer!.VerifyData(
                    transcript, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                CryptographicOperations.ZeroMemory(transcript);
                return false;
            }
            byte[] secret = _clientEcdh!.DeriveRawSecretAgreement(imported.PublicKey);
            byte[] master = ProtocolLoopbackHandshake.DeriveMaster(
                secret, _nonce, _cookie, transcript);
            CryptographicOperations.ZeroMemory(secret);
            _transcript = transcript;
            _master = master;
            _connection = connection;
            _securityEpoch = epoch;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private bool TryAcceptServerFinish(ReadOnlySpan<byte> datagram)
    {
        if (!ProtocolLoopbackHandshake.TryReadServerFinish(
                datagram, _connection, _securityEpoch, out ReadOnlySpan<byte> proof))
            return false;
        Span<byte> expected = stackalloc byte[ProtocolLoopbackHandshake.ProofBytes];
        ProtocolLoopbackHandshake.ComputeServerProof(_master!, _transcript!, expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, proof)) return false;
        _serverFinish = datagram.ToArray();
        return true;
    }

    private void EstablishProtocol()
    {
        _peer = new NetworkProtocolPeer(
            _endpoint,
            _manifest,
            _protocolLimits,
            new DeterministicDatagramLimits(1, _udpLimits.MaximumDatagramBytes),
            _connection,
            _securityEpoch,
            _master!,
            sendsClientToServer: true,
            usesMonotonicPacing: true);
        _endpoint.BindKnownDatagramHandler(this);
        _handshakeStage = HandshakeStage.Complete;
        _state = HostedUdpClientState.Established;
        _lastActivity = _clock.Milliseconds;
        _lastKeepAlive = _lastActivity;
        ReleaseHandshakeSecrets();
    }

    private void BeginDrainCore(ulong now)
    {
        _peer!.StopAcceptingNewSends();
        _state = HostedUdpClientState.Draining;
        _drainDeadline = checked(now + (ulong)_udpLimits.DrainTimeoutMilliseconds);
    }

    private void CloseTransport()
    {
        if (_state == HostedUdpClientState.Closed) return;
        _endpoint.Dispose();
        _state = HostedUdpClientState.Closed;
    }

    private void FailHandshake()
    {
        ReleaseHandshakeSecrets();
        _endpoint.Dispose();
        _state = HostedUdpClientState.Failed;
    }

    private void ReleaseHandshakeSecrets()
    {
        _clientEcdh?.Dispose();
        _clientEcdh = null;
        _trustedServer?.Dispose();
        _trustedServer = null;
        if (_master is not null)
        {
            CryptographicOperations.ZeroMemory(_master);
            _master = null;
        }
        if (_transcript is not null)
        {
            CryptographicOperations.ZeroMemory(_transcript);
            _transcript = null;
        }
    }

    private static ulong SaturatingIncrement(ulong value) =>
        value == ulong.MaxValue ? value : value + 1;

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The hosted UDP client is owner-thread-bound.");
    }
}
