using System.Collections.Concurrent;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace UltraSim.Network;

/// <summary>
/// Real UDP transport for local socket-based tests and the first live client/server slices.
/// </summary>
public sealed class UdpSocketNetworkTransport : INetworkTransport
{
    private readonly record struct FlushExecutionResult(
        long BundleElapsedTicks,
        long SendElapsedTicks,
        int LogicalDatagramCount,
        int WireDatagramCount,
        int BundledWireDatagramCount,
        int SingleWireDatagramCount);

    private sealed class FlushBatchState
    {
        public readonly Dictionary<ConnectionId, List<OutboundDatagram>> ByConnection = new();
        public readonly Stack<List<OutboundDatagram>> ListPool = new();

        public int ConnectionCount => ByConnection.Count;

        public void DrainFrom(ConcurrentQueue<OutboundDatagram> source)
        {
            while (source.TryDequeue(out OutboundDatagram datagram))
            {
                if (!ByConnection.TryGetValue(datagram.ConnectionId, out List<OutboundDatagram>? list))
                {
                    list = ListPool.Count > 0 ? ListPool.Pop() : new List<OutboundDatagram>();
                    ByConnection.Add(datagram.ConnectionId, list);
                }

                list.Add(datagram);
            }
        }

        public void Reset()
        {
            foreach (List<OutboundDatagram> list in ByConnection.Values)
            {
                list.Clear();
                ListPool.Push(list);
            }

            ByConnection.Clear();
        }

        public void DisposeContents()
        {
            foreach (List<OutboundDatagram> list in ByConnection.Values)
            {
                for (int i = 0; i < list.Count; i++)
                    list[i].Dispose();
                list.Clear();
                ListPool.Push(list);
            }

            ByConnection.Clear();
        }
    }

    private readonly UdpSocketNetworkTransportOptions _options;
    private readonly PacketMetadataRegistry _packetMetadata;
    private readonly ConcurrentQueue<OutboundDatagram> _outbound = new();
    private readonly ConcurrentQueue<InboundDatagram> _pendingInbound = new();
    private readonly Dictionary<ConnectionId, IPEndPoint> _remoteByConnection = new();
    private readonly Dictionary<string, ConnectionId> _connectionByRemote = new(StringComparer.Ordinal);
    private readonly Dictionary<ConnectionId, List<OutboundDatagram>> _flushByConnection = new();
    private readonly Stack<List<OutboundDatagram>> _flushListPool = new();
    private readonly List<ImmutablePayloadBuffer> _bundlePayloadScratch = new();
    private readonly FlushBatchState _asyncFlushBatchA = new();
    private readonly FlushBatchState _asyncFlushBatchB = new();
    private readonly object _sync = new();
    private Socket? _socket;
    private Task _pendingAsyncFlushTask = Task.CompletedTask;
    private FlushBatchState? _buildFlushBatch;
    private FlushBatchState? _sendFlushBatch;
    private Task _pendingAsyncReceiveTask = Task.CompletedTask;
    private long _asyncAccumulatedReceiveElapsedTicks;
    private int _pendingInboundCount;
    private int _asyncMaxPendingBatchCount;
    private int _asyncInlineFallbackCount = 0;
    private long _lastAsyncCompletedFlushElapsedTicks;
    private long _lastAsyncCompletedSendElapsedTicks;
    private int _lastAsyncCompletedLogicalDatagramCount;
    private int _lastAsyncCompletedWireDatagramCount;
    private int _lastAsyncCompletedBundledWireDatagramCount;
    private int _lastAsyncCompletedSingleWireDatagramCount;
    private volatile bool _disposed;
    private volatile bool _running;
    private int _nextDynamicConnectionId;

    public UdpSocketNetworkTransport(UdpSocketNetworkTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _packetMetadata = options.PacketMetadata ?? throw new ArgumentNullException(nameof(options.PacketMetadata));
        if (options.MaxDatagramBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));

        _nextDynamicConnectionId = 1;
        _buildFlushBatch = _asyncFlushBatchA;
        _sendFlushBatch = _asyncFlushBatchB;

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
    public TransportFlushProfileSnapshot LastFlushProfile { get; private set; }
    public TransportPollProfileSnapshot LastPollProfile { get; private set; }

