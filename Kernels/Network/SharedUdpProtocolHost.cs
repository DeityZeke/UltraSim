using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Diagnostics;

namespace UltraSim.Network;

/// <summary>Platform-specialized shared-listener socket submission policy.</summary>
public enum SharedUdpSocketIoMode : byte
{
    /// <summary>Use the portable one-datagram socket operations.</summary>
    Scalar = 0,
    /// <summary>
    /// Use bounded native batching where supported and otherwise fall back to
    /// the scalar contract without changing packet or application semantics.
    /// </summary>
    PlatformBatch = 1,
}

/// <summary>Fixed ownership and routing bounds for one shared UDP server socket.</summary>
/// <remarks>
/// The listener requests aggregate socket-buffer capacity equal to the admitted
/// per-session server capacity, capped at the existing one-gibibyte socket-buffer
/// limit. This consolidates ownership without silently reducing total admitted
/// kernel buffering as connections are moved behind one socket.
/// </remarks>
public readonly record struct SharedUdpProtocolLimits(
    int MaximumSessions = 256,
    int MaximumQueuedDatagramsPerSession = 8,
    int MaximumInboundDatagramsPerPump = 4_096,
    SharedUdpSocketIoMode SocketIoMode = SharedUdpSocketIoMode.Scalar,
    int MaximumNativeSendBatchDatagrams = 32)
{
    public static SharedUdpProtocolLimits Default => new();

    internal void Validate(UdpProtocolLimits udp)
    {
        udp.Validate();
        if (MaximumSessions is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumSessions));
        if (MaximumQueuedDatagramsPerSession is <= 0 or > 1_024)
            throw new ArgumentOutOfRangeException(nameof(MaximumQueuedDatagramsPerSession));
        if (MaximumInboundDatagramsPerPump is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(MaximumInboundDatagramsPerPump));
        if (!Enum.IsDefined(SocketIoMode))
            throw new ArgumentOutOfRangeException(nameof(SocketIoMode));
        if (MaximumNativeSendBatchDatagrams is < 2 or > 256)
            throw new ArgumentOutOfRangeException(nameof(MaximumNativeSendBatchDatagrams));
        _ = checked((long)MaximumSessions * MaximumQueuedDatagramsPerSession *
            udp.MaximumDatagramBytes);
    }

    internal UdpProtocolLimits CreateListenerLimits(UdpProtocolLimits udp)
    {
        Validate(udp);
        const long maximumSocketBufferBytes = 1_073_741_824L;
        int sendBytes = checked((int)Math.Min(
            maximumSocketBufferBytes,
            checked((long)udp.SocketSendBufferBytes * MaximumSessions)));
        int receiveBytes = checked((int)Math.Min(
            maximumSocketBufferBytes,
            checked((long)udp.SocketReceiveBufferBytes * MaximumSessions)));
        return udp with
        {
            SocketSendBufferBytes = sendBytes,
            SocketReceiveBufferBytes = receiveBytes,
        };
    }

    /// <summary>Estimates the explicitly owned demultiplexing payload.</summary>
    public long EstimateReservedPayloadBytes(UdpProtocolLimits udp)
        => EstimateReservedPayloadBytes(udp, SocketIoMode == SharedUdpSocketIoMode.PlatformBatch);

    internal long EstimateReservedPayloadBytes(UdpProtocolLimits udp, bool includeOutboundBatch)
    {
        Validate(udp);
        int routeCapacity = checked((int)BitOperations.RoundUpToPowerOf2(
            checked((uint)MaximumSessions * 2U)));
        long bytes = checked(
            (long)MaximumSessions * MaximumQueuedDatagramsPerSession * udp.MaximumDatagramBytes +
            (long)MaximumSessions * MaximumQueuedDatagramsPerSession * sizeof(int) +
            (long)MaximumSessions * IntPtr.Size +
            (long)routeCapacity * (sizeof(ulong) + sizeof(int)) +
            udp.MaximumDatagramBytes);
        if (!includeOutboundBatch) return bytes;
        int outbound = checked(MaximumSessions * MaximumQueuedDatagramsPerSession);
        return checked(bytes +
            (long)outbound * udp.MaximumDatagramBytes +
            (long)outbound * (sizeof(int) + 16 + IntPtr.Size) +
            (long)(MaximumNativeSendBatchDatagrams + 1) * sizeof(ulong));
    }
}

