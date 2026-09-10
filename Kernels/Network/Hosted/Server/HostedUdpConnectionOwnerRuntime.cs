using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;

namespace UltraSim.Network;

/// <summary>Cold H0.5 ownership and transfer evidence for one sharded host.</summary>
public readonly record struct HostedUdpConnectionOwnerRuntimeSnapshot(
    int OwnerCount,
    int ActiveTransports,
    int MaximumIngressTransfersPerPass,
    int PeakIngressTransfersPerPass,
    ulong OwnerPasses,
    ulong ApplicationMessages,
    ulong UnhandledApplicationMessages,
    ulong PathRequests,
    ulong PathChallenges,
    ulong PathProofs,
    ulong PathCommits,
    ulong PathRejections,
    ulong ListenerSecureDatagrams,
    ulong ListenerPathDatagrams,
    ulong ListenerCapacityDrops,
    long OwnerProtocolStopwatchTicks,
    long ReservedBytes,
    HostedUdpOwnerHandoffSnapshot Handoff);

/// <summary>
/// H0.5 composition of the accepted listener-owned packet handoff with fixed
/// connection-owner threads. The listener retains socket/path tables; workers
/// exclusively own secure peers, crypto, replay/lane state, H0.3 authentication,
/// and their attached application adapter.
/// </summary>
internal sealed class HostedUdpConnectionOwnerRuntime : IDisposable
{
    private readonly int _listenerThreadId = Environment.CurrentManagedThreadId;
    private readonly HostedUdpConnectionOwnerHandoff _handoff;
    private readonly OwnerWorker[] _workers;
    private readonly OwnerWorker?[] _workerByOwnerSlot;
    private readonly NetworkProtocolManifest _manifest;
    private readonly NetworkProtocolLimits _protocolLimits;
    private readonly UdpProtocolLimits _udpLimits;
    private readonly HostedUdpAdmissionLimits _admissionLimits;
    private readonly HostedUdpOwnerShardingLimits _ownerLimits;
    private readonly INetworkMonotonicClock _clock;
    private readonly IHostedUdpSessionApplicationFactory _applicationFactory;
    private readonly byte[] _listenerPayloadScratch;
    private readonly byte[] _listenerPathScratch =
        new byte[HostedUdpConnectionOwnerHandoff.PathBindingBytes];
    private int _nextWorker;
    private long _ownerPasses;
    private long _applicationMessages;
    private long _unhandledApplicationMessages;
    private long _pathRequests;
    private long _pathChallenges;
    private long _pathProofs;
    private long _pathCommits;
    private long _pathRejections;
    private long _listenerSecureDatagrams;
    private long _listenerPathDatagrams;
    private long _listenerCapacityDrops;
    private long _ownerProtocolStopwatchTicks;
    private int _peakIngressTransfersPerPass;
    private bool _stopped;
    private bool _joinCleanupComplete;
    private bool _disposed;

    internal HostedUdpConnectionOwnerRuntime(
        HostedUdpOwnerShardingLimits limits,
        NetworkProtocolManifest manifest,
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        HostedUdpAdmissionLimits admissionLimits,
        IHostedUdpSessionApplicationFactory applicationFactory,
        INetworkMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(applicationFactory);
        ArgumentNullException.ThrowIfNull(clock);
        limits.Validate();
        protocolLimits.Validate(new DeterministicDatagramLimits(
            1, udpLimits.MaximumDatagramBytes));
        admissionLimits.Validate(udpLimits);
        if (limits.MaximumConnections < 1 ||
            limits.MaximumDatagramBytes != udpLimits.MaximumDatagramBytes)
            throw new ArgumentOutOfRangeException(nameof(limits));
        _manifest = manifest;
        _protocolLimits = protocolLimits;
        _udpLimits = udpLimits;
        _admissionLimits = admissionLimits;
        _ownerLimits = limits;
        _clock = clock;
        _applicationFactory = applicationFactory;
        _handoff = new HostedUdpConnectionOwnerHandoff(limits);
        _listenerPayloadScratch = new byte[limits.MaximumDatagramBytes];
        _workers = new OwnerWorker[limits.MaximumOwners];
        _workerByOwnerSlot = new OwnerWorker[limits.MaximumOwners];
        for (int index = 0; index < _workers.Length; index++)
        {
            var worker = new OwnerWorker(this, index, limits.MaximumConnections);
            _workers[index] = worker;
            worker.Start();
        }
        try
        {
            for (int index = 0; index < _workers.Length; index++)
            {
                OwnerWorker worker = _workers[index];
                if (!worker.WaitStarted(TimeSpan.FromSeconds(5)) ||
                    !_handoff.TryOpenOwner(worker.ThreadId, out HostedConnectionOwnerId owner))
                    throw new InvalidOperationException(
                        "A hosted connection-owner worker could not be registered.");
                worker.PublishOwner(owner);
                _workerByOwnerSlot[owner.Slot] = worker;
            }
        }
        catch
        {
            StopAndJoin();
            _handoff.Dispose();
            throw;
        }
    }

    internal long ReservedBytes => checked(
        _handoff.ReservedBytes + _listenerPayloadScratch.Length +
        _listenerPathScratch.Length +
        (long)_workers.Length * IntPtr.Size +
        (long)_workerByOwnerSlot.Length * IntPtr.Size);

    internal bool IsQuiescent => _joinCleanupComplete;