    public void Start()
    {
        ThrowIfDisposed();
        if (_running)
            return;

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            Blocking = false,
        };
        if (_options.SocketReceiveBufferBytes > 0)
            socket.ReceiveBufferSize = _options.SocketReceiveBufferBytes;
        if (_options.SocketSendBufferBytes > 0)
            socket.SendBufferSize = _options.SocketSendBufferBytes;
        socket.Bind(_options.LocalEndPoint);
        _socket = socket;
        _running = true;
        if (_options.EnableAsyncReceive)
            _pendingAsyncReceiveTask = Task.Run(() => ExecuteAsyncReceiveLoop(socket));
    }

    public void Stop()
    {
        _running = false;
        try
        {
            _pendingAsyncReceiveTask.GetAwaiter().GetResult();
        }
        catch
        {
        }

        _pendingAsyncReceiveTask = Task.CompletedTask;
    }

    public int PollInbound(Span<InboundDatagram> destination)
    {
        if (!IsRunning || destination.Length == 0)
        {
            LastPollProfile = default;
            return 0;
        }

        Socket socket = _socket ?? throw new InvalidOperationException("Transport has not been started.");
        long pendingDrainStart = Stopwatch.GetTimestamp();
        int asyncQueuedCount = Volatile.Read(ref _pendingInboundCount);
        int count = DrainPendingInbound(destination);
        long pendingDrainTicks = Stopwatch.GetTimestamp() - pendingDrainStart;
        if (_options.EnableAsyncReceive)
        {
            LastPollProfile = new TransportPollProfileSnapshot(
                PendingDrainElapsedTicks: pendingDrainTicks,
                SocketReceiveElapsedTicks: 0,
                BundleUnpackElapsedTicks: 0,
                BundleIndexElapsedTicks: 0,
                BundleMaterializeElapsedTicks: 0,
                BundleSliceElapsedTicks: 0,
                BundleDatagramCreateElapsedTicks: 0,
                BundleEnqueueElapsedTicks: 0,
                PayloadCopyElapsedTicks: 0,
                EnvelopeParseElapsedTicks: 0,
                QueuedDatagramCount: count,
                AsyncEnabled: true,
                AsyncQueuedDatagramCount: asyncQueuedCount,
                AsyncReceiveElapsedTicks: Interlocked.Exchange(ref _asyncAccumulatedReceiveElapsedTicks, 0));
            return count;
        }

        bool profilePoll = _options.EnablePollProfiling;
        long socketReceiveTicks = 0;
        long bundleUnpackTicks = 0;
        long bundleIndexTicks = 0;
        long bundleMaterializeTicks = 0;
        long bundleSliceTicks = 0;
        long bundleDatagramCreateTicks = 0;
        long bundleEnqueueTicks = 0;
        long payloadCopyTicks = 0;
        long envelopeParseTicks = 0;
        while (count < destination.Length && socket.Available > 0)
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int received;
            byte[] rentedReceiveBuffer = ArrayPool<byte>.Shared.Rent(_options.MaxDatagramBytes);
            try
            {
                long receiveStart = profilePoll ? Stopwatch.GetTimestamp() : 0;
                received = socket.ReceiveFrom(rentedReceiveBuffer, 0, _options.MaxDatagramBytes, SocketFlags.None, ref remote);
                if (profilePoll)
                    socketReceiveTicks += Stopwatch.GetTimestamp() - receiveStart;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.IOPending or SocketError.NoBufferSpaceAvailable)
            {
                ArrayPool<byte>.Shared.Return(rentedReceiveBuffer);
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                ArrayPool<byte>.Shared.Return(rentedReceiveBuffer);
                continue;
            }

            if (received <= 0 || remote is not IPEndPoint remoteEndPoint)
            {
                ArrayPool<byte>.Shared.Return(rentedReceiveBuffer);
                continue;
            }

            if (!TryGetOrAssignConnectionId(remoteEndPoint, out ConnectionId connectionId))
            {
                ArrayPool<byte>.Shared.Return(rentedReceiveBuffer);
                continue;
            }

            InboundPayloadBuffer wirePayload = InboundPayloadBuffer.FromPooled(rentedReceiveBuffer, received);
            ReadOnlySpan<byte> datagramPayload = wirePayload.Span;
            if (TransportDatagramBundleCodec.TryBeginReadBundle(datagramPayload, out ushort entryCount, out ReadOnlySpan<byte> offsets, out ReadOnlySpan<byte> payloads))
            {
                long bundleStart = profilePoll ? Stopwatch.GetTimestamp() : 0;
                wirePayload.AddRefs(entryCount);
                int createdSlices = 0;
                for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
                {
                    long entryIndexStart = profilePoll ? Stopwatch.GetTimestamp() : 0;
                    int currentStart = BinaryPrimitives.ReadUInt16LittleEndian(offsets.Slice(entryIndex * sizeof(ushort)));
                    int nextStart = entryIndex + 1 < entryCount
                        ? BinaryPrimitives.ReadUInt16LittleEndian(offsets.Slice((entryIndex + 1) * sizeof(ushort)))
                        : payloads.Length;
                    if (profilePoll)
                        bundleIndexTicks += Stopwatch.GetTimestamp() - entryIndexStart;
                    if ((uint)currentStart > (uint)payloads.Length || (uint)nextStart > (uint)payloads.Length || nextStart < currentStart)
                        break;

                    int entryLength = nextStart - currentStart;
                    long materializeStart = profilePoll ? Stopwatch.GetTimestamp() : 0;
                    int absoluteOffset = datagramPayload.Length - payloads.Length + currentStart;
                    if (count < destination.Length)
                    {
                        ReadOnlySpan<byte> entryPayloadSpan = payloads.Slice(currentStart, entryLength);
                        if (TryDecodeInboundMetadata(entryPayloadSpan, out PacketEnvelopeHeader header, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel, ref envelopeParseTicks))
                        {
                            long sliceStart = profilePoll ? Stopwatch.GetTimestamp() : 0;
                            ReadOnlyMemory<byte> entryPayloadMemory = wirePayload.Memory.Slice(absoluteOffset, entryLength);
                            if (profilePoll)
                                bundleSliceTicks += Stopwatch.GetTimestamp() - sliceStart;
                            if (entryLength > 0)
                                createdSlices++;
                            long createStart = profilePoll ? Stopwatch.GetTimestamp() : 0;
                            InboundDatagram inbound = new(connectionId, plane, packetClass, channel, header, wirePayload, entryPayloadMemory);
                            if (profilePoll)
                                bundleDatagramCreateTicks += Stopwatch.GetTimestamp() - createStart;
                            long enqueueStart = profilePoll ? Stopwatch.GetTimestamp() : 0;
                            destination[count++] = inbound;
                            if (profilePoll)
                                bundleEnqueueTicks += Stopwatch.GetTimestamp() - enqueueStart;
                        }
                        if (profilePoll)
                            bundleMaterializeTicks += Stopwatch.GetTimestamp() - materializeStart;
                        continue;
                    }

                    try
                    {
                        long sliceStart = profilePoll ? Stopwatch.GetTimestamp() : 0;
                        InboundPayloadBuffer bundledPayload = InboundPayloadBuffer.SliceRetained(
                            wirePayload,
                            absoluteOffset,
                            entryLength);
                        if (profilePoll)
                            bundleSliceTicks += Stopwatch.GetTimestamp() - sliceStart;
                        if (entryLength > 0)
                            createdSlices++;
                        TryQueueInbound(
                            connectionId,
                            bundledPayload,
                            destination,
                            ref count,
                            ref envelopeParseTicks,
                            ref payloadCopyTicks,
                            ref bundleDatagramCreateTicks,
                            ref bundleEnqueueTicks);
                        if (profilePoll)
                            bundleMaterializeTicks += Stopwatch.GetTimestamp() - materializeStart;
                    }
                    catch
                    {
                        int unusedRefs = entryCount - createdSlices;
                        if (unusedRefs > 0)
                            wirePayload.ReleaseRefs(unusedRefs);
                        wirePayload.Dispose();
                        throw;
                    }
                }
                int unusedSliceRefs = entryCount - createdSlices;
                if (unusedSliceRefs > 0)
                    wirePayload.ReleaseRefs(unusedSliceRefs);
                wirePayload.Dispose();
                if (profilePoll)
                    bundleUnpackTicks += Stopwatch.GetTimestamp() - bundleStart;
                continue;
            }

            TryQueueInbound(connectionId, wirePayload, destination, ref count, ref envelopeParseTicks, ref payloadCopyTicks);
        }

        LastPollProfile = new TransportPollProfileSnapshot(
            PendingDrainElapsedTicks: pendingDrainTicks,
            SocketReceiveElapsedTicks: socketReceiveTicks,
            BundleUnpackElapsedTicks: bundleUnpackTicks,
            BundleIndexElapsedTicks: bundleIndexTicks,
            BundleMaterializeElapsedTicks: bundleMaterializeTicks,
            BundleSliceElapsedTicks: bundleSliceTicks,
            BundleDatagramCreateElapsedTicks: bundleDatagramCreateTicks,
            BundleEnqueueElapsedTicks: bundleEnqueueTicks,
            PayloadCopyElapsedTicks: payloadCopyTicks,
            EnvelopeParseElapsedTicks: envelopeParseTicks,
            QueuedDatagramCount: count,
            AsyncEnabled: false,
            AsyncQueuedDatagramCount: 0,
            AsyncReceiveElapsedTicks: 0);
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

        if (_options.EnableAsyncFlush)
        {
            FlushOutboundAsyncStaged();
            return;
        }

        Socket socket = _socket ?? throw new InvalidOperationException("Transport has not been started.");
        LastFlushWireDatagramCount = 0;
        LastFlushProfile = default;

        long drainStart = Stopwatch.GetTimestamp();
        DrainOutboundByConnection();
        long drainEnd = Stopwatch.GetTimestamp();
        if (_flushByConnection.Count == 0)
        {
            LastFlushProfile = new TransportFlushProfileSnapshot(
                DrainElapsedTicks: drainEnd - drainStart,
                BundleElapsedTicks: 0,
                SendElapsedTicks: 0,
                ConnectionCount: 0,
                LogicalDatagramCount: 0,
                WireDatagramCount: 0,
                BundledWireDatagramCount: 0,
                SingleWireDatagramCount: 0,
                AsyncEnabled: false,
                AsyncStaged: false,
                AsyncPendingBatchCount: 0,
                AsyncMaxPendingBatchCount: 0,
                AsyncInlineFallbackCount: 0,
                AsyncPreviousFlushElapsedTicks: 0,
                AsyncPreviousSendElapsedTicks: 0,
                AsyncHandoffWaitElapsedTicks: 0,
                AsyncPreviousLogicalDatagramCount: 0,
                AsyncPreviousWireDatagramCount: 0,
                AsyncPreviousBundledWireDatagramCount: 0,
                AsyncPreviousSingleWireDatagramCount: 0);
            return;
        }

        long bundleTicks = 0;
        long sendTicks = 0;
        int connectionCount = 0;
        int logicalDatagramCount = 0;
        int bundledWireDatagramCount = 0;
        int singleWireDatagramCount = 0;

        try
        {
            foreach ((ConnectionId connectionId, List<OutboundDatagram> datagrams) in _flushByConnection)
            {
                connectionCount++;
                logicalDatagramCount += datagrams.Count;
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
                        long bundleEncodeStart = Stopwatch.GetTimestamp();
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
                            long bundleEnd = Stopwatch.GetTimestamp();
                            bundleTicks += bundleEnd - bundleEncodeStart;
                            long sendStart = Stopwatch.GetTimestamp();
                            socket.SendTo(rented.AsSpan(0, bundleBytes), SocketFlags.None, remote);
                            long sendEnd = Stopwatch.GetTimestamp();
                            sendTicks += sendEnd - sendStart;
                            LastFlushWireDatagramCount++;
                            bundledWireDatagramCount++;
                            for (int i = bundleStart; i < bundleStart + packedCount; i++)
                                datagrams[i].Dispose();
                            index += packedCount;
                        }
                        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.NoBufferSpaceAvailable)
                        {
                            for (int i = bundleStart; i < datagrams.Count; i++)
                                lock (_sync)
        {
            _outbound.Enqueue(datagrams[i]);
        }
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
                        long sendStart = Stopwatch.GetTimestamp();
                        socket.SendTo(outboundPayload.Span, SocketFlags.None, remote);
                        long sendEnd = Stopwatch.GetTimestamp();
                        sendTicks += sendEnd - sendStart;
                        LastFlushWireDatagramCount++;
                        singleWireDatagramCount++;
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
            LastFlushProfile = new TransportFlushProfileSnapshot(
                DrainElapsedTicks: drainEnd - drainStart,
                BundleElapsedTicks: bundleTicks,
                SendElapsedTicks: sendTicks,
                ConnectionCount: connectionCount,
                LogicalDatagramCount: logicalDatagramCount,
                WireDatagramCount: LastFlushWireDatagramCount,
                BundledWireDatagramCount: bundledWireDatagramCount,
                SingleWireDatagramCount: singleWireDatagramCount,
                AsyncEnabled: false,
                AsyncStaged: false,
                AsyncPendingBatchCount: 0,
                AsyncMaxPendingBatchCount: 0,
                AsyncInlineFallbackCount: 0,
                AsyncPreviousFlushElapsedTicks: 0,
                AsyncPreviousSendElapsedTicks: 0,
                AsyncHandoffWaitElapsedTicks: 0,
                AsyncPreviousLogicalDatagramCount: 0,
                AsyncPreviousWireDatagramCount: 0,
                AsyncPreviousBundledWireDatagramCount: 0,
                AsyncPreviousSingleWireDatagramCount: 0);
            ResetFlushScratch();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _running = false;

        try
        {
            _pendingAsyncReceiveTask.GetAwaiter().GetResult();
        }
        catch
        {
        }

        try
        {
            _pendingAsyncFlushTask.GetAwaiter().GetResult();
        }
        catch
        {
        }

        Socket? socket = _socket;
        _socket = null;
        socket?.Dispose();

        while (_outbound.TryDequeue(out OutboundDatagram outbound))
        {
            outbound.Dispose();
        }

        while (_pendingInbound.TryDequeue(out InboundDatagram inbound))
        {
            inbound.Dispose();
        }

        _asyncFlushBatchA.DisposeContents();
        _asyncFlushBatchB.DisposeContents();
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
        while (count < destination.Length && _pendingInbound.TryDequeue(out InboundDatagram inbound))
        {
            destination[count++] = inbound;
            Interlocked.Decrement(ref _pendingInboundCount);
        }

        return count;
    }

    private void TryQueueInbound(
        ConnectionId connectionId,
        InboundPayloadBuffer payloadBuffer,
        Span<InboundDatagram> destination,
        ref int count,
        ref long envelopeParseTicks,
        ref long payloadCopyTicks)
    {
        long ignoredCreateTicks = 0;
        long ignoredEnqueueTicks = 0;
        TryQueueInbound(
            connectionId,
            payloadBuffer,
            destination,
            ref count,
            ref envelopeParseTicks,
            ref payloadCopyTicks,
            ref ignoredCreateTicks,
            ref ignoredEnqueueTicks);
    }

    private void TryQueueInbound(
        ConnectionId connectionId,
        InboundPayloadBuffer payloadBuffer,
        Span<InboundDatagram> destination,
        ref int count,
        ref long envelopeParseTicks,
        ref long payloadCopyTicks,
        ref long datagramCreateTicks,
        ref long enqueueTicks)
    {
        if (!TryCreateInboundDatagram(connectionId, payloadBuffer, out InboundDatagram inbound, ref envelopeParseTicks, ref datagramCreateTicks))
            return;

        long enqueueStart = _options.EnablePollProfiling ? Stopwatch.GetTimestamp() : 0;
        if (count < destination.Length)
        {
            destination[count++] = inbound;
            if (_options.EnablePollProfiling)
                enqueueTicks += Stopwatch.GetTimestamp() - enqueueStart;
            return;
        }

        QueuePendingInbound(inbound);
        if (_options.EnablePollProfiling)
            enqueueTicks += Stopwatch.GetTimestamp() - enqueueStart;
    }

    private bool TryQueueInboundPending(
        ConnectionId connectionId,
        InboundPayloadBuffer payloadBuffer,
        ref long envelopeParseTicks)
    {
        long ignoredCreateTicks = 0;
        long ignoredEnqueueTicks = 0;
        return TryQueueInboundPending(
            connectionId,
            payloadBuffer,
            ref envelopeParseTicks,
            ref ignoredCreateTicks,
            ref ignoredEnqueueTicks);
    }

    private bool TryQueueInboundPending(
        ConnectionId connectionId,
        InboundPayloadBuffer payloadBuffer,
        ref long envelopeParseTicks,
        ref long datagramCreateTicks,
        ref long enqueueTicks)
    {
        if (!TryCreateInboundDatagram(connectionId, payloadBuffer, out InboundDatagram inbound, ref envelopeParseTicks, ref datagramCreateTicks))
            return false;

        long enqueueStart = _options.EnablePollProfiling ? Stopwatch.GetTimestamp() : 0;
        QueuePendingInbound(inbound);
        if (_options.EnablePollProfiling)
            enqueueTicks += Stopwatch.GetTimestamp() - enqueueStart;
        return true;
    }

    private bool TryCreateInboundDatagram(
        ConnectionId connectionId,
        InboundPayloadBuffer payloadBuffer,
        out InboundDatagram inbound,
        ref long envelopeParseTicks)
    {
        long ignoredCreateTicks = 0;
        return TryCreateInboundDatagram(connectionId, payloadBuffer, out inbound, ref envelopeParseTicks, ref ignoredCreateTicks);
    }

    private bool TryCreateInboundDatagram(
        ConnectionId connectionId,
        InboundPayloadBuffer payloadBuffer,
        out InboundDatagram inbound,
        ref long envelopeParseTicks,
        ref long datagramCreateTicks)
    {
        if (!TryDecodeInboundMetadata(payloadBuffer.Span, out PacketEnvelopeHeader header, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel, ref envelopeParseTicks))
        {
            payloadBuffer.Dispose();
            inbound = default;
            return false;
        }

        long createStart = _options.EnablePollProfiling ? Stopwatch.GetTimestamp() : 0;
        inbound = new InboundDatagram(connectionId, plane, packetClass, channel, header, payloadBuffer);
        if (_options.EnablePollProfiling)
            datagramCreateTicks += Stopwatch.GetTimestamp() - createStart;
        return true;
    }

    private bool TryDecodeInboundMetadata(
        ReadOnlySpan<byte> payload,
        out PacketEnvelopeHeader header,
        out NetworkPlane plane,
        out PacketClass packetClass,
        out ChannelType channel,
        ref long envelopeParseTicks)
    {
        long parseStart = _options.EnablePollProfiling ? Stopwatch.GetTimestamp() : 0;
        if (!PacketCodec.TryDecodeEnvelope(payload, out header, out _))
        {
            if (_options.EnablePollProfiling)
                envelopeParseTicks += Stopwatch.GetTimestamp() - parseStart;
            plane = default;
            packetClass = default;
            channel = default;
            return false;
        }

        if (!_packetMetadata.TryGet(header.PacketId, out plane, out packetClass, out channel))
        {
            if (_options.EnablePollProfiling)
                envelopeParseTicks += Stopwatch.GetTimestamp() - parseStart;
            return false;
        }

        if (_options.EnablePollProfiling)
            envelopeParseTicks += Stopwatch.GetTimestamp() - parseStart;
        return true;
    }

    private void QueuePendingInbound(InboundDatagram inbound)
    {
        _pendingInbound.Enqueue(inbound);
        Interlocked.Increment(ref _pendingInboundCount);
    }

    private void ExecuteAsyncReceiveLoop(Socket socket)
    {
        int idleCount = 0;
        while (_running && !_disposed)
        {
            bool receivedAny = false;
            long receiveElapsedTicks = 0;
            while (_running && !_disposed)
            {
                if (!TryReceiveAndQueuePending(socket, ref receiveElapsedTicks))
                    break;

                receivedAny = true;
            }

            if (receivedAny)
            {
                Interlocked.Add(ref _asyncAccumulatedReceiveElapsedTicks, receiveElapsedTicks);
                idleCount = 0;
                continue;
            }

            if (!_running || _disposed)
                break;

            if (idleCount < 4)
            {
                Thread.Yield();
                idleCount++;
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    private bool TryReceiveAndQueuePending(Socket socket, ref long totalElapsedTicks)
    {
        try
        {
            if (socket.Available <= 0)
                return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        int received;
        byte[] rentedReceiveBuffer = ArrayPool<byte>.Shared.Rent(_options.MaxDatagramBytes);
        try
        {
            long receiveStart = Stopwatch.GetTimestamp();
            received = socket.ReceiveFrom(rentedReceiveBuffer, 0, _options.MaxDatagramBytes, SocketFlags.None, ref remote);
            totalElapsedTicks += Stopwatch.GetTimestamp() - receiveStart;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.IOPending or SocketError.NoBufferSpaceAvailable)
        {
            ArrayPool<byte>.Shared.Return(rentedReceiveBuffer);
            return false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
        {
            ArrayPool<byte>.Shared.Return(rentedReceiveBuffer);
            return true;
        }
        catch (ObjectDisposedException)
        {
            ArrayPool<byte>.Shared.Return(rentedReceiveBuffer);
            return false;
        }

        if (received <= 0 || remote is not IPEndPoint remoteEndPoint)
        {
            ArrayPool<byte>.Shared.Return(rentedReceiveBuffer);
            return true;
        }

        if (!TryGetOrAssignConnectionId(remoteEndPoint, out ConnectionId connectionId))
        {
            ArrayPool<byte>.Shared.Return(rentedReceiveBuffer);
            return true;
        }

        long unpackStart = Stopwatch.GetTimestamp();
        InboundPayloadBuffer wirePayload = InboundPayloadBuffer.FromPooled(rentedReceiveBuffer, received);
        ReadOnlySpan<byte> datagramPayload = wirePayload.Span;
        long ignoredParseTicks = 0;
        if (TransportDatagramBundleCodec.TryBeginReadBundle(datagramPayload, out ushort entryCount, out ReadOnlySpan<byte> offsets, out ReadOnlySpan<byte> payloads))
        {
            wirePayload.AddRefs(entryCount);
            int createdSlices = 0;
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                int currentStart = BinaryPrimitives.ReadUInt16LittleEndian(offsets.Slice(entryIndex * sizeof(ushort)));
                int nextStart = entryIndex + 1 < entryCount
                    ? BinaryPrimitives.ReadUInt16LittleEndian(offsets.Slice((entryIndex + 1) * sizeof(ushort)))
                    : payloads.Length;
                if ((uint)currentStart > (uint)payloads.Length || (uint)nextStart > (uint)payloads.Length || nextStart < currentStart)
                    break;

                int entryLength = nextStart - currentStart;
                InboundPayloadBuffer bundledPayload = InboundPayloadBuffer.SliceRetained(
                    wirePayload,
                    datagramPayload.Length - payloads.Length + currentStart,
                    entryLength);
                if (entryLength > 0)
                    createdSlices++;
                TryQueueInboundPending(connectionId, bundledPayload, ref ignoredParseTicks);
            }
            int unusedSliceRefs = entryCount - createdSlices;
            if (unusedSliceRefs > 0)
                wirePayload.ReleaseRefs(unusedSliceRefs);
            wirePayload.Dispose();
            totalElapsedTicks += Stopwatch.GetTimestamp() - unpackStart;
            return true;
        }

        TryQueueInboundPending(connectionId, wirePayload, ref ignoredParseTicks);
        totalElapsedTicks += Stopwatch.GetTimestamp() - unpackStart;
        return true;
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

    private void FlushOutboundAsyncStaged()
    {
        Socket socket = _socket ?? throw new InvalidOperationException("Transport has not been started.");
        LastFlushWireDatagramCount = 0;
        LastFlushProfile = default;

        FlushBatchState buildBatch = _buildFlushBatch ?? throw new InvalidOperationException("Async flush build batch is not initialized.");
        long drainStart = Stopwatch.GetTimestamp();
        buildBatch.DrainFrom(_outbound);
        long drainEnd = Stopwatch.GetTimestamp();

        if (buildBatch.ConnectionCount == 0)
        {
            LastFlushProfile = new TransportFlushProfileSnapshot(
                DrainElapsedTicks: drainEnd - drainStart,
                BundleElapsedTicks: 0,
                SendElapsedTicks: 0,
                ConnectionCount: 0,
                LogicalDatagramCount: 0,
                WireDatagramCount: 0,
                BundledWireDatagramCount: 0,
                SingleWireDatagramCount: 0,
                AsyncEnabled: true,
                AsyncStaged: false,
                AsyncPendingBatchCount: _pendingAsyncFlushTask.IsCompleted ? 0 : 1,
                AsyncMaxPendingBatchCount: _asyncMaxPendingBatchCount,
                AsyncInlineFallbackCount: _asyncInlineFallbackCount,
                AsyncPreviousFlushElapsedTicks: Interlocked.Read(ref _lastAsyncCompletedFlushElapsedTicks),
                AsyncPreviousSendElapsedTicks: Interlocked.Read(ref _lastAsyncCompletedSendElapsedTicks),
                AsyncHandoffWaitElapsedTicks: 0,
                AsyncPreviousLogicalDatagramCount: Volatile.Read(ref _lastAsyncCompletedLogicalDatagramCount),
                AsyncPreviousWireDatagramCount: Volatile.Read(ref _lastAsyncCompletedWireDatagramCount),
                AsyncPreviousBundledWireDatagramCount: Volatile.Read(ref _lastAsyncCompletedBundledWireDatagramCount),
                AsyncPreviousSingleWireDatagramCount: Volatile.Read(ref _lastAsyncCompletedSingleWireDatagramCount));
            return;
        }

        int pendingBatchCount = _pendingAsyncFlushTask.IsCompleted ? 0 : 1;
        if (pendingBatchCount > _asyncMaxPendingBatchCount)
            _asyncMaxPendingBatchCount = pendingBatchCount;
        if (pendingBatchCount > 0)
            Interlocked.Increment(ref _asyncInlineFallbackCount);

        long handoffWaitStart = Stopwatch.GetTimestamp();
        _pendingAsyncFlushTask.GetAwaiter().GetResult();
        long handoffWaitEnd = Stopwatch.GetTimestamp();
        long handoffWaitTicks = handoffWaitEnd - handoffWaitStart;

        FlushBatchState sendBatch = buildBatch;
        _buildFlushBatch = _sendFlushBatch;
        _sendFlushBatch = sendBatch;
        _pendingAsyncFlushTask = Task.Run(() => ExecuteAsyncFlushBatch(socket, sendBatch));

        int logicalDatagramCount = CountLogicalDatagrams(sendBatch.ByConnection);
        LastFlushProfile = new TransportFlushProfileSnapshot(
            DrainElapsedTicks: drainEnd - drainStart,
            BundleElapsedTicks: 0,
            SendElapsedTicks: 0,
            ConnectionCount: sendBatch.ConnectionCount,
            LogicalDatagramCount: logicalDatagramCount,
            WireDatagramCount: 0,
            BundledWireDatagramCount: 0,
            SingleWireDatagramCount: 0,
            AsyncEnabled: true,
            AsyncStaged: true,
            AsyncPendingBatchCount: 1,
            AsyncMaxPendingBatchCount: _asyncMaxPendingBatchCount,
            AsyncInlineFallbackCount: _asyncInlineFallbackCount,
            AsyncPreviousFlushElapsedTicks: Interlocked.Read(ref _lastAsyncCompletedFlushElapsedTicks),
            AsyncPreviousSendElapsedTicks: Interlocked.Read(ref _lastAsyncCompletedSendElapsedTicks),
            AsyncHandoffWaitElapsedTicks: handoffWaitTicks,
            AsyncPreviousLogicalDatagramCount: Volatile.Read(ref _lastAsyncCompletedLogicalDatagramCount),
            AsyncPreviousWireDatagramCount: Volatile.Read(ref _lastAsyncCompletedWireDatagramCount),
            AsyncPreviousBundledWireDatagramCount: Volatile.Read(ref _lastAsyncCompletedBundledWireDatagramCount),
            AsyncPreviousSingleWireDatagramCount: Volatile.Read(ref _lastAsyncCompletedSingleWireDatagramCount));
    }

    private void ExecuteAsyncFlushBatch(Socket socket, FlushBatchState batch)
    {
        try
        {
            FlushExecutionResult result = ExecuteFlushBatch(socket, batch.ByConnection, clearAfterSend: true);
            Interlocked.Exchange(ref _lastAsyncCompletedFlushElapsedTicks, result.BundleElapsedTicks + result.SendElapsedTicks);
            Interlocked.Exchange(ref _lastAsyncCompletedSendElapsedTicks, result.SendElapsedTicks);
            Volatile.Write(ref _lastAsyncCompletedLogicalDatagramCount, result.LogicalDatagramCount);
            Volatile.Write(ref _lastAsyncCompletedWireDatagramCount, result.WireDatagramCount);
            Volatile.Write(ref _lastAsyncCompletedBundledWireDatagramCount, result.BundledWireDatagramCount);
            Volatile.Write(ref _lastAsyncCompletedSingleWireDatagramCount, result.SingleWireDatagramCount);
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            batch.Reset();
        }
    }

    private FlushExecutionResult ExecuteFlushBatch(Socket socket, Dictionary<ConnectionId, List<OutboundDatagram>> flushByConnection, bool clearAfterSend)
    {
        long bundleTicks = 0;
        long sendTicks = 0;
        int logicalDatagramCount = 0;
        int wireDatagramCount = 0;
        int bundledWireDatagramCount = 0;
        int singleWireDatagramCount = 0;

        Dictionary<ConnectionId, List<OutboundDatagram>>.Enumerator enumerator = flushByConnection.GetEnumerator();
        while (enumerator.MoveNext())
        {
            (ConnectionId connectionId, List<OutboundDatagram> datagrams) = enumerator.Current;
            logicalDatagramCount += datagrams.Count;
            if (!TryGetRemote(connectionId, out IPEndPoint? remote) || remote is null)
            {
                for (int i = 0; i < datagrams.Count; i++)
                    datagrams[i].Dispose();
                continue;
            }

            int index = 0;
            while (index < datagrams.Count)
            {
                int bundleStart = index;
                if (datagrams.Count - index == 1)
                {
                    try
                    {
                        long sendStart = Stopwatch.GetTimestamp();
                        socket.SendTo(datagrams[index].Payload.Span, SocketFlags.None, remote);
                        long sendEnd = Stopwatch.GetTimestamp();
                        sendTicks += sendEnd - sendStart;
                        datagrams[index].Dispose();
                        index++;
                        wireDatagramCount++;
                        singleWireDatagramCount++;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.IOPending or SocketError.NoBufferSpaceAvailable)
                    {
                        RequeueCurrentAndRemaining(ref enumerator, datagrams, bundleStart);
                        goto CompleteFlush;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        datagrams[index].Dispose();
                        index++;
                    }
                    continue;
                }

                _bundlePayloadScratch.Clear();
                for (int i = index; i < datagrams.Count; i++)
                    _bundlePayloadScratch.Add(datagrams[i].Payload);
                int bundleBytes = TransportDatagramBundleCodec.MeasureBundle(
                    _bundlePayloadScratch,
                    0,
                    _options.MaxDatagramBytes,
                    out int packedCount);
                byte[] rented = ArrayPool<byte>.Shared.Rent(bundleBytes);
                try
                {
                    long bundleEncodeStart = Stopwatch.GetTimestamp();
                    TransportDatagramBundleCodec.WriteBundle(
                        _bundlePayloadScratch,
                        0,
                        packedCount,
                        rented.AsSpan(0, bundleBytes));
                    long bundleEncodeEnd = Stopwatch.GetTimestamp();
                    bundleTicks += bundleEncodeEnd - bundleEncodeStart;
                    long sendStart = Stopwatch.GetTimestamp();
                    socket.SendTo(rented.AsSpan(0, bundleBytes), SocketFlags.None, remote);
                    long sendEnd = Stopwatch.GetTimestamp();
                    sendTicks += sendEnd - sendStart;
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.IOPending or SocketError.NoBufferSpaceAvailable)
                {
                    RequeueCurrentAndRemaining(ref enumerator, datagrams, bundleStart);
                    goto CompleteFlush;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    for (int i = bundleStart; i < bundleStart + packedCount; i++)
                        datagrams[i].Dispose();
                    index += packedCount;
                    continue;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }

                wireDatagramCount++;
                bundledWireDatagramCount++;
                for (int i = bundleStart; i < bundleStart + packedCount; i++)
                    datagrams[i].Dispose();
                index += packedCount;
            }
        }

CompleteFlush:
        if (!clearAfterSend)
            ResetFlushScratch();

        LastFlushWireDatagramCount = wireDatagramCount;
        _bundlePayloadScratch.Clear();
        return new FlushExecutionResult(
            BundleElapsedTicks: bundleTicks,
            SendElapsedTicks: sendTicks,
            LogicalDatagramCount: logicalDatagramCount,
            WireDatagramCount: wireDatagramCount,
            BundledWireDatagramCount: bundledWireDatagramCount,
            SingleWireDatagramCount: singleWireDatagramCount);
    }

    private void RequeueCurrentAndRemaining(
        ref Dictionary<ConnectionId, List<OutboundDatagram>>.Enumerator enumerator,
        List<OutboundDatagram> currentDatagrams,
        int currentStartIndex)
    {
        RequeueDatagrams(currentDatagrams, currentStartIndex);
        while (enumerator.MoveNext())
            RequeueDatagrams(enumerator.Current.Value, 0);
    }

    private void RequeueDatagrams(List<OutboundDatagram> datagrams, int startIndex)
    {
        for (int i = startIndex; i < datagrams.Count; i++)
            _outbound.Enqueue(datagrams[i]);
    }

    private static int CountLogicalDatagrams(Dictionary<ConnectionId, List<OutboundDatagram>> flushByConnection)
    {
        int total = 0;
        foreach (List<OutboundDatagram> list in flushByConnection.Values)
            total += list.Count;
        return total;
    }
}