/// <summary>Aggregate byte-movement evidence for the shared server socket.</summary>
public readonly record struct SharedUdpListenerSnapshot(
    ulong Sent,
    ulong Received,
    ulong SentBytes,
    ulong ReceivedBytes,
    ulong UnknownPathDatagrams,
    ulong CapacityDrops,
    ulong RejectedDatagrams,
    ulong SendCalls,
    ulong ReceivePollCalls,
    ulong ReceiveCalls,
    ulong WouldBlockEvents,
    bool UsesNativeSendBatching,
    ulong NativeSendCalls,
    ulong NativeSentDatagrams,
    ulong PartialNativeSendCalls,
    ulong NativeSendWouldBlockEvents,
    int NativeSendBatchP50,
    int NativeSendBatchP95,
    int MaximumNativeSendBatch,
    int MaximumOutboundQueuedDatagrams,
    ulong SendSystemCallTicks,
    ulong ReceivePollTicks,
    ulong ReceiveSystemCallTicks,
    int MaximumSentDatagramBytes,
    int MaximumReceivedDatagramBytes,
    int RegisteredPaths,
    int MaximumQueuedDatagrams,
    bool IsOpen,
    SocketError LastSocketError,
    long ReservedBytes);

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
    long KnownReservedBytes);

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
/// additional work.
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
            trustedServerPublicKey, serverManifest)
    {
    }

    internal SharedUdpProtocolHost(
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        SharedUdpProtocolLimits sharedLimits,
        INetworkMonotonicClock clock,
        NetworkProtocolManifest? manifest = null,
        NetworkServerIdentity? serverIdentity = null,
        ReadOnlyMemory<byte> trustedServerPublicKey = default,
        NetworkProtocolManifest? serverManifest = null)
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
            if (!ProtocolLoopbackHandshake.TryEstablishUdp(
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
        long reserved = checked(
            _listener.ReservedBytes +
            _connections.GetSnapshot().ReservedBytes +
            (long)_sessions.Length * IntPtr.Size +
            (long)_sessionFree.Length * sizeof(int) +
            _trustedServerPublicKey.Length);
        for (int index = 0; index < _sessions.Length; index++)
        {
            if (_sessions[index] is not SharedUdpProtocolSession current) continue;
            reserved = checked(reserved + current.Client.GetSnapshot().ReservedBytes +
                current.Server.GetSnapshot().ReservedBytes);
        }
        long configuredSockets = checked(
            (long)_sessionCount *
                (_udpLimits.SocketSendBufferBytes + _udpLimits.SocketReceiveBufferBytes) +
            _listener.ConfiguredSocketBufferBytes);
        return new(_sessionCount, _sessions.Length, _listener.GetSnapshot(),
            _connections.GetSnapshot(), configuredSockets, reserved);
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

internal sealed class SharedUdpServerListener : IDisposable
{
    private const ulong Tombstone = ulong.MaxValue;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Socket _socket;
    private readonly int _socketDescriptor;
    private readonly int _maximumDatagramBytes;
    private readonly int _maximumQueuedDatagramsPerSession;
    private readonly int _maximumInboundDatagramsPerPump;
    private readonly long _configuredSocketBufferBytes;
    private readonly byte[] _receiveScratch;
    private readonly SocketAddress _receivedAddress;
    private readonly SharedUdpServerEndpoint?[] _endpoints;
    private readonly int[] _freeEndpoints;
    private readonly ulong[] _routeKeys;
    private readonly int[] _routeSlots;
    private readonly bool _usesNativeSendBatching;
    private readonly int _maximumNativeSendBatchDatagrams;
    private readonly byte[] _outboundPayloads;
    private readonly byte[] _outboundAddresses;
    private readonly int[] _outboundLengths;
    private readonly SharedUdpServerEndpoint?[] _outboundEndpoints;
    private readonly ulong[] _nativeSendBatchHistogram;
    private int _outboundHead;
    private int _outboundCount;
    private int _freeCount;
    private int _registered;
    private int _maximumQueued;
    private ulong _sent;
    private ulong _received;
    private ulong _sentBytes;
    private ulong _receivedBytes;
    private ulong _unknownPaths;
    private ulong _capacityDrops;
    private ulong _rejected;
    private ulong _sendCalls;
    private ulong _receivePollCalls;
    private ulong _receiveCalls;
    private ulong _wouldBlockEvents;
    private ulong _nativeSendCalls;
    private ulong _nativeSentDatagrams;
    private ulong _partialNativeSendCalls;
    private ulong _nativeSendWouldBlockEvents;
    private int _maximumNativeSendBatch;
    private int _maximumOutboundQueued;
    private bool _stageTimingEnabled;
    private ulong _sendSystemCallTicks;
    private ulong _receivePollTicks;
    private ulong _receiveSystemCallTicks;
    private int _maximumSentDatagramBytes;
    private int _maximumReceivedDatagramBytes;
    private SocketError _lastError;
    private bool _disposed;

    internal SharedUdpServerListener(UdpProtocolLimits udp, SharedUdpProtocolLimits shared)
    {
        shared.Validate(udp);
        UdpProtocolLimits listenerLimits = shared.CreateListenerLimits(udp);
        _maximumDatagramBytes = udp.MaximumDatagramBytes;
        _maximumQueuedDatagramsPerSession = shared.MaximumQueuedDatagramsPerSession;
        _maximumInboundDatagramsPerPump = shared.MaximumInboundDatagramsPerPump;
        _maximumNativeSendBatchDatagrams = shared.MaximumNativeSendBatchDatagrams;
        _usesNativeSendBatching =
            shared.SocketIoMode == SharedUdpSocketIoMode.PlatformBatch && LinuxUdpBatch.IsSupported;
        _receiveScratch = new byte[udp.MaximumDatagramBytes];
        _receivedAddress = new SocketAddress(AddressFamily.InterNetwork, 16);
        _endpoints = new SharedUdpServerEndpoint[shared.MaximumSessions];
        _freeEndpoints = new int[shared.MaximumSessions];
        for (int index = 0; index < _freeEndpoints.Length; index++)
            _freeEndpoints[index] = _freeEndpoints.Length - 1 - index;
        _freeCount = _freeEndpoints.Length;
        int routeCapacity = checked((int)BitOperations.RoundUpToPowerOf2(
            checked((uint)shared.MaximumSessions * 2U)));
        _routeKeys = new ulong[routeCapacity];
        _routeSlots = new int[routeCapacity];
        Array.Fill(_routeSlots, -1);
        int outboundCapacity = _usesNativeSendBatching
            ? checked(shared.MaximumSessions * shared.MaximumQueuedDatagramsPerSession)
            : 0;
        _outboundPayloads = outboundCapacity == 0
            ? Array.Empty<byte>()
            : new byte[checked(outboundCapacity * udp.MaximumDatagramBytes)];
        _outboundAddresses = outboundCapacity == 0
            ? Array.Empty<byte>()
            : new byte[checked(outboundCapacity * 16)];
        _outboundLengths = outboundCapacity == 0 ? Array.Empty<int>() : new int[outboundCapacity];
        _outboundEndpoints = outboundCapacity == 0
            ? Array.Empty<SharedUdpServerEndpoint>()
            : new SharedUdpServerEndpoint[outboundCapacity];
        _nativeSendBatchHistogram = _usesNativeSendBatching
            ? new ulong[shared.MaximumNativeSendBatchDatagrams + 1]
            : Array.Empty<ulong>();
        _configuredSocketBufferBytes = checked(
            (long)listenerLimits.SocketSendBufferBytes +
            listenerLimits.SocketReceiveBufferBytes);
        _socket = UdpDatagramEndpoint.CreateSocket(listenerLimits);
        _socketDescriptor = _usesNativeSendBatching
            ? _socket.SafeHandle.DangerousGetHandle().ToInt32()
            : -1;
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        _socket.Blocking = false;
        LocalEndPoint = (IPEndPoint)_socket.LocalEndPoint!;
        ReservedBytes = shared.EstimateReservedPayloadBytes(udp, _usesNativeSendBatching);
    }

    internal IPEndPoint LocalEndPoint { get; }
    internal long ReservedBytes { get; }
    internal long ConfiguredSocketBufferBytes => _configuredSocketBufferBytes;

    internal bool TryRegister(IPEndPoint remote, out SharedUdpServerEndpoint? endpoint)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(remote);
        ulong key = Pack(remote);
        if (TryResolve(key, out _) || _freeCount == 0)
        {
            endpoint = null;
            return false;
        }
        int routeIndex = FindInsertion(key);
        if (routeIndex < 0)
        {
            endpoint = null;
            return false;
        }
        int slot = _freeEndpoints[--_freeCount];
        endpoint = new SharedUdpServerEndpoint(
            this, slot, remote, _maximumDatagramBytes,
            _maximumQueuedDatagramsPerSession);
        _endpoints[slot] = endpoint;
        _routeKeys[routeIndex] = key;
        _routeSlots[routeIndex] = slot;
        _registered++;
        return true;
    }

    internal void PumpInbound()
    {
        EnsureOperational();
        int budget = _maximumInboundDatagramsPerPump;
        while (budget-- > 0)
        {
            _receivePollCalls = SaturatingIncrement(_receivePollCalls);
            long pollStarted = StartStageTiming();
            bool readable = _socket.Poll(0, SelectMode.SelectRead);
            RecordStageTiming(ref _receivePollTicks, pollStarted);
            if (!readable) break;
            try
            {
                _receiveCalls = SaturatingIncrement(_receiveCalls);
                long receiveStarted = StartStageTiming();
                int length = _socket.ReceiveFrom(_receiveScratch, SocketFlags.None, _receivedAddress);
                RecordStageTiming(ref _receiveSystemCallTicks, receiveStarted);
                if (length <= 0) break;
                _received = SaturatingIncrement(_received);
                _receivedBytes = SaturatingAdd(_receivedBytes, checked((ulong)length));
                _maximumReceivedDatagramBytes = Math.Max(_maximumReceivedDatagramBytes, length);
                if (_receivedAddress.Family != AddressFamily.InterNetwork ||
                    !TryResolve(Pack(_receivedAddress), out int slot) ||
                    _endpoints[slot] is not SharedUdpServerEndpoint endpoint)
                {
                    _unknownPaths = SaturatingIncrement(_unknownPaths);
                    continue;
                }
                if (!endpoint.TryEnqueue(_receiveScratch.AsSpan(0, length)))
                    _capacityDrops = SaturatingIncrement(_capacityDrops);
                else
                    _maximumQueued = Math.Max(_maximumQueued, endpoint.QueuedDatagrams);
            }
            catch (SocketException exception) when (IsTransient(exception.SocketErrorCode))
            {
                _lastError = exception.SocketErrorCode;
                _wouldBlockEvents = SaturatingIncrement(_wouldBlockEvents);
                break;
            }
            catch (SocketException exception)
            {
                _lastError = exception.SocketErrorCode;
                _rejected = SaturatingIncrement(_rejected);
                break;
            }
        }
    }

    internal DatagramSendStatus TrySend(int slot, ReadOnlySpan<byte> datagram) =>
        TrySend(slot, datagram, allowBatch: true);

    internal DatagramSendStatus TrySendImmediate(int slot, ReadOnlySpan<byte> datagram) =>
        TrySend(slot, datagram, allowBatch: false);

    private DatagramSendStatus TrySend(
        int slot,
        ReadOnlySpan<byte> datagram,
        bool allowBatch)
    {
        EnsureOperational();
        if ((uint)slot >= (uint)_endpoints.Length ||
            _endpoints[slot] is not SharedUdpServerEndpoint endpoint)
            return DatagramSendStatus.CapacityExceeded;
        if (datagram.Length > _maximumDatagramBytes)
        {
            _rejected = SaturatingIncrement(_rejected);
            return DatagramSendStatus.DatagramTooLarge;
        }
        if (_usesNativeSendBatching && allowBatch)
        {
            if (_outboundCount == _outboundLengths.Length)
            {
                _capacityDrops = SaturatingIncrement(_capacityDrops);
                return DatagramSendStatus.CapacityExceeded;
            }
            int tail = (_outboundHead + _outboundCount) % _outboundLengths.Length;
            datagram.CopyTo(_outboundPayloads.AsSpan(tail * _maximumDatagramBytes));
            Span<byte> nativeAddress = _outboundAddresses.AsSpan(tail * 16, 16);
            for (int index = 0; index < nativeAddress.Length; index++)
                nativeAddress[index] = (byte)endpoint.RemoteAddress[index];
            _outboundLengths[tail] = datagram.Length;
            _outboundEndpoints[tail] = endpoint;
            _outboundCount++;
            _maximumOutboundQueued = Math.Max(_maximumOutboundQueued, _outboundCount);
            return DatagramSendStatus.Accepted;
        }
        return TrySendScalar(endpoint, datagram);
    }

    internal void PumpOutbound()
    {
        EnsureOperational();
        if (!_usesNativeSendBatching || _outboundCount == 0) return;

        DiscardInvalidOutboundHead();
        while (_outboundCount != 0)
        {
            int requested = Math.Min(_outboundCount, _maximumNativeSendBatchDatagrams);
            for (int offset = 1; offset < requested; offset++)
            {
                int slot = (_outboundHead + offset) % _outboundLengths.Length;
                SharedUdpServerEndpoint? candidate = _outboundEndpoints[slot];
                if (candidate is null || candidate.IsDisposed ||
                    (uint)candidate.Slot >= (uint)_endpoints.Length ||
                    !ReferenceEquals(_endpoints[candidate.Slot], candidate))
                {
                    requested = offset;
                    break;
                }
            }
            long sendStarted = StartStageTiming();
            int sent = LinuxUdpBatch.Send(
                _socketDescriptor,
                _outboundPayloads,
                _maximumDatagramBytes,
                _outboundAddresses,
                16,
                _outboundLengths,
                _outboundHead,
                _outboundLengths.Length,
                requested,
                out int error);
            RecordStageTiming(ref _sendSystemCallTicks, sendStarted);
            _sendCalls = SaturatingIncrement(_sendCalls);
            _nativeSendCalls = SaturatingIncrement(_nativeSendCalls);
            if (sent < 0)
            {
                if (error is 11 or 105)
                {
                    _lastError = error == 105
                        ? SocketError.NoBufferSpaceAvailable
                        : SocketError.WouldBlock;
                    _wouldBlockEvents = SaturatingIncrement(_wouldBlockEvents);
                    _nativeSendWouldBlockEvents = SaturatingIncrement(_nativeSendWouldBlockEvents);
                    break;
                }

                _lastError = SocketError.SocketError;
                _rejected = SaturatingIncrement(_rejected);
                ConsumeOutbound(1, sentSuccessfully: false);
                DiscardInvalidOutboundHead();
                continue;
            }
            if (sent == 0) break;
            if (sent < requested)
                _partialNativeSendCalls = SaturatingIncrement(_partialNativeSendCalls);
            _nativeSentDatagrams = SaturatingAdd(_nativeSentDatagrams, checked((ulong)sent));
            _maximumNativeSendBatch = Math.Max(_maximumNativeSendBatch, sent);
            _nativeSendBatchHistogram[sent] = SaturatingIncrement(_nativeSendBatchHistogram[sent]);
            ConsumeOutbound(sent, sentSuccessfully: true);
            DiscardInvalidOutboundHead();
        }
    }

    private DatagramSendStatus TrySendScalar(
        SharedUdpServerEndpoint endpoint,
        ReadOnlySpan<byte> datagram)
    {
        try
        {
            _sendCalls = SaturatingIncrement(_sendCalls);
            long sendStarted = StartStageTiming();
            if (_socket.SendTo(datagram, SocketFlags.None, endpoint.RemoteAddress) != datagram.Length)
            {
                RecordStageTiming(ref _sendSystemCallTicks, sendStarted);
                _capacityDrops = SaturatingIncrement(_capacityDrops);
                return DatagramSendStatus.CapacityExceeded;
            }
            RecordStageTiming(ref _sendSystemCallTicks, sendStarted);
            _sent = SaturatingIncrement(_sent);
            _sentBytes = SaturatingAdd(_sentBytes, checked((ulong)datagram.Length));
            _maximumSentDatagramBytes = Math.Max(_maximumSentDatagramBytes, datagram.Length);
            return DatagramSendStatus.Accepted;
        }
        catch (SocketException exception) when (IsTransient(exception.SocketErrorCode))
        {
            _lastError = exception.SocketErrorCode;
            _capacityDrops = SaturatingIncrement(_capacityDrops);
            _wouldBlockEvents = SaturatingIncrement(_wouldBlockEvents);
            return DatagramSendStatus.CapacityExceeded;
        }
        catch (SocketException exception)
        {
            _lastError = exception.SocketErrorCode;
            _rejected = SaturatingIncrement(_rejected);
            return DatagramSendStatus.CapacityExceeded;
        }
    }

    private void ConsumeOutbound(int count, bool sentSuccessfully)
    {
        for (int offset = 0; offset < count; offset++)
        {
            int slot = _outboundHead;
            if (sentSuccessfully)
            {
                int length = _outboundLengths[slot];
                _sent = SaturatingIncrement(_sent);
                _sentBytes = SaturatingAdd(_sentBytes, checked((ulong)length));
                _maximumSentDatagramBytes = Math.Max(_maximumSentDatagramBytes, length);
            }
            _outboundPayloads.AsSpan(slot * _maximumDatagramBytes, _outboundLengths[slot]).Clear();
            _outboundAddresses.AsSpan(slot * 16, 16).Clear();
            _outboundLengths[slot] = 0;
            _outboundEndpoints[slot] = null;
            _outboundHead = (_outboundHead + 1) % _outboundLengths.Length;
            _outboundCount--;
        }
    }

    private void DiscardInvalidOutboundHead()
    {
        while (_outboundCount != 0)
        {
            SharedUdpServerEndpoint? endpoint = _outboundEndpoints[_outboundHead];
            if (endpoint is not null && !endpoint.IsDisposed &&
                (uint)endpoint.Slot < (uint)_endpoints.Length &&
                ReferenceEquals(_endpoints[endpoint.Slot], endpoint)) return;
            _rejected = SaturatingIncrement(_rejected);
            ConsumeOutbound(1, sentSuccessfully: false);
        }
    }

    internal void Unregister(SharedUdpServerEndpoint endpoint)
    {
        EnsureOwnerThread();
        if (_disposed || (uint)endpoint.Slot >= (uint)_endpoints.Length ||
            !ReferenceEquals(_endpoints[endpoint.Slot], endpoint)) return;
        ulong key = Pack(endpoint.RemoteEndPoint);
        int mask = _routeKeys.Length - 1;
        int index = Hash(key) & mask;
        for (int probe = 0; probe < _routeKeys.Length; probe++)
        {
            ulong current = _routeKeys[index];
            if (current == 0) break;
            if (current == key && _routeSlots[index] == endpoint.Slot)
            {
                _routeKeys[index] = Tombstone;
                _routeSlots[index] = -1;
                break;
            }
            index = (index + 1) & mask;
        }
        DiscardEndpointOutbound(endpoint);
        _endpoints[endpoint.Slot] = null;
        _freeEndpoints[_freeCount++] = endpoint.Slot;
        _registered--;
    }

    private void DiscardEndpointOutbound(SharedUdpServerEndpoint endpoint)
    {
        if (_outboundCount == 0) return;
        int originalCount = _outboundCount;
        int writeOffset = 0;
        for (int readOffset = 0; readOffset < originalCount; readOffset++)
        {
            int read = (_outboundHead + readOffset) % _outboundLengths.Length;
            if (ReferenceEquals(_outboundEndpoints[read], endpoint))
            {
                _outboundPayloads.AsSpan(
                    read * _maximumDatagramBytes, _outboundLengths[read]).Clear();
                _outboundAddresses.AsSpan(read * 16, 16).Clear();
                _outboundLengths[read] = 0;
                _outboundEndpoints[read] = null;
                continue;
            }

            int write = (_outboundHead + writeOffset) % _outboundLengths.Length;
            if (write != read)
            {
                _outboundPayloads.AsSpan(
                    read * _maximumDatagramBytes, _outboundLengths[read]).CopyTo(
                        _outboundPayloads.AsSpan(write * _maximumDatagramBytes));
                _outboundAddresses.AsSpan(read * 16, 16).CopyTo(
                    _outboundAddresses.AsSpan(write * 16, 16));
                _outboundLengths[write] = _outboundLengths[read];
                _outboundEndpoints[write] = _outboundEndpoints[read];
                _outboundPayloads.AsSpan(
                    read * _maximumDatagramBytes, _outboundLengths[read]).Clear();
                _outboundAddresses.AsSpan(read * 16, 16).Clear();
                _outboundLengths[read] = 0;
                _outboundEndpoints[read] = null;
            }
            writeOffset++;
        }
        _outboundCount = writeOffset;
    }

    internal SharedUdpListenerSnapshot GetSnapshot()
    {
        EnsureOperational();
        int p50 = BatchPercentile(0.50);
        int p95 = BatchPercentile(0.95);
        return new(_sent, _received, _sentBytes, _receivedBytes,
            _unknownPaths, _capacityDrops, _rejected, _sendCalls, _receivePollCalls,
            _receiveCalls, _wouldBlockEvents, _usesNativeSendBatching,
            _nativeSendCalls, _nativeSentDatagrams, _partialNativeSendCalls,
            _nativeSendWouldBlockEvents, p50, p95, _maximumNativeSendBatch,
            _maximumOutboundQueued, _sendSystemCallTicks, _receivePollTicks,
            _receiveSystemCallTicks, _maximumSentDatagramBytes,
            _maximumReceivedDatagramBytes, _registered,
            _maximumQueued, true, _lastError, ReservedBytes);
    }

    internal void ResetStageTiming(bool enabled)
    {
        EnsureOperational();
        _sendSystemCallTicks = 0;
        _receivePollTicks = 0;
        _receiveSystemCallTicks = 0;
        if (enabled)
        {
            _nativeSendCalls = 0;
            _nativeSentDatagrams = 0;
            _partialNativeSendCalls = 0;
            _nativeSendWouldBlockEvents = 0;
            _maximumNativeSendBatch = 0;
            _maximumOutboundQueued = _outboundCount;
            Array.Clear(_nativeSendBatchHistogram);
        }
        _stageTimingEnabled = enabled;
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose();
        CryptographicOperations.ZeroMemory(_receiveScratch);
        CryptographicOperations.ZeroMemory(_outboundPayloads);
        CryptographicOperations.ZeroMemory(_outboundAddresses);
        Array.Clear(_outboundLengths);
        Array.Clear(_outboundEndpoints);
    }

    private int FindInsertion(ulong key)
    {
        int mask = _routeKeys.Length - 1;
        int index = Hash(key) & mask;
        int tombstone = -1;
        for (int probe = 0; probe < _routeKeys.Length; probe++)
        {
            ulong current = _routeKeys[index];
            if (current == 0) return tombstone >= 0 ? tombstone : index;
            if (current == Tombstone && tombstone < 0) tombstone = index;
            index = (index + 1) & mask;
        }
        return tombstone;
    }

    private bool TryResolve(ulong key, out int slot)
    {
        int mask = _routeKeys.Length - 1;
        int index = Hash(key) & mask;
        for (int probe = 0; probe < _routeKeys.Length; probe++)
        {
            ulong current = _routeKeys[index];
            if (current == 0) break;
            if (current == key)
            {
                slot = _routeSlots[index];
                return slot >= 0;
            }
            index = (index + 1) & mask;
        }
        slot = -1;
        return false;
    }

    private static int Hash(ulong key)
    {
        unchecked
        {
            key ^= key >> 33;
            key *= 0xff51afd7ed558ccdUL;
            key ^= key >> 33;
            return (int)key;
        }
    }

    private static ulong Pack(IPEndPoint endpoint)
    {
        byte[] bytes = endpoint.Address.GetAddressBytes();
        if (bytes.Length != 4) throw new ArgumentException("Only IPv4 paths are supported.", nameof(endpoint));
        uint address = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) |
            ((uint)bytes[2] << 8) | bytes[3];
        return ((ulong)address << 16) | checked((ushort)endpoint.Port);
    }

    private static ulong Pack(SocketAddress endpoint)
    {
        uint address = ((uint)endpoint[4] << 24) | ((uint)endpoint[5] << 16) |
            ((uint)endpoint[6] << 8) | endpoint[7];
        ushort port = (ushort)((endpoint[2] << 8) | endpoint[3]);
        return ((ulong)address << 16) | port;
    }

    private static bool IsTransient(SocketError error) => error is
        SocketError.WouldBlock or SocketError.IOPending or SocketError.NoBufferSpaceAvailable;

    private static ulong SaturatingIncrement(ulong value) =>
        value == ulong.MaxValue ? value : value + 1;

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private int BatchPercentile(double percentile)
    {
        ulong total = 0;
        for (int index = 1; index < _nativeSendBatchHistogram.Length; index++)
            total = SaturatingAdd(total, _nativeSendBatchHistogram[index]);
        if (total == 0) return 0;
        ulong target = checked((ulong)Math.Ceiling(total * percentile));
        ulong cumulative = 0;
        for (int index = 1; index < _nativeSendBatchHistogram.Length; index++)
        {
            cumulative = SaturatingAdd(cumulative, _nativeSendBatchHistogram[index]);
            if (cumulative >= target) return index;
        }
        return _maximumNativeSendBatch;
    }

    private long StartStageTiming() => _stageTimingEnabled ? Stopwatch.GetTimestamp() : 0;

    private void RecordStageTiming(ref ulong accumulator, long started)
    {
        if (!_stageTimingEnabled) return;
        long elapsed = Stopwatch.GetTimestamp() - started;
        if (elapsed > 0)
            accumulator = SaturatingAdd(accumulator, checked((ulong)elapsed));
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The shared UDP listener is owner-thread-bound.");
    }
}

