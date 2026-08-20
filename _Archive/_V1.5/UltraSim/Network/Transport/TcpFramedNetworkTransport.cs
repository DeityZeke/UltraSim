using UltraSim.Network;
using UltraSim.Network.Protocol;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

public sealed class TcpFramedNetworkTransport : INetworkTransport
{
    private const byte InternalControlChannel = 0xFF;
    private const byte UdpWireData = 0;
    private const byte UdpWireHello = 1;
    private const byte UdpWireAck = 2;
    private const byte UdpWireBatchData = 3;
    private const byte UdpWireDataToken = 4;
    private const byte UdpWireBatchDataToken = 5;
    private const byte TcpControlUdpOffer = 1;
    private const int MaxUdpBatchDatagramBytes = 1200;

    public enum TransportRole
    {
        Server = 0,
        Client = 1,
    }

    public enum ConnectionEventKind
    {
        Connected = 0,
        Disconnected = 1,
        ConnectFailed = 2,
        ListenFailed = 3,
        ListenerRuntimeError = 4,
    }

    public readonly struct ConnectionEvent
    {
        public readonly ConnectionEventKind Kind;
        public readonly int ConnectionId;
        public readonly string Message;

        public ConnectionEvent(ConnectionEventKind kind, int connectionId, string message)
        {
            Kind = kind;
            ConnectionId = connectionId;
            Message = message;
        }
    }

    public readonly struct TransportChannelStatsSnapshot
    {
        public readonly long ReliableOrdered;
        public readonly long ReliableUnordered;
        public readonly long UnreliableSequenced;
        public readonly long Unreliable;

        public TransportChannelStatsSnapshot(
            long reliableOrdered,
            long reliableUnordered,
            long unreliableSequenced,
            long unreliable)
        {
            ReliableOrdered = reliableOrdered;
            ReliableUnordered = reliableUnordered;
            UnreliableSequenced = unreliableSequenced;
            Unreliable = unreliable;
        }
    }

        public readonly struct TcpUdpStatsSnapshot
        {
            public readonly long UdpTxDatagrams;
            public readonly long UdpTxBytes;
            public readonly long UdpRxDatagrams;
        public readonly long UdpRxBytes;
        public readonly long UdpAssocHelloRx;
        public readonly long UdpAssocAckRx;
        public readonly long UdpDropsUnknownEndpoint;
        public readonly long UdpDropsUnknownToken;
        public readonly long UdpTxFailures;
        public readonly int UdpAssociatedPeers;
        public readonly long TcpTxBatches;
        public readonly long UdpTxBatches;
        public readonly long TcpTxFrames;
        public readonly long UdpTxFrames;
        public readonly long TcpTxFramedBytes;
        public readonly long UdpTxFramedBytes;
        public readonly long TcpWriteCalls;
        public readonly long TcpWriteElapsedTicksTotal;
        public readonly long TcpWriteElapsedTicksMax;
        public readonly long TcpSingleWriteCalls;
        public readonly long TcpSingleWriteElapsedTicksTotal;
        public readonly long TcpSingleWriteElapsedTicksMax;
        public readonly long TcpBatchWriteCalls;
        public readonly long TcpBatchWriteElapsedTicksTotal;
        public readonly long TcpBatchWriteElapsedTicksMax;
        public readonly long TcpControlWriteCalls;
        public readonly long TcpControlWriteElapsedTicksTotal;
        public readonly long TcpControlWriteElapsedTicksMax;
        public readonly long ServerFlushCalls;
        public readonly long ServerFlushElapsedTicksTotal;
            public readonly long ServerFlushElapsedTicksMax;
            public readonly long ServerUdpFallbackTcpBatches;
            public readonly long ServerUdpFallbackNoRouteBatches;
            public readonly long ServerUdpFallbackSendFailBatches;
            public readonly long BorrowedEnqueueAttempts;
            public readonly long BorrowedEnqueueSuccesses;
            public readonly long BorrowedPatchedEnqueueAttempts;
            public readonly long BorrowedPatchedEnqueueSuccesses;
            public readonly long BorrowedMulticastEnqueueAttempts;
            public readonly long BorrowedMulticastEnqueueSuccesses;
            public readonly long OutboundCopyCalls;
            public readonly long OutboundCopyBytes;
            public readonly long OutboundItemPoolMisses;
            public readonly long PongQueueAgeSamples;
            public readonly long PongQueueAgeTicksTotal;
            public readonly long PongQueueAgeTicksMax;
            public readonly long PingInboundQueueAgeSamples;
            public readonly long PingInboundQueueAgeTicksTotal;
            public readonly long PingInboundQueueAgeTicksMax;
            public readonly long InboundQueueDepth;
            public readonly long InboundQueueDepthMax;
            public readonly TransportChannelStatsSnapshot TcpChannelFrames;
            public readonly TransportChannelStatsSnapshot UdpChannelFrames;
            public readonly TransportChannelStatsSnapshot TcpChannelFramedBytes;
            public readonly TransportChannelStatsSnapshot UdpChannelFramedBytes;

        public TcpUdpStatsSnapshot(
            long udpTxDatagrams,
            long udpTxBytes,
            long udpRxDatagrams,
            long udpRxBytes,
            long udpAssocHelloRx,
            long udpAssocAckRx,
            long udpDropsUnknownEndpoint,
            long udpDropsUnknownToken,
            long udpTxFailures,
            int udpAssociatedPeers,
            long tcpTxBatches,
            long udpTxBatches,
            long tcpTxFrames,
            long udpTxFrames,
            long tcpTxFramedBytes,
            long udpTxFramedBytes,
            long tcpWriteCalls,
            long tcpWriteElapsedTicksTotal,
            long tcpWriteElapsedTicksMax,
            long tcpSingleWriteCalls,
            long tcpSingleWriteElapsedTicksTotal,
            long tcpSingleWriteElapsedTicksMax,
            long tcpBatchWriteCalls,
            long tcpBatchWriteElapsedTicksTotal,
            long tcpBatchWriteElapsedTicksMax,
            long tcpControlWriteCalls,
            long tcpControlWriteElapsedTicksTotal,
            long tcpControlWriteElapsedTicksMax,
            long serverFlushCalls,
            long serverFlushElapsedTicksTotal,
            long serverFlushElapsedTicksMax,
            long serverUdpFallbackTcpBatches,
            long serverUdpFallbackNoRouteBatches,
            long serverUdpFallbackSendFailBatches,
            long borrowedEnqueueAttempts,
            long borrowedEnqueueSuccesses,
            long borrowedPatchedEnqueueAttempts,
            long borrowedPatchedEnqueueSuccesses,
            long borrowedMulticastEnqueueAttempts,
            long borrowedMulticastEnqueueSuccesses,
            long outboundCopyCalls,
            long outboundCopyBytes,
            long outboundItemPoolMisses,
            long pongQueueAgeSamples,
            long pongQueueAgeTicksTotal,
            long pongQueueAgeTicksMax,
            long pingInboundQueueAgeSamples,
            long pingInboundQueueAgeTicksTotal,
            long pingInboundQueueAgeTicksMax,
            long inboundQueueDepth,
            long inboundQueueDepthMax,
            TransportChannelStatsSnapshot tcpChannelFrames,
            TransportChannelStatsSnapshot udpChannelFrames,
            TransportChannelStatsSnapshot tcpChannelFramedBytes,
            TransportChannelStatsSnapshot udpChannelFramedBytes)
        {
            UdpTxDatagrams = udpTxDatagrams;
            UdpTxBytes = udpTxBytes;
            UdpRxDatagrams = udpRxDatagrams;
            UdpRxBytes = udpRxBytes;
            UdpAssocHelloRx = udpAssocHelloRx;
            UdpAssocAckRx = udpAssocAckRx;
            UdpDropsUnknownEndpoint = udpDropsUnknownEndpoint;
            UdpDropsUnknownToken = udpDropsUnknownToken;
            UdpTxFailures = udpTxFailures;
            UdpAssociatedPeers = udpAssociatedPeers;
            TcpTxBatches = tcpTxBatches;
            UdpTxBatches = udpTxBatches;
            TcpTxFrames = tcpTxFrames;
            UdpTxFrames = udpTxFrames;
            TcpTxFramedBytes = tcpTxFramedBytes;
            UdpTxFramedBytes = udpTxFramedBytes;
            TcpWriteCalls = tcpWriteCalls;
            TcpWriteElapsedTicksTotal = tcpWriteElapsedTicksTotal;
            TcpWriteElapsedTicksMax = tcpWriteElapsedTicksMax;
            TcpSingleWriteCalls = tcpSingleWriteCalls;
            TcpSingleWriteElapsedTicksTotal = tcpSingleWriteElapsedTicksTotal;
            TcpSingleWriteElapsedTicksMax = tcpSingleWriteElapsedTicksMax;
            TcpBatchWriteCalls = tcpBatchWriteCalls;
            TcpBatchWriteElapsedTicksTotal = tcpBatchWriteElapsedTicksTotal;
            TcpBatchWriteElapsedTicksMax = tcpBatchWriteElapsedTicksMax;
            TcpControlWriteCalls = tcpControlWriteCalls;
            TcpControlWriteElapsedTicksTotal = tcpControlWriteElapsedTicksTotal;
            TcpControlWriteElapsedTicksMax = tcpControlWriteElapsedTicksMax;
            ServerFlushCalls = serverFlushCalls;
            ServerFlushElapsedTicksTotal = serverFlushElapsedTicksTotal;
            ServerFlushElapsedTicksMax = serverFlushElapsedTicksMax;
            ServerUdpFallbackTcpBatches = serverUdpFallbackTcpBatches;
            ServerUdpFallbackNoRouteBatches = serverUdpFallbackNoRouteBatches;
            ServerUdpFallbackSendFailBatches = serverUdpFallbackSendFailBatches;
            BorrowedEnqueueAttempts = borrowedEnqueueAttempts;
            BorrowedEnqueueSuccesses = borrowedEnqueueSuccesses;
            BorrowedPatchedEnqueueAttempts = borrowedPatchedEnqueueAttempts;
            BorrowedPatchedEnqueueSuccesses = borrowedPatchedEnqueueSuccesses;
            BorrowedMulticastEnqueueAttempts = borrowedMulticastEnqueueAttempts;
            BorrowedMulticastEnqueueSuccesses = borrowedMulticastEnqueueSuccesses;
            OutboundCopyCalls = outboundCopyCalls;
            OutboundCopyBytes = outboundCopyBytes;
            OutboundItemPoolMisses = outboundItemPoolMisses;
            PongQueueAgeSamples = pongQueueAgeSamples;
            PongQueueAgeTicksTotal = pongQueueAgeTicksTotal;
            PongQueueAgeTicksMax = pongQueueAgeTicksMax;
            PingInboundQueueAgeSamples = pingInboundQueueAgeSamples;
            PingInboundQueueAgeTicksTotal = pingInboundQueueAgeTicksTotal;
            PingInboundQueueAgeTicksMax = pingInboundQueueAgeTicksMax;
            InboundQueueDepth = inboundQueueDepth;
            InboundQueueDepthMax = inboundQueueDepthMax;
            TcpChannelFrames = tcpChannelFrames;
            UdpChannelFrames = udpChannelFrames;
            TcpChannelFramedBytes = tcpChannelFramedBytes;
            UdpChannelFramedBytes = udpChannelFramedBytes;
        }
    }

    private sealed class OwnedInbound
    {
        public int ConnectionId;
        public ChannelType Channel;
        public byte[] Buffer = Array.Empty<byte>();
        public int Length;
        public long EnqueuedAtTicks;
        public bool IsPingProbe;
    }

    private sealed class OwnedOutbound
    {
        public int ConnectionId;
        public ChannelType Channel;
        public byte[] Buffer = Array.Empty<byte>();
        public int Offset;
        public int Length;
        public bool ReturnToPool;
        public bool HasUInt16Patch;
        public int UInt16PatchOffset;
        public ushort UInt16PatchValue;
        public bool IsPongProbe;
        public long EnqueuedAtTicks;
        public int[]? ConnectionIds;
        public int ConnectionIdCount;
        public bool ReturnConnectionIdsToPool;
    }

