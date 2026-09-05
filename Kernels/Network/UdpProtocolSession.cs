using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace UltraSim.Network;

/// <summary>Fixed socket/session bounds for one owner-thread localhost UDP topology.</summary>
/// <remarks>
/// <c>CookieBucketMilliseconds</c> is one rotating bucket duration. Validation accepts
/// the current and immediately previous bucket, so maximum cookie age can approach twice it.
/// </remarks>
public readonly record struct UdpProtocolLimits(
    int MaximumDatagramBytes = 1_472,
    int SocketSendBufferBytes = 262_144,
    int SocketReceiveBufferBytes = 262_144,
    int MaximumHandshakePumps = 4_096,
    int CookieBucketMilliseconds = 30_000,
    int IdleTimeoutMilliseconds = 30_000,
    int DrainTimeoutMilliseconds = 5_000,
    int KeepAliveIntervalMilliseconds = 5_000)
{
    public static UdpProtocolLimits Default => new(
        MaximumDatagramBytes: 1_472,
        SocketSendBufferBytes: 262_144,
        SocketReceiveBufferBytes: 262_144,
        MaximumHandshakePumps: 4_096,
        CookieBucketMilliseconds: 30_000,
        IdleTimeoutMilliseconds: 30_000,
        DrainTimeoutMilliseconds: 5_000,
        KeepAliveIntervalMilliseconds: 5_000);

    internal void Validate()
    {
        if (MaximumDatagramBytes is < 256 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumDatagramBytes));
        if (SocketSendBufferBytes < MaximumDatagramBytes || SocketSendBufferBytes > 1_073_741_824)
            throw new ArgumentOutOfRangeException(nameof(SocketSendBufferBytes));
        if (SocketReceiveBufferBytes < MaximumDatagramBytes || SocketReceiveBufferBytes > 1_073_741_824)
            throw new ArgumentOutOfRangeException(nameof(SocketReceiveBufferBytes));
        if (MaximumHandshakePumps is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(MaximumHandshakePumps));
        if (CookieBucketMilliseconds is <= 0 or > 3_600_000)
            throw new ArgumentOutOfRangeException(nameof(CookieBucketMilliseconds));
        if (IdleTimeoutMilliseconds is <= 0 or > 86_400_000)
            throw new ArgumentOutOfRangeException(nameof(IdleTimeoutMilliseconds));
        if (DrainTimeoutMilliseconds <= 0 || DrainTimeoutMilliseconds > IdleTimeoutMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(DrainTimeoutMilliseconds));
        if (KeepAliveIntervalMilliseconds <= 0 || KeepAliveIntervalMilliseconds >= IdleTimeoutMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(KeepAliveIntervalMilliseconds));
    }

    /// <summary>
    /// Reports configured socket-buffer payload for the two connected endpoints.
    /// The operating system may round or cap the actual kernel reservation.
    /// </summary>
    public long EstimateConfiguredSocketBufferBytes()
    {
        Validate();
        return checked(2L * (SocketSendBufferBytes + SocketReceiveBufferBytes));
    }
}

public enum UdpProtocolSessionState : byte
{
    Established = 0,
    Draining = 1,
    Closed = 2,
}

public readonly record struct UdpDatagramEndpointSnapshot(
    ulong Sent,
    ulong Received,
    ulong SentBytes,
    ulong ReceivedBytes,
    ulong CapacityDrops,
    ulong RejectedDatagrams,
    ulong SendCalls,
    ulong ReceivePollCalls,
    ulong ReceiveCalls,
    ulong WouldBlockEvents,
    int MaximumSentDatagramBytes,
    int MaximumReceivedDatagramBytes,
    bool IsOpen,
    SocketError LastSocketError);

public readonly record struct UdpProtocolSessionSnapshot(
    UdpProtocolSessionState State,
    ulong MonotonicMilliseconds,
    ulong LastActivityMilliseconds,
    ConnectionId Connection,
    ProtocolHandshakeSnapshot Handshake,
    NetworkProtocolSnapshot Client,
    NetworkProtocolSnapshot Server,
    UdpDatagramEndpointSnapshot ClientDatagrams,
    UdpDatagramEndpointSnapshot ServerDatagrams,
    NetworkConnectionSnapshot Connections,
    long ConfiguredSocketBufferBytes);