internal sealed class SharedUdpServerEndpoint : INetworkDatagramEndpoint, IDisposable
{
    private readonly SharedUdpServerListener _listener;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly int _maximumDatagramBytes;
    private readonly byte[] _payloads;
    private readonly int[] _lengths;
    private int _head;
    private int _count;
    private ConnectionId _connection;
    private ulong _sent;
    private ulong _received;
    private ulong _sentBytes;
    private ulong _receivedBytes;
    private ulong _capacityDrops;
    private ulong _rejected;
    private int _maximumSentDatagramBytes;
    private int _maximumReceivedDatagramBytes;
    private bool _disposed;

    internal SharedUdpServerEndpoint(
        SharedUdpServerListener listener,
        int slot,
        IPEndPoint remote,
        int maximumDatagramBytes,
        int queueCapacity)
    {
        _listener = listener;
        Slot = slot;
        RemoteEndPoint = remote;
        RemoteAddress = remote.Serialize();
        RemotePathBinding = UdpDatagramEndpoint.CreatePathBinding(remote);
        _maximumDatagramBytes = maximumDatagramBytes;
        _payloads = new byte[checked(queueCapacity * maximumDatagramBytes)];
        _lengths = new int[queueCapacity];
    }

    internal int Slot { get; }
    internal IPEndPoint RemoteEndPoint { get; }
    internal SocketAddress RemoteAddress { get; }
    internal byte[] RemotePathBinding { get; }
    internal int QueuedDatagrams => _count;
    internal bool IsDisposed => _disposed;

