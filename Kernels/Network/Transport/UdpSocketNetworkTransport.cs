using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace UltraSim.Network;

/// <summary>
/// Real UDP transport for local socket-based tests and the first live client/server slices.
/// </summary>
public sealed class UdpSocketNetworkTransport : INetworkTransport
{
    private readonly UdpSocketNetworkTransportOptions _options;
    private readonly PacketMetadataRegistry _packetMetadata;
    private readonly ConcurrentQueue<OutboundDatagram> _outbound = new();
    private readonly Dictionary<ConnectionId, IPEndPoint> _remoteByConnection = new();
    private readonly Dictionary<string, ConnectionId> _connectionByRemote = new(StringComparer.Ordinal);
    private readonly byte[] _receiveBuffer;
    private readonly object _sync = new();
    private Socket? _socket;
    private volatile bool _disposed;
    private volatile bool _running;
    private int _nextDynamicConnectionId;

    public UdpSocketNetworkTransport(UdpSocketNetworkTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _packetMetadata = options.PacketMetadata ?? throw new ArgumentNullException(nameof(options.PacketMetadata));
        if (options.MaxDatagramBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));

        _receiveBuffer = new byte[options.MaxDatagramBytes];
        _nextDynamicConnectionId = 1;

        if (options.DefaultRemoteEndPoint is not null)
        {
            if (!options.DefaultConnectionId.IsValid)
                throw new ArgumentOutOfRangeException(nameof(options), "A valid default connection id is required when a default remote endpoint is configured.");

            string remoteKey = GetRemoteKey(options.DefaultRemoteEndPoint);
            _remoteByConnection[options.DefaultConnectionId] = Clone(options.DefaultRemoteEndPoint);
            _connectionByRemote[remoteKey] = options.DefaultConnectionId;
            _nextDynamicConnectionId = Math.Max(_nextDynamicConnectionId, options.DefaultConnectionId.Value + 1);
        }
    }

    public bool IsRunning => _running && !_disposed;

    public EndPoint? LocalEndPoint => _socket?.LocalEndPoint;

    public void Start()
    {
        ThrowIfDisposed();
        if (_running)
            return;

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            Blocking = false,
        };
        socket.Bind(_options.LocalEndPoint);
        _socket = socket;
        _running = true;
    }

    public void Stop()
    {
        _running = false;
    }

    public int PollInbound(Span<InboundDatagram> destination)
    {
        if (!IsRunning || destination.Length == 0)
            return 0;

        Socket socket = _socket ?? throw new InvalidOperationException("Transport has not been started.");
        int count = 0;
        while (count < destination.Length && socket.Available > 0)
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int received;
            try
            {
                received = socket.ReceiveFrom(_receiveBuffer, 0, _receiveBuffer.Length, SocketFlags.None, ref remote);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.IOPending or SocketError.NoBufferSpaceAvailable)
            {
                break;
            }

            if (received <= 0 || remote is not IPEndPoint remoteEndPoint)
                continue;

            if (!PacketCodec.TryDecodeEnvelope(_receiveBuffer.AsSpan(0, received), out PacketEnvelopeHeader header, out _))
                continue;
            if (!_packetMetadata.TryGet(header.PacketId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel))
                continue;

            if (!TryGetOrAssignConnectionId(remoteEndPoint, out ConnectionId connectionId))
                continue;

            byte[] payloadCopy = new byte[received];
            Buffer.BlockCopy(_receiveBuffer, 0, payloadCopy, 0, received);
            destination[count++] = new InboundDatagram(
                connectionId,
                plane,
                packetClass,
                channel,
                payloadCopy);
        }

        return count;
    }

    public void EnqueueOutbound(in OutboundDatagram datagram)
    {
        if (!IsRunning)
            throw new InvalidOperationException("Transport must be running before enqueue.");

        _outbound.Enqueue(datagram);
    }

    public void FlushOutbound()
    {
        if (!IsRunning)
            return;

        Socket socket = _socket ?? throw new InvalidOperationException("Transport has not been started.");
        while (_outbound.TryDequeue(out OutboundDatagram datagram))
        {
            if (!TryGetRemote(datagram.ConnectionId, out IPEndPoint? remote))
                continue;
            if (remote is null)
                continue;

            try
            {
                socket.SendTo(datagram.Payload.Span, SocketFlags.None, remote);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.NoBufferSpaceAvailable)
            {
                _outbound.Enqueue(datagram);
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _running = false;

        Socket? socket = _socket;
        _socket = null;
        socket?.Dispose();

        while (_outbound.TryDequeue(out _))
        {
        }
    }

    private bool TryGetOrAssignConnectionId(IPEndPoint remoteEndPoint, out ConnectionId connectionId)
    {
        string remoteKey = GetRemoteKey(remoteEndPoint);
        lock (_sync)
        {
            if (_connectionByRemote.TryGetValue(remoteKey, out connectionId))
                return true;

            if (!_options.AllowDynamicConnections)
            {
                connectionId = default;
                return false;
            }

            while (_remoteByConnection.ContainsKey(new ConnectionId(_nextDynamicConnectionId)))
                _nextDynamicConnectionId++;

            connectionId = new ConnectionId(_nextDynamicConnectionId++);
            _remoteByConnection[connectionId] = Clone(remoteEndPoint);
            _connectionByRemote[remoteKey] = connectionId;
            return true;
        }
    }

    private bool TryGetRemote(ConnectionId connectionId, out IPEndPoint? remoteEndPoint)
    {
        lock (_sync)
        {
            if (_remoteByConnection.TryGetValue(connectionId, out IPEndPoint? remote))
            {
                remoteEndPoint = remote;
                return true;
            }
        }

        remoteEndPoint = null;
        return false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UdpSocketNetworkTransport));
    }

    private static string GetRemoteKey(IPEndPoint remote) => remote.Address + ":" + remote.Port;

    private static IPEndPoint Clone(IPEndPoint endpoint) => new(endpoint.Address, endpoint.Port);
}