    private sealed class OutboundBatch
    {
        public byte[] Buffer = Array.Empty<byte>();
        public int Length;

        public void Clear() => Length = 0;
    }

    // Holds all state for one in-flight server flush (build on main thread, send on background thread).
    private sealed class FlushSendSet
    {
        public readonly Dictionary<int, OutboundBatch> TcpBatchesByConnection = new(64);
        public readonly List<int> TcpConnectionIds = new(64);
        public readonly List<OutboundBatch> TcpItems = new(64);
        public readonly List<PeerState?> TcpPeerScratch = new(64);
        public readonly Dictionary<int, OutboundBatch> UdpBatchesByConnection = new(64);
        public readonly List<int> UdpConnectionIds = new(64);
        public readonly List<OutboundBatch> UdpItems = new(64);
        public readonly List<ServerPeerFlushSnapshot> UdpPeerScratch = new(64);
        public readonly Stack<OutboundBatch> BatchPool = new(64);
        public UdpClient? ServerUdp;
        // Phase 1 stats (accumulated on main thread during drain)
        public int FrameTcp, FrameUdp;
        public readonly int[] ChannelFramesTcp = new int[4];
        public readonly int[] ChannelFramesUdp = new int[4];
        public readonly long[] ChannelBytesTcp = new long[4];
        public readonly long[] ChannelBytesUdp = new long[4];
        // Phase 3 stats (accumulated on background thread during send)
        public int BatchTcp, BatchUdp;
        public int FramedBytesTcp, FramedBytesUdp;
        public int UdpFallbackTcpBatches, UdpFallbackNoRouteBatches, UdpFallbackSendFailBatches;

        public OutboundBatch RentBatch()
        {
            OutboundBatch b = BatchPool.Count > 0 ? BatchPool.Pop() : new OutboundBatch();
            b.Clear();
            if (b.Buffer.Length == 0)
                b.Buffer = ArrayPool<byte>.Shared.Rent(4096);
            return b;
        }

        public void ReturnBatch(OutboundBatch batch)
        {
            batch.Clear();
            BatchPool.Push(batch);
        }

        public void Reset()
        {
            TcpPeerScratch.Clear();
            UdpPeerScratch.Clear();
            ServerUdp = null;
            FrameTcp = FrameUdp = BatchTcp = BatchUdp = 0;
            FramedBytesTcp = FramedBytesUdp = 0;
            UdpFallbackTcpBatches = UdpFallbackNoRouteBatches = UdpFallbackSendFailBatches = 0;
            Array.Clear(ChannelFramesTcp, 0, 4);
            Array.Clear(ChannelFramesUdp, 0, 4);
            Array.Clear(ChannelBytesTcp, 0, 4);
            Array.Clear(ChannelBytesUdp, 0, 4);
            if (TcpItems.Count > 0)
            {
                for (int i = 0; i < TcpItems.Count; i++)
                    ReturnBatch(TcpItems[i]);
                TcpBatchesByConnection.Clear();
                TcpConnectionIds.Clear();
                TcpItems.Clear();
            }
            if (UdpItems.Count > 0)
            {
                for (int i = 0; i < UdpItems.Count; i++)
                    ReturnBatch(UdpItems[i]);
                UdpBatchesByConnection.Clear();
                UdpConnectionIds.Clear();
                UdpItems.Clear();
            }
        }

        public void DisposeBatchStorage()
        {
            Reset();
            while (BatchPool.Count > 0)
            {
                OutboundBatch b = BatchPool.Pop();
                if (b.Buffer.Length > 0)
                {
                    ArrayPool<byte>.Shared.Return(b.Buffer);
                    b.Buffer = Array.Empty<byte>();
                }
                b.Length = 0;
            }
        }
    }

    private readonly struct ServerPeerFlushSnapshot
    {
        public readonly PeerState? Peer;
        public readonly IPEndPoint? UdpEndpoint;
        public readonly long UdpToken;
        public readonly bool HasUdpRoute;

        public ServerPeerFlushSnapshot(PeerState? peer, IPEndPoint? udpEndpoint, long udpToken, bool hasUdpRoute)
        {
            Peer = peer;
            UdpEndpoint = udpEndpoint;
            UdpToken = udpToken;
            HasUdpRoute = hasUdpRoute;
        }
    }

    private readonly struct UdpRouteSnapshot
    {
        public readonly IPEndPoint Endpoint;
        public readonly long Token;

        public UdpRouteSnapshot(IPEndPoint endpoint, long token)
        {
            Endpoint = endpoint;
            Token = token;
        }
    }

    private sealed class PeerState : IDisposable
    {
        public readonly int ConnectionId;
        public readonly TcpClient Client;
        public readonly NetworkStream Stream;
        public readonly object SendSync = new object();
        public readonly LengthPrefixedFrameReader Reader;
        public readonly byte[] ReceiveScratch;
        public long UdpToken;
        public volatile bool Closed;
        public Task? ReceiveTask;

        public PeerState(int connectionId, TcpClient client, int maxDatagramSize)
        {
            ConnectionId = connectionId;
            Client = client;
            try { Client.NoDelay = true; } catch { }
            Stream = client.GetStream();
            Reader = new LengthPrefixedFrameReader(
                initialCapacity: 4096,
                maxFrameLength: maxDatagramSize + 1,
                invalidFramePolicy: InvalidFramePolicy.DropByte);
            ReceiveScratch = new byte[4096];
        }

        public void Dispose()
        {
            if (Closed)
                return;

            Closed = true;
            try { Stream.Close(); } catch { }
            try { Client.Close(); } catch { }
            Reader.Dispose();
        }
    }

    private readonly object _sync = new object();
    private readonly TransportRole _role;
    private readonly string _host;
    private readonly int _port;
    private readonly int _clientConnectionId;
    private readonly int _maxDatagramSize;
    private readonly bool _enableUdp;
    private readonly int _udpPort;
    private readonly bool _enableUdpSendScratchReuse;

    private TcpListener? _listener;
    private Task? _acceptTask;
    private Task? _clientConnectTask;
    private PeerState? _clientPeer;
    private readonly Dictionary<int, PeerState> _serverPeers = new Dictionary<int, PeerState>(16);
    private int _nextServerConnectionId = 1000;

    private readonly ConcurrentQueue<OwnedInbound> _inboundQueue = new ConcurrentQueue<OwnedInbound>();
    private readonly ConcurrentQueue<OwnedOutbound> _outboundQueue = new ConcurrentQueue<OwnedOutbound>();
    private readonly ConcurrentQueue<OwnedInbound> _inboundItemPool = new ConcurrentQueue<OwnedInbound>();
    private readonly ConcurrentQueue<OwnedOutbound> _outboundItemPool = new ConcurrentQueue<OwnedOutbound>();
    private readonly ConcurrentQueue<ConnectionEvent> _connectionEvents = new ConcurrentQueue<ConnectionEvent>();
    private readonly List<OwnedInbound> _activeInbound = new List<OwnedInbound>(256);
    // Double-buffered flush sets: main thread builds into _buildSet, background sends from _sendSet.
    private readonly FlushSendSet _setA = new FlushSendSet();
    private readonly FlushSendSet _setB = new FlushSendSet();
    private FlushSendSet _buildSet = null!; // assigned in constructor to _setA
    private FlushSendSet _sendSet = null!;  // assigned in constructor to _setB
    private Task _pendingFlushTask = Task.CompletedTask;
    // Client-only batch pool (server path uses per-set pools inside FlushSendSet)
    private readonly Stack<OutboundBatch> _flushBatchPool = new Stack<OutboundBatch>(64);
    private UdpClient? _serverUdp;
    private UdpClient? _clientUdp;
    private Task? _serverUdpReceiveTask;
    private Task? _clientUdpReceiveTask;
    private readonly Dictionary<int, IPEndPoint> _udpEndpointByConnection = new Dictionary<int, IPEndPoint>(64);
    private readonly Dictionary<IPEndPoint, int> _udpConnectionByEndpoint = new Dictionary<IPEndPoint, int>(64);
    private readonly Dictionary<long, int> _udpConnectionByToken = new Dictionary<long, int>(64);
    private readonly Dictionary<long, int> _udpPendingTokens = new Dictionary<long, int>(64);
    private IPEndPoint? _clientServerUdpEndpoint;
    private long _clientPendingUdpToken;
    private int _clientServerAssignedConnectionId;
    private volatile bool _clientUdpAssociated;
    private long _clientUdpHelloRetryDueTicks;

    private long _udpTxDatagrams;
    private long _udpTxBytes;
    private long _udpRxDatagrams;
    private long _udpRxBytes;
    private long _udpAssocHelloRx;
    private long _udpAssocAckRx;
    private long _udpDropsUnknownEndpoint;
    private long _udpDropsUnknownToken;
    private long _udpTxFailures;
    private long _txBatchTcp;
    private long _txBatchUdp;
    private long _txFrameTcp;
    private long _txFrameUdp;
    private long _txFramedBytesTcp;
    private long _txFramedBytesUdp;
    private long _tcpWriteCalls;
    private long _tcpWriteElapsedTicksTotal;
    private long _tcpWriteElapsedTicksMax;
    private long _tcpSingleWriteCalls;
    private long _tcpSingleWriteElapsedTicksTotal;
    private long _tcpSingleWriteElapsedTicksMax;
    private long _tcpBatchWriteCalls;
    private long _tcpBatchWriteElapsedTicksTotal;
    private long _tcpBatchWriteElapsedTicksMax;
    private long _tcpControlWriteCalls;
    private long _tcpControlWriteElapsedTicksTotal;
    private long _tcpControlWriteElapsedTicksMax;
    private long _serverFlushCalls;
    private long _serverFlushElapsedTicksTotal;
    private long _serverFlushElapsedTicksMax;
    private long _serverUdpFallbackTcpBatches;
    private long _serverUdpFallbackNoRouteBatches;
    private long _serverUdpFallbackSendFailBatches;
    private long _borrowedEnqueueAttempts;
    private long _borrowedEnqueueSuccesses;
    private long _borrowedPatchedEnqueueAttempts;
    private long _borrowedPatchedEnqueueSuccesses;
    private long _borrowedMulticastEnqueueAttempts;
    private long _borrowedMulticastEnqueueSuccesses;
    private long _outboundCopyCalls;
    private long _outboundCopyBytes;
    private long _outboundItemPoolMisses;
    private long _pongQueueAgeSamples;
    private long _pongQueueAgeTicksTotal;
    private long _pongQueueAgeTicksMax;
    private long _pingInboundQueueAgeSamples;
    private long _pingInboundQueueAgeTicksTotal;
    private long _pingInboundQueueAgeTicksMax;
    private long _inboundQueueDepth;
    private long _inboundQueueDepthMax;
    private readonly long[] _txChannelFramesTcp = new long[4];
    private readonly long[] _txChannelFramesUdp = new long[4];
    private readonly long[] _txChannelFramedBytesTcp = new long[4];
    private readonly long[] _txChannelFramedBytesUdp = new long[4];
    private byte[] _udpSendScratch = Array.Empty<byte>();

    public int ClientServerAssignedConnectionId => Volatile.Read(ref _clientServerAssignedConnectionId);

    private volatile bool _running;
    private volatile bool _disposed;
    private static readonly long UdpHelloRetryIntervalTicks = Stopwatch.Frequency / 4; // 250ms
    private const int MaxServerConnections = 4096;

    public TcpFramedNetworkTransport(
        TransportRole role,
        string host,
        int port,
        int clientConnectionId = 1,
        int maxDatagramSize = 65535,
        bool enableUdp = false,
        int? udpPort = null,
        bool enableServerFlushRouteSnapshot = false,
        bool enableUdpSendScratchReuse = false)
    {
        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (maxDatagramSize <= 0 || maxDatagramSize > 65535)
            throw new ArgumentOutOfRangeException(nameof(maxDatagramSize));
        if (clientConnectionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(clientConnectionId));

        _role = role;
        _host = host ?? "127.0.0.1";
        _port = port;
        _clientConnectionId = clientConnectionId;
        _maxDatagramSize = maxDatagramSize;
        _enableUdp = enableUdp;
        _udpPort = udpPort.GetValueOrDefault(port);
        _ = enableServerFlushRouteSnapshot; // Batched server flushes always snapshot active routes now.
        _enableUdpSendScratchReuse = enableUdpSendScratchReuse;
        if (_udpPort <= 0 || _udpPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(udpPort));
        _buildSet = _setA;
        _sendSet = _setB;
    }