    internal void BindConnection(ConnectionId connection)
    {
        EnsureOperational();
        if (_connection.IsValid || !connection.IsValid)
            throw new InvalidOperationException("The shared endpoint connection binding is invalid.");
        _connection = connection;
    }

    internal bool TryEnqueue(ReadOnlySpan<byte> datagram)
    {
        EnsureOperational();
        if (datagram.Length > _maximumDatagramBytes)
        {
            _rejected = SaturatingIncrement(_rejected);
            return false;
        }
        if (_count == _lengths.Length)
        {
            _capacityDrops = SaturatingIncrement(_capacityDrops);
            return false;
        }
        int tail = (_head + _count) % _lengths.Length;
        datagram.CopyTo(_payloads.AsSpan(tail * _maximumDatagramBytes));
        _lengths[tail] = datagram.Length;
        _count++;
        _received = SaturatingIncrement(_received);
        _receivedBytes = SaturatingAdd(_receivedBytes, checked((ulong)datagram.Length));
        return true;
    }

    public DatagramSendStatus TrySend(ReadOnlySpan<byte> datagram)
    {
        EnsureOwnerThread();
        if (_disposed) return DatagramSendStatus.CapacityExceeded;
        DatagramSendStatus status = _connection.IsValid
            ? _listener.TrySend(Slot, datagram)
            : _listener.TrySendImmediate(Slot, datagram);
        if (status == DatagramSendStatus.Accepted)
        {
            _sent = SaturatingIncrement(_sent);
            _sentBytes = SaturatingAdd(_sentBytes, checked((ulong)datagram.Length));
            _maximumSentDatagramBytes = Math.Max(_maximumSentDatagramBytes, datagram.Length);
        }
        else if (status == DatagramSendStatus.CapacityExceeded)
            _capacityDrops = SaturatingIncrement(_capacityDrops);
        else if (status == DatagramSendStatus.DatagramTooLarge)
            _rejected = SaturatingIncrement(_rejected);
        return status;
    }