    internal bool TryCreateTransport(
        ConnectionId connection,
        SecurityEpoch epoch,
        ReadOnlySpan<byte> master,
        out HostedUdpOwnedTransport? transport)
    {
        EnsureListenerOperational();
        if (!connection.IsValid || epoch.Value == 0 ||
            master.Length != ProtocolLoopbackHandshake.MasterSecretBytes)
        {
            transport = null;
            return false;
        }
        OwnerWorker worker = _workers[_nextWorker];
        _nextWorker = _nextWorker + 1 == _workers.Length ? 0 : _nextWorker + 1;
        if (_handoff.TryBindConnection(connection, worker.Owner) !=
            HostedPacketHandoffStatus.Accepted)
        {
            transport = null;
            return false;
        }
        if (worker.TryCreate(connection, epoch, master, out transport)) return true;
        _ = _handoff.TryUnbindConnection(connection, worker.Owner);
        return false;
    }

    internal void PublishTransport(
        HostedUdpOwnedTransport transport,
        HostedUdpProtocolSession session)
    {
        EnsureListenerOperational();
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(session);
        transport.PublishSession(session);
    }

    internal void CancelTransport(HostedUdpOwnedTransport transport)
    {
        EnsureListenerOperational();
        ArgumentNullException.ThrowIfNull(transport);
        transport.RequestStop();
        transport.Worker.Wake();
        if (!transport.WaitStopped(TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException(
                "A cancelled hosted owner transport did not become quiescent.");
        if (_handoff.TryUnbindConnection(
                transport.Connection, transport.Worker.Owner) !=
            HostedPacketHandoffStatus.Accepted)
            throw new InvalidOperationException(
                "A cancelled hosted owner transport could not release its binding.");
    }

    internal void RetireTransport(
        HostedUdpOwnedTransport transport,
        SharedUdpServerListener listener,
        HostedUdpProtocolSession?[] sessions)
    {
        EnsureListenerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(sessions);
        transport.RequestStop();
        transport.Worker.Wake();
        if (!transport.WaitStopped(TimeSpan.FromMilliseconds(
                _ownerLimits.TransportRetirementTimeoutMilliseconds)))
            throw new InvalidOperationException(
                "A retired hosted owner transport did not become quiescent.");
        if (_stopped || transport.ListenerOwnershipReleased) return;
        DrainListenerEgress(listener, sessions);
        if (_handoff.TryUnbindConnection(
                transport.Connection, transport.Worker.Owner) !=
            HostedPacketHandoffStatus.Accepted)
            throw new InvalidOperationException(
                "A retired hosted owner transport could not release its binding.");
        transport.PublishListenerOwnershipReleased();
    }

    internal HostedPacketHandoffStatus TryOfferSecure(
        HostedUdpProtocolSession session,
        ReadOnlySpan<byte> datagram)
    {
        EnsureListenerOperational();
        HostedUdpOwnedTransport? transport = session.OwnedTransport;
        if (transport is null) return HostedPacketHandoffStatus.UnknownConnection;
        HostedPacketHandoffStatus status = _handoff.TryOfferIngress(
            session.Connection,
            session.ListenerPathGeneration,
            HostedPacketTransferKind.SecureProtocol,
            datagram);
        if (status == HostedPacketHandoffStatus.Accepted) transport.Worker.Wake();
        else Interlocked.Increment(ref _listenerCapacityDrops);
        return status;
    }

    internal HostedPacketHandoffStatus TryOfferPathControl(
        ReadOnlySpan<byte> datagram,
        ReadOnlySpan<byte> observedPathBinding)
    {
        EnsureListenerOperational();
        HostedPacketHandoffStatus status = _handoff.TryOfferPathControl(
            _manifest, datagram, observedPathBinding);
        if (status == HostedPacketHandoffStatus.Accepted &&
            TryPeekPathConnection(datagram, out ConnectionId connection) &&
            _handoff.TryGetBinding(connection, out _, out HostedConnectionOwnerId owner) &&
            owner.Slot < (uint)_workerByOwnerSlot.Length)
            _workerByOwnerSlot[owner.Slot]?.Wake();
        else if (status != HostedPacketHandoffStatus.Accepted)
            Interlocked.Increment(ref _pathRejections);
        return status;
    }

    internal void DrainListenerEgress(
        SharedUdpServerListener listener,
        HostedUdpProtocolSession?[] sessions)
    {
        EnsureListenerOperational();
        for (int workerIndex = 0; workerIndex < _workers.Length; workerIndex++)
        {
            OwnerWorker worker = _workers[workerIndex];
            if (worker.Failure is not null)
                throw new InvalidOperationException(
                    "A hosted connection-owner worker failed.", worker.Failure);
            while (_handoff.TryAcquireEgress(
                worker.Owner, out HostedPacketTransfer transfer))
            {
                int length = transfer.Length;
                _handoff.GetEgressPayload(transfer).CopyTo(_listenerPayloadScratch);
                bool hasPath = transfer.HasPathBinding &&
                    _handoff.TryCopyEgressPathBinding(transfer, _listenerPathScratch);
                if (!_handoff.TryReleaseEgress(transfer.Id))
                    throw new InvalidOperationException(
                        "The listener could not release a hosted egress lease.");
                if (!TryGetSession(sessions, transfer.Connection,
                        out HostedUdpProtocolSession? session) ||
                    session is not { OwnedTransport: HostedUdpOwnedTransport transport } ||
                    transport.Worker != worker)
                {
                    Interlocked.Increment(ref _listenerCapacityDrops);
                    continue;
                }
                ReadOnlySpan<byte> payload = _listenerPayloadScratch.AsSpan(0, length);
                ReadOnlySpan<byte> path = hasPath ? _listenerPathScratch : default;
                switch (transfer.Kind)
                {
                    case HostedPacketTransferKind.SecureProtocol:
                        if (transfer.PathGeneration != session.ListenerPathGeneration ||
                            session.Endpoint.TrySend(payload) != DatagramSendStatus.Accepted)
                            Interlocked.Increment(ref _listenerCapacityDrops);
                        else
                            Interlocked.Increment(ref _listenerSecureDatagrams);
                        break;
                    case HostedPacketTransferKind.PathControl:
                        if (!hasPath || SendUnknown(listener, payload, path) !=
                                DatagramSendStatus.Accepted)
                            Interlocked.Increment(ref _listenerCapacityDrops);
                        else
                            Interlocked.Increment(ref _listenerPathDatagrams);
                        break;
                    case HostedPacketTransferKind.PathRebindCommit:
                        ApplyPathCommit(listener, session, transport,
                            transfer.PathGeneration, payload, path);
                        break;
                    default:
                        Interlocked.Increment(ref _listenerCapacityDrops);
                        break;
                }
            }
        }
    }

    internal HostedUdpConnectionOwnerRuntimeSnapshot GetSnapshot()
    {
        EnsureListenerOperational();
        int active = 0;
        for (int index = 0; index < _workers.Length; index++)
            active += _workers[index].ActiveCount;
        return new(
            _workers.Length,
            active,
            _ownerLimits.MaximumIngressTransfersPerPass,
            Volatile.Read(ref _peakIngressTransfersPerPass),
            checked((ulong)Interlocked.Read(ref _ownerPasses)),
            checked((ulong)Interlocked.Read(ref _applicationMessages)),
            checked((ulong)Interlocked.Read(ref _unhandledApplicationMessages)),
            checked((ulong)Interlocked.Read(ref _pathRequests)),
            checked((ulong)Interlocked.Read(ref _pathChallenges)),
            checked((ulong)Interlocked.Read(ref _pathProofs)),
            checked((ulong)Interlocked.Read(ref _pathCommits)),
            checked((ulong)Interlocked.Read(ref _pathRejections)),
            checked((ulong)Interlocked.Read(ref _listenerSecureDatagrams)),
            checked((ulong)Interlocked.Read(ref _listenerPathDatagrams)),
            checked((ulong)Interlocked.Read(ref _listenerCapacityDrops)),
            Interlocked.Read(ref _ownerProtocolStopwatchTicks),
            ReservedBytes,
            _handoff.GetSnapshot());
    }

    internal void StopAndJoin()
    {
        EnsureListenerThread();
        if (_joinCleanupComplete) return;
        if (!_stopped)
        {
            _stopped = true;
            for (int index = 0; index < _workers.Length; index++)
                _workers[index].RequestStop();
        }
        List<Exception>? failures = null;
        bool allJoined = true;
        for (int index = 0; index < _workers.Length; index++)
        {
            OwnerWorker worker = _workers[index];
            if (!worker.Join(TimeSpan.FromSeconds(10)))
            {
                allJoined = false;
                AddFailure(ref failures, new InvalidOperationException(
                    $"Hosted connection-owner worker {worker.Ordinal} did not become quiescent."));
                continue;
            }
            if (worker.Failure is not null)
                AddFailure(ref failures, new InvalidOperationException(
                    $"Hosted connection-owner worker {worker.Ordinal} failed.", worker.Failure));
            HostedPacketHandoffStatus close =
                _handoff.ForceCloseFailedOwner(worker.Owner);
            if (close is not (HostedPacketHandoffStatus.Accepted or
                HostedPacketHandoffStatus.DestinationClosed))
                AddFailure(ref failures, new InvalidOperationException(
                    $"Hosted connection-owner worker {worker.Ordinal} could not release its handoff ownership ({close})."));
        }
        _joinCleanupComplete = allJoined;
        if (failures is not null)
            throw new AggregateException(
                "One or more hosted connection owners failed during shutdown.", failures);
    }

    public void Dispose()
    {
        EnsureListenerThread();
        if (_disposed) return;
        List<Exception>? failures = null;
        try { StopAndJoin(); }
        catch (Exception exception) { AddFailure(ref failures, exception); }
        if (_joinCleanupComplete)
        {
            try { _handoff.Dispose(); }
            catch (Exception exception) { AddFailure(ref failures, exception); }
            CryptographicOperations.ZeroMemory(_listenerPayloadScratch);
            CryptographicOperations.ZeroMemory(_listenerPathScratch);
            _disposed = true;
        }
        if (failures is not null)
            throw new AggregateException(
                "The hosted connection-owner runtime failed to dispose completely.", failures);
    }

    private void ApplyPathCommit(
        SharedUdpServerListener listener,
        HostedUdpProtocolSession session,
        HostedUdpOwnedTransport transport,
        HostedPathGeneration generation,
        ReadOnlySpan<byte> commit,
        ReadOnlySpan<byte> pathBinding)
    {
        if (!generation.IsValid || pathBinding.Length !=
                HostedUdpConnectionOwnerHandoff.PathBindingBytes ||
            !TryUnpackPath(pathBinding, out IPEndPoint? replacement) ||
            replacement is null ||
            !listener.TryRebind(session.Endpoint, replacement))
        {
            transport.PublishListenerCommitResult(generation, applied: false);
            transport.Worker.Wake();
            Interlocked.Increment(ref _pathRejections);
            return;
        }
        session.ListenerPathGeneration = generation;
        pathBinding.CopyTo(session.CurrentPathBinding);
        transport.PublishListenerCommitResult(generation, applied: true);
        transport.Worker.Wake();
        if (session.Endpoint.TrySendHandshake(commit) == DatagramSendStatus.Accepted)
        {
            Interlocked.Increment(ref _listenerPathDatagrams);
            Interlocked.Increment(ref _pathCommits);
        }
        else
            Interlocked.Increment(ref _listenerCapacityDrops);
    }

    private static DatagramSendStatus SendUnknown(
        SharedUdpServerListener listener,
        ReadOnlySpan<byte> datagram,
        ReadOnlySpan<byte> pathBinding)
    {
        Span<byte> address = stackalloc byte[16];
        if (!TryWriteSocketAddress(pathBinding, address))
            return DatagramSendStatus.CapacityExceeded;
        return listener.TrySendUnknown(datagram, address);
    }

    private static bool TryGetSession(
        HostedUdpProtocolSession?[] sessions,
        ConnectionId connection,
        out HostedUdpProtocolSession? session)
    {
        if (connection.IsValid && connection.Slot < (uint)sessions.Length &&
            sessions[connection.Slot] is HostedUdpProtocolSession current &&
            current.Connection == connection)
        {
            session = current;
            return true;
        }
        session = null;
        return false;
    }

    private bool TryPeekPathConnection(
        ReadOnlySpan<byte> datagram,
        out ConnectionId connection) =>
        HostedUdpPathRebindCodec.TryPeekConnection(
            datagram, HostedPathDatagramKind.Request, _manifest, out connection) ||
        HostedUdpPathRebindCodec.TryPeekConnection(
            datagram, HostedPathDatagramKind.Proof, _manifest, out connection);

    private static bool TryUnpackPath(
        ReadOnlySpan<byte> path,
        out IPEndPoint? endpoint)
    {
        endpoint = null;
        if (path.Length != HostedUdpConnectionOwnerHandoff.PathBindingBytes || path[0] != 4)
            return false;
        endpoint = new IPEndPoint(
            new IPAddress(path.Slice(1, 4)),
            (path[5] << 8) | path[6]);
        return true;
    }

    private static bool TryWriteSocketAddress(
        ReadOnlySpan<byte> path,
        Span<byte> destination)
    {
        if (path.Length != HostedUdpConnectionOwnerHandoff.PathBindingBytes ||
            path[0] != 4 || destination.Length < 16) return false;
        destination.Clear();
        destination[0] = 2;
        destination[2] = path[5];
        destination[3] = path[6];
        path.Slice(1, 4).CopyTo(destination[4..8]);
        return true;
    }

    private void EnsureListenerOperational()
    {
        EnsureListenerThread();
        ObjectDisposedException.ThrowIf(_disposed || _stopped, this);
    }

    private void EnsureListenerThread()
    {
        if (Environment.CurrentManagedThreadId != _listenerThreadId)
            throw new InvalidOperationException(
                "The hosted owner runtime listener operation belongs to another thread.");
    }

    internal sealed class HostedUdpOwnedTransport : IHostedUdpOwnerPathControlHandler
    {
        private readonly HostedUdpConnectionOwnerRuntime _runtime;
        private int _stopRequested;
        private int _drainRequested;
        private long _requestedDrainMilliseconds;
        private int _drainApplied;
        private int _stoppedOnOwner;
        private int _listenerOwnershipReleased;
        private int _committed;
        private int _listenerCommitState;
        private uint _listenerCommitGeneration;
        private HostedUdpProtocolSession? _session;

        internal HostedUdpOwnedTransport(
            HostedUdpConnectionOwnerRuntime runtime,
            OwnerWorker worker,
            ConnectionId connection,
            SecurityEpoch epoch,
            ReadOnlySpan<byte> master)
        {
            _runtime = runtime;
            Worker = worker;
            Connection = connection;
            Endpoint = new HostedUdpConnectionOwnerEndpoint(
                runtime._handoff,
                worker.Owner,
                connection,
                new HostedPathGeneration(1),
                runtime._udpLimits.MaximumDatagramBytes,
                externallyDemultiplexed: true);
            Peer = new NetworkProtocolPeer(
                Endpoint,
                runtime._manifest,
                runtime._protocolLimits,
                new DeterministicDatagramLimits(
                    1, runtime._udpLimits.MaximumDatagramBytes),
                connection,
                epoch,
                master,
                sendsClientToServer: false,
                usesMonotonicPacing: true);
            Application = runtime._applicationFactory.Create(connection, Peer) ??
                throw new InvalidOperationException(
                    "The hosted application factory returned no owner application.");
            CachedProtocolSnapshot = Peer.GetSnapshot();
            CachedDatagramSnapshot = Endpoint.GetSnapshot();
        }

        internal OwnerWorker Worker { get; }
        internal ConnectionId Connection { get; }
        internal HostedUdpConnectionOwnerEndpoint Endpoint { get; }
        internal NetworkProtocolPeer Peer { get; }
        internal IHostedUdpSessionApplication Application { get; }
        internal NetworkProtocolSnapshot CachedProtocolSnapshot { get; private set; }
        internal UdpDatagramEndpointSnapshot CachedDatagramSnapshot { get; private set; }
        internal bool IsCommitted => Volatile.Read(ref _committed) != 0;
        internal bool StopRequested => Volatile.Read(ref _stopRequested) != 0;
        internal bool StoppedOnOwner => Volatile.Read(ref _stoppedOnOwner) != 0;
        internal bool ListenerOwnershipReleased =>
            Volatile.Read(ref _listenerOwnershipReleased) != 0;

        internal void PublishSession(HostedUdpProtocolSession session)
        {
            if (Interlocked.CompareExchange(ref _session, session, null) is not null)
                throw new InvalidOperationException(
                    "The hosted owner transport already has a session.");
            Volatile.Write(ref _committed, 1);
            Worker.Wake();
        }

        internal void RequestStop() => Volatile.Write(ref _stopRequested, 1);

        internal void RequestDrain(ulong now)
        {
            Volatile.Write(ref _requestedDrainMilliseconds, unchecked((long)now));
            Volatile.Write(ref _drainRequested, 1);
        }

        internal bool WaitStopped(TimeSpan timeout) =>
            SpinWait.SpinUntil(() => StoppedOnOwner, timeout);

        internal void PublishStopped() => Volatile.Write(ref _stoppedOnOwner, 1);

        internal void PublishListenerOwnershipReleased() =>
            Volatile.Write(ref _listenerOwnershipReleased, 1);

        internal void CopyCachedSnapshots(
            out NetworkProtocolSnapshot protocol,
            out UdpDatagramEndpointSnapshot datagrams)
        {
            lock (this)
            {
                protocol = CachedProtocolSnapshot;
                datagrams = CachedDatagramSnapshot;
            }
        }

        internal void PublishListenerCommitResult(
            HostedPathGeneration generation,
            bool applied)
        {
            Volatile.Write(ref _listenerCommitGeneration, generation.Value);
            Volatile.Write(ref _listenerCommitState, applied ? 1 : -1);
        }

        internal void Process(ulong now)
        {
            long protocolStarted = Stopwatch.GetTimestamp();
            ApplyListenerCommitResult(now);
            HostedUdpProtocolSession session = _session ??
                throw new InvalidOperationException(
                    "A committed owner transport has no hosted session.");
            if (Volatile.Read(ref _drainRequested) != 0)
                BeginDrainOnOwner(unchecked((ulong)Volatile.Read(
                    ref _requestedDrainMilliseconds)));
            if (session.HasPendingPathRebind && !session.PathCommitAwaitingListener &&
                now >= session.PendingPathExpiration)
            {
                session.ClearPendingPathRebind();
                Interlocked.Increment(ref _runtime._pathRejections);
            }
            ulong received = Peer.ReceivedPacketCount;
            _ = Peer.ProcessInbound(now);
            if (Peer.ReceivedPacketCount != received)
                session.LastActivityMilliseconds = now;
            Interlocked.Add(
                ref _runtime._ownerProtocolStopwatchTicks,
                Stopwatch.GetTimestamp() - protocolStarted);
            while (Peer.TryPeekReceived(
                out NetworkDeliveredMessage message,
                out ReadOnlySpan<byte> payload))
            {
                bool handled = Application.TryProcess(in message, payload);
                if (!Peer.TryConsumeReceived())
                    throw new InvalidOperationException(
                        "The hosted owner could not consume a received message.");
                Interlocked.Increment(ref _runtime._applicationMessages);
                if (!handled)
                    Interlocked.Increment(ref _runtime._unhandledApplicationMessages);
            }
            _ = Application.TryProgress();
            protocolStarted = Stopwatch.GetTimestamp();
            if (session.RawState == UdpProtocolSessionState.Established &&
                now - session.LastKeepAliveMilliseconds >=
                    (ulong)_runtime._udpLimits.KeepAliveIntervalMilliseconds &&
                Peer.TryEmitKeepAlive())
                session.LastKeepAliveMilliseconds = now;
            Peer.Emit(now);
            HostedUdpSessionLifetimeDecision lifetime =
                HostedUdpSessionLifetimePolicy.Evaluate(
                    session.RawState,
                    Application is IHostedUdpSessionApplicationLifetime
                        { RetirementRequested: true },
                    now,
                    session.LastActivityMilliseconds,
                    session.DrainDeadlineMilliseconds,
                    Peer.ReliableQueued,
                    _runtime._udpLimits);
            if (lifetime == HostedUdpSessionLifetimeDecision.BeginDrain)
                BeginDrainOnOwner(now);
            else if (lifetime == HostedUdpSessionLifetimeDecision.Retire)
            {
                if (session.RawState == UdpProtocolSessionState.Established)
                    BeginDrainOnOwner(now);
                RequestStop();
            }
            Interlocked.Add(
                ref _runtime._ownerProtocolStopwatchTicks,
                Stopwatch.GetTimestamp() - protocolStarted);
            lock (this)
            {
                CachedProtocolSnapshot = Peer.GetSnapshot();
                CachedDatagramSnapshot = Endpoint.GetSnapshot();
            }
            Interlocked.Increment(ref _runtime._ownerPasses);
        }

        internal void ProcessSecureIngress(HostedPacketTransfer transfer, ulong now)
        {
            Endpoint.StageIngress(transfer);
            long protocolStarted = Stopwatch.GetTimestamp();
            try
            {
                HostedUdpProtocolSession session = _session ??
                    throw new InvalidOperationException(
                        "A committed owner transport has no hosted session.");
                ulong received = Peer.ReceivedPacketCount;
                _ = Peer.ProcessInbound(now);
                if (Peer.ReceivedPacketCount != received)
                    session.LastActivityMilliseconds = now;
            }
            finally
            {
                Interlocked.Add(
                    ref _runtime._ownerProtocolStopwatchTicks,
                    Stopwatch.GetTimestamp() - protocolStarted);
                Endpoint.ReleaseStagedIngress();
            }
        }

        public void HandlePathControl(
            HostedPacketTransferKind kind,
            HostedPathGeneration pathGeneration,
            ReadOnlySpan<byte> datagram,
            ReadOnlySpan<byte> observedPathBinding)
        {
            HostedUdpProtocolSession? session = _session;
            if (session is null || session.RawState != UdpProtocolSessionState.Established)
            {
                Interlocked.Increment(ref _runtime._pathRejections);
                return;
            }
            if (kind != HostedPacketTransferKind.PathControl)
            {
                Interlocked.Increment(ref _runtime._pathRejections);
                return;
            }
            if (session.LastPathProofLength != 0 &&
                datagram.SequenceEqual(session.LastPathProof.AsSpan(
                    0, session.LastPathProofLength)))
            {
                OfferPath(
                    HostedPacketTransferKind.PathControl,
                    session.PathGeneration,
                    session.LastPathCommit.AsSpan(0, session.LastPathCommitLength),
                    observedPathBinding);
                return;
            }
            if (HostedUdpPathRebindCodec.TryReadRequest(
                datagram,
                _runtime._manifest,
                session.Connection,
                session.PathKey,
                out ulong attempt,
                out ReadOnlySpan<byte> nonce))
            {
                HandleRequest(session, attempt, nonce, observedPathBinding);
                return;
            }
            if (HostedUdpPathRebindCodec.TryReadPathFlight(
                datagram,
                HostedPathDatagramKind.Proof,
                _runtime._manifest,
                session.Connection,
                session.PathKey,
                out attempt,
                out HostedPathGeneration generation,
                out nonce,
                out ReadOnlySpan<byte> challenge,
                out ReadOnlySpan<byte> path))
            {
                HandleProof(
                    session, datagram, attempt, generation, nonce,
                    challenge, path, observedPathBinding);
                return;
            }
            Interlocked.Increment(ref _runtime._pathRejections);
        }

        internal void DisposeOnOwner()
        {
            List<Exception>? failures = null;
            try { Application.Dispose(); }
            catch (Exception exception) { AddFailure(ref failures, exception); }
            try { Peer.Dispose(); }
            catch (Exception exception) { AddFailure(ref failures, exception); }
            try { Endpoint.Dispose(); }
            catch (Exception exception) { AddFailure(ref failures, exception); }
            if (failures is not null)
                throw new AggregateException(
                    "A hosted owner transport failed to dispose completely.", failures);
        }

        private void BeginDrainOnOwner(ulong now)
        {
            if (Interlocked.Exchange(ref _drainApplied, 1) != 0) return;
            Peer.StopAcceptingNewSends();
            HostedUdpProtocolSession session = _session ??
                throw new InvalidOperationException(
                    "A committed owner transport has no hosted session.");
            session.DrainDeadlineMilliseconds =
                HostedUdpSessionLifetimePolicy.GetDrainDeadline(
                    now, _runtime._udpLimits);
            session.RawState = UdpProtocolSessionState.Draining;
        }

        private void HandleRequest(
            HostedUdpProtocolSession session,
            ulong attempt,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> observedPathBinding)
        {
            Interlocked.Increment(ref _runtime._pathRequests);
            if (observedPathBinding.Length != HostedUdpConnectionOwnerHandoff.PathBindingBytes ||
                observedPathBinding.SequenceEqual(session.CurrentPathBinding) ||
                attempt <= session.LastCommittedPathAttempt ||
                session.PathCommitAwaitingListener)
            {
                Interlocked.Increment(ref _runtime._pathRejections);
                return;
            }
            if (session.HasPendingPathRebind)
            {
                if (attempt == session.PendingPathAttempt &&
                    observedPathBinding.SequenceEqual(session.PendingPathBinding) &&
                    CryptographicOperations.FixedTimeEquals(
                        nonce, session.PendingPathNonce))
                {
                    OfferPath(
                        HostedPacketTransferKind.PathControl,
                        session.PathGeneration,
                        session.PendingPathChallengeFlight.AsSpan(
                            0, session.PendingPathChallengeLength),
                        observedPathBinding);
                    return;
                }
                if (attempt <= session.PendingPathAttempt)
                {
                    Interlocked.Increment(ref _runtime._pathRejections);
                    return;
                }
                session.ClearPendingPathRebind();
            }
            if (session.LastIssuedPathGeneration == uint.MaxValue)
            {
                Interlocked.Increment(ref _runtime._pathRejections);
                return;
            }
            session.LastIssuedPathGeneration++;
            session.PendingPathGeneration = new(session.LastIssuedPathGeneration);
            session.PendingPathAttempt = attempt;
            session.PendingPathExpiration = checked(
                _runtime._clock.Milliseconds +
                (ulong)_runtime._admissionLimits.ValidatedCandidateTimeoutMilliseconds);
            nonce.CopyTo(session.PendingPathNonce);
            observedPathBinding.CopyTo(session.PendingPathBinding);
            RandomNumberGenerator.Fill(session.PendingPathChallenge);
            session.PendingPathChallengeLength = HostedUdpPathRebindCodec.WritePathFlight(
                session.PendingPathChallengeFlight,
                HostedPathDatagramKind.Challenge,
                _runtime._manifest,
                session.Connection,
                attempt,
                session.PendingPathGeneration,
                session.PendingPathNonce,
                session.PendingPathChallenge,
                session.PendingPathBinding,
                session.PathKey);
            OfferPath(
                HostedPacketTransferKind.PathControl,
                session.PathGeneration,
                session.PendingPathChallengeFlight.AsSpan(
                    0, session.PendingPathChallengeLength),
                session.PendingPathBinding);
            Interlocked.Increment(ref _runtime._pathChallenges);
        }

        private void HandleProof(
            HostedUdpProtocolSession session,
            ReadOnlySpan<byte> datagram,
            ulong attempt,
            HostedPathGeneration generation,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> challenge,
            ReadOnlySpan<byte> pathBinding,
            ReadOnlySpan<byte> observedPathBinding)
        {
            Interlocked.Increment(ref _runtime._pathProofs);
            if (!session.HasPendingPathRebind || session.PathCommitAwaitingListener ||
                generation != session.PendingPathGeneration ||
                attempt != session.PendingPathAttempt ||
                !observedPathBinding.SequenceEqual(session.PendingPathBinding) ||
                !pathBinding.SequenceEqual(session.PendingPathBinding) ||
                !CryptographicOperations.FixedTimeEquals(
                    nonce, session.PendingPathNonce) ||
                !CryptographicOperations.FixedTimeEquals(
                    challenge, session.PendingPathChallenge))
            {
                Interlocked.Increment(ref _runtime._pathRejections);
                return;
            }
            int commitLength = HostedUdpPathRebindCodec.WritePathFlight(
                session.LastPathCommit,
                HostedPathDatagramKind.Commit,
                _runtime._manifest,
                session.Connection,
                attempt,
                generation,
                nonce,
                challenge,
                pathBinding,
                session.PathKey);
            datagram.CopyTo(session.LastPathProof);
            session.LastPathProofLength = datagram.Length;
            session.LastPathCommitLength = commitLength;
            session.PathCommitAwaitingListener = true;
            OfferPath(
                HostedPacketTransferKind.PathRebindCommit,
                generation,
                session.LastPathCommit.AsSpan(0, commitLength),
                session.PendingPathBinding);
        }

        private void ApplyListenerCommitResult(ulong now)
        {
            int state = Interlocked.Exchange(ref _listenerCommitState, 0);
            if (state == 0) return;
            HostedUdpProtocolSession? session = _session;
            uint generationValue = Volatile.Read(ref _listenerCommitGeneration);
            if (session is null || !session.PathCommitAwaitingListener ||
                !session.PendingPathGeneration.IsValid ||
                session.PendingPathGeneration.Value != generationValue)
            {
                Interlocked.Increment(ref _runtime._pathRejections);
                return;
            }
            if (state < 0)
            {
                session.ClearPendingPathRebind();
                Interlocked.Increment(ref _runtime._pathRejections);
                return;
            }
            HostedPathGeneration generation = session.PendingPathGeneration;
            Endpoint.SetPathGeneration(generation);
            session.PathGeneration = generation;
            session.LastCommittedPathAttempt = session.PendingPathAttempt;
            session.ClearPendingPathRebind(clearFlights: false);
            session.LastActivityMilliseconds = now;
        }

        private void OfferPath(
            HostedPacketTransferKind kind,
            HostedPathGeneration generation,
            ReadOnlySpan<byte> payload,
            ReadOnlySpan<byte> pathBinding)
        {
            if (_runtime._handoff.TryOfferEgress(
                    Worker.Owner,
                    Connection,
                    generation,
                    kind,
                    payload,
                    pathBinding) != HostedPacketHandoffStatus.Accepted)
                Interlocked.Increment(ref _runtime._listenerCapacityDrops);
        }

    }

    internal sealed class OwnerWorker
    {
        private readonly HostedUdpConnectionOwnerRuntime _runtime;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _started = new(false);
        private readonly ManualResetEventSlim _registered = new(false);
        private readonly AutoResetEvent _wake = new(false);
        private readonly object _commandGate = new();
        private readonly HostedUdpOwnedTransport?[] _transports;
        private ConnectionId _requestConnection;
        private SecurityEpoch _requestEpoch;
        private byte[]? _requestMaster;
        private HostedUdpOwnedTransport? _requestResult;
        private int _requestState;
        private int _stop;
        private int _activeCount;

        internal OwnerWorker(
            HostedUdpConnectionOwnerRuntime runtime,
            int ordinal,
            int maximumConnections)
        {
            _runtime = runtime;
            Ordinal = ordinal;
            _transports = new HostedUdpOwnedTransport[maximumConnections];
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = $"UltraSim.Hosted.Owner.{ordinal}",
            };
        }

        internal int Ordinal { get; }
        internal int ThreadId { get; private set; }
        internal HostedConnectionOwnerId Owner { get; private set; }
        internal Exception? Failure { get; private set; }
        internal int ActiveCount => Volatile.Read(ref _activeCount);

        internal void Start() => _thread.Start();
        internal bool WaitStarted(TimeSpan timeout) => _started.Wait(timeout);

        internal void PublishOwner(HostedConnectionOwnerId owner)
        {
            Owner = owner;
            _registered.Set();
        }

        internal bool TryCreate(
            ConnectionId connection,
            SecurityEpoch epoch,
            ReadOnlySpan<byte> master,
            out HostedUdpOwnedTransport? transport)
        {
            lock (_commandGate)
            {
                if (_requestState != 0 || Failure is not null ||
                    connection.Slot >= (uint)_transports.Length ||
                    _transports[connection.Slot] is not null)
                {
                    transport = null;
                    return false;
                }
                _requestConnection = connection;
                _requestEpoch = epoch;
                _requestMaster = master.ToArray();
                _requestResult = null;
                _requestState = 1;
                _wake.Set();
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (_requestState == 1 && Failure is null)
                {
                    TimeSpan remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero || !Monitor.Wait(_commandGate, remaining))
                        break;
                }
                transport = _requestResult;
                bool created = _requestState == 2 && transport is not null;
                _requestState = 0;
                _requestResult = null;
                return created;
            }
        }

        internal void Wake() => _wake.Set();

        internal void RequestStop()
        {
            Volatile.Write(ref _stop, 1);
            _wake.Set();
            _registered.Set();
        }

        internal bool Join(TimeSpan timeout) => _thread.Join(timeout);

        private void Run()
        {
            try
            {
                ThreadId = Environment.CurrentManagedThreadId;
                _started.Set();
                _registered.Wait();
                while (Volatile.Read(ref _stop) == 0)
                {
                    ProcessCreateRequest();
                    ulong now = _runtime._clock.Milliseconds;
                    int ingress = DrainIngress(now);
                    UpdateMaximum(ref _runtime._peakIngressTransfersPerPass, ingress);
                    for (int index = 0; index < _transports.Length; index++)
                    {
                        HostedUdpOwnedTransport? transport = _transports[index];
                        if (transport is null) continue;
                        if (transport.StopRequested)
                        {
                            _transports[index] = null;
                            Interlocked.Decrement(ref _activeCount);
                            try { transport.DisposeOnOwner(); }
                            finally { transport.PublishStopped(); }
                            continue;
                        }
                        if (!transport.IsCommitted) continue;
                        transport.Process(now);
                    }
                    // Application progression is retained work, but it is not a reason to
                    // spin a dedicated core while every connection is otherwise idle. An
                    // ingress/create/stop publication wakes the owner immediately; the
                    // bounded timeout supplies cadence for application and keepalive work.
                    _wake.WaitOne(1);
                }
                ProcessCreateRequest(cancel: true);
            }
            catch (Exception exception)
            {
                Failure = exception;
                lock (_commandGate)
                {
                    _requestState = -1;
                    Monitor.PulseAll(_commandGate);
                }
            }
            finally
            {
                for (int index = 0; index < _transports.Length; index++)
                {
                    HostedUdpOwnedTransport? transport = _transports[index];
                    if (transport is null) continue;
                    try { transport.DisposeOnOwner(); }
                    catch (Exception exception) { Failure ??= exception; }
                    finally { transport.PublishStopped(); }
                    _transports[index] = null;
                }
                Volatile.Write(ref _activeCount, 0);
            }
        }

        private void ProcessCreateRequest(bool cancel = false)
        {
            ConnectionId connection;
            SecurityEpoch epoch;
            byte[]? master;
            lock (_commandGate)
            {
                if (_requestState != 1) return;
                connection = _requestConnection;
                epoch = _requestEpoch;
                master = _requestMaster;
                _requestMaster = null;
            }
            HostedUdpOwnedTransport? result = null;
            if (!cancel && master is not null)
            {
                try
                {
                    result = new HostedUdpOwnedTransport(
                        _runtime, this, connection, epoch, master);
                    _transports[connection.Slot] = result;
                    Interlocked.Increment(ref _activeCount);
                }
                catch (Exception exception)
                {
                    Failure = exception;
                }
            }
            if (master is not null) CryptographicOperations.ZeroMemory(master);
            lock (_commandGate)
            {
                _requestResult = result;
                _requestState = result is null ? -1 : 2;
                Monitor.PulseAll(_commandGate);
            }
        }

        private int DrainIngress(ulong now)
        {
            Span<byte> path =
                stackalloc byte[HostedUdpConnectionOwnerHandoff.PathBindingBytes];
            int admitted = 0;
            for (;
                 admitted < _runtime._ownerLimits.MaximumIngressTransfersPerPass &&
                 _runtime._handoff.TryAcquireIngress(
                    Owner, out HostedPacketTransfer transfer); admitted++)
            {
                HostedUdpOwnedTransport? transport =
                    transfer.Connection.IsValid &&
                    transfer.Connection.Slot < (uint)_transports.Length
                    ? _transports[transfer.Connection.Slot]
                    : null;
                if (transport is null || transport.Connection != transfer.Connection ||
                    !transport.IsCommitted || transport.StopRequested)
                {
                    ReleaseIngress(transfer.Id);
                    continue;
                }
                if (transfer.Kind == HostedPacketTransferKind.SecureProtocol)
                {
                    transport.ProcessSecureIngress(transfer, now);
                    continue;
                }
                try
                {
                    long protocolStarted = Stopwatch.GetTimestamp();
                    path.Clear();
                    ReadOnlySpan<byte> binding = transfer.HasPathBinding &&
                        _runtime._handoff.TryCopyIngressPathBinding(
                            Owner, transfer, path)
                        ? path
                        : default;
                    transport.HandlePathControl(
                        transfer.Kind,
                        transfer.PathGeneration,
                        _runtime._handoff.GetIngressPayload(Owner, transfer),
                        binding);
                    Interlocked.Add(
                        ref _runtime._ownerProtocolStopwatchTicks,
                        Stopwatch.GetTimestamp() - protocolStarted);
                }
                finally
                {
                    ReleaseIngress(transfer.Id);
                }
            }
            return admitted;
        }

        private void ReleaseIngress(HostedPacketTransferId id)
        {
            if (!_runtime._handoff.TryReleaseIngress(Owner, id))
                throw new InvalidOperationException(
                    "The connection owner could not release a demultiplexed packet lease.");
        }

        private static void UpdateMaximum(ref int target, int value)
        {
            while (true)
            {
                int current = Volatile.Read(ref target);
                if (current >= value ||
                    Interlocked.CompareExchange(ref target, value, current) == current)
                    return;
            }
        }
    }

    private static void AddFailure(ref List<Exception>? failures, Exception exception)
    {
        failures ??= new List<Exception>();
        failures.Add(exception);
    }
}