    public bool IsRunning => _running;

    public bool IsClientConnected => _clientPeer != null && !_clientPeer.Closed;

    public int ActiveConnectionCount
    {
        get
        {
            lock (_sync)
            {
                if (_role == TransportRole.Client)
                    return IsClientConnected ? 1 : 0;
                return _serverPeers.Count;
            }
        }
    }

    public bool IsUdpEnabled => _enableUdp;

    public int UdpPort => _udpPort;

    public TcpUdpStatsSnapshot SnapshotTcpUdpStats() => new TcpUdpStatsSnapshot(
        udpTxDatagrams: Interlocked.Read(ref _udpTxDatagrams),
        udpTxBytes: Interlocked.Read(ref _udpTxBytes),
        udpRxDatagrams: Interlocked.Read(ref _udpRxDatagrams),
        udpRxBytes: Interlocked.Read(ref _udpRxBytes),
        udpAssocHelloRx: Interlocked.Read(ref _udpAssocHelloRx),
        udpAssocAckRx: Interlocked.Read(ref _udpAssocAckRx),
        udpDropsUnknownEndpoint: Interlocked.Read(ref _udpDropsUnknownEndpoint),
        udpDropsUnknownToken: Interlocked.Read(ref _udpDropsUnknownToken),
        udpTxFailures: Interlocked.Read(ref _udpTxFailures),
        udpAssociatedPeers: GetUdpAssociatedPeerCount(),
        tcpTxBatches: Interlocked.Read(ref _txBatchTcp),
        udpTxBatches: Interlocked.Read(ref _txBatchUdp),
        tcpTxFrames: Interlocked.Read(ref _txFrameTcp),
        udpTxFrames: Interlocked.Read(ref _txFrameUdp),
        tcpTxFramedBytes: Interlocked.Read(ref _txFramedBytesTcp),
        udpTxFramedBytes: Interlocked.Read(ref _txFramedBytesUdp),
        tcpWriteCalls: Interlocked.Read(ref _tcpWriteCalls),
        tcpWriteElapsedTicksTotal: Interlocked.Read(ref _tcpWriteElapsedTicksTotal),
        tcpWriteElapsedTicksMax: Interlocked.Read(ref _tcpWriteElapsedTicksMax),
        tcpSingleWriteCalls: Interlocked.Read(ref _tcpSingleWriteCalls),
        tcpSingleWriteElapsedTicksTotal: Interlocked.Read(ref _tcpSingleWriteElapsedTicksTotal),
        tcpSingleWriteElapsedTicksMax: Interlocked.Read(ref _tcpSingleWriteElapsedTicksMax),
        tcpBatchWriteCalls: Interlocked.Read(ref _tcpBatchWriteCalls),
        tcpBatchWriteElapsedTicksTotal: Interlocked.Read(ref _tcpBatchWriteElapsedTicksTotal),
        tcpBatchWriteElapsedTicksMax: Interlocked.Read(ref _tcpBatchWriteElapsedTicksMax),
        tcpControlWriteCalls: Interlocked.Read(ref _tcpControlWriteCalls),
        tcpControlWriteElapsedTicksTotal: Interlocked.Read(ref _tcpControlWriteElapsedTicksTotal),
        tcpControlWriteElapsedTicksMax: Interlocked.Read(ref _tcpControlWriteElapsedTicksMax),
        serverFlushCalls: Interlocked.Read(ref _serverFlushCalls),
        serverFlushElapsedTicksTotal: Interlocked.Read(ref _serverFlushElapsedTicksTotal),
        serverFlushElapsedTicksMax: Interlocked.Read(ref _serverFlushElapsedTicksMax),
        serverUdpFallbackTcpBatches: Interlocked.Read(ref _serverUdpFallbackTcpBatches),
        serverUdpFallbackNoRouteBatches: Interlocked.Read(ref _serverUdpFallbackNoRouteBatches),
        serverUdpFallbackSendFailBatches: Interlocked.Read(ref _serverUdpFallbackSendFailBatches),
        borrowedEnqueueAttempts: Interlocked.Read(ref _borrowedEnqueueAttempts),
        borrowedEnqueueSuccesses: Interlocked.Read(ref _borrowedEnqueueSuccesses),
        borrowedPatchedEnqueueAttempts: Interlocked.Read(ref _borrowedPatchedEnqueueAttempts),
        borrowedPatchedEnqueueSuccesses: Interlocked.Read(ref _borrowedPatchedEnqueueSuccesses),
        borrowedMulticastEnqueueAttempts: Interlocked.Read(ref _borrowedMulticastEnqueueAttempts),
        borrowedMulticastEnqueueSuccesses: Interlocked.Read(ref _borrowedMulticastEnqueueSuccesses),
        outboundCopyCalls: Interlocked.Read(ref _outboundCopyCalls),
        outboundCopyBytes: Interlocked.Read(ref _outboundCopyBytes),
        outboundItemPoolMisses: Interlocked.Read(ref _outboundItemPoolMisses),
        pongQueueAgeSamples: Interlocked.Read(ref _pongQueueAgeSamples),
        pongQueueAgeTicksTotal: Interlocked.Read(ref _pongQueueAgeTicksTotal),
        pongQueueAgeTicksMax: Interlocked.Read(ref _pongQueueAgeTicksMax),
        pingInboundQueueAgeSamples: Interlocked.Read(ref _pingInboundQueueAgeSamples),
        pingInboundQueueAgeTicksTotal: Interlocked.Read(ref _pingInboundQueueAgeTicksTotal),
        pingInboundQueueAgeTicksMax: Interlocked.Read(ref _pingInboundQueueAgeTicksMax),
        inboundQueueDepth: Interlocked.Read(ref _inboundQueueDepth),
        inboundQueueDepthMax: Interlocked.Read(ref _inboundQueueDepthMax),
        tcpChannelFrames: SnapshotChannelStats(_txChannelFramesTcp),
        udpChannelFrames: SnapshotChannelStats(_txChannelFramesUdp),
        tcpChannelFramedBytes: SnapshotChannelStats(_txChannelFramedBytesTcp),
        udpChannelFramedBytes: SnapshotChannelStats(_txChannelFramedBytesUdp));

    private static TransportChannelStatsSnapshot SnapshotChannelStats(long[] counters) => new TransportChannelStatsSnapshot(
        reliableOrdered: Interlocked.Read(ref counters[(int)ChannelType.ReliableOrdered]),
        reliableUnordered: Interlocked.Read(ref counters[(int)ChannelType.ReliableUnordered]),
        unreliableSequenced: Interlocked.Read(ref counters[(int)ChannelType.UnreliableSequenced]),
        unreliable: Interlocked.Read(ref counters[(int)ChannelType.Unreliable]));

    public int DrainConnectionEvents(Span<ConnectionEvent> destination)
    {
        int count = 0;
        while (count < destination.Length && _connectionEvents.TryDequeue(out ConnectionEvent evt))
            destination[count++] = evt;
        return count;
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (_running)
            return;

        _running = true;

        if (_role == TransportRole.Server)
            StartServer();
        else
            StartClient();
    }

    public void Stop()
    {
        if (!_running)
            return;

        _running = false;

        lock (_sync)
        {
            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                _listener = null;
            }

            if (_clientPeer != null)
            {
                ClosePeer_NoLock(_clientPeer, ConnectionEventKind.Disconnected, "client_stopped");
                _clientPeer = null;
            }

            if (_serverPeers.Count > 0)
            {
                foreach (PeerState peer in _serverPeers.Values)
                    ClosePeer_NoLock(peer, ConnectionEventKind.Disconnected, "server_stopped");
                _serverPeers.Clear();
            }

            _udpEndpointByConnection.Clear();
            _udpConnectionByEndpoint.Clear();
            _udpPendingTokens.Clear();
            Interlocked.Exchange(ref _clientPendingUdpToken, 0);
            _clientServerAssignedConnectionId = 0;
            _clientUdpAssociated = false;
            _clientServerUdpEndpoint = null;

            if (_serverUdp != null)
            {
                try { _serverUdp.Close(); } catch { }
                _serverUdp.Dispose();
                _serverUdp = null;
            }

            if (_clientUdp != null)
            {
                try { _clientUdp.Close(); } catch { }
                _clientUdp.Dispose();
                _clientUdp = null;
            }
        }