/// <summary>
/// Real connected-UDP transport for one secure client/server protocol session.
/// </summary>
/// <remarks>
/// This first production topology is deliberately single-owner: socket I/O and
/// <see cref="NetworkProtocolPeer"/> execution happen on the creating thread.
/// No cross-thread packet-buffer ownership is implied. Connected sockets bind
/// the admitted path, while the stateless handshake cookie additionally binds
/// the client IP/port and expires by monotonic time bucket.
/// </remarks>
public sealed class UdpProtocolSession : IDisposable
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly UdpProtocolLimits _udpLimits;
    private readonly NetworkConnectionTable _connections;
    private readonly UdpDatagramEndpoint _clientEndpoint;
    private readonly UdpDatagramEndpoint _serverEndpoint;
    private readonly bool _ownsIdentity;
    private readonly INetworkMonotonicClock _clock;
    private NetworkServerIdentity? _identity;
    private readonly ulong _startedMilliseconds;
    private ulong _lastActivity;
    private ulong _drainDeadline;
    private ulong _lastKeepAlive;
    private UdpProtocolSessionState _state;
    private bool _disposed;

    private UdpProtocolSession(
        UdpProtocolLimits udpLimits,
        UdpDatagramEndpoint clientEndpoint,
        UdpDatagramEndpoint serverEndpoint,
        NetworkProtocolManifest clientManifest,
        NetworkProtocolManifest serverManifest,
        NetworkProtocolLimits protocolLimits,
        NetworkServerIdentity identity,
        bool ownsIdentity,
        INetworkMonotonicClock clock,
        ProtocolHandshakeResult handshake)
    {
        _udpLimits = udpLimits;
        _clientEndpoint = clientEndpoint;
        _serverEndpoint = serverEndpoint;
        _connections = handshake.Connections;
        _identity = identity;
        _ownsIdentity = ownsIdentity;
        _clock = clock;
        _startedMilliseconds = clock.Milliseconds;
        Handshake = handshake.Snapshot;
        Connection = handshake.Connection;
        var datagramLimits = new DeterministicDatagramLimits(1, udpLimits.MaximumDatagramBytes);
        Client = new NetworkProtocolPeer(
            clientEndpoint, clientManifest, protocolLimits, datagramLimits,
            handshake.Connection, handshake.SecurityEpoch, handshake.MasterSecret,
            sendsClientToServer: true,
            usesMonotonicPacing: true);
        Server = new NetworkProtocolPeer(
            serverEndpoint, serverManifest, protocolLimits, datagramLimits,
            handshake.Connection, handshake.SecurityEpoch, handshake.MasterSecret,
            sendsClientToServer: false,
            usesMonotonicPacing: true);
        CryptographicOperations.ZeroMemory(handshake.MasterSecret);
        _state = UdpProtocolSessionState.Established;
    }

    public ConnectionId Connection { get; }
    public ProtocolHandshakeSnapshot Handshake { get; }
    public NetworkProtocolPeer Client { get; }
    public NetworkProtocolPeer Server { get; }
    public UdpProtocolSessionState State { get { EnsureOwnerThread(); return _state; } }

    public static bool TryCreate(
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        out UdpProtocolSession? session,
        out ProtocolHandshakeSnapshot handshake,
        NetworkProtocolManifest? manifest = null,
        NetworkServerIdentity? serverIdentity = null,
        ReadOnlyMemory<byte> trustedServerPublicKey = default,
        NetworkProtocolManifest? serverManifest = null) =>
        TryCreateCore(protocolLimits, udpLimits, StopwatchNetworkMonotonicClock.Instance,
            out session, out handshake, manifest, serverIdentity,
            trustedServerPublicKey, serverManifest, allowSelfTrust: false);

    /// <summary>
    /// Creates a controlled in-process loopback pair and explicitly trusts the
    /// server identity owned by that pair. This is test/benchmark topology, not
    /// a production remote-client trust policy.
    /// </summary>
    public static bool TryCreateSelfTrustedLoopback(
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        out UdpProtocolSession? session,
        out ProtocolHandshakeSnapshot handshake,
        NetworkProtocolManifest? manifest = null,
        NetworkProtocolManifest? serverManifest = null) =>
        TryCreateCore(protocolLimits, udpLimits, StopwatchNetworkMonotonicClock.Instance,
            out session, out handshake, manifest, serverIdentity: null,
            trustedServerPublicKey: default, serverManifest: serverManifest, allowSelfTrust: true);

    internal static bool TryCreateForTesting(
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        INetworkMonotonicClock clock,
        out UdpProtocolSession? session,
        out ProtocolHandshakeSnapshot handshake,
        NetworkProtocolManifest? manifest = null,
        NetworkServerIdentity? serverIdentity = null,
        ReadOnlyMemory<byte> trustedServerPublicKey = default,
        NetworkProtocolManifest? serverManifest = null) =>
        TryCreateCore(protocolLimits, udpLimits, clock, out session, out handshake,
            manifest, serverIdentity, trustedServerPublicKey, serverManifest,
            allowSelfTrust: true);

    private static bool TryCreateCore(
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        INetworkMonotonicClock clock,
        out UdpProtocolSession? session,
        out ProtocolHandshakeSnapshot handshake,
        NetworkProtocolManifest? manifest,
        NetworkServerIdentity? serverIdentity,
        ReadOnlyMemory<byte> trustedServerPublicKey,
        NetworkProtocolManifest? serverManifest,
        bool allowSelfTrust)
    {
        ArgumentNullException.ThrowIfNull(clock);
        udpLimits.Validate();
        var datagramLimits = new DeterministicDatagramLimits(1, udpLimits.MaximumDatagramBytes);
        protocolLimits.Validate(datagramLimits);
        manifest ??= CoreNetworkProtocol.Manifest;
        serverManifest ??= manifest;
        bool ownsIdentity = serverIdentity is null;
        serverIdentity ??= NetworkServerIdentity.CreateEphemeral();
        if (trustedServerPublicKey.IsEmpty)
        {
            if (!allowSelfTrust)
            {
                if (ownsIdentity) serverIdentity.Dispose();
                session = null;
                handshake = default;
                return false;
            }
            trustedServerPublicKey = serverIdentity.PublicKey.ToArray();
        }

        UdpDatagramEndpoint? client = null;
        UdpDatagramEndpoint? server = null;
        try
        {
            UdpDatagramEndpoint.CreateConnectedLoopbackPair(udpLimits, out client, out server);
            ulong CookieBucket() => clock.Milliseconds /
                checked((ulong)udpLimits.CookieBucketMilliseconds);
            if (!ProtocolLoopbackHandshake.TryEstablishUdp(
                    client, server, udpLimits.MaximumDatagramBytes, CookieBucket,
                    server.RemotePathBinding, manifest, serverManifest, serverIdentity,
                    trustedServerPublicKey.Span, udpLimits.MaximumHandshakePumps,
                    out ProtocolHandshakeResult result))
            {
                handshake = default;
                session = null;
                client.Dispose();
                server.Dispose();
                if (ownsIdentity) serverIdentity.Dispose();
                return false;
            }

            DrainHandshakeResidue(client, server, udpLimits.MaximumDatagramBytes);
            session = new UdpProtocolSession(
                udpLimits, client, server, manifest, serverManifest,
                protocolLimits, serverIdentity, ownsIdentity, clock, result);
            handshake = result.Snapshot;
            return true;
        }
        catch
        {
            client?.Dispose();
            server?.Dispose();
            if (ownsIdentity) serverIdentity.Dispose();
            throw;
        }
    }

    /// <summary>Runs one nonblocking owner-thread socket/protocol pump.</summary>
    public void Pump()
    {
        EnsureOperational();
        if (_state == UdpProtocolSessionState.Closed) return;
        ulong now = ElapsedMilliseconds;
        ulong clientReceived = Client.ReceivedPacketCount;
        ulong serverReceived = Server.ReceivedPacketCount;
        int inbound = Client.ProcessInbound(now) + Server.ProcessInbound(now);
        if (_state == UdpProtocolSessionState.Established &&
            now - _lastKeepAlive >= (ulong)_udpLimits.KeepAliveIntervalMilliseconds)
        {
            bool clientKeepAlive = Client.TryEmitKeepAlive();
            bool serverKeepAlive = Server.TryEmitKeepAlive();
            if (clientKeepAlive || serverKeepAlive) _lastKeepAlive = now;
        }
        Client.Emit(now);
        Server.Emit(now);
        if (inbound != 0 && (Client.ReceivedPacketCount != clientReceived ||
            Server.ReceivedPacketCount != serverReceived))
            _lastActivity = now;

        if (_state == UdpProtocolSessionState.Established &&
            now - _lastActivity >= (ulong)_udpLimits.IdleTimeoutMilliseconds)
            BeginDrainCore(now);
        if (_state == UdpProtocolSessionState.Draining &&
            ((Client.ReliableQueued == 0 && Server.ReliableQueued == 0) ||
             now >= _drainDeadline))
            CloseSockets();
    }

    /// <summary>
    /// Stops accepting new lifetime work and closes sockets after reliable
    /// retention drains or the configured monotonic deadline expires.
    /// </summary>
    public void BeginDrain()
    {
        EnsureOperational();
        if (_state == UdpProtocolSessionState.Established)
            BeginDrainCore(ElapsedMilliseconds);
    }

    public UdpProtocolSessionSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new(
            _state,
            ElapsedMilliseconds,
            _lastActivity,
            Connection,
            Handshake,
            Client.GetSnapshot(),
            Server.GetSnapshot(),
            _clientEndpoint.GetSnapshot(),
            _serverEndpoint.GetSnapshot(),
            _connections.GetSnapshot(),
            _udpLimits.EstimateConfiguredSocketBufferBytes());
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        CloseSockets();
        Client.Dispose();
        Server.Dispose();
        if (_connections.TryGetState(Connection, out NetworkConnectionState state) &&
            state != NetworkConnectionState.Closed)
            _ = _connections.TryAdvance(Connection, NetworkConnectionState.Closed);
        _ = _connections.TryReleaseClosed(Connection);
        if (_ownsIdentity) _identity!.Dispose();
        _identity = null;
        _disposed = true;
    }

    internal IPEndPoint ClientLocalEndPoint => _clientEndpoint.LocalEndPoint;
    internal IPEndPoint ServerLocalEndPoint => _serverEndpoint.LocalEndPoint;

    internal void AbortClientTransportForTesting()
    {
        EnsureOperational();
        _clientEndpoint.Dispose();
        if (_state == UdpProtocolSessionState.Established)
            BeginDrainCore(ElapsedMilliseconds);
    }

    internal void AbortServerTransportForTesting()
    {
        EnsureOperational();
        _serverEndpoint.Dispose();
        if (_state == UdpProtocolSessionState.Established)
            BeginDrainCore(ElapsedMilliseconds);
    }

    internal void SuppressOutboundDatagramsForTesting(bool suppress)
    {
        EnsureOperational();
        _clientEndpoint.SuppressSendsForTesting = suppress;
        _serverEndpoint.SuppressSendsForTesting = suppress;
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

    private void BeginDrainCore(ulong now)
    {
        Client.StopAcceptingNewSends();
        Server.StopAcceptingNewSends();
        _state = UdpProtocolSessionState.Draining;
        _drainDeadline = checked(now + (ulong)_udpLimits.DrainTimeoutMilliseconds);
        if (_connections.TryGetState(Connection, out NetworkConnectionState state) &&
            state == NetworkConnectionState.Established)
            _ = _connections.TryAdvance(Connection, NetworkConnectionState.Draining);
    }

    private void CloseSockets()
    {
        if (_state == UdpProtocolSessionState.Closed) return;
        _clientEndpoint.Dispose();
        _serverEndpoint.Dispose();
        if (_connections.TryGetState(Connection, out NetworkConnectionState state) &&
            state != NetworkConnectionState.Closed)
            _ = _connections.TryAdvance(Connection, NetworkConnectionState.Closed);
        _state = UdpProtocolSessionState.Closed;
    }

    private static void DrainHandshakeResidue(
        UdpDatagramEndpoint client,
        UdpDatagramEndpoint server,
        int maximumDatagramBytes)
    {
        byte[] scratch = new byte[maximumDatagramBytes];
        while (client.TryReceive(scratch, out _)) { }
        while (server.TryReceive(scratch, out _)) { }
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The UDP protocol session is bound to its creating owner thread.");
    }
}

