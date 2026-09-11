using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Diagnostics;

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
    HostedPathGeneration PathGeneration,
    HostedPathRebindState PathRebindState,
    ulong PathRebindAttempts,
    ulong PathRebindChallenges,
    ulong PathRebindCommits,
    ulong DuplicatePathCommits,
    ulong PathRebindTimeouts,
    UdpDatagramEndpointSnapshot Datagrams,
    NetworkProtocolSnapshot Protocol);

/// <summary>
/// Cold traffic view for one hosted connection lifetime. UDP payload totals are
/// physical socket-boundary values. Secure protocol totals are a classified subset;
/// the residual includes handshake, path-control, and rejected/unattributed traffic.
/// </summary>
public readonly record struct HostedUdpConnectionTrafficSnapshot(
    HostedUdpClientState State,
    ConnectionId Connection,
    UdpPayloadTrafficSnapshot UdpPayload,
    NetworkTrafficSnapshot? SecureProtocol)
{
    public ulong NonSecureOrUnattributedUdpPayloadBytesSent => Difference(
        SecureProtocol?.Transport.SecureUdpPayloadBytesSent ?? 0,
        UdpPayload.PayloadBytesSent);
    public ulong NonSecureOrUnattributedUdpPayloadBytesReceived => Difference(
        SecureProtocol?.Transport.SecureUdpPayloadBytesReceived ?? 0,
        UdpPayload.PayloadBytesReceived);
    public ulong NonSecureOrUnattributedDatagramsSent => Difference(
        SecureProtocol?.Transport.SecureDatagramsSent ?? 0,
        UdpPayload.DatagramsSent);
    public ulong NonSecureOrUnattributedDatagramsReceived => Difference(
        SecureProtocol?.Transport.SecureDatagramsReceived ?? 0,
        UdpPayload.DatagramsReceived);

    private static ulong Difference(ulong classified, ulong physical) =>
        physical >= classified ? physical - classified : 0;
}

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
    private UdpDatagramEndpoint _endpoint;
    private readonly IPEndPoint _serverEndPoint;
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
    private readonly byte[] _pathKey = new byte[HostedUdpPathRebindCodec.KeyBytes];
    private readonly byte[] _pathNonce = new byte[HostedUdpPathRebindCodec.NonceBytes];
    private readonly byte[] _pathChallenge = new byte[HostedUdpPathRebindCodec.ChallengeBytes];
    private readonly byte[] _pathBinding = new byte[HostedUdpPathRebindCodec.PathBindingBytes];
    private readonly byte[] _pathFlight = new byte[HostedUdpPathRebindCodec.PathFlightBytes];
    private readonly byte[] _lastPathCommit = new byte[HostedUdpPathRebindCodec.PathFlightBytes];
    private UdpDatagramEndpoint? _pendingPathEndpoint;
    private NetworkProtocolPeer? _peer;
    private ConnectionId _connection;
    private SecurityEpoch _securityEpoch;
    private HandshakeStage _handshakeStage;
    private HostedUdpClientState _state;
    private HostedPathGeneration _pathGeneration;
    private HostedPathGeneration _pendingPathGeneration;
    private HostedPathRebindState _pathRebindState;
    private ulong _nextPathAttempt;
    private ulong _pendingPathAttempt;
    private int _pathFlightLength;
    private int _lastPathCommitLength;
    private int _pathRebindPumps;
    private ulong _lastActivity;
    private ulong _lastKeepAlive;
    private ulong _drainDeadline;
    private int _handshakePumps;
    private ulong _clientFlights;
    private ulong _serverFlights;
    private ulong _invalidDatagrams;
    private ulong _duplicateServerFinishes;
    private ulong _pathRebindAttempts;
    private ulong _pathRebindChallenges;
    private ulong _pathRebindCommits;
    private ulong _duplicatePathCommits;
    private ulong _pathRebindTimeouts;
    private ulong _priorUdpDatagramsSent;
    private ulong _priorUdpDatagramsReceived;
    private ulong _priorUdpPayloadBytesSent;
    private ulong _priorUdpPayloadBytesReceived;
    private readonly NetworkTimingHistogram _pumpIntervals = new();
    private readonly NetworkTimingHistogram _pumpDurations = new();
    private NetworkTelemetryLevel _telemetryLevel;
    private long _lastPumpTimestamp;
    private bool _disposed;

    private HostedUdpProtocolClient(
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        NetworkProtocolManifest manifest,
        INetworkMonotonicClock clock,
        UdpDatagramEndpoint endpoint,
        IPEndPoint serverEndPoint,
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
        _serverEndPoint = new IPEndPoint(
            serverEndPoint.Address, serverEndPoint.Port);
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

    /// <summary>
    /// Selects bounded transport diagnostics. This may be changed for an active
    /// connection and resets diagnostic observations only; protocol state is untouched.
    /// </summary>
    public void ConfigureTelemetry(NetworkTelemetryLevel level)
    {
        EnsureOperational();
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        _telemetryLevel = level;
        _lastPumpTimestamp = 0;
        _pumpIntervals.Clear();
        _pumpDurations.Clear();
        _peer?.ConfigureTelemetry(level);
    }

    /// <summary>Captures cold immutable service timing for this transport lifetime.</summary>
    public NetworkServiceTimingSnapshot GetTelemetrySnapshot()
    {
        EnsureOperational();
        return new NetworkServiceTimingSnapshot(
            _telemetryLevel,
            _pumpIntervals.Snapshot(),
            _pumpDurations.Snapshot(),
            _peer?.GetStageTiming() ?? default);
    }

    public HostedPathGeneration PathGeneration
    {
        get { EnsureOperational(); return _pathGeneration; }
    }

    public HostedPathRebindState PathRebindState
    {
        get { EnsureOperational(); return _pathRebindState; }
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
                serverEndPoint, ecdh, trusted, nonce, publicKey);
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
        long started = 0;
        if (_telemetryLevel != NetworkTelemetryLevel.Off)
        {
            started = Stopwatch.GetTimestamp();
            if (_lastPumpTimestamp != 0)
                _pumpIntervals.Record(started - _lastPumpTimestamp);
            _lastPumpTimestamp = started;
        }
        try
        {
            PumpCore();
        }
        finally
        {
            if (_telemetryLevel != NetworkTelemetryLevel.Off)
                _pumpDurations.Record(Stopwatch.GetTimestamp() - started);
        }
    }

    private void PumpCore()
    {
        if (_state is HostedUdpClientState.Closed or HostedUdpClientState.Failed) return;
        ulong now = _clock.Milliseconds;
        if (_state == HostedUdpClientState.Connecting)
        {
            PumpHandshake();
            return;
        }

        if (_state == HostedUdpClientState.Established &&
            _pathRebindState != HostedPathRebindState.None)
        {
            PumpPathRebind();
            if (_state != HostedUdpClientState.Established) return;
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

    /// <summary>
    /// Begins one bounded authenticated ownership transfer to a fresh local UDP
    /// path. The current path remains the secure protocol path until commit.
    /// </summary>
    public bool TryBeginPathRebind(IPEndPoint? localEndPoint = null)
    {
        EnsureOperational();
        if (_state != HostedUdpClientState.Established ||
            _pathRebindState != HostedPathRebindState.None ||
            _nextPathAttempt == ulong.MaxValue) return false;
        UdpDatagramEndpoint? pending = null;
        try
        {
            pending = UdpDatagramEndpoint.CreateConnectedClient(
                _udpLimits,
                localEndPoint ?? new IPEndPoint(IPAddress.Any, 0),
                _serverEndPoint);
            _nextPathAttempt++;
            RandomNumberGenerator.Fill(_pathNonce);
            _pathFlightLength = HostedUdpPathRebindCodec.WriteRequest(
                _pathFlight,
                _manifest,
                _connection,
                _nextPathAttempt,
                _pathNonce,
                _pathKey);
            _pendingPathEndpoint = pending;
            pending = null;
            _pendingPathAttempt = _nextPathAttempt;
            _pendingPathGeneration = default;
            _pathRebindPumps = 0;
            _pathRebindState = HostedPathRebindState.Requesting;
            _pathRebindAttempts = SaturatingIncrement(_pathRebindAttempts);
            return true;
        }
        catch (SocketException)
        {
            pending?.Dispose();
            return false;
        }
    }

    public bool TryCancelPathRebind()
    {
        EnsureOperational();
        if (_pathRebindState != HostedPathRebindState.Requesting) return false;
        CancelPathRebindCore(timedOut: false);
        return true;
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
            _pathGeneration,
            _pathRebindState,
            _pathRebindAttempts,
            _pathRebindChallenges,
            _pathRebindCommits,
            _duplicatePathCommits,
            _pathRebindTimeouts,
            _endpoint.GetSnapshot(),
            _peer?.GetSnapshot() ?? default);
    }

    /// <summary>Creates a cold immutable traffic snapshot for this transport lifetime.</summary>
    public HostedUdpConnectionTrafficSnapshot GetTrafficSnapshot()
    {
        EnsureOperational();
        UdpPayloadTrafficSnapshot current = _endpoint.GetSnapshot().UdpPayload;
        return new(
            _state,
            _connection,
            new UdpPayloadTrafficSnapshot(
                SaturatingAdd(_priorUdpDatagramsSent, current.DatagramsSent),
                SaturatingAdd(_priorUdpDatagramsReceived, current.DatagramsReceived),
                SaturatingAdd(_priorUdpPayloadBytesSent, current.PayloadBytesSent),
                SaturatingAdd(_priorUdpPayloadBytesReceived, current.PayloadBytesReceived)),
            _peer?.GetTrafficSnapshot());
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _peer?.Dispose();
        _peer = null;
        _pendingPathEndpoint?.Dispose();
        _pendingPathEndpoint = null;
        _endpoint.Dispose();
        ReleaseHandshakeSecrets();
        CryptographicOperations.ZeroMemory(_nonce);
        CryptographicOperations.ZeroMemory(_clientPublic);
        CryptographicOperations.ZeroMemory(_cookie);
        CryptographicOperations.ZeroMemory(_sendScratch);
        CryptographicOperations.ZeroMemory(_receiveScratch);
        CryptographicOperations.ZeroMemory(_pathKey);
        CryptographicOperations.ZeroMemory(_pathNonce);
        CryptographicOperations.ZeroMemory(_pathChallenge);
        CryptographicOperations.ZeroMemory(_pathBinding);
        CryptographicOperations.ZeroMemory(_pathFlight);
        CryptographicOperations.ZeroMemory(_lastPathCommit);
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

    internal bool TryCopyPendingPathFlightForTesting(
        Span<byte> destination,
        out int written)
    {
        EnsureOperational();
        written = 0;
        if (_pathRebindState == HostedPathRebindState.None ||
            destination.Length < _pathFlightLength) return false;
        _pathFlight.AsSpan(0, _pathFlightLength).CopyTo(destination);
        written = _pathFlightLength;
        return true;
    }

    bool IUdpKnownDatagramHandler.TryHandleKnownDatagram(
        ReadOnlySpan<byte> datagram)
    {
        if (_serverFinish is null || datagram.Length != _serverFinish.Length ||
            !CryptographicOperations.FixedTimeEquals(datagram, _serverFinish))
        {
            if (_lastPathCommitLength == 0 ||
                datagram.Length != _lastPathCommitLength ||
                !CryptographicOperations.FixedTimeEquals(
                    datagram, _lastPathCommit.AsSpan(0, _lastPathCommitLength)))
                return false;
            _duplicatePathCommits = SaturatingIncrement(_duplicatePathCommits);
            return true;
        }
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

    private void PumpPathRebind()
    {
        UdpDatagramEndpoint pending = _pendingPathEndpoint ??
            throw new InvalidOperationException(
                "The hosted client path transaction has no endpoint.");
        if (_pathRebindPumps >= _udpLimits.MaximumHandshakePumps)
        {
            if (_pathRebindState == HostedPathRebindState.Requesting)
                CancelPathRebindCore(timedOut: true);
            else
                FailUncertainPathRebind();
            return;
        }
        _pathRebindPumps++;
        _ = pending.TrySend(_pathFlight.AsSpan(0, _pathFlightLength));

        while (pending.TryReceive(_receiveScratch, out int received))
        {
            ReadOnlySpan<byte> datagram = _receiveScratch.AsSpan(0, received);
            if (_pathRebindState == HostedPathRebindState.Requesting &&
                HostedUdpPathRebindCodec.TryReadPathFlight(
                    datagram,
                    HostedPathDatagramKind.Challenge,
                    _manifest,
                    _connection,
                    _pathKey,
                    out ulong attempt,
                    out HostedPathGeneration generation,
                    out ReadOnlySpan<byte> nonce,
                    out ReadOnlySpan<byte> challenge,
                    out ReadOnlySpan<byte> pathBinding) &&
                attempt == _pendingPathAttempt &&
                generation.Value > _pathGeneration.Value &&
                CryptographicOperations.FixedTimeEquals(nonce, _pathNonce))
            {
                challenge.CopyTo(_pathChallenge);
                pathBinding.CopyTo(_pathBinding);
                _pendingPathGeneration = generation;
                _pathFlightLength = HostedUdpPathRebindCodec.WritePathFlight(
                    _pathFlight,
                    HostedPathDatagramKind.Proof,
                    _manifest,
                    _connection,
                    attempt,
                    generation,
                    _pathNonce,
                    _pathChallenge,
                    _pathBinding,
                    _pathKey);
                _pathRebindState = HostedPathRebindState.Proving;
                _pathRebindChallenges = SaturatingIncrement(_pathRebindChallenges);
                continue;
            }
            if (_pathRebindState == HostedPathRebindState.Proving &&
                HostedUdpPathRebindCodec.TryReadPathFlight(
                    datagram,
                    HostedPathDatagramKind.Commit,
                    _manifest,
                    _connection,
                    _pathKey,
                    out attempt,
                    out generation,
                    out nonce,
                    out challenge,
                    out pathBinding) &&
                attempt == _pendingPathAttempt &&
                generation == _pendingPathGeneration &&
                CryptographicOperations.FixedTimeEquals(nonce, _pathNonce) &&
                CryptographicOperations.FixedTimeEquals(challenge, _pathChallenge) &&
                CryptographicOperations.FixedTimeEquals(pathBinding, _pathBinding))
            {
                datagram.CopyTo(_lastPathCommit);
                _lastPathCommitLength = datagram.Length;
                pending.BindKnownDatagramHandler(this);
                _peer!.ReplaceEndpoint(_endpoint, pending);
                UdpDatagramEndpoint prior = _endpoint;
                UdpPayloadTrafficSnapshot priorTraffic = prior.GetSnapshot().UdpPayload;
                _priorUdpDatagramsSent = SaturatingAdd(
                    _priorUdpDatagramsSent, priorTraffic.DatagramsSent);
                _priorUdpDatagramsReceived = SaturatingAdd(
                    _priorUdpDatagramsReceived, priorTraffic.DatagramsReceived);
                _priorUdpPayloadBytesSent = SaturatingAdd(
                    _priorUdpPayloadBytesSent, priorTraffic.PayloadBytesSent);
                _priorUdpPayloadBytesReceived = SaturatingAdd(
                    _priorUdpPayloadBytesReceived, priorTraffic.PayloadBytesReceived);
                _endpoint = pending;
                _pendingPathEndpoint = null;
                _pathGeneration = generation;
                _pathRebindState = HostedPathRebindState.None;
                _pendingPathGeneration = default;
                _pendingPathAttempt = 0;
                _pathFlightLength = 0;
                _pathRebindPumps = 0;
                CryptographicOperations.ZeroMemory(_pathNonce);
                CryptographicOperations.ZeroMemory(_pathChallenge);
                CryptographicOperations.ZeroMemory(_pathBinding);
                CryptographicOperations.ZeroMemory(_pathFlight);
                prior.Dispose();
                _pathRebindCommits = SaturatingIncrement(_pathRebindCommits);
                return;
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
        _peer.ConfigureTelemetry(_telemetryLevel);
        HostedUdpPathRebindCodec.DeriveKey(
            _master!, _manifest, _connection, _securityEpoch, _pathKey);
        _endpoint.BindKnownDatagramHandler(this);
        _handshakeStage = HandshakeStage.Complete;
        _state = HostedUdpClientState.Established;
        _pathGeneration = new(1);
        _lastActivity = _clock.Milliseconds;
        _lastKeepAlive = _lastActivity;
        ReleaseHandshakeSecrets();
    }

    private void BeginDrainCore(ulong now)
    {
        if (_pathRebindState == HostedPathRebindState.Proving)
        {
            FailUncertainPathRebind();
            return;
        }
        if (_pathRebindState == HostedPathRebindState.Requesting)
            CancelPathRebindCore(timedOut: false);
        _peer!.StopAcceptingNewSends();
        _state = HostedUdpClientState.Draining;
        _drainDeadline = checked(now + (ulong)_udpLimits.DrainTimeoutMilliseconds);
    }

    private void CloseTransport()
    {
        if (_state == HostedUdpClientState.Closed) return;
        _pendingPathEndpoint?.Dispose();
        _pendingPathEndpoint = null;
        _endpoint.Dispose();
        _pathRebindState = HostedPathRebindState.None;
        _state = HostedUdpClientState.Closed;
    }

    private void FailHandshake()
    {
        ReleaseHandshakeSecrets();
        _endpoint.Dispose();
        _state = HostedUdpClientState.Failed;
    }

    private void CancelPathRebindCore(bool timedOut)
    {
        _pendingPathEndpoint?.Dispose();
        _pendingPathEndpoint = null;
        _pendingPathGeneration = default;
        _pendingPathAttempt = 0;
        _pathFlightLength = 0;
        _pathRebindPumps = 0;
        _pathRebindState = HostedPathRebindState.None;
        CryptographicOperations.ZeroMemory(_pathNonce);
        CryptographicOperations.ZeroMemory(_pathChallenge);
        CryptographicOperations.ZeroMemory(_pathBinding);
        CryptographicOperations.ZeroMemory(_pathFlight);
        if (timedOut)
            _pathRebindTimeouts = SaturatingIncrement(_pathRebindTimeouts);
    }

    private void FailUncertainPathRebind()
    {
        _peer?.Dispose();
        _peer = null;
        _pendingPathEndpoint?.Dispose();
        _pendingPathEndpoint = null;
        _endpoint.Dispose();
        _pendingPathGeneration = default;
        _pendingPathAttempt = 0;
        _pathFlightLength = 0;
        _pathRebindPumps = 0;
        _pathRebindState = HostedPathRebindState.None;
        CryptographicOperations.ZeroMemory(_pathNonce);
        CryptographicOperations.ZeroMemory(_pathChallenge);
        CryptographicOperations.ZeroMemory(_pathBinding);
        CryptographicOperations.ZeroMemory(_pathFlight);
        _pathRebindTimeouts = SaturatingIncrement(_pathRebindTimeouts);
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

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

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