        try { _acceptTask?.Wait(200); } catch { }
        _acceptTask = null;
        try { _clientConnectTask?.Wait(200); } catch { }
        _clientConnectTask = null;
        try { _serverUdpReceiveTask?.Wait(200); } catch { }
        _serverUdpReceiveTask = null;
        try { _clientUdpReceiveTask?.Wait(200); } catch { }
        _clientUdpReceiveTask = null;
    }

    public int PollInbound(Span<InboundDatagram> destination)
    {
        ThrowIfDisposed();
        int count = 0;
        while (count < destination.Length && _inboundQueue.TryDequeue(out OwnedInbound? owned))
        {
            Interlocked.Decrement(ref _inboundQueueDepth);
            if (owned.IsPingProbe && owned.EnqueuedAtTicks != 0)
                RecordPingInboundQueueAge(Stopwatch.GetTimestamp() - owned.EnqueuedAtTicks);
            _activeInbound.Add(owned);
            destination[count++] = new InboundDatagram(
                owned.ConnectionId,
                owned.Channel,
                new ReadOnlyMemory<byte>(owned.Buffer, 0, owned.Length));
        }

        return count;
    }

    public void EnqueueOutbound(in OutboundDatagram datagram)
    {
        ThrowIfDisposed();
        if (datagram.Payload.Length <= 0 || datagram.Payload.Length > _maxDatagramSize)
            return;

        EnqueueOutboundCopy(datagram.ConnectionId, datagram.Channel, datagram.Payload.Span);
    }

    // Enqueue an array-backed payload without copying. Returns false when payload is not array-backed
    // or invalid for this transport, allowing caller fallback to copy-based enqueue.
    public bool TryEnqueueOutboundBorrowed(int connectionId, ChannelType channel, ReadOnlyMemory<byte> payload)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _borrowedEnqueueAttempts);
        if (payload.Length <= 0 || payload.Length > _maxDatagramSize)
            return false;
        if (!MemoryMarshal.TryGetArray(payload, out ArraySegment<byte> slice) || slice.Array == null)
            return false;

        OwnedOutbound outbound = RentOutboundItem();
        outbound.ConnectionId = connectionId;
        outbound.Channel = channel;
        outbound.Buffer = slice.Array;
        outbound.Offset = slice.Offset;
        outbound.Length = slice.Count;
        outbound.ReturnToPool = false;
        outbound.HasUInt16Patch = false;
        outbound.UInt16PatchOffset = 0;
        outbound.UInt16PatchValue = 0;
        outbound.IsPongProbe = IsPongPayload(payload.Span);
        outbound.EnqueuedAtTicks = Stopwatch.GetTimestamp();
        outbound.ConnectionIds = null;
        outbound.ConnectionIdCount = 0;
        outbound.ReturnConnectionIdsToPool = false;
        _outboundQueue.Enqueue(outbound);
        Interlocked.Increment(ref _borrowedEnqueueSuccesses);
        return true;
    }

    // Enqueue an array-backed payload without copying and apply a small in-frame
    // UInt16 patch during framed-batch copy.
    public bool TryEnqueueOutboundBorrowedPatchedUInt16(
        int connectionId,
        ChannelType channel,
        ReadOnlyMemory<byte> payload,
        int patchOffset,
        ushort patchValue)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _borrowedPatchedEnqueueAttempts);
        if (payload.Length <= 0 || payload.Length > _maxDatagramSize)
            return false;
        if ((uint)patchOffset > (uint)(payload.Length - sizeof(ushort)))
            return false;
        if (!MemoryMarshal.TryGetArray(payload, out ArraySegment<byte> slice) || slice.Array == null)
            return false;

        OwnedOutbound outbound = RentOutboundItem();
        outbound.ConnectionId = connectionId;
        outbound.Channel = channel;
        outbound.Buffer = slice.Array;
        outbound.Offset = slice.Offset;
        outbound.Length = slice.Count;
        outbound.ReturnToPool = false;
        outbound.HasUInt16Patch = true;
        outbound.UInt16PatchOffset = patchOffset;
        outbound.UInt16PatchValue = patchValue;
        outbound.IsPongProbe = IsPongPayload(payload.Span);
        outbound.EnqueuedAtTicks = Stopwatch.GetTimestamp();
        outbound.ConnectionIds = null;
        outbound.ConnectionIdCount = 0;
        outbound.ReturnConnectionIdsToPool = false;
        _outboundQueue.Enqueue(outbound);
        Interlocked.Increment(ref _borrowedPatchedEnqueueSuccesses);
        return true;
    }

    // Enqueue a single borrowed payload for multiple server connections. The payload is not copied.
    // Connection IDs are copied into a pooled array owned by the queue item.
    public bool TryEnqueueOutboundBorrowedMulticast(
        ReadOnlySpan<int> connectionIds,
        ChannelType channel,
        ReadOnlyMemory<byte> payload)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _borrowedMulticastEnqueueAttempts);
        if (_role != TransportRole.Server)
            return false;
        if (connectionIds.Length <= 0)
            return false;
        if (payload.Length <= 0 || payload.Length > _maxDatagramSize)
            return false;
        if (!MemoryMarshal.TryGetArray(payload, out ArraySegment<byte> slice) || slice.Array == null)
            return false;

        int[] ids = ArrayPool<int>.Shared.Rent(connectionIds.Length);
        connectionIds.CopyTo(ids);

        OwnedOutbound outbound = RentOutboundItem();
        outbound.ConnectionId = ids[0];
        outbound.Channel = channel;
        outbound.Buffer = slice.Array;
        outbound.Offset = slice.Offset;
        outbound.Length = slice.Count;
        outbound.ReturnToPool = false;
        outbound.HasUInt16Patch = false;
        outbound.UInt16PatchOffset = 0;
        outbound.UInt16PatchValue = 0;
        outbound.IsPongProbe = IsPongPayload(payload.Span);
        outbound.EnqueuedAtTicks = Stopwatch.GetTimestamp();
        outbound.ConnectionIds = ids;
        outbound.ConnectionIdCount = connectionIds.Length;
        outbound.ReturnConnectionIdsToPool = true;
        _outboundQueue.Enqueue(outbound);
        Interlocked.Increment(ref _borrowedMulticastEnqueueSuccesses);
        return true;
    }

    private void EnqueueOutboundCopy(int connectionId, ChannelType channel, ReadOnlySpan<byte> payload)
    {
        Interlocked.Increment(ref _outboundCopyCalls);
        Interlocked.Add(ref _outboundCopyBytes, payload.Length);
        byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(payloadBuffer.AsSpan(0, payload.Length));
        OwnedOutbound outbound = RentOutboundItem();
        outbound.ConnectionId = connectionId;
        outbound.Channel = channel;
        outbound.Buffer = payloadBuffer;
        outbound.Offset = 0;
        outbound.Length = payload.Length;
        outbound.ReturnToPool = true;
        outbound.HasUInt16Patch = false;
        outbound.UInt16PatchOffset = 0;
        outbound.UInt16PatchValue = 0;
        outbound.IsPongProbe = IsPongPayload(payload);
        outbound.EnqueuedAtTicks = Stopwatch.GetTimestamp();
        outbound.ConnectionIds = null;
        outbound.ConnectionIdCount = 0;
        outbound.ReturnConnectionIdsToPool = false;
        _outboundQueue.Enqueue(outbound);
    }

    private OwnedOutbound RentOutboundItem() =>
        _outboundItemPool.TryDequeue(out OwnedOutbound? item)
            ? item
            : CreateOutboundItem();

    private OwnedOutbound CreateOutboundItem()
    {
        Interlocked.Increment(ref _outboundItemPoolMisses);
        return new OwnedOutbound();
    }

    private static bool IsPongPayload(ReadOnlySpan<byte> payload) =>
        payload.Length >= NetworkControlMessageCodec.PongProbePayloadBytes &&
        payload[0] == NetworkControlMessageCodec.PongProbeMessageType;

    private static bool IsPingPayload(ReadOnlySpan<byte> payload) =>
        payload.Length >= NetworkControlMessageCodec.PingProbePayloadBytes &&
        payload[0] == NetworkControlMessageCodec.PingProbeMessageType;

    private void RecordPongQueueAge(long queueAgeTicks)
    {
        Interlocked.Increment(ref _pongQueueAgeSamples);
        Interlocked.Add(ref _pongQueueAgeTicksTotal, queueAgeTicks);
        long currentMax = Volatile.Read(ref _pongQueueAgeTicksMax);
        while (queueAgeTicks > currentMax)
        {
            long observed = Interlocked.CompareExchange(ref _pongQueueAgeTicksMax, queueAgeTicks, currentMax);
            if (observed == currentMax)
                break;
            currentMax = observed;
        }
    }

    private void RecordPingInboundQueueAge(long queueAgeTicks)
    {
        Interlocked.Increment(ref _pingInboundQueueAgeSamples);
        Interlocked.Add(ref _pingInboundQueueAgeTicksTotal, queueAgeTicks);
        long currentMax = Volatile.Read(ref _pingInboundQueueAgeTicksMax);
        while (queueAgeTicks > currentMax)
        {
            long observed = Interlocked.CompareExchange(ref _pingInboundQueueAgeTicksMax, queueAgeTicks, currentMax);
            if (observed == currentMax)
                break;
            currentMax = observed;
        }
    }

    private void OnInboundQueued()
    {
        long depth = Interlocked.Increment(ref _inboundQueueDepth);
        UpdateElapsedMax(ref _inboundQueueDepthMax, depth);
    }

    private void ReturnOutboundItem(OwnedOutbound item)
    {
        if (item.ReturnConnectionIdsToPool && item.ConnectionIds != null)
            ArrayPool<int>.Shared.Return(item.ConnectionIds);
        item.ConnectionId = 0;
        item.Channel = default;
        item.Buffer = Array.Empty<byte>();
        item.Offset = 0;
        item.Length = 0;
        item.ReturnToPool = false;
        item.HasUInt16Patch = false;
        item.UInt16PatchOffset = 0;
        item.UInt16PatchValue = 0;
        item.IsPongProbe = false;
        item.EnqueuedAtTicks = 0;
        item.ConnectionIds = null;
        item.ConnectionIdCount = 0;
        item.ReturnConnectionIdsToPool = false;
        _outboundItemPool.Enqueue(item);
    }

    private OwnedInbound RentInboundItem() =>
        _inboundItemPool.TryDequeue(out OwnedInbound? item) ? item : new OwnedInbound();

    private void ReturnInboundItem(OwnedInbound item)
    {
        item.ConnectionId = 0;
        item.Channel = default;
        item.Buffer = Array.Empty<byte>();
        item.Length = 0;
        item.EnqueuedAtTicks = 0;
        item.IsPingProbe = false;
        _inboundItemPool.Enqueue(item);
    }

    public void FlushOutbound()
    {
        ThrowIfDisposed();
        if (_role == TransportRole.Client)
        {
            TryClientUdpHelloRetry();
            FlushOutboundClientBatched();
            return;
        }

        long start = Stopwatch.GetTimestamp();
        FlushOutboundServerBatched();
        long elapsed = Stopwatch.GetTimestamp() - start;
        Interlocked.Increment(ref _serverFlushCalls);
        Interlocked.Add(ref _serverFlushElapsedTicksTotal, elapsed);
        UpdateElapsedMax(ref _serverFlushElapsedTicksMax, elapsed);
    }

    public void CompleteInboundConsume()
    {
        for (int i = 0; i < _activeInbound.Count; i++)
        {
            OwnedInbound inbound = _activeInbound[i];
            ArrayPool<byte>.Shared.Return(inbound.Buffer);
            ReturnInboundItem(inbound);
        }
        _activeInbound.Clear();
    }

    public bool DisconnectConnection(int connectionId)
    {
        lock (_sync)
        {
            if (_role == TransportRole.Client)
            {
                if (_clientPeer != null && _clientPeer.ConnectionId == connectionId && !_clientPeer.Closed)
                {
                    ClosePeer_NoLock(_clientPeer, ConnectionEventKind.Disconnected, "client_disconnect");
                    _clientPeer = null;
                    return true;
                }

                return false;
            }

            if (_serverPeers.TryGetValue(connectionId, out PeerState? peer))
            {
                ClosePeer_NoLock(peer, ConnectionEventKind.Disconnected, "server_disconnect");
                _serverPeers.Remove(connectionId);
                return true;
            }

            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();

        while (_outboundQueue.TryDequeue(out OwnedOutbound? outbound))
        {
            if (outbound.ReturnToPool)
                ArrayPool<byte>.Shared.Return(outbound.Buffer);
            ReturnOutboundItem(outbound);
        }

        while (_inboundQueue.TryDequeue(out OwnedInbound? inbound))
        {
            Interlocked.Decrement(ref _inboundQueueDepth);
            ArrayPool<byte>.Shared.Return(inbound.Buffer);
            ReturnInboundItem(inbound);
        }

        DisposeFlushBatchStorage();
        DisposeUdpSendScratch();
        CompleteInboundConsume();
        Interlocked.Exchange(ref _inboundQueueDepth, 0);
    }

    private void StartServer()
    {
        try
        {
            if (_enableUdp)
            {
                _serverUdp = new UdpClient(_udpPort);
                _serverUdpReceiveTask = Task.Run(ServerUdpReceiveLoopAsync);
            }

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            _acceptTask = Task.Run(AcceptLoopAsync);
        }
        catch (Exception ex)
        {
            _connectionEvents.Enqueue(new ConnectionEvent(
                ConnectionEventKind.ListenFailed,
                0,
                ex.Message));
            _running = false;
        }
    }

    private void StartClient()
    {
        if (_enableUdp)
        {
            _clientUdp = new UdpClient(0);
            _clientServerUdpEndpoint = new IPEndPoint(ResolveHostAddress(_host), _udpPort);
            _clientUdpReceiveTask = Task.Run(ClientUdpReceiveLoopAsync);
        }

        _clientConnectTask = Task.Run(ClientConnectLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (_running && !_disposed)
            {
                TcpClient? client = null;
                try
                {
                    TcpListener? listener = _listener;
                    if (listener == null)
                        return;

                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    client.NoDelay = true;
                }
                catch (SocketException)
                {
                    if (!_running)
                        return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _connectionEvents.Enqueue(new ConnectionEvent(ConnectionEventKind.ListenerRuntimeError, 0, ex.Message));
                    continue;
                }

                if (client == null)
                    continue;

                int connectionId;
                PeerState peer;
                lock (_sync)
                {
                    if (_serverPeers.Count >= MaxServerConnections)
                    {
                        client.Close();
                        continue;
                    }

                    connectionId = _nextServerConnectionId++;
                    peer = new PeerState(connectionId, client, _maxDatagramSize);
                    if (_enableUdp)
                    {
                        peer.UdpToken = NextUdpToken_NoLock();
                        _udpPendingTokens[peer.UdpToken] = connectionId;
                    }
                    _serverPeers[connectionId] = peer;
                }

                string remoteIp = (client.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? string.Empty;
                _connectionEvents.Enqueue(new ConnectionEvent(ConnectionEventKind.Connected, connectionId, remoteIp));
                StartReceiveTask(peer);
                TrySendTcpUdpOffer(peer);
            }
        }
        catch (Exception ex) when (!_disposed)
        {
            _connectionEvents.Enqueue(new ConnectionEvent(ConnectionEventKind.ListenerRuntimeError, 0, $"Accept loop fatal: {ex.GetType().Name}: {ex.Message}"));
        }
    }

    private async Task ClientConnectLoopAsync()
    {
        TcpClient? client = null;
        try
        {
            client = new TcpClient
            {
                NoDelay = true
            };
            await client.ConnectAsync(_host, _port).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            client?.Dispose();
            _connectionEvents.Enqueue(new ConnectionEvent(ConnectionEventKind.ConnectFailed, _clientConnectionId, ex.Message));
            _running = false;
            return;
        }

        PeerState peer = new PeerState(_clientConnectionId, client!, _maxDatagramSize);
        lock (_sync)
            _clientPeer = peer;

        _connectionEvents.Enqueue(new ConnectionEvent(ConnectionEventKind.Connected, _clientConnectionId, "connected"));
        StartReceiveTask(peer);
    }

    private void StartReceiveTask(PeerState peer)
    {
        peer.ReceiveTask = Task.Run(() => ReceiveLoopAsync(peer));
    }

    private async Task ReceiveLoopAsync(PeerState peer)
    {
        try
        {
            while (_running && !peer.Closed)
            {
                int read = await peer.Stream.ReadAsync(peer.ReceiveScratch, 0, peer.ReceiveScratch.Length).ConfigureAwait(false);
                if (read <= 0)
                    break;

                peer.Reader.Append(peer.ReceiveScratch.AsSpan(0, read));
                while (peer.Reader.TryPeekFrame(out FrameSegments frame, out int frameLength))
                {
                    if (frameLength <= 0)
                    {
                        peer.Reader.ConsumeFrame(frameLength);
                        continue;
                    }

                    // Parse directly from frame segments to avoid the extra whole-frame copy.
                    try
                    {
                        if (!frame.TryReadByte(0, out byte channelByte))
                            continue;

                        if (channelByte == InternalControlChannel)
                        {
                            int controlLength = frameLength - 1;
                            if (controlLength > 0)
                            {
                                byte[] controlPayload = ArrayPool<byte>.Shared.Rent(controlLength);
                                try
                                {
                                    frame.CopyTo(controlPayload.AsSpan(0, controlLength), sourceOffset: 1);
                                    HandleTcpControlFrame(peer, controlPayload.AsSpan(0, controlLength));
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(controlPayload);
                                }
                            }
                            else
                            {
                                HandleTcpControlFrame(peer, ReadOnlySpan<byte>.Empty);
                            }
                            continue;
                        }

                        ChannelType channel = (ChannelType)channelByte;
                        int payloadLength = frameLength - 1;
                        if (payloadLength > 0)
                        {
                            byte[] payload = ArrayPool<byte>.Shared.Rent(payloadLength);
                            frame.CopyTo(payload.AsSpan(0, payloadLength), sourceOffset: 1);
                            OwnedInbound inbound = RentInboundItem();
                            inbound.ConnectionId = peer.ConnectionId;
                            inbound.Channel = channel;
                            inbound.Buffer = payload;
                            inbound.Length = payloadLength;
                            inbound.EnqueuedAtTicks = Stopwatch.GetTimestamp();
                            inbound.IsPingProbe = IsPingPayload(payload.AsSpan(0, payloadLength));
                            _inboundQueue.Enqueue(inbound);
                            OnInboundQueued();
                        }
                    }
                    finally
                    {
                        peer.Reader.ConsumeFrame(frameLength);
                    }
                }
            }
        }
        catch
        {
            // Transport-level disconnect; surfaced below.
        }
        finally
        {
            lock (_sync)
            {
                if (_role == TransportRole.Client)
                {
                    if (_clientPeer == peer)
                    {
                        ClosePeer_NoLock(peer, ConnectionEventKind.Disconnected, "remote_closed");
                        _clientPeer = null;
                    }
                }
                else if (_serverPeers.TryGetValue(peer.ConnectionId, out PeerState? existing) && existing == peer)
                {
                    ClosePeer_NoLock(peer, ConnectionEventKind.Disconnected, "remote_closed");
                    _serverPeers.Remove(peer.ConnectionId);
                }
            }
        }
    }

    private void SendToPeer(PeerState peer, ChannelType channel, ReadOnlySpan<byte> payload)
    {
        int frameLength = payload.Length + 1;
        if (frameLength <= 0 || frameLength > ushort.MaxValue)
            return;

        int packetLength = frameLength + 2;
        byte[] packet = ArrayPool<byte>.Shared.Rent(packetLength);
        try
        {
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), (ushort)frameLength);
            packet[2] = (byte)channel;
            payload.CopyTo(packet.AsSpan(3, payload.Length));
            lock (peer.SendSync)
            {
                if (peer.Closed)
                    return;
                long start = Stopwatch.GetTimestamp();
                peer.Stream.Write(packet, 0, packetLength);
                RecordTcpWriteElapsed(Stopwatch.GetTimestamp() - start, TcpWriteKind.Single);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packet);
        }
    }

    private void SendBatchToPeer(PeerState peer, byte[] buffer, int length)
    {
        if (length <= 0)
            return;
        lock (peer.SendSync)
        {
            if (peer.Closed)
                return;
            long start = Stopwatch.GetTimestamp();
            try
            {
                peer.Stream.Write(buffer, 0, length);
                RecordTcpWriteElapsed(Stopwatch.GetTimestamp() - start, TcpWriteKind.Batch);
            }
            catch (Exception ex) when (IsTransportDisconnectDuringSend(ex))
            {
                HandleSendFailure(peer);
            }
        }
    }

    private void FlushOutboundClientBatched()
    {
        PeerState? peer = _clientPeer;
        if (peer == null || peer.Closed)
        {
            while (_outboundQueue.TryDequeue(out OwnedOutbound? drop))
            {
                if (drop.ReturnToPool)
                    ArrayPool<byte>.Shared.Return(drop.Buffer);
                ReturnOutboundItem(drop);
            }
            return;
        }

        OutboundBatch batch = RentFlushBatch();
        OutboundBatch udpBatch = RentFlushBatch();
        int frameTcp = 0;
        int frameUdp = 0;
        int batchTcp = 0;
        int batchUdp = 0;
        int framedBytesTcp = 0;
        int framedBytesUdp = 0;
        Span<int> channelFramesTcp = stackalloc int[4];
        Span<int> channelFramesUdp = stackalloc int[4];
        Span<long> channelBytesTcp = stackalloc long[4];
        Span<long> channelBytesUdp = stackalloc long[4];
        try
        {
            while (_outboundQueue.TryDequeue(out OwnedOutbound? owned))
            {
                try
                {
                    ReadOnlySpan<byte> payload = owned.Buffer.AsSpan(owned.Offset, owned.Length);
                    if (CanSendClientUdp(owned.Channel))
                    {
                        AppendFramedOutbound(
                            udpBatch,
                            owned.Channel,
                            payload,
                            owned.HasUInt16Patch,
                            owned.UInt16PatchOffset,
                            owned.UInt16PatchValue);
                        frameUdp++;
                        AccumulateChannelStats(channelFramesUdp, channelBytesUdp, owned.Channel, payload.Length + 3);
                    }
                    else
                    {
                        AppendFramedOutbound(
                            batch,
                            owned.Channel,
                            payload,
                            owned.HasUInt16Patch,
                            owned.UInt16PatchOffset,
                            owned.UInt16PatchValue);
                        frameTcp++;
                        AccumulateChannelStats(channelFramesTcp, channelBytesTcp, owned.Channel, payload.Length + 3);
                    }
                }
                finally
                {
                    if (owned.ReturnToPool)
                        ArrayPool<byte>.Shared.Return(owned.Buffer);
                    ReturnOutboundItem(owned);
                }
            }

            if (udpBatch.Length > 0)
            {
                TrySendClientUdpBatch(udpBatch.Buffer, udpBatch.Length);
                batchUdp++;
                framedBytesUdp += udpBatch.Length;
            }
            if (batch.Length > 0)
            {
                SendBatchToPeer(peer, batch.Buffer, batch.Length);
                batchTcp++;
                framedBytesTcp += batch.Length;
            }
        }
        finally
        {
            RecordFlushStats(
                frameTcp,
                frameUdp,
                batchTcp,
                batchUdp,
                framedBytesTcp,
                framedBytesUdp,
                channelFramesTcp,
                channelFramesUdp,
                channelBytesTcp,
                channelBytesUdp);
            ReturnFlushBatch(udpBatch);
            ReturnFlushBatch(batch);
        }
    }

    // Phase 1+2: drain outbound queue into _buildSet, snapshot peer state under lock.
    // Then wait for the previous background send, swap sets, and fire Phase 3 on a Task.
    private void FlushOutboundServerBatched()
    {
        FlushSendSet buildSet = _buildSet;
        int lastTcpConnectionId = int.MinValue;
        int lastUdpConnectionId = int.MinValue;
        OutboundBatch? lastTcpBatch = null;
        OutboundBatch? lastUdpBatch = null;

        // Phase 1: drain outbound queue into per-connection batch buffers
        while (_outboundQueue.TryDequeue(out OwnedOutbound? owned))
        {
            try
            {
                if (owned.IsPongProbe && owned.EnqueuedAtTicks != 0)
                    RecordPongQueueAge(Stopwatch.GetTimestamp() - owned.EnqueuedAtTicks);

                ReadOnlySpan<byte> payload = owned.Buffer.AsSpan(owned.Offset, owned.Length);
                if (owned.ConnectionIdCount > 0 && owned.ConnectionIds != null)
                {
                    ReadOnlySpan<int> multicastIds = owned.ConnectionIds.AsSpan(0, owned.ConnectionIdCount);
                    if (IsUdpChannel(owned.Channel))
                    {
                        for (int j = 0; j < multicastIds.Length; j++)
                        {
                            int connectionId = multicastIds[j];
                            OutboundBatch connectionBatch;
                            if (lastUdpBatch != null && connectionId == lastUdpConnectionId)
                            {
                                connectionBatch = lastUdpBatch;
                            }
                            else
                            {
                                connectionBatch = GetOrCreateUdpFlushBatch(connectionId);
                                lastUdpConnectionId = connectionId;
                                lastUdpBatch = connectionBatch;
                            }
                            AppendFramedOutbound(connectionBatch, owned.Channel, payload,
                                owned.HasUInt16Patch, owned.UInt16PatchOffset, owned.UInt16PatchValue);
                            buildSet.FrameUdp++;
                            AccumulateChannelStats(buildSet.ChannelFramesUdp, buildSet.ChannelBytesUdp, owned.Channel, payload.Length + 3);
                        }
                    }
                    else
                    {
                        for (int j = 0; j < multicastIds.Length; j++)
                        {
                            int connectionId = multicastIds[j];
                            OutboundBatch connectionBatch;
                            if (lastTcpBatch != null && connectionId == lastTcpConnectionId)
                            {
                                connectionBatch = lastTcpBatch;
                            }
                            else
                            {
                                connectionBatch = GetOrCreateFlushBatch(connectionId);
                                lastTcpConnectionId = connectionId;
                                lastTcpBatch = connectionBatch;
                            }
                            AppendFramedOutbound(connectionBatch, owned.Channel, payload,
                                owned.HasUInt16Patch, owned.UInt16PatchOffset, owned.UInt16PatchValue);
                            buildSet.FrameTcp++;
                            AccumulateChannelStats(buildSet.ChannelFramesTcp, buildSet.ChannelBytesTcp, owned.Channel, payload.Length + 3);
                        }
                    }
                    continue;
                }

                if (IsUdpChannel(owned.Channel))
                {
                    OutboundBatch connectionBatch;
                    if (lastUdpBatch != null && owned.ConnectionId == lastUdpConnectionId)
                    {
                        connectionBatch = lastUdpBatch;
                    }
                    else
                    {
                        connectionBatch = GetOrCreateUdpFlushBatch(owned.ConnectionId);
                        lastUdpConnectionId = owned.ConnectionId;
                        lastUdpBatch = connectionBatch;
                    }
                    AppendFramedOutbound(connectionBatch, owned.Channel, payload,
                        owned.HasUInt16Patch, owned.UInt16PatchOffset, owned.UInt16PatchValue);
                    buildSet.FrameUdp++;
                    AccumulateChannelStats(buildSet.ChannelFramesUdp, buildSet.ChannelBytesUdp, owned.Channel, payload.Length + 3);
                }
                else
                {
                    OutboundBatch connectionBatch;
                    if (lastTcpBatch != null && owned.ConnectionId == lastTcpConnectionId)
                    {
                        connectionBatch = lastTcpBatch;
                    }
                    else
                    {
                        connectionBatch = GetOrCreateFlushBatch(owned.ConnectionId);
                        lastTcpConnectionId = owned.ConnectionId;
                        lastTcpBatch = connectionBatch;
                    }
                    AppendFramedOutbound(connectionBatch, owned.Channel, payload,
                        owned.HasUInt16Patch, owned.UInt16PatchOffset, owned.UInt16PatchValue);
                    buildSet.FrameTcp++;
                    AccumulateChannelStats(buildSet.ChannelFramesTcp, buildSet.ChannelBytesTcp, owned.Channel, payload.Length + 3);
                }
            }
            finally
            {
                if (owned.ReturnToPool)
                    ArrayPool<byte>.Shared.Return(owned.Buffer);
                ReturnOutboundItem(owned);
            }
        }

        // Phase 2: snapshot peer state under lock (only if there is work to send)
        if (buildSet.TcpItems.Count > 0 || buildSet.UdpItems.Count > 0)
        {
            lock (_sync)
            {
                buildSet.ServerUdp = _serverUdp;

                buildSet.TcpPeerScratch.Clear();
                for (int i = 0; i < buildSet.TcpItems.Count; i++)
                {
                    int connectionId = buildSet.TcpConnectionIds[i];
                    _serverPeers.TryGetValue(connectionId, out PeerState? peer);
                    buildSet.TcpPeerScratch.Add(peer);
                }

                buildSet.UdpPeerScratch.Clear();
                for (int i = 0; i < buildSet.UdpItems.Count; i++)
                {
                    int connectionId = buildSet.UdpConnectionIds[i];
                    PeerState? peer = null;
                    IPEndPoint? endpoint = null;
                    long token = 0;
                    bool hasUdpRoute = false;
                    if (_serverPeers.TryGetValue(connectionId, out PeerState? peerForUdp))
                    {
                        peer = peerForUdp;
                        if (buildSet.ServerUdp != null &&
                            _udpEndpointByConnection.TryGetValue(connectionId, out IPEndPoint? endpointForUdp))
                        {
                            endpoint = endpointForUdp;
                            token = peerForUdp.UdpToken;
                            hasUdpRoute = true;
                        }
                    }
                    buildSet.UdpPeerScratch.Add(new ServerPeerFlushSnapshot(peer, endpoint, token, hasUdpRoute));
                }
            }
        }

        // Wait for the previous tick's background send to complete (normally already done),
        // then swap build/send sets and fire Phase 3 in the background.
        _pendingFlushTask.GetAwaiter().GetResult();

        // Swap: the just-built set goes to background; the now-free set becomes the new build set.
        FlushSendSet sendSet = buildSet;
        _buildSet = _sendSet;   // _sendSet is now free: previous task completed above and called Reset()
        _sendSet = sendSet;

        if (sendSet.TcpItems.Count > 0 || sendSet.UdpItems.Count > 0)
        {
            _pendingFlushTask = Task.Run(() => ExecuteAsyncSend(sendSet));
        }
        else
        {
            sendSet.Reset();
            _pendingFlushTask = Task.CompletedTask;
        }
    }

    // Phase 3: send batches to peers (runs on a background thread).
    // Calls set.Reset() when done so the set is clean for reuse as _buildSet next tick.
    private void ExecuteAsyncSend(FlushSendSet set)
    {
        try
        {
            if (set.TcpItems.Count > 0)
            {
                for (int i = 0; i < set.TcpItems.Count; i++)
                {
                    OutboundBatch batch = set.TcpItems[i];
                    if (batch.Length <= 0)
                        continue;
                    PeerState? peer = set.TcpPeerScratch[i];
                    if (peer != null && !peer.Closed)
                    {
                        SendBatchToPeer(peer, batch.Buffer, batch.Length);
                        set.BatchTcp++;
                        set.FramedBytesTcp += batch.Length;
                    }
                }
            }

            if (set.UdpItems.Count > 0)
            {
                for (int i = 0; i < set.UdpItems.Count; i++)
                {
                    int connectionId = set.UdpConnectionIds[i];
                    OutboundBatch udpBatch = set.UdpItems[i];
                    if (udpBatch.Length <= 0)
                        continue;

                    ServerPeerFlushSnapshot snapshot = set.UdpPeerScratch[i];
                    PeerState? peer = snapshot.Peer;

                    bool sentViaUdp;
                    bool hadUdpRoute = set.ServerUdp != null && snapshot.HasUdpRoute && snapshot.UdpEndpoint != null;
                    if (set.ServerUdp != null && snapshot.HasUdpRoute && snapshot.UdpEndpoint != null)
                    {
                        sentViaUdp = TrySendUdpFramedBatchDatagrams(
                            set.ServerUdp,
                            snapshot.UdpEndpoint,
                            snapshot.UdpToken,
                            udpBatch.Buffer.AsSpan(0, udpBatch.Length));
                    }
                    else
                    {
                        sentViaUdp = TrySendServerUdpBatch(connectionId, udpBatch.Buffer, udpBatch.Length);
                    }

                    if (sentViaUdp)
                    {
                        set.BatchUdp++;
                        set.FramedBytesUdp += udpBatch.Length;
                        continue;
                    }

                    if (peer != null && !peer.Closed)
                    {
                        set.UdpFallbackTcpBatches++;
                        if (!hadUdpRoute)
                            set.UdpFallbackNoRouteBatches++;
                        else
                            set.UdpFallbackSendFailBatches++;
                        SendBatchToPeer(peer, udpBatch.Buffer, udpBatch.Length);
                        set.BatchTcp++;
                        set.FramedBytesTcp += udpBatch.Length;
                    }
                }
            }
        }
        finally
        {
            RecordFlushStats(
                set.FrameTcp,
                set.FrameUdp,
                set.BatchTcp,
                set.BatchUdp,
                set.FramedBytesTcp,
                set.FramedBytesUdp,
                set.ChannelFramesTcp,
                set.ChannelFramesUdp,
                set.ChannelBytesTcp,
                set.ChannelBytesUdp);
            if (set.UdpFallbackTcpBatches > 0)
                Interlocked.Add(ref _serverUdpFallbackTcpBatches, set.UdpFallbackTcpBatches);
            if (set.UdpFallbackNoRouteBatches > 0)
                Interlocked.Add(ref _serverUdpFallbackNoRouteBatches, set.UdpFallbackNoRouteBatches);
            if (set.UdpFallbackSendFailBatches > 0)
                Interlocked.Add(ref _serverUdpFallbackSendFailBatches, set.UdpFallbackSendFailBatches);
            set.Reset();
        }
    }

    private OutboundBatch GetOrCreateFlushBatch(int connectionId)
    {
        if (_buildSet.TcpBatchesByConnection.TryGetValue(connectionId, out OutboundBatch? existing))
            return existing;

        OutboundBatch created = _buildSet.RentBatch();
        _buildSet.TcpBatchesByConnection.Add(connectionId, created);
        _buildSet.TcpConnectionIds.Add(connectionId);
        _buildSet.TcpItems.Add(created);
        return created;
    }

    private OutboundBatch GetOrCreateUdpFlushBatch(int connectionId)
    {
        if (_buildSet.UdpBatchesByConnection.TryGetValue(connectionId, out OutboundBatch? existing))
            return existing;

        OutboundBatch created = _buildSet.RentBatch();
        _buildSet.UdpBatchesByConnection.Add(connectionId, created);
        _buildSet.UdpConnectionIds.Add(connectionId);
        _buildSet.UdpItems.Add(created);
        return created;
    }

    // Used only by the client flush path.
    private OutboundBatch RentFlushBatch()
    {
        OutboundBatch batch = _flushBatchPool.Count > 0 ? _flushBatchPool.Pop() : new OutboundBatch();
        batch.Clear();
        if (batch.Buffer.Length == 0)
            batch.Buffer = ArrayPool<byte>.Shared.Rent(4096);
        return batch;
    }

    // Used only by the client flush path.
    private void ReturnFlushBatch(OutboundBatch batch)
    {
        batch.Clear();
        _flushBatchPool.Push(batch);
    }

    private void DisposeFlushBatchStorage()
    {
        // Wait for any in-flight async send before tearing down the sets.
        try { _pendingFlushTask.GetAwaiter().GetResult(); } catch { }
        _setA.DisposeBatchStorage();
        _setB.DisposeBatchStorage();
        while (_flushBatchPool.Count > 0)
        {
            OutboundBatch batch = _flushBatchPool.Pop();
            if (batch.Buffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(batch.Buffer);
                batch.Buffer = Array.Empty<byte>();
            }
            batch.Length = 0;
        }
    }

    private void DisposeUdpSendScratch()
    {
        if (_udpSendScratch.Length == 0)
            return;

        ArrayPool<byte>.Shared.Return(_udpSendScratch);
        _udpSendScratch = Array.Empty<byte>();
    }

    private byte[] EnsureUdpSendScratchCapacity(int requiredBytes)
    {
        if (requiredBytes <= _udpSendScratch.Length)
            return _udpSendScratch;

        byte[] next = ArrayPool<byte>.Shared.Rent(requiredBytes);
        if (_udpSendScratch.Length > 0)
            ArrayPool<byte>.Shared.Return(_udpSendScratch);
        _udpSendScratch = next;
        return next;
    }

    private static void AppendFramedOutbound(
        OutboundBatch batch,
        ChannelType channel,
        ReadOnlySpan<byte> payload,
        bool patchUInt16,
        int patchOffset,
        ushort patchValue)
    {
        int frameLength = payload.Length + 1;
        if (frameLength <= 0 || frameLength > ushort.MaxValue)
            return;

        int writeBytes = 2 + frameLength;
        EnsureBatchCapacity(batch, writeBytes);

        Span<byte> destination = batch.Buffer.AsSpan(batch.Length, writeBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), (ushort)frameLength);
        destination[2] = (byte)channel;
        payload.CopyTo(destination.Slice(3));
        if (patchUInt16 && (uint)patchOffset <= (uint)(payload.Length - sizeof(ushort)))
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(3 + patchOffset, sizeof(ushort)), patchValue);
        batch.Length += writeBytes;
    }

    private void RecordFlushStats(
        int frameTcp,
        int frameUdp,
        int batchTcp,
        int batchUdp,
        int framedBytesTcp,
        int framedBytesUdp,
        ReadOnlySpan<int> channelFramesTcp,
        ReadOnlySpan<int> channelFramesUdp,
        ReadOnlySpan<long> channelBytesTcp,
        ReadOnlySpan<long> channelBytesUdp)
    {
        if (frameTcp > 0)
            Interlocked.Add(ref _txFrameTcp, frameTcp);
        if (frameUdp > 0)
            Interlocked.Add(ref _txFrameUdp, frameUdp);
        if (batchTcp > 0)
            Interlocked.Add(ref _txBatchTcp, batchTcp);
        if (batchUdp > 0)
            Interlocked.Add(ref _txBatchUdp, batchUdp);
        if (framedBytesTcp > 0)
            Interlocked.Add(ref _txFramedBytesTcp, framedBytesTcp);
        if (framedBytesUdp > 0)
            Interlocked.Add(ref _txFramedBytesUdp, framedBytesUdp);
        for (int i = 0; i < 4; i++)
        {
            if (channelFramesTcp[i] > 0)
                Interlocked.Add(ref _txChannelFramesTcp[i], channelFramesTcp[i]);
            if (channelFramesUdp[i] > 0)
                Interlocked.Add(ref _txChannelFramesUdp[i], channelFramesUdp[i]);
            if (channelBytesTcp[i] > 0)
                Interlocked.Add(ref _txChannelFramedBytesTcp[i], channelBytesTcp[i]);
            if (channelBytesUdp[i] > 0)
                Interlocked.Add(ref _txChannelFramedBytesUdp[i], channelBytesUdp[i]);
        }
    }

    private static void AccumulateChannelStats(
        Span<int> frameCounts,
        Span<long> framedBytes,
        ChannelType channel,
        int frameBytes)
    {
        int channelIndex = (int)channel;
        if ((uint)channelIndex >= 4u)
            return;

        frameCounts[channelIndex]++;
        framedBytes[channelIndex] += frameBytes;
    }

    private enum TcpWriteKind
    {
        Single = 0,
        Batch = 1,
        Control = 2,
    }

    private void RecordTcpWriteElapsed(long elapsedTicks, TcpWriteKind kind)
    {
        Interlocked.Increment(ref _tcpWriteCalls);
        Interlocked.Add(ref _tcpWriteElapsedTicksTotal, elapsedTicks);
        UpdateElapsedMax(ref _tcpWriteElapsedTicksMax, elapsedTicks);

        switch (kind)
        {
            case TcpWriteKind.Single:
                Interlocked.Increment(ref _tcpSingleWriteCalls);
                Interlocked.Add(ref _tcpSingleWriteElapsedTicksTotal, elapsedTicks);
                UpdateElapsedMax(ref _tcpSingleWriteElapsedTicksMax, elapsedTicks);
                break;
            case TcpWriteKind.Batch:
                Interlocked.Increment(ref _tcpBatchWriteCalls);
                Interlocked.Add(ref _tcpBatchWriteElapsedTicksTotal, elapsedTicks);
                UpdateElapsedMax(ref _tcpBatchWriteElapsedTicksMax, elapsedTicks);
                break;
            case TcpWriteKind.Control:
                Interlocked.Increment(ref _tcpControlWriteCalls);
                Interlocked.Add(ref _tcpControlWriteElapsedTicksTotal, elapsedTicks);
                UpdateElapsedMax(ref _tcpControlWriteElapsedTicksMax, elapsedTicks);
                break;
        }
    }

    private static void UpdateElapsedMax(ref long location, long elapsedTicks)
    {
        long currentMax = Volatile.Read(ref location);
        while (elapsedTicks > currentMax)
        {
            long previous = Interlocked.CompareExchange(ref location, elapsedTicks, currentMax);
            if (previous == currentMax)
                break;
            currentMax = previous;
        }
    }

    private static void EnsureBatchCapacity(OutboundBatch batch, int appendBytes)
    {
        int required = batch.Length + appendBytes;
        if (required <= batch.Buffer.Length)
            return;

        int newSize = batch.Buffer.Length <= 0 ? 4096 : batch.Buffer.Length;
        while (newSize < required)
            newSize <<= 1;

        byte[] next = ArrayPool<byte>.Shared.Rent(newSize);
        if (batch.Length > 0)
            batch.Buffer.AsSpan(0, batch.Length).CopyTo(next.AsSpan(0, batch.Length));
        if (batch.Buffer.Length > 0)
            ArrayPool<byte>.Shared.Return(batch.Buffer);
        batch.Buffer = next;
    }

    private bool CanSendClientUdp(ChannelType channel)
    {
        if (!_enableUdp || !IsUdpChannel(channel))
            return false;

        return _clientUdp != null && _clientServerUdpEndpoint != null && _clientUdpAssociated;
    }

    private bool CanSendServerUdp(int connectionId, ChannelType channel)
    {
        if (!_enableUdp || !IsUdpChannel(channel))
            return false;

        lock (_sync)
            return _serverUdp != null && _udpEndpointByConnection.ContainsKey(connectionId);
    }

    private bool TrySendClientUdpBatch(byte[] framedBuffer, int framedLength)
    {
        UdpClient? udp = _clientUdp;
        IPEndPoint? endpoint = _clientServerUdpEndpoint;
        if (udp == null || endpoint == null || !_clientUdpAssociated || framedLength <= 0)
            return false;

        long token = Interlocked.Read(ref _clientPendingUdpToken);
        return TrySendUdpFramedBatchDatagrams(udp, endpoint, token, framedBuffer.AsSpan(0, framedLength));
    }

    private bool TrySendServerUdpBatch(int connectionId, byte[] framedBuffer, int framedLength)
    {
        if (framedLength <= 0)
            return false;

        UdpClient? udp = _serverUdp;
        IPEndPoint? endpoint;
        long token = 0;
        lock (_sync)
        {
            if (udp == null || !_udpEndpointByConnection.TryGetValue(connectionId, out endpoint))
                return false;
            if (_serverPeers.TryGetValue(connectionId, out PeerState? peer))
                token = peer.UdpToken;
        }

        return TrySendUdpFramedBatchDatagrams(udp!, endpoint!, token, framedBuffer.AsSpan(0, framedLength));
    }

    private bool TrySendUdpDatagram(UdpClient udp, IPEndPoint endpoint, long token, ChannelType channel, ReadOnlySpan<byte> payload)
    {
        bool tokenized = token != 0;
        int headerLen = tokenized ? (1 + sizeof(long) + 1) : 2;
        int len = headerLen + payload.Length;
        byte[] buffer = _enableUdpSendScratchReuse
            ? EnsureUdpSendScratchCapacity(len)
            : ArrayPool<byte>.Shared.Rent(len);
        try
        {
            if (tokenized)
            {
                buffer[0] = UdpWireDataToken;
                BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(1, sizeof(long)), token);
                buffer[1 + sizeof(long)] = (byte)channel;
                payload.CopyTo(buffer.AsSpan(1 + sizeof(long) + 1, payload.Length));
            }
            else
            {
                buffer[0] = UdpWireData;
                buffer[1] = (byte)channel;
                payload.CopyTo(buffer.AsSpan(2, payload.Length));
            }
            udp.Send(buffer, len, endpoint);
            Interlocked.Increment(ref _udpTxDatagrams);
            Interlocked.Add(ref _udpTxBytes, len);
            return true;
        }
        catch
        {
            Interlocked.Increment(ref _udpTxFailures);
            return false;
        }
        finally
        {
            if (!_enableUdpSendScratchReuse)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private bool TrySendUdpFramedBatchDatagrams(UdpClient udp, IPEndPoint endpoint, long token, ReadOnlySpan<byte> framed)
    {
        if (framed.Length <= 0)
            return true;

        int offset = 0;
        bool allSent = true;
        while (offset < framed.Length)
        {
            bool tokenized = token != 0;
            int datagramHeaderBytes = tokenized ? (1 + sizeof(long)) : 1;
            int datagramPayloadBudget = MaxUdpBatchDatagramBytes - datagramHeaderBytes;
            if (datagramPayloadBudget <= 0)
                return false;

            int chunkStart = offset;
            int chunkBytes = 0;
            while (offset < framed.Length)
            {
                if (offset + 2 > framed.Length)
                    return false;
                int frameLen = BinaryPrimitives.ReadUInt16LittleEndian(framed.Slice(offset, 2));
                if (frameLen <= 0 || frameLen > 0xFFFF)
                    return false;
                int frameBytes = 2 + frameLen;
                if (offset + frameBytes > framed.Length)
                    return false;

                if (chunkBytes > 0 && chunkBytes + frameBytes > datagramPayloadBudget)
                    break;

                if (frameBytes > datagramPayloadBudget)
                {
                    // Single oversized frame; fall back to legacy single-frame UDP datagram.
                    ChannelType channel = (ChannelType)framed[offset + 2];
                    ReadOnlySpan<byte> payload = framed.Slice(offset + 3, frameLen - 1);
                    if (!TrySendUdpDatagram(udp, endpoint, token, channel, payload))
                        allSent = false;
                    offset += frameBytes;
                    chunkStart = offset;
                    chunkBytes = 0;
                    break;
                }

                chunkBytes += frameBytes;
                offset += frameBytes;
            }

            if (chunkBytes <= 0)
                continue;

            int datagramLen = datagramHeaderBytes + chunkBytes;
            byte[] datagram = _enableUdpSendScratchReuse
                ? EnsureUdpSendScratchCapacity(datagramLen)
                : ArrayPool<byte>.Shared.Rent(datagramLen);
            try
            {
                if (tokenized)
                {
                    datagram[0] = UdpWireBatchDataToken;
                    BinaryPrimitives.WriteInt64LittleEndian(datagram.AsSpan(1, sizeof(long)), token);
                    framed.Slice(chunkStart, chunkBytes).CopyTo(datagram.AsSpan(1 + sizeof(long), chunkBytes));
                }
                else
                {
                    datagram[0] = UdpWireBatchData;
                    framed.Slice(chunkStart, chunkBytes).CopyTo(datagram.AsSpan(1, chunkBytes));
                }
                udp.Send(datagram, datagramLen, endpoint);
                Interlocked.Increment(ref _udpTxDatagrams);
                Interlocked.Add(ref _udpTxBytes, datagramLen);
            }
            catch
            {
                Interlocked.Increment(ref _udpTxFailures);
                allSent = false;
            }
            finally
            {
                if (!_enableUdpSendScratchReuse)
                    ArrayPool<byte>.Shared.Return(datagram);
            }
        }

        return allSent;
    }

    private async Task ServerUdpReceiveLoopAsync()
    {
        while (_running && !_disposed)
        {
            UdpClient? udp = _serverUdp;
            if (udp == null)
                return;

            try
            {
                UdpReceiveResult result = await udp.ReceiveAsync().ConfigureAwait(false);
                ProcessServerUdpDatagram(result.RemoteEndPoint, result.Buffer);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (!_running)
                    return;
                continue;
            }
            catch
            {
                continue;
            }
        }
    }

    private async Task ClientUdpReceiveLoopAsync()
    {
        while (_running && !_disposed)
        {
            UdpClient? udp = _clientUdp;
            if (udp == null)
                return;

            try
            {
                UdpReceiveResult result = await udp.ReceiveAsync().ConfigureAwait(false);
                ProcessClientUdpDatagram(result.RemoteEndPoint, result.Buffer);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (!_running)
                    return;
                continue;
            }
            catch
            {
                continue;
            }
        }
    }

    private void ProcessServerUdpDatagram(IPEndPoint remoteEndPoint, byte[] buffer)
    {
        if (buffer.Length <= 0)
            return;

        byte kind = buffer[0];
        if (kind == UdpWireHello)
        {
            if (buffer.Length < 1 + sizeof(long))
            {
                Interlocked.Increment(ref _udpDropsUnknownToken);
                return;
            }

            long token = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(1, sizeof(long)));
            int connectionId;
            lock (_sync)
            {
                if (_udpConnectionByToken.TryGetValue(token, out connectionId))
                {
                    if (_udpEndpointByConnection.TryGetValue(connectionId, out IPEndPoint? existing))
                        _udpConnectionByEndpoint.Remove(existing);
                    _udpEndpointByConnection[connectionId] = new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port);
                    _udpConnectionByEndpoint[new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port)] = connectionId;
                }
                else if (!_udpPendingTokens.TryGetValue(token, out connectionId))
                {
                    Interlocked.Increment(ref _udpDropsUnknownToken);
                    return;
                }
                else
                {
                    _udpPendingTokens.Remove(token);
                    _udpConnectionByToken[token] = connectionId;
                    if (_udpEndpointByConnection.TryGetValue(connectionId, out IPEndPoint? existing))
                        _udpConnectionByEndpoint.Remove(existing);
                    _udpEndpointByConnection[connectionId] = new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port);
                    _udpConnectionByEndpoint[new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port)] = connectionId;
                }
            }

            Interlocked.Increment(ref _udpAssocHelloRx);
            SendUdpAck(_serverUdp, remoteEndPoint, token);
            return;
        }

        if (kind == UdpWireBatchData)
        {
            if (buffer.Length < 1 + 3)
                return;

            int mappedBatchConnectionId;
            lock (_sync)
            {
                if (!_udpConnectionByEndpoint.TryGetValue(remoteEndPoint, out mappedBatchConnectionId))
                {
                    Interlocked.Increment(ref _udpDropsUnknownEndpoint);
                    return;
                }
            }

            ProcessUdpFramedBatch(mappedBatchConnectionId, buffer.AsSpan(1));
            return;
        }

        if (kind == UdpWireBatchDataToken)
        {
            if (buffer.Length < 1 + sizeof(long) + 3)
                return;

            long token = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(1, sizeof(long)));
            int mappedBatchConnectionId;
            lock (_sync)
            {
                if (!_udpConnectionByToken.TryGetValue(token, out mappedBatchConnectionId))
                {
                    Interlocked.Increment(ref _udpDropsUnknownToken);
                    return;
                }
            }

            ProcessUdpFramedBatch(mappedBatchConnectionId, buffer.AsSpan(1 + sizeof(long)));
            return;
        }

        if (kind == UdpWireDataToken)
        {
            if (buffer.Length < 1 + sizeof(long) + 1)
                return;

            long token = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(1, sizeof(long)));
            int mappedConnectionIdByToken;
            lock (_sync)
            {
                if (!_udpConnectionByToken.TryGetValue(token, out mappedConnectionIdByToken))
                {
                    Interlocked.Increment(ref _udpDropsUnknownToken);
                    return;
                }
            }

            EnqueueUdpInbound(mappedConnectionIdByToken, (ChannelType)buffer[1 + sizeof(long)], buffer.AsSpan(1 + sizeof(long) + 1));
            return;
        }

        if (kind != UdpWireData || buffer.Length < 2)
            return;

        int mappedConnectionId;
        lock (_sync)
        {
            if (!_udpConnectionByEndpoint.TryGetValue(remoteEndPoint, out mappedConnectionId))
            {
                Interlocked.Increment(ref _udpDropsUnknownEndpoint);
                return;
            }
        }

        EnqueueUdpInbound(mappedConnectionId, (ChannelType)buffer[1], buffer.AsSpan(2));
    }

    private void ProcessClientUdpDatagram(IPEndPoint remoteEndPoint, byte[] buffer)
    {
        if (buffer.Length <= 0)
            return;

        if (_clientServerUdpEndpoint != null)
        {
            if (!remoteEndPoint.Address.Equals(_clientServerUdpEndpoint.Address) || remoteEndPoint.Port != _clientServerUdpEndpoint.Port)
                return;
        }

        byte kind = buffer[0];
        if (kind == UdpWireAck)
        {
            if (buffer.Length >= 1 + sizeof(long))
            {
                long token = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(1, sizeof(long)));
                if (token == Interlocked.Read(ref _clientPendingUdpToken) && token != 0)
                {
                    _clientUdpAssociated = true;
                    _clientUdpHelloRetryDueTicks = 0;
                    Interlocked.Increment(ref _udpAssocAckRx);
                }
            }

            return;
        }

        if (kind == UdpWireBatchData)
        {
            if (buffer.Length < 1 + 3)
                return;
            ProcessUdpFramedBatch(_clientConnectionId, buffer.AsSpan(1));
            return;
        }

        if (kind == UdpWireBatchDataToken)
        {
            if (buffer.Length < 1 + sizeof(long) + 3)
                return;
            long token = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(1, sizeof(long)));
            long pendingToken = Interlocked.Read(ref _clientPendingUdpToken);
            if (token == pendingToken || token == 0 || pendingToken == 0)
                ProcessUdpFramedBatch(_clientConnectionId, buffer.AsSpan(1 + sizeof(long)));
            return;
        }

        if (kind == UdpWireDataToken)
        {
            if (buffer.Length < 1 + sizeof(long) + 1)
                return;
            long token = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(1, sizeof(long)));
            long pendingToken = Interlocked.Read(ref _clientPendingUdpToken);
            if (token == pendingToken || token == 0 || pendingToken == 0)
                EnqueueUdpInbound(_clientConnectionId, (ChannelType)buffer[1 + sizeof(long)], buffer.AsSpan(1 + sizeof(long) + 1));
            return;
        }

        if (kind != UdpWireData || buffer.Length < 2)
            return;

        EnqueueUdpInbound(_clientConnectionId, (ChannelType)buffer[1], buffer.AsSpan(2));
    }

    private void ProcessUdpFramedBatch(int connectionId, ReadOnlySpan<byte> framed)
    {
        int offset = 0;
        while (offset + 2 <= framed.Length)
        {
            int frameLen = BinaryPrimitives.ReadUInt16LittleEndian(framed.Slice(offset, 2));
            if (frameLen <= 0 || offset + 2 + frameLen > framed.Length)
                return;

            ChannelType channel = (ChannelType)framed[offset + 2];
            ReadOnlySpan<byte> payload = framed.Slice(offset + 3, frameLen - 1);
            EnqueueUdpInbound(connectionId, channel, payload);
            offset += 2 + frameLen;
        }
    }

    private void EnqueueUdpInbound(int connectionId, ChannelType channel, ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= 0)
            return;

        byte[] copied = ArrayPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(copied.AsSpan(0, payload.Length));
        OwnedInbound inbound = RentInboundItem();
        inbound.ConnectionId = connectionId;
        inbound.Channel = channel;
        inbound.Buffer = copied;
        inbound.Length = payload.Length;
        inbound.EnqueuedAtTicks = Stopwatch.GetTimestamp();
        inbound.IsPingProbe = IsPingPayload(payload);
        _inboundQueue.Enqueue(inbound);
        OnInboundQueued();

        Interlocked.Increment(ref _udpRxDatagrams);
        Interlocked.Add(ref _udpRxBytes, payload.Length + 2);
    }

    private void SendUdpAck(UdpClient? udp, IPEndPoint endpoint, long token)
    {
        if (udp == null)
            return;

        Span<byte> ack = stackalloc byte[1 + sizeof(long)];
        ack[0] = UdpWireAck;
        BinaryPrimitives.WriteInt64LittleEndian(ack.Slice(1), token);
        try
        {
            udp.Send(ack.ToArray(), ack.Length, endpoint);
            Interlocked.Increment(ref _udpTxDatagrams);
            Interlocked.Add(ref _udpTxBytes, ack.Length);
        }
        catch
        {
            Interlocked.Increment(ref _udpTxFailures);
        }
    }

    private void TrySendTcpUdpOffer(PeerState peer)
    {
        Span<byte> payload = stackalloc byte[1 + sizeof(int) + sizeof(long) + sizeof(ushort)];
        payload[0] = TcpControlUdpOffer;
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(1, sizeof(int)), peer.ConnectionId);
        BinaryPrimitives.WriteInt64LittleEndian(payload.Slice(1 + sizeof(int), sizeof(long)), peer.UdpToken);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(1 + sizeof(int) + sizeof(long), sizeof(ushort)), (ushort)_udpPort);
        SendInternalControlToPeer(peer, payload);
    }

    private void SendInternalControlToPeer(PeerState peer, ReadOnlySpan<byte> controlPayload)
    {
        int frameLength = controlPayload.Length + 1;
        int packetLength = frameLength + 2;
        byte[] packet = ArrayPool<byte>.Shared.Rent(packetLength);
        try
        {
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), (ushort)frameLength);
            packet[2] = InternalControlChannel;
            if (controlPayload.Length > 0)
                controlPayload.CopyTo(packet.AsSpan(3, controlPayload.Length));
            lock (peer.SendSync)
            {
                if (peer.Closed)
                    return;
                long start = Stopwatch.GetTimestamp();
                peer.Stream.Write(packet, 0, packetLength);
                RecordTcpWriteElapsed(Stopwatch.GetTimestamp() - start, TcpWriteKind.Control);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packet);
        }
    }

    private void HandleTcpControlFrame(PeerState peer, ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= 0)
            return;

        if (payload[0] != TcpControlUdpOffer || _role != TransportRole.Client)
            return;

        if (payload.Length < 1 + sizeof(int) + sizeof(long) + sizeof(ushort))
            return;

        _clientServerAssignedConnectionId = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(1, sizeof(int)));
        if (!_enableUdp)
            return;

        Interlocked.Exchange(ref _clientPendingUdpToken, BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(1 + sizeof(int), sizeof(long))));
        int udpPort = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1 + sizeof(int) + sizeof(long), sizeof(ushort)));
        if (_clientServerUdpEndpoint != null && udpPort > 0)
            _clientServerUdpEndpoint = new IPEndPoint(_clientServerUdpEndpoint.Address, udpPort);
        _clientUdpAssociated = false;
        _clientUdpHelloRetryDueTicks = 0;
        SendClientUdpHello();
    }

    private void SendClientUdpHello()
    {
        long pendingToken = Interlocked.Read(ref _clientPendingUdpToken);
        if (!_enableUdp || _clientUdp == null || _clientServerUdpEndpoint == null || pendingToken == 0)
            return;

        Span<byte> hello = stackalloc byte[1 + sizeof(long)];
        hello[0] = UdpWireHello;
        BinaryPrimitives.WriteInt64LittleEndian(hello.Slice(1), pendingToken);
        try
        {
            _clientUdp.Send(hello.ToArray(), hello.Length, _clientServerUdpEndpoint);
            Interlocked.Increment(ref _udpTxDatagrams);
            Interlocked.Add(ref _udpTxBytes, hello.Length);
            _clientUdpHelloRetryDueTicks = Stopwatch.GetTimestamp() + UdpHelloRetryIntervalTicks;
        }
        catch
        {
            Interlocked.Increment(ref _udpTxFailures);
        }
    }

    private void TryClientUdpHelloRetry()
    {
        if (!_enableUdp || _role != TransportRole.Client || _clientUdpAssociated || Interlocked.Read(ref _clientPendingUdpToken) == 0)
            return;
        long due = Volatile.Read(ref _clientUdpHelloRetryDueTicks);
        long now = Stopwatch.GetTimestamp();
        if (due != 0 && now < due)
            return;
        SendClientUdpHello();
    }

    private static bool IsUdpChannel(ChannelType channel) =>
        channel == ChannelType.Unreliable || channel == ChannelType.UnreliableSequenced;

    private int GetUdpAssociatedPeerCount()
    {
        lock (_sync)
        {
            if (_role == TransportRole.Client)
                return _clientUdpAssociated ? 1 : 0;
            return _udpEndpointByConnection.Count;
        }
    }

    private long NextUdpToken_NoLock()
    {
        long token;
        do
        {
            token = Random.Shared.NextInt64(1, long.MaxValue);
        }
        while (_udpPendingTokens.ContainsKey(token));

        return token;
    }

    private static IPAddress ResolveHostAddress(string host)
    {
        if (IPAddress.TryParse(host, out IPAddress? parsed))
            return parsed;

        IPAddress[] addresses = Dns.GetHostAddresses(host);
        for (int i = 0; i < addresses.Length; i++)
        {
            if (addresses[i].AddressFamily == AddressFamily.InterNetwork)
                return addresses[i];
        }

        return addresses.Length > 0 ? addresses[0] : IPAddress.Loopback;
    }

    private void HandleSendFailure(PeerState peer)
    {
        lock (_sync)
        {
            if (_role == TransportRole.Client)
            {
                if (_clientPeer == peer)
                {
                    ClosePeer_NoLock(peer, ConnectionEventKind.Disconnected, "send_failed");
                    _clientPeer = null;
                }

                return;
            }

            if (_serverPeers.TryGetValue(peer.ConnectionId, out PeerState? existing) && existing == peer)
            {
                ClosePeer_NoLock(peer, ConnectionEventKind.Disconnected, "send_failed");
                _serverPeers.Remove(peer.ConnectionId);
            }
        }
    }

    private static bool IsTransportDisconnectDuringSend(Exception ex) =>
        ex is IOException ||
        ex is ObjectDisposedException ||
        ex is SocketException;

    private void ClosePeer_NoLock(PeerState peer, ConnectionEventKind kind, string reason)
    {
        if (peer.Closed)
            return;

        _udpPendingTokens.Remove(peer.UdpToken);
        if (peer.UdpToken != 0)
            _udpConnectionByToken.Remove(peer.UdpToken);
        if (_udpEndpointByConnection.TryGetValue(peer.ConnectionId, out IPEndPoint? udpEndpoint))
        {
            _udpEndpointByConnection.Remove(peer.ConnectionId);
            _udpConnectionByEndpoint.Remove(udpEndpoint);
        }
        if (_role == TransportRole.Client)
        {
            _clientUdpAssociated = false;
            Interlocked.Exchange(ref _clientPendingUdpToken, 0);
            _clientServerAssignedConnectionId = 0;
            _clientUdpHelloRetryDueTicks = 0;
        }

        peer.Dispose();
        _connectionEvents.Enqueue(new ConnectionEvent(kind, peer.ConnectionId, reason));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TcpFramedNetworkTransport));
    }
}
