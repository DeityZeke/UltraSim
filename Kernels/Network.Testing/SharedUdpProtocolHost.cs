using System.Net;
using System.Security.Cryptography;
using System.Diagnostics;

namespace UltraSim.Network;

/// <summary>Snapshot of one secure session routed through a shared UDP listener.</summary>
public readonly record struct SharedUdpProtocolSessionSnapshot(
    UdpProtocolSessionState State,
    ConnectionId Connection,
    ProtocolHandshakeSnapshot Handshake,
    NetworkProtocolSnapshot Client,
    NetworkProtocolSnapshot Server,
    UdpDatagramEndpointSnapshot ClientDatagrams,
    UdpDatagramEndpointSnapshot ServerDatagrams);

/// <summary>Snapshot of one bounded, single-owner shared-listener topology.</summary>
public readonly record struct SharedUdpProtocolHostSnapshot(
    int Sessions,
    int SessionCapacity,
    SharedUdpListenerSnapshot Listener,
    NetworkConnectionSnapshot Connections,
    long ConfiguredSocketBufferBytes,
    long KnownReservedBytes,
    long KnownServerReservedBytes,
    long KnownClientReservedBytes,
    long ConfiguredListenerSocketBufferBytes,
    long ConfiguredClientSocketBufferBytes);

