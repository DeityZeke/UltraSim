using System.Collections.Concurrent;
using System.Buffers;
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
    private readonly Queue<InboundDatagram> _pendingInbound = new();
    private readonly Dictionary<ConnectionId, IPEndPoint> _remoteByConnection = new();
    private readonly Dictionary<string, ConnectionId> _connectionByRemote = new(StringComparer.Ordinal);
    private readonly Dictionary<ConnectionId, List<OutboundDatagram>> _flushByConnection = new();
    private readonly Stack<List<OutboundDatagram>> _flushListPool = new();
    private readonly List<ImmutablePayloadBuffer> _bundlePayloadScratch = new();
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
    public int LastFlushWireDatagramCount { get; private set; }

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
        int count = DrainPendingInbound(destination);
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
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                continue;
            }

            if (received <= 0 || remote is not IPEndPoint remoteEndPoint)
                continue;

            if (!TryGetOrAssignConnectionId(remoteEndPoint, out ConnectionId connectionId))
                continue;

            ReadOnlySpan<byte> datagramPayload = _receiveBuffer.AsSpan(0, received);
            if (TransportDatagramBundleCodec.IsBundle(datagramPayload))
            {
                var reader = new TransportDatagramBundleCodec.Reader(datagramPayload);
                while (reader.TryReadNext(out ReadOnlySpan<byte> bundledPayload))
                    TryQueueInbound(connectionId, bundledPayload, destination, ref count);
                continue;
            }

            TryQueueInbound(connectionId, datagramPayload, destination, ref count);
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
        LastFlushWireDatagramCount = 0;

        DrainOutboundByConnection();
        if (_flushByConnection.Count == 0)
            return;

        try
        {
            foreach ((ConnectionId connectionId, List<OutboundDatagram> datagrams) in _flushByConnection)
            {
                if (!TryGetRemote(connectionId, out IPEndPoint? remote))
                {
                    for (int i = 0; i < datagrams.Count; i++)
                        datagrams[i].Dispose();
                    continue;
                }
                if (remote is null)
                {
                    for (int i = 0; i < datagrams.Count; i++)
                        datagrams[i].Dispose();
                    continue;
                }

                int index = 0;
                while (index < datagrams.Count)
                {
                    int bundleStart = index;
                    ImmutablePayloadBuffer outboundPayload;
                    int packedCount;
                    if (datagrams.Count - index == 1)
                    {
                        outboundPayload = datagrams[index].Payload;
                        packedCount = 1;
                    }
                    else
                    {
                        _bundlePayloadScratch.Clear();
                        for (int i = index; i < datagrams.Count; i++)
                            _bundlePayloadScratch.Add(datagrams[i].Payload);
                        int bundleBytes = TransportDatagramBundleCodec.MeasureBundle(
                            _bundlePayloadScratch,
                            0,
                            _options.MaxDatagramBytes,
                            out packedCount);
                        byte[] rented = ArrayPool<byte>.Shared.Rent(bundleBytes);
                        try
                        {
                            TransportDatagramBundleCodec.WriteBundle(
                                _bundlePayloadScratch,
                                0,
                                packedCount,
                                rented.AsSpan(0, bundleBytes));
                            socket.SendTo(rented.AsSpan(0, bundleBytes), SocketFlags.None, remote);
                            LastFlushWireDatagramCount++;
                            for (int i = bundleStart; i < bundleStart + packedCount; i++)
                                datagrams[i].Dispose();
                            index += packedCount;
                        }
                        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.NoBufferSpaceAvailable)
                        {
                            for (int i = bundleStart; i < datagrams.Count; i++)
                                _outbound.Enqueue(datagrams[i]);
                            return;
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(rented);
                        }

                        continue;
                    }

                    try
                    {
                        socket.SendTo(outboundPayload.Span, SocketFlags.None, remote);
                        LastFlushWireDatagramCount++;
                        datagrams[index].Dispose();
                        index += packedCount;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.NoBufferSpaceAvailable)
                    {
                        for (int i = bundleStart; i < datagrams.Count; i++)
                            _outbound.Enqueue(datagrams[i]);
                        return;
                    }
                }
            }
        }
        finally
        {
            ResetFlushScratch();
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

        while (_outbound.TryDequeue(out OutboundDatagram outbound))
        {
            outbound.Dispose();
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

            while (true)
            {
                if (_nextDynamicConnectionId <= 0)
                    throw new InvalidOperationException("Connection id space exhausted.");

                connectionId = new ConnectionId(_nextDynamicConnectionId);
                if (!_remoteByConnection.ContainsKey(connectionId))
                    break;

                _nextDynamicConnectionId++;
            }

            _nextDynamicConnectionId++;
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

    private int DrainPendingInbound(Span<InboundDatagram> destination)
    {
        int count = 0;
        while (count < destination.Length && _pendingInbound.Count > 0)
        {
            destination[count++] = _pendingInbound.Dequeue();
        }

        return count;
    }

    private void TryQueueInbound(
        ConnectionId connectionId,
        ReadOnlySpan<byte> payload,
        Span<InboundDatagram> destination,
        ref int count)
    {
        if (!PacketCodec.TryDecodeEnvelope(payload, out PacketEnvelopeHeader header, out _))
            return;
        if (!_packetMetadata.TryGet(header.PacketId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel))
            return;

        InboundPayloadBuffer payloadBuffer = InboundPayloadBuffer.CopyFromPooled(payload);
        var inbound = new InboundDatagram(connectionId, plane, packetClass, channel, payloadBuffer);
        if (count < destination.Length)
        {
            destination[count++] = inbound;
            return;
        }

        _pendingInbound.Enqueue(inbound);
    }

    private void DrainOutboundByConnection()
    {
        while (_outbound.TryDequeue(out OutboundDatagram datagram))
        {
            if (!_flushByConnection.TryGetValue(datagram.ConnectionId, out List<OutboundDatagram>? list))
            {
                list = _flushListPool.Count > 0 ? _flushListPool.Pop() : new List<OutboundDatagram>();
                _flushByConnection.Add(datagram.ConnectionId, list);
            }

            list.Add(datagram);
        }
    }

    private void ResetFlushScratch()
    {
        foreach (List<OutboundDatagram> list in _flushByConnection.Values)
        {
            list.Clear();
            _flushListPool.Push(list);
        }

        _flushByConnection.Clear();
        _bundlePayloadScratch.Clear();
    }
}
