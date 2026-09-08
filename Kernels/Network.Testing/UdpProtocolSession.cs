using System.Net;
using System.Security.Cryptography;

namespace UltraSim.Network;

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
            if (!ProtocolLoopbackHandshakeHarness.TryEstablishUdp(
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