    public bool TryReceive(Span<byte> destination, out int length)
    {
        EnsureOwnerThread();
        length = 0;
        if (_disposed) return false;
        if (destination.Length < _maximumDatagramBytes)
            throw new ArgumentException("The UDP receive destination is below the admitted datagram size.", nameof(destination));
        if (!_connection.IsValid) _listener.PumpInbound();
        if (_count == 0) return false;
        length = _lengths[_head];
        _payloads.AsSpan(_head * _maximumDatagramBytes, length).CopyTo(destination);
        _payloads.AsSpan(_head * _maximumDatagramBytes, length).Clear();
        _lengths[_head] = 0;
        _head = (_head + 1) % _lengths.Length;
        _count--;
        _maximumReceivedDatagramBytes = Math.Max(_maximumReceivedDatagramBytes, length);
        return true;
    }

    internal UdpDatagramEndpointSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(_sent, _received, _sentBytes, _receivedBytes,
            _capacityDrops, _rejected, 0, 0, 0, 0,
            _maximumSentDatagramBytes, _maximumReceivedDatagramBytes,
            !_disposed, SocketError.Success);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _listener.Unregister(this);
        _disposed = true;
        CryptographicOperations.ZeroMemory(_payloads);
        CryptographicOperations.ZeroMemory(RemotePathBinding);
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
            throw new InvalidOperationException("The shared UDP endpoint is owner-thread-bound.");
    }
}