internal interface IUdpKnownDatagramHandler
{
    bool TryHandleKnownDatagram(ReadOnlySpan<byte> datagram);
}

internal sealed class UdpDatagramEndpoint : INetworkDatagramEndpoint, IDisposable
{
    private readonly Socket _socket;
    private readonly int _maximumDatagramBytes;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _disposed;
    private IUdpKnownDatagramHandler? _knownDatagramHandler;
    internal bool SuppressSendsForTesting { get; set; }
    private ulong _sent;
    private ulong _received;
    private ulong _sentBytes;
    private ulong _receivedBytes;
    private ulong _capacityDrops;
    private ulong _rejected;
    private ulong _sendCalls;
    private ulong _receivePollCalls;
    private ulong _receiveCalls;
    private ulong _wouldBlockEvents;
    private int _maximumSentDatagramBytes;
    private int _maximumReceivedDatagramBytes;
    private SocketError _lastError;

    private UdpDatagramEndpoint(Socket socket, int maximumDatagramBytes)
    {
        _socket = socket;
        _maximumDatagramBytes = maximumDatagramBytes;
        LocalEndPoint = (IPEndPoint)socket.LocalEndPoint!;
        IPEndPoint remote = (IPEndPoint)socket.RemoteEndPoint!;
        RemotePathBinding = CreatePathBinding(remote);
    }