/// <summary>
/// One owner-thread UDP listener that routes a bounded set of secure protocol
/// sessions without giving every server-side connection its own socket.
/// </summary>
/// <remarks>
/// N0.10.1 deliberately changes only byte-movement topology. Every connection
/// retains its own protocol peer, cryptographic state, lane queues, congestion
/// state, and application semantics. The listener performs one bounded receive
/// sweep, routes by the kernel-observed IPv4 source path, then the host pumps
/// every admitted peer in deterministic slot order. It does not introduce
/// workers, sharding, compression, or a second protocol. An opt-in platform
/// send-batch implementation may submit several already-built independent
/// datagrams in one native call; it does not batch protocol frames or wait for
/// additional work. A separately selected platform receive-batch path drains
/// only datagrams already readable at the current pump into fixed listener
/// storage before applying the same per-datagram routing/admission contract.
///
/// The current public constructor creates loopback clients for controlled
/// topology and benchmark evidence. A public Internet-facing acceptor remains
/// outside this checkpoint.
/// </remarks>
public sealed class SharedUdpProtocolHost : IDisposable
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly NetworkProtocolLimits _protocolLimits;
    private readonly UdpProtocolLimits _udpLimits;
    private readonly SharedUdpProtocolLimits _sharedLimits;
    private readonly NetworkProtocolManifest _clientManifest;
    private readonly NetworkProtocolManifest _serverManifest;
    private readonly NetworkConnectionTable _connections;
    private readonly SharedUdpServerListener _listener;
    private readonly SharedUdpProtocolSession?[] _sessions;
    private readonly int[] _sessionFree;
    private readonly INetworkMonotonicClock _clock;
    private readonly ulong _startedMilliseconds;
    private readonly bool _ownsIdentity;
    private NetworkServerIdentity? _identity;
    private byte[] _trustedServerPublicKey;
    private int _sessionCount;
    private int _sessionFreeCount;
    private bool _disposed;

    public SharedUdpProtocolHost(
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        SharedUdpProtocolLimits sharedLimits,
        NetworkProtocolManifest? manifest = null,
        NetworkServerIdentity? serverIdentity = null,
        ReadOnlyMemory<byte> trustedServerPublicKey = default,
        NetworkProtocolManifest? serverManifest = null)
        : this(protocolLimits, udpLimits, sharedLimits,
            StopwatchNetworkMonotonicClock.Instance, manifest, serverIdentity,
            trustedServerPublicKey, serverManifest, allowSelfTrust: false)
    {
    }

    /// <summary>
    /// Creates a controlled loopback-only host with an ephemeral server identity
    /// trusted by the in-process clients it creates. Production remote clients
    /// must use an explicitly pinned identity instead.
    /// </summary>
    public static SharedUdpProtocolHost CreateSelfTrustedLoopback(
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        SharedUdpProtocolLimits sharedLimits,
        NetworkProtocolManifest? manifest = null,
        NetworkProtocolManifest? serverManifest = null) =>
        new(protocolLimits, udpLimits, sharedLimits,
            StopwatchNetworkMonotonicClock.Instance, manifest, serverIdentity: null,
            trustedServerPublicKey: default, serverManifest: serverManifest,
            allowSelfTrust: true);

    internal SharedUdpProtocolHost(
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        SharedUdpProtocolLimits sharedLimits,
        INetworkMonotonicClock clock,
        NetworkProtocolManifest? manifest = null,
        NetworkServerIdentity? serverIdentity = null,
        ReadOnlyMemory<byte> trustedServerPublicKey = default,
        NetworkProtocolManifest? serverManifest = null,
        bool allowSelfTrust = true)
    {
        ArgumentNullException.ThrowIfNull(clock);
        sharedLimits.Validate(udpLimits);
        var datagramLimits = new DeterministicDatagramLimits(1, udpLimits.MaximumDatagramBytes);
        protocolLimits.Validate(datagramLimits);
        _protocolLimits = protocolLimits;
        _udpLimits = udpLimits;
        _sharedLimits = sharedLimits;
        _clientManifest = manifest ?? CoreNetworkProtocol.Manifest;
        _serverManifest = serverManifest ?? _clientManifest;
        _ownsIdentity = serverIdentity is null;
        _identity = serverIdentity ?? NetworkServerIdentity.CreateEphemeral();
        if (trustedServerPublicKey.IsEmpty && !allowSelfTrust)
        {
            if (_ownsIdentity) _identity.Dispose();
            throw new ArgumentException(
                "A pinned server public key is required. Use CreateSelfTrustedLoopback only for controlled in-process topology.",
                nameof(trustedServerPublicKey));
        }
        _trustedServerPublicKey = trustedServerPublicKey.IsEmpty
            ? _identity.PublicKey.ToArray()
            : trustedServerPublicKey.ToArray();
        _clock = clock;
        _startedMilliseconds = clock.Milliseconds;
        _connections = new NetworkConnectionTable(sharedLimits.MaximumSessions);
        _sessions = new SharedUdpProtocolSession[sharedLimits.MaximumSessions];
        _sessionFree = new int[sharedLimits.MaximumSessions];
        for (int index = 0; index < _sessionFree.Length; index++)
            _sessionFree[index] = _sessionFree.Length - 1 - index;
        _sessionFreeCount = _sessionFree.Length;
        _listener = new SharedUdpServerListener(udpLimits, sharedLimits);
    }

    public int SessionCount
    {
        get { EnsureOperational(); return _sessionCount; }
    }

    internal IPEndPoint ListenerLocalEndPoint => _listener.LocalEndPoint;

    /// <summary>
    /// Establishes one real connected loopback client against the shared server
    /// socket. Failure leaves session and connection capacity reusable.
    /// </summary>
    public bool TryAddLoopbackSession(
        out SharedUdpProtocolSession? session,
        out ProtocolHandshakeSnapshot handshake)
    {
        EnsureOperational();
        session = null;
        handshake = default;
        if (_sessionFreeCount == 0) return false;
        int sessionSlot = _sessionFree[_sessionFreeCount - 1];

        UdpDatagramEndpoint? clientEndpoint = null;
        SharedUdpServerEndpoint? serverEndpoint = null;
        NetworkProtocolPeer? clientPeer = null;
        NetworkProtocolPeer? serverPeer = null;
        ProtocolHandshakeResult result = default;
        bool hasResult = false;
        try
        {
            clientEndpoint = UdpDatagramEndpoint.CreateConnectedLoopbackClient(
                _udpLimits, _listener.LocalEndPoint);
            if (!_listener.TryRegister(clientEndpoint.LocalEndPoint, out serverEndpoint) ||
                serverEndpoint is null)
                return false;

            ulong CookieBucket() => _clock.Milliseconds /
                checked((ulong)_udpLimits.CookieBucketMilliseconds);
            if (!ProtocolLoopbackHandshakeHarness.TryEstablishUdp(
                    clientEndpoint, serverEndpoint, _udpLimits.MaximumDatagramBytes,
                    CookieBucket, serverEndpoint.RemotePathBinding,
                    _clientManifest, _serverManifest, _identity!,
                    _trustedServerPublicKey, _udpLimits.MaximumHandshakePumps,
                    out result, _connections))
                return false;
            hasResult = true;

            DrainHandshakeResidue(clientEndpoint, serverEndpoint);
            serverEndpoint.BindConnection(result.Connection);
            var datagramLimits = new DeterministicDatagramLimits(1, _udpLimits.MaximumDatagramBytes);
            clientPeer = new NetworkProtocolPeer(
                clientEndpoint, _clientManifest, _protocolLimits, datagramLimits,
                result.Connection, result.SecurityEpoch, result.MasterSecret,
                sendsClientToServer: true, usesMonotonicPacing: true);
            serverPeer = new NetworkProtocolPeer(
                serverEndpoint, _serverManifest, _protocolLimits, datagramLimits,
                result.Connection, result.SecurityEpoch, result.MasterSecret,
                sendsClientToServer: false, usesMonotonicPacing: true);
            ulong now = ElapsedMilliseconds;
            session = new SharedUdpProtocolSession(
                this, sessionSlot, clientEndpoint, serverEndpoint,
                clientPeer, serverPeer, result.Connection, result.Snapshot, now);
            _sessions[sessionSlot] = session;
            _sessionFreeCount--;
            _sessionCount++;
            handshake = result.Snapshot;
            clientEndpoint = null;
            serverEndpoint = null;
            clientPeer = null;
            serverPeer = null;
            return true;
        }
        finally
        {
            if (hasResult && result.MasterSecret is not null)
                CryptographicOperations.ZeroMemory(result.MasterSecret);
            if (hasResult && session is null && result.Connection.IsValid)
            {
                if (_connections.TryGetState(result.Connection, out NetworkConnectionState state) &&
                    state != NetworkConnectionState.Closed)
                    _ = _connections.TryAdvance(result.Connection, NetworkConnectionState.Closed);
                _ = _connections.TryReleaseClosed(result.Connection);
            }
            clientPeer?.Dispose();
            serverPeer?.Dispose();
            clientEndpoint?.Dispose();
            serverEndpoint?.Dispose();
        }
    }

    /// <summary>Runs one bounded receive, protocol, emit, and lifetime pass.</summary>
    public void Pump()
    {
        EnsureOperational();
        ulong now = ElapsedMilliseconds;
        _listener.PumpInbound();

        for (int index = 0; index < _sessions.Length; index++)
        {
            if (_sessions[index] is not SharedUdpProtocolSession current) continue;
            if (current.RawState == UdpProtocolSessionState.Closed) continue;
            ulong clientReceived = current.Client.ReceivedPacketCount;
            ulong serverReceived = current.Server.ReceivedPacketCount;
            _ = current.Client.ProcessInbound(now);
            _ = current.Server.ProcessInbound(now);
            if (current.Client.ReceivedPacketCount != clientReceived ||
                current.Server.ReceivedPacketCount != serverReceived)
                current.LastActivityMilliseconds = now;
        }

        for (int index = 0; index < _sessions.Length; index++)
        {
            if (_sessions[index] is not SharedUdpProtocolSession current) continue;
            if (current.RawState == UdpProtocolSessionState.Closed) continue;
            if (current.RawState == UdpProtocolSessionState.Established &&
                now - current.LastKeepAliveMilliseconds >=
                    (ulong)_udpLimits.KeepAliveIntervalMilliseconds)
            {
                if (current.Client.TryEmitKeepAlive() || current.Server.TryEmitKeepAlive())
                    current.LastKeepAliveMilliseconds = now;
            }
            current.Client.Emit(now);
            current.Server.Emit(now);
        }

        // Submit only datagrams already admitted during this pump. The listener
        // never waits for more work merely to form a larger native batch.
        _listener.PumpOutbound();

        for (int index = 0; index < _sessions.Length; index++)
        {
            if (_sessions[index] is not SharedUdpProtocolSession current) continue;
            if (current.RawState == UdpProtocolSessionState.Established &&
                now - current.LastActivityMilliseconds >=
                    (ulong)_udpLimits.IdleTimeoutMilliseconds)
                BeginDrainCore(current, now);
            if (current.RawState == UdpProtocolSessionState.Draining &&
                ((current.Client.ReliableQueued == 0 && current.Server.ReliableQueued == 0) ||
                 now >= current.DrainDeadlineMilliseconds))
                CloseTransport(current);
        }
    }

    public SharedUdpProtocolHostSnapshot GetSnapshot()
    {
        EnsureOperational();
        long serverReserved = checked(
            _listener.ReservedBytes +
            _connections.GetSnapshot().ReservedBytes +
            (long)_sessions.Length * IntPtr.Size +
            (long)_sessionFree.Length * sizeof(int) +
            _trustedServerPublicKey.Length);
        long clientReserved = 0;
        for (int index = 0; index < _sessions.Length; index++)
        {
            if (_sessions[index] is not SharedUdpProtocolSession current) continue;
            serverReserved = checked(serverReserved + current.Server.GetSnapshot().ReservedBytes);
            clientReserved = checked(clientReserved + current.Client.GetSnapshot().ReservedBytes);
        }
        long configuredClientSockets = checked((long)_sessionCount *
            (_udpLimits.SocketSendBufferBytes + _udpLimits.SocketReceiveBufferBytes));
        long configuredSockets = checked(configuredClientSockets +
            _listener.ConfiguredSocketBufferBytes);
        return new(_sessionCount, _sessions.Length, _listener.GetSnapshot(),
            _connections.GetSnapshot(), configuredSockets,
            checked(serverReserved + clientReserved), serverReserved, clientReserved,
            _listener.ConfiguredSocketBufferBytes, configuredClientSockets);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        for (int index = 0; index < _sessions.Length; index++)
        {
            if (_sessions[index] is SharedUdpProtocolSession current)
                RetireCore(current);
        }
        _listener.Dispose();
        if (_ownsIdentity) _identity!.Dispose();
        _identity = null;
        CryptographicOperations.ZeroMemory(_trustedServerPublicKey);
        _trustedServerPublicKey = Array.Empty<byte>();
        _disposed = true;
    }

    internal UdpProtocolSessionState GetState(SharedUdpProtocolSession session)
    {
        EnsureSession(session);
        return session.RawState;
    }

    internal void BeginDrain(SharedUdpProtocolSession session)
    {
        EnsureSession(session);
        if (session.RawState == UdpProtocolSessionState.Established)
            BeginDrainCore(session, ElapsedMilliseconds);
    }

    internal SharedUdpProtocolSessionSnapshot GetSessionSnapshot(SharedUdpProtocolSession session)
    {
        EnsureSession(session);
        return new(session.RawState, session.Connection, session.Handshake,
            session.Client.GetSnapshot(), session.Server.GetSnapshot(),
            session.ClientEndpoint.GetSnapshot(), session.ServerEndpoint.GetSnapshot());
    }

    internal void ResetListenerStageTiming(bool enabled)
    {
        EnsureOperational();
        _listener.ResetStageTiming(enabled);
    }

    internal void Retire(SharedUdpProtocolSession session)
    {
        EnsureSession(session);
        RetireCore(session);
    }

    private void BeginDrainCore(SharedUdpProtocolSession session, ulong now)
    {
        session.Client.StopAcceptingNewSends();
        session.Server.StopAcceptingNewSends();
        session.RawState = UdpProtocolSessionState.Draining;
        session.DrainDeadlineMilliseconds = checked(
            now + (ulong)_udpLimits.DrainTimeoutMilliseconds);
        if (_connections.TryGetState(session.Connection, out NetworkConnectionState state) &&
            state == NetworkConnectionState.Established)
            _ = _connections.TryAdvance(session.Connection, NetworkConnectionState.Draining);
    }

    private void CloseTransport(SharedUdpProtocolSession session)
    {
        if (session.RawState == UdpProtocolSessionState.Closed) return;
        session.ClientEndpoint.Dispose();
        session.ServerEndpoint.Dispose();
        if (_connections.TryGetState(session.Connection, out NetworkConnectionState state) &&
            state != NetworkConnectionState.Closed)
            _ = _connections.TryAdvance(session.Connection, NetworkConnectionState.Closed);
        session.RawState = UdpProtocolSessionState.Closed;
    }

    private void RetireCore(SharedUdpProtocolSession session)
    {
        if (session.RawState == UdpProtocolSessionState.Established)
            BeginDrainCore(session, ElapsedMilliseconds);
        CloseTransport(session);
        session.Client.Dispose();
        session.Server.Dispose();
        if (_connections.TryGetState(session.Connection, out NetworkConnectionState state) &&
            state != NetworkConnectionState.Closed)
            _ = _connections.TryAdvance(session.Connection, NetworkConnectionState.Closed);
        _ = _connections.TryReleaseClosed(session.Connection);
        _sessions[session.Slot] = null;
        _sessionFree[_sessionFreeCount++] = session.Slot;
        _sessionCount--;
        session.MarkDisposed();
    }

    private void DrainHandshakeResidue(
        UdpDatagramEndpoint client,
        SharedUdpServerEndpoint server)
    {
        byte[] scratch = new byte[_udpLimits.MaximumDatagramBytes];
        while (client.TryReceive(scratch, out _)) { }
        while (server.TryReceive(scratch, out _)) { }
        CryptographicOperations.ZeroMemory(scratch);
    }

    private ulong ElapsedMilliseconds
    {
        get
        {
            ulong current = _clock.Milliseconds;
            if (current < _startedMilliseconds)
                throw new InvalidOperationException("The network monotonic clock moved backward.");
            return current - _startedMilliseconds;
        }
    }

    private void EnsureSession(SharedUdpProtocolSession session)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(session);
        if (!ReferenceEquals(session.Owner, this) || session.Slot < 0 ||
            session.Slot >= _sessions.Length || !ReferenceEquals(_sessions[session.Slot], session))
            throw new ArgumentException("The shared UDP session does not belong to this host.", nameof(session));
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The shared UDP protocol host is owner-thread-bound.");
    }
}