    internal IPEndPoint LocalEndPoint { get; }
    internal byte[] RemotePathBinding { get; }

    internal void BindKnownDatagramHandler(IUdpKnownDatagramHandler handler)
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(handler);
        if (_knownDatagramHandler is not null)
            throw new InvalidOperationException("The connected UDP handshake handler is already bound.");
        _knownDatagramHandler = handler;
    }

    internal static void CreateConnectedLoopbackPair(
        UdpProtocolLimits limits,
        out UdpDatagramEndpoint client,
        out UdpDatagramEndpoint server)
    {
        Socket? clientSocket = null;
        Socket? serverSocket = null;
        try
        {
            clientSocket = CreateSocket(limits);
            serverSocket = CreateSocket(limits);
            clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            clientSocket.Connect(serverSocket.LocalEndPoint!);
            serverSocket.Connect(clientSocket.LocalEndPoint!);
            clientSocket.Blocking = false;
            serverSocket.Blocking = false;
            client = new UdpDatagramEndpoint(clientSocket, limits.MaximumDatagramBytes);
            server = new UdpDatagramEndpoint(serverSocket, limits.MaximumDatagramBytes);
        }
        catch
        {
            clientSocket?.Dispose();
            serverSocket?.Dispose();
            throw;
        }
    }

    internal static UdpDatagramEndpoint CreateConnectedLoopbackClient(
        UdpProtocolLimits limits,
        IPEndPoint serverEndPoint)
        => CreateConnectedClient(
            limits,
            new IPEndPoint(IPAddress.Loopback, 0),
            serverEndPoint);

    internal static UdpDatagramEndpoint CreateConnectedClient(
        UdpProtocolLimits limits,
        IPEndPoint localEndPoint,
        IPEndPoint serverEndPoint)
    {
        limits.Validate();
        ArgumentNullException.ThrowIfNull(localEndPoint);
        ArgumentNullException.ThrowIfNull(serverEndPoint);
        if (localEndPoint.AddressFamily != AddressFamily.InterNetwork ||
            serverEndPoint.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Hosted H0 currently supports IPv4 endpoints only.");
        Socket? socket = null;
        try
        {
            socket = CreateSocket(limits);
            socket.Bind(localEndPoint);
            socket.Connect(serverEndPoint);
            socket.Blocking = false;
            return new UdpDatagramEndpoint(socket, limits.MaximumDatagramBytes);
        }
        catch
        {
            socket?.Dispose();
            throw;
        }
    }

    public DatagramSendStatus TrySend(ReadOnlySpan<byte> datagram)
    {
        EnsureOwnerThread();
        if (_disposed) return DatagramSendStatus.CapacityExceeded;
        if (datagram.Length > _maximumDatagramBytes)
        {
            _rejected++;
            return DatagramSendStatus.DatagramTooLarge;
        }
        if (SuppressSendsForTesting) return DatagramSendStatus.DroppedByImpairment;
        try
        {
            _sendCalls++;
            if (_socket.Send(datagram, SocketFlags.None) != datagram.Length)
            {
                _capacityDrops++;
                return DatagramSendStatus.CapacityExceeded;
            }
            _sent++;
            _sentBytes = SaturatingAdd(_sentBytes, checked((ulong)datagram.Length));
            _maximumSentDatagramBytes = Math.Max(_maximumSentDatagramBytes, datagram.Length);
            return DatagramSendStatus.Accepted;
        }
        catch (SocketException exception) when (IsTransient(exception.SocketErrorCode))
        {
            _lastError = exception.SocketErrorCode;
            _capacityDrops++;
            _wouldBlockEvents++;
            return DatagramSendStatus.CapacityExceeded;
        }
        catch (SocketException exception)
        {
            _lastError = exception.SocketErrorCode;
            _rejected++;
            return DatagramSendStatus.CapacityExceeded;
        }
    }

    public bool TryReceive(Span<byte> destination, out int length)
    {
        EnsureOwnerThread();
        length = 0;
        if (_disposed) return false;
        if (destination.Length < _maximumDatagramBytes)
            throw new ArgumentException("The UDP receive destination is below the admitted datagram size.", nameof(destination));
        // Avoid using WouldBlock exceptions as the normal empty-queue branch;
        // Linux SocketException construction otherwise allocates on every pump.
        _receivePollCalls++;
        if (!_socket.Poll(0, SelectMode.SelectRead)) return false;
        try
        {
            _receiveCalls++;
            length = _socket.Receive(destination, SocketFlags.None);
            if (length == 0) return false;
            _received++;
            _receivedBytes = SaturatingAdd(_receivedBytes, checked((ulong)length));
            _maximumReceivedDatagramBytes = Math.Max(_maximumReceivedDatagramBytes, length);
            if (_knownDatagramHandler?.TryHandleKnownDatagram(
                    destination[..length]) == true)
            {
                length = 0;
                return false;
            }
            return true;
        }
        catch (SocketException exception) when (exception.SocketErrorCode is
            SocketError.WouldBlock or SocketError.IOPending or SocketError.NoBufferSpaceAvailable)
        {
            _lastError = exception.SocketErrorCode;
            _wouldBlockEvents++;
            return false;
        }
        catch (SocketException exception)
        {
            _lastError = exception.SocketErrorCode;
            _rejected++;
            return false;
        }
    }

    internal UdpDatagramEndpointSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(_sent, _received, _sentBytes, _receivedBytes,
            _capacityDrops, _rejected, _sendCalls, _receivePollCalls, _receiveCalls,
            _wouldBlockEvents, _maximumSentDatagramBytes, _maximumReceivedDatagramBytes,
            !_disposed, _lastError);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _disposed = true;
        try { _socket.Shutdown(SocketShutdown.Both); } catch (SocketException) { }
        _socket.Dispose();
        _knownDatagramHandler = null;
        CryptographicOperations.ZeroMemory(RemotePathBinding);
    }

    internal static Socket CreateSocket(UdpProtocolLimits limits)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            SendBufferSize = limits.SocketSendBufferBytes,
            ReceiveBufferSize = limits.SocketReceiveBufferBytes,
        };
        return socket;
    }

    internal static byte[] CreatePathBinding(IPEndPoint remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        byte[] address = remote.Address.GetAddressBytes();
        byte[] binding = new byte[checked(1 + address.Length + 2)];
        binding[0] = checked((byte)address.Length);
        address.CopyTo(binding, 1);
        binding[^2] = checked((byte)(remote.Port >> 8));
        binding[^1] = unchecked((byte)remote.Port);
        return binding;
    }

    private static bool IsTransient(SocketError error) => error is
        SocketError.WouldBlock or SocketError.IOPending or SocketError.NoBufferSpaceAvailable;

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The UDP endpoint is bound to its creating owner thread.");
    }
}