/// <summary>One connection routed through a <see cref="SharedUdpProtocolHost"/>.</summary>
public sealed class SharedUdpProtocolSession : IDisposable
{
    internal SharedUdpProtocolSession(
        SharedUdpProtocolHost owner,
        int slot,
        UdpDatagramEndpoint clientEndpoint,
        SharedUdpServerEndpoint serverEndpoint,
        NetworkProtocolPeer client,
        NetworkProtocolPeer server,
        ConnectionId connection,
        ProtocolHandshakeSnapshot handshake,
        ulong now)
    {
        Owner = owner;
        Slot = slot;
        ClientEndpoint = clientEndpoint;
        ServerEndpoint = serverEndpoint;
        Client = client;
        Server = server;
        Connection = connection;
        Handshake = handshake;
        RawState = UdpProtocolSessionState.Established;
        LastActivityMilliseconds = now;
        LastKeepAliveMilliseconds = now;
    }

    internal SharedUdpProtocolHost Owner { get; }
    internal int Slot { get; }
    internal UdpDatagramEndpoint ClientEndpoint { get; }
    internal SharedUdpServerEndpoint ServerEndpoint { get; }
    internal UdpProtocolSessionState RawState { get; set; }
    internal ulong LastActivityMilliseconds { get; set; }
    internal ulong LastKeepAliveMilliseconds { get; set; }
    internal ulong DrainDeadlineMilliseconds { get; set; }
    private bool _disposed;

    public ConnectionId Connection { get; }
    public ProtocolHandshakeSnapshot Handshake { get; }
    public NetworkProtocolPeer Client { get; }
    public NetworkProtocolPeer Server { get; }
    public UdpProtocolSessionState State => _disposed
        ? UdpProtocolSessionState.Closed
        : Owner.GetState(this);

    public void BeginDrain()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Owner.BeginDrain(this);
    }

    public SharedUdpProtocolSessionSnapshot GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Owner.GetSessionSnapshot(this);
    }

    /// <summary>Retires this connection and returns its bounded host capacity.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Owner.Retire(this);
    }

    internal void MarkDisposed() => _disposed = true;
}
