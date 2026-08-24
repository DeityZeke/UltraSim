using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;
using static H05Harness;

const ulong applicationSessionEpoch = 0x4850_3501;
var contentFingerprint = new WorldContentFingerprint(0x4850_3501, 0xA0D0_7105);

if (Has(args, "--server"))
{
    RunServer(args);
    return;
}
if (Has(args, "--client"))
{
    RunClient(args);
    return;
}
RunCoordinator(args);

void RunCoordinator(string[] values)
{
    int clients = ReadInt(values, "--clients=", 4);
    int ticks = ReadInt(values, "--ticks=", 24);
    int reconnects = ReadInt(values, "--reconnects=", 1);
    int owners = ReadInt(values, "--owners=", Math.Min(2, clients));
    string output = ReadString(values, "--output=") ?? Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../results/network-hosted-h0.5.json"));
    if (clients <= 0 || clients > 64 || ticks <= 0 || reconnects < 0 ||
        reconnects > 4 || owners <= 0 || owners > clients)
        throw new ArgumentOutOfRangeException(nameof(values));

    string runDirectory = Path.Combine(
        Path.GetTempPath(), $"ultrasim-h05-{Environment.ProcessId}-{Guid.NewGuid():N}");
    Directory.CreateDirectory(runDirectory);
    string readyPath = Path.Combine(runDirectory, "server-ready.json");
    string serverResultPath = Path.Combine(runDirectory, "server-result.json");
    string assembly = Assembly.GetExecutingAssembly().Location;
    var children = new List<Process>();
    try
    {
        Process server = StartChild(assembly,
            "--server",
            $"--clients={clients}",
            $"--ticks={ticks}",
            $"--reconnects={reconnects}",
            $"--owners={owners}",
            $"--ready={readyPath}",
            $"--result={serverResultPath}");
        children.Add(server);
        WaitForFile(readyPath, server, TimeSpan.FromSeconds(30));
        ServerReady ready = ReadJson<ServerReady>(readyPath);
        for (int index = 0; index < clients; index++)
        {
            string result = Path.Combine(runDirectory, $"client-{index}.json");
            Process client = StartChild(assembly,
                "--client",
                $"--index={index}",
                $"--ticks={ticks}",
                $"--reconnects={reconnects}",
                $"--endpoint={ready.EndPoint}",
                $"--key={ready.ServerPublicKey}",
                $"--result={result}");
            children.Add(client);
        }
        for (int index = 1; index < children.Count; index++)
            WaitForExit(children[index], TimeSpan.FromSeconds(90));
        WaitForExit(server, TimeSpan.FromSeconds(90));

        ServerEvidence serverEvidence = ReadJson<ServerEvidence>(serverResultPath);
        var clientEvidence = new ClientEvidence[clients];
        for (int index = 0; index < clients; index++)
            clientEvidence[index] = ReadJson<ClientEvidence>(
                Path.Combine(runDirectory, $"client-{index}.json"));
        Require(clientEvidence.All(client => client.CompletedPhases == reconnects + 1));
        Require(clientEvidence.All(client => client.DisclosureViolations == 0));
        Require(serverEvidence.CompletedApplications == clients * (reconnects + 1));
        Require(serverEvidence.DisclosureViolations == 0);

        var report = new H05Report(
            "network-hosted-h0.5-dedicated-sharded-v1",
            DateTimeOffset.UtcNow,
            Environment.Version.ToString(),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            GCSettings.IsServerGC,
            clients,
            owners,
            ticks,
            reconnects,
            serverEvidence,
            clientEvidence,
            new H05Scope(
                "Dedicated server process plus independent client-worker processes. " +
                "Every client uses HostedUdpProtocolClient against one externally admitted " +
                "shared listener; server peers and Game adapters run on fixed connection-owner " +
                "threads through the H0.4 packet slab.",
                "N1 RemoteChunkWorldStateServer/Client is used unchanged. Mixed traffic also " +
                "carries OwnerState, retained Event, client Control, chunk application proof, " +
                "and Event application proof. Each phase crosses one chunk boundary while its " +
                "client performs one authenticated path rebind; a fresh reconnect phase is " +
                "created by default.",
                "Loopback host evidence, not public-WAN, multi-host, production CCU, N1.5, " +
                "2K/4K, or permission to delete N0."));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,
            JsonSerializer.Serialize(report, JsonOptions()) + Environment.NewLine);
        Console.WriteLine($"H0.5 dedicated server: clients={clients}, owners={owners}, " +
            $"phases/client={reconnects + 1}, ticks/phase={ticks}");
        Console.WriteLine($"server wall={serverEvidence.WallMilliseconds:F3} ms, " +
            $"cpu={serverEvidence.ProcessCpuMilliseconds:F3} ms, " +
            $"semantic={serverEvidence.ServerSemanticMilliseconds:F3} ms, " +
            $"listener pumps={serverEvidence.ListenerPumps}");
        Console.WriteLine($"client e2e p95={Metric(clientEvidence.SelectMany(
            client => client.EndToEndTickMicroseconds).ToArray()).P95:F3} us, " +
            $"rebinds={clientEvidence.Aggregate(
                0UL, (sum, client) => checked(sum + client.PathRebinds))}, " +
            $"disclosure=0");
        Console.WriteLine(Path.GetFullPath(output));
    }
    finally
    {
        for (int index = 0; index < children.Count; index++)
        {
            Process child = children[index];
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
            }
            child.Dispose();
        }
        if (Directory.Exists(runDirectory)) Directory.Delete(runDirectory, recursive: true);
    }
}

void RunServer(string[] values)
{
    int clients = ReadInt(values, "--clients=", 4);
    int ticks = ReadInt(values, "--ticks=", 24);
    int reconnects = ReadInt(values, "--reconnects=", 1);
    int owners = ReadInt(values, "--owners=", Math.Min(2, clients));
    string readyPath = RequiredString(values, "--ready=");
    string resultPath = RequiredString(values, "--result=");
    int expectedApplications = checked(clients * (reconnects + 1));
    int maximumSessions = expectedApplications;
    NetworkProtocolLimits protocol = NetworkProtocolLimits.Default;
    UdpProtocolLimits udp = H05UdpLimits() with
    {
        IdleTimeoutMilliseconds = 60_000,
        MaximumHandshakePumps = 16_384,
    };
    var shared = new SharedUdpProtocolLimits(
        MaximumSessions: maximumSessions,
        MaximumQueuedDatagramsPerSession: 64,
        MaximumInboundDatagramsPerPump: 4_096,
        SocketIoMode: SharedUdpSocketIoMode.PlatformSendReceiveBatch,
        AggregateSocketSendBufferBytes: 4 * 1024 * 1024,
        AggregateSocketReceiveBufferBytes: 4 * 1024 * 1024,
        MaximumNativeSendBatchDatagrams: 64,
        MaximumNativeReceiveBatchDatagrams: 64);
    var admission = new HostedUdpAdmissionLimits(
        MaximumValidatedCandidates: maximumSessions,
        MaximumTrackedSourceAddresses: 128,
        // All localhost workers intentionally share one source IPv4. Admission
        // mechanism bounds remain fixed, while this evidence profile admits the
        // legitimate same-address startup/reconnect burst without treating the
        // conservative H0.1 policy defaults as a CCU conclusion.
        MaximumChallengesPerAddressPerBucket: checked(maximumSessions * 64),
        MaximumValidationsPerAddressPerBucket: checked(maximumSessions * 64),
        ValidatedCandidateTimeoutMilliseconds: 10_000);
    var sharding = new HostedUdpOwnerShardingLimits(
        MaximumOwners: owners,
        MaximumConnections: maximumSessions,
        MaximumPacketSlots: checked(maximumSessions * 128),
        MaximumQueuedPacketsPerOwner: checked(maximumSessions * 64),
        MaximumDatagramBytes: udp.MaximumDatagramBytes);
    using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
    var factory = new ServerApplicationFactory(
        ticks, applicationSessionEpoch, contentFingerprint);
    using var host = new HostedUdpAdmissionHost(
        new IPEndPoint(IPAddress.Loopback, 0),
        identity,
        protocol,
        udp,
        shared,
        admission,
        sharding,
        factory,
        UltraSimGameProtocol.Manifest);
    var ready = new ServerReady(
        $"{host.LocalEndPoint.Address}:{host.LocalEndPoint.Port}",
        Convert.ToBase64String(identity.PublicKey));
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(readyPath))!);
    File.WriteAllText(readyPath,
        JsonSerializer.Serialize(ready, JsonOptions()) + Environment.NewLine);

    Process process = Process.GetCurrentProcess();
    TimeSpan cpuBefore = process.TotalProcessorTime;
    long started = Stopwatch.GetTimestamp();
    long pumpTicks = 0;
    ulong pumps = 0;
    long deadline = Stopwatch.GetTimestamp() + checked(Stopwatch.Frequency * 75L);
    while (factory.CompletedApplications < expectedApplications)
    {
        long pumpStarted = Stopwatch.GetTimestamp();
        host.Pump();
        pumpTicks += Stopwatch.GetTimestamp() - pumpStarted;
        pumps++;
        if (Stopwatch.GetTimestamp() >= deadline)
            throw new TimeoutException(
                $"The H0.5 server completed {factory.CompletedApplications}/{expectedApplications} " +
                $"applications (control={factory.ControlsReceived}, delta={factory.PublicDeltasQueued}, " +
                $"chunk-proof={factory.ChunkProofsApplied}, event-proof={factory.EventProofsApplied}).");
        Thread.Sleep(1);
    }
    // Give final application proofs and queued acknowledgements a bounded drain.
    for (int drain = 0; drain < 256; drain++)
    {
        long pumpStarted = Stopwatch.GetTimestamp();
        host.Pump();
        pumpTicks += Stopwatch.GetTimestamp() - pumpStarted;
        pumps++;
        Thread.Sleep(1);
    }
    long stopped = Stopwatch.GetTimestamp();
    TimeSpan cpu = process.TotalProcessorTime - cpuBefore;
    HostedUdpAdmissionSnapshot hosted = host.GetSnapshot();
    Require(host.TryGetConnectionOwnerSnapshot(
        out HostedUdpConnectionOwnerRuntimeSnapshot ownerSnapshot));
    var result = new ServerEvidence(
        expectedApplications,
        factory.CompletedApplications,
        factory.ControlsReceived,
        factory.PublicDeltasQueued,
        factory.PublicSnapshotsStarted,
        factory.PublicStateRetriesQueued,
        factory.ChunkProofsApplied,
        factory.OwnerStatesQueued,
        factory.EventsQueued,
        factory.EventProofsApplied,
        factory.DisclosureViolations,
        Milliseconds(stopped - started),
        cpu.TotalMilliseconds,
        Milliseconds(factory.SemanticStopwatchTicks),
        Milliseconds(pumpTicks),
        Milliseconds(ownerSnapshot.OwnerProtocolStopwatchTicks),
        Milliseconds(pumpTicks + ownerSnapshot.OwnerProtocolStopwatchTicks),
        pumps,
        hosted.ReservedBytes,
        hosted.Listener.ReservedBytes,
        ownerSnapshot.ReservedBytes,
        hosted.Listener.NativeSendCalls,
        hosted.Listener.ReceiveCensus.NativeReceiveCalls,
        ownerSnapshot);
    File.WriteAllText(resultPath,
        JsonSerializer.Serialize(result, JsonOptions()) + Environment.NewLine);
}

void RunClient(string[] values)
{
    int index = ReadInt(values, "--index=", 0);
    int ticks = ReadInt(values, "--ticks=", 24);
    int reconnects = ReadInt(values, "--reconnects=", 1);
    string endpointValue = RequiredString(values, "--endpoint=");
    string keyValue = RequiredString(values, "--key=");
    string resultPath = RequiredString(values, "--result=");
    IPEndPoint endpoint = ParseEndpoint(endpointValue);
    byte[] key = Convert.FromBase64String(keyValue);
    var tickTimes = new List<double>(checked(ticks * (reconnects + 1)));
    long socketTicks = 0;
    long applyTicks = 0;
    ulong pathRebinds = 0;
    ulong snapshots = 0;
    ulong deltas = 0;
    ulong ownerStates = 0;
    ulong events = 0;
    ulong disclosure = 0;
    long started = Stopwatch.GetTimestamp();
    Process process = Process.GetCurrentProcess();
    TimeSpan cpuBefore = process.TotalProcessorTime;
    for (int phase = 0; phase <= reconnects; phase++)
    {
        using ClientPhase client = new(
            endpoint, key, ticks, applicationSessionEpoch, contentFingerprint);
        client.Connect(ref socketTicks);
        client.Run(
            tickTimes,
            ref socketTicks,
            ref applyTicks,
            rebindAtTick: Math.Max(1, ticks / 2));
        pathRebinds += client.PathRebinds;
        snapshots += client.SnapshotsApplied;
        deltas += client.DeltasApplied;
        ownerStates += client.OwnerStatesApplied;
        events += client.EventsApplied;
        disclosure += client.DisclosureViolations;
    }
    long stopped = Stopwatch.GetTimestamp();
    TimeSpan cpu = process.TotalProcessorTime - cpuBefore;
    var result = new ClientEvidence(
        index,
        reconnects + 1,
        ticks,
        snapshots,
        deltas,
            ownerStates,
            events,
            true,
            pathRebinds,
        disclosure,
        Milliseconds(stopped - started),
        cpu.TotalMilliseconds,
        Milliseconds(socketTicks),
        Milliseconds(applyTicks),
        tickTimes.ToArray());
    File.WriteAllText(resultPath,
        JsonSerializer.Serialize(result, JsonOptions()) + Environment.NewLine);
    CryptographicOperations.ZeroMemory(key);
}

sealed class ServerApplicationFactory(
    int ticks,
    ulong applicationEpoch,
    WorldContentFingerprint content) : IHostedUdpSessionApplicationFactory
{
    private long _completedApplications;
    private long _controlsReceived;
    private long _publicDeltasQueued;
    private long _publicSnapshotsStarted;
    private long _publicStateRetriesQueued;
    private long _chunkProofsApplied;
    private long _ownerStatesQueued;
    private long _eventsQueued;
    private long _eventProofsApplied;
    private long _disclosureViolations;
    private long _semanticTicks;

    internal int CompletedApplications => checked((int)Interlocked.Read(ref _completedApplications));
    internal ulong ControlsReceived => checked((ulong)Interlocked.Read(ref _controlsReceived));
    internal ulong PublicDeltasQueued => checked((ulong)Interlocked.Read(ref _publicDeltasQueued));
    internal ulong PublicSnapshotsStarted =>
        checked((ulong)Interlocked.Read(ref _publicSnapshotsStarted));
    internal ulong PublicStateRetriesQueued =>
        checked((ulong)Interlocked.Read(ref _publicStateRetriesQueued));
    internal ulong ChunkProofsApplied => checked((ulong)Interlocked.Read(ref _chunkProofsApplied));
    internal ulong OwnerStatesQueued => checked((ulong)Interlocked.Read(ref _ownerStatesQueued));
    internal ulong EventsQueued => checked((ulong)Interlocked.Read(ref _eventsQueued));
    internal ulong EventProofsApplied => checked((ulong)Interlocked.Read(ref _eventProofsApplied));
    internal ulong DisclosureViolations => checked((ulong)Interlocked.Read(ref _disclosureViolations));
    internal long SemanticStopwatchTicks => Interlocked.Read(ref _semanticTicks);

    public IHostedUdpSessionApplication Create(
        ConnectionId connection,
        NetworkProtocolPeer peer) =>
        new ServerApplication(this, peer, connection, ticks, applicationEpoch, content);

    internal void AddSemantic(long ticksElapsed) =>
        Interlocked.Add(ref _semanticTicks, ticksElapsed);
    internal void Control() => Interlocked.Increment(ref _controlsReceived);
    internal void Delta() => Interlocked.Increment(ref _publicDeltasQueued);
    internal void Snapshot() => Interlocked.Increment(ref _publicSnapshotsStarted);
    internal void StateRetry() => Interlocked.Increment(ref _publicStateRetriesQueued);
    internal void ChunkProof() => Interlocked.Increment(ref _chunkProofsApplied);
    internal void Owner() => Interlocked.Increment(ref _ownerStatesQueued);
    internal void Event() => Interlocked.Increment(ref _eventsQueued);
    internal void EventProof() => Interlocked.Increment(ref _eventProofsApplied);
    internal void Disclosure() => Interlocked.Increment(ref _disclosureViolations);
    internal void Complete() => Interlocked.Increment(ref _completedApplications);
}

sealed class ServerApplication : IHostedUdpSessionApplication
{
    private const ulong StateRetryServiceSteps = 256;
    private const int MaximumConcurrentStateAttempts = 4;
    private readonly ServerApplicationFactory _factory;
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _participant;
    private readonly int _targetTicks;
    private readonly ulong _applicationEpoch;
    private readonly EcsWorld _world;
    private readonly Entity _entity;
    private readonly ChunkPublicationStore _source;
    private readonly OfflineDirectChunkRecipient _recipient;
    private readonly ChunkPublicationWorkScheduler _scheduler;
    private readonly ChunkReplicationRecipientId _recipientId;
    private readonly RemoteChunkWorldStateServer _chunks;
    private SpatialChunkKey _key;
    private WorldPosition _anchor;
    private readonly OfflineDirectChunkSubscriptionChange[] _reconcileChanges = new
        OfflineDirectChunkSubscriptionChange[2];
    private readonly WorldPublicDescriptor _descriptor = new(
        GameWorldPublicPrefabIds.Character,
        WorldYaw.Zero,
        WorldPublicStateFlags.Visible);
    private readonly WorldPublicChange[] _change = new WorldPublicChange[1];
    private readonly byte[] _ownerScratch = new byte[RemoteOwnerStateMessageCodec.EncodedSize];
    private readonly byte[] _eventScratch = new byte[RemoteApplicationEventMessageCodec.EncodedSize];
    private int _controls;
    private int _published;
    private int _eventProofs;
    private ulong _observedChunkProofs;
    private bool _initialScheduled;
    private bool _completionQueued;
    private ulong _serviceStep;
    private ulong _nextStateRetryStep;

    internal ServerApplication(
        ServerApplicationFactory factory,
        NetworkProtocolPeer peer,
        ConnectionId connection,
        int ticks,
        ulong applicationEpoch,
        WorldContentFingerprint content)
    {
        _factory = factory;
        _peer = peer;
        _participant = checked((ulong)connection.Slot + 1);
        _targetTicks = ticks;
        _applicationEpoch = applicationEpoch;
        _world = CreateEcs(out ArchetypeHandle empty);
        Require(_world.TryCreate(empty, out _entity));
        _key = Key(0, 0);
        _anchor = Position(0, 8_000);
        _source = new ChunkPublicationStore(
            PublicationEpoch.Create(),
            new ChunkPublicationLimits(1, 2, 1, 256));
        Require(_source.TryCommit([
            WorldPublicChange.Present(_entity, _anchor, _descriptor)
        ], out _));
        _recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(1, 64, 64),
            new ChunkInterestProfile(0, 0, 0, 0, 0, 0),
            new OfflineDirectChunkCadence(1, 1, 1));
        _scheduler = new ChunkPublicationWorkScheduler(
            _source,
            new ChunkPublicationWorkSchedulerLimits(1, 1));
        Require(_scheduler.TryRegister(_recipient, out _recipientId));
        Require(_recipient.Reconcile(
            _source,
            GameWorldState.DefaultSpace,
            _anchor,
            _reconcileChanges,
            out OfflineDirectChunkReconcileResult reconciliation) ==
            OfflineDirectChunkReconcileStatus.Reconciled);
        Require(_scheduler.TryApplyReconciliation(
            _recipientId,
            _reconcileChanges.AsSpan(0, reconciliation.ChangeCount),
            reconciliation));
        _chunks = new RemoteChunkWorldStateServer(
            peer,
            _recipient,
            _source,
            applicationEpoch,
            new RemoteChunkWorldStateTransportLimits(128));
    }

    public bool TryProcess(
        in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            if (_chunks.TryProcess(in message, payload))
            {
                ulong proofs = _chunks.GetSnapshot().AppliedProofs;
                while (_observedChunkProofs < proofs)
                {
                    _observedChunkProofs++;
                    _factory.ChunkProof();
                }
                return true;
            }
            if (message.MessageId == RemoteUiActionHeaderCodec.Descriptor.Id)
            {
                if (!RemoteUiActionHeaderCodec.TryReadExact(
                        payload, out RemoteUiActionHeader action) ||
                    action.ApplicationSessionEpoch != _applicationEpoch)
                    _factory.Disclosure();
                else
                {
                    _controls++;
                    _factory.Control();
                }
                return true;
            }
            if (message.MessageId == RemoteApplicationEventAppliedMessageCodec.Descriptor.Id)
            {
                if (!RemoteApplicationEventAppliedMessageCodec.TryReadExact(
                        payload, out RemoteApplicationEventAppliedMessage proof) ||
                    proof.ApplicationSessionEpoch != _applicationEpoch ||
                    proof.StateEpoch != 1 ||
                    proof.Status != RemoteApplicationEventApplyStatus.Applied ||
                    proof.NextSequence == 0)
                    _factory.Disclosure();
                else
                {
                    _eventProofs = Math.Max(_eventProofs, checked((int)proof.NextSequence - 1));
                    _factory.EventProof();
                }
                return true;
            }
            return false;
        }
        finally
        {
            _factory.AddSemantic(Stopwatch.GetTimestamp() - started);
        }
    }

    public bool TryProgress()
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            _serviceStep++;
            _ = _chunks.TryProgress();
            if (!_initialScheduled)
            {
                Require(_scheduler.TryDequeueDue(
                    _serviceStep, out ChunkPublicationWork incoming));
                Require(incoming.Recipient == _recipientId && incoming.Key == _key &&
                    incoming.Kind == ChunkPublicationWorkKind.Incoming);
                RemoteChunkWorldStateScheduleStatus status = _chunks.TrySchedule(
                    incoming.Key, _serviceStep);
                Require(status is RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted or
                    RemoteChunkWorldStateScheduleStatus.QueuedDelta);
                if (status == RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted)
                    _factory.Snapshot();
                _initialScheduled = true;
            }
            if (_initialScheduled && _observedChunkProofs != 0 &&
                _published < _controls && _published < _targetTicks)
                PublishNext();
            if (_published != 0 && HasAppliedPublicationCursor() &&
                !PublishedRevisionIsApplied() &&
                _serviceStep >= _nextStateRetryStep &&
                _chunks.GetSnapshot().OutstandingDeliveries < MaximumConcurrentStateAttempts)
                RetryLatestState();
            if (!_completionQueued && _controls > _targetTicks &&
                _published == _targetTicks && PublishedRevisionIsApplied() &&
                _eventProofs >= _targetTicks)
                QueueCompletion();
            return true;
        }
        finally
        {
            _factory.AddSemantic(Stopwatch.GetTimestamp() - started);
        }
    }

    public void Dispose()
    {
        _world.Dispose();
    }

    private void PublishNext()
    {
        int next = _published + 1;
        bool crossesBoundary = next == Math.Max(1, _targetTicks / 2);
        if (crossesBoundary)
        {
            _key = Key(1, 0);
            _anchor = Position(1, 8_000);
        }
        _change[0] = WorldPublicChange.Present(
            _entity,
            Position(_key.Coord.X, checked((ushort)(8_000 + (next & 255)))),
            _descriptor);
        Require(_source.TryCommit(_change, out _));
        if (crossesBoundary)
        {
            Require(_chunks.Reconcile(
                GameWorldState.DefaultSpace,
                _anchor,
                _reconcileChanges,
                out OfflineDirectChunkReconcileResult reconciliation) ==
                OfflineDirectChunkReconcileStatus.Reconciled);
            Require(_scheduler.TryApplyReconciliation(
                _recipientId,
                _reconcileChanges.AsSpan(0, reconciliation.ChangeCount),
                reconciliation));
        }
        Require(_scheduler.ObserveCommit(_source) ==
            ChunkPublicationCommitObservationStatus.Observed);
        Require(_scheduler.TryDequeueDue(
            _serviceStep, out ChunkPublicationWork dirty));
        Require(dirty.Recipient == _recipientId && dirty.Key == _key);
        RemoteChunkWorldStateScheduleStatus scheduled =
            _chunks.TrySchedule(dirty.Key, _serviceStep);
        if (scheduled is not (RemoteChunkWorldStateScheduleStatus.QueuedDelta or
                RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted))
            throw new InvalidOperationException(
                $"H0.5 publication schedule mismatch: crossing={crossesBoundary}, " +
                $"kind={dirty.Kind}, status={scheduled}, tick={next}.");
        _nextStateRetryStep = checked(_serviceStep + StateRetryServiceSteps);

        var owner = new RemoteOwnerStateMessage
        {
            ApplicationSessionEpoch = _applicationEpoch,
            StateEpoch = 1,
            EntityIndex = checked((uint)_participant),
            EntityGeneration = 1,
            SimulationTick = checked((ulong)next),
            FrameSequence = checked((ulong)next),
            LastProcessedInputSequence = checked((ulong)next),
            TickMarker = next,
            InputVelocity = next,
            ClockRevision = 1,
            IsPresent = true,
        };
        Require(RemoteOwnerStateMessageCodec.TryWriteExact(_ownerScratch, in owner));
        if (_peer.TryQueue(RemoteOwnerStateMessageCodec.Descriptor.Id, _ownerScratch) !=
            NetworkQueueStatus.Accepted) return;
        var applicationEvent = new RemoteApplicationEventMessage
        {
            ApplicationSessionEpoch = _applicationEpoch,
            StateEpoch = 1,
            Sequence = checked((ulong)next),
            ParticipantId = _participant,
            Kind = 1,
            Subject = checked((ulong)next),
            PayloadHash = checked(_participant * 1_000_003UL + (ulong)next),
        };
        Require(RemoteApplicationEventMessageCodec.TryWriteExact(
            _eventScratch, in applicationEvent));
        if (_peer.TryQueue(RemoteApplicationEventMessageCodec.Descriptor.Id, _eventScratch) !=
            NetworkQueueStatus.Accepted) return;
        _published = next;
        if (scheduled == RemoteChunkWorldStateScheduleStatus.QueuedDelta)
            _factory.Delta();
        else
            _factory.Snapshot();
        _factory.Owner();
        _factory.Event();
    }

    private void QueueCompletion()
    {
        var completion = new RemoteApplicationEventMessage
        {
            ApplicationSessionEpoch = _applicationEpoch,
            StateEpoch = 1,
            Sequence = checked((ulong)_targetTicks + 1),
            ParticipantId = _participant,
            Kind = ushort.MaxValue,
            Subject = _participant,
            PayloadHash = 0x4830_3505,
        };
        Require(RemoteApplicationEventMessageCodec.TryWriteExact(
            _eventScratch, in completion));
        if (_peer.TryQueue(RemoteApplicationEventMessageCodec.Descriptor.Id, _eventScratch) !=
            NetworkQueueStatus.Accepted) return;
        _completionQueued = true;
        _factory.Complete();
    }

    private void RetryLatestState()
    {
        // Owner service passes are roughly millisecond scheduling opportunities, not authority
        // ticks. A cold client must not turn delayed application proof into an unbounded stream of
        // redundant independent-state offers that fills both the delivery ledger and Network
        // queue. A tiny bounded overlap still proves lost-proof/latest-state recovery.
        _nextStateRetryStep = checked(_serviceStep + StateRetryServiceSteps);
        RemoteChunkWorldStateScheduleStatus status =
            _chunks.TrySchedule(_key, _serviceStep);
        if (status is RemoteChunkWorldStateScheduleStatus.QueuedDelta or
            RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted)
        {
            _factory.StateRetry();
            if (status == RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted)
                _factory.Snapshot();
        }
        QueueLatestOwner();
    }

    private void QueueLatestOwner()
    {
        int tick = _published;
        var owner = new RemoteOwnerStateMessage
        {
            ApplicationSessionEpoch = _applicationEpoch,
            StateEpoch = 1,
            EntityIndex = checked((uint)_participant),
            EntityGeneration = 1,
            SimulationTick = checked((ulong)tick),
            FrameSequence = checked((ulong)tick),
            LastProcessedInputSequence = checked((ulong)tick),
            TickMarker = tick,
            InputVelocity = tick,
            ClockRevision = 1,
            IsPresent = true,
        };
        Require(RemoteOwnerStateMessageCodec.TryWriteExact(_ownerScratch, in owner));
        _ = _peer.TryQueue(RemoteOwnerStateMessageCodec.Descriptor.Id, _ownerScratch);
    }

    private bool PublishedRevisionIsApplied()
    {
        if (!_source.TryGetCommittedView(_key, out ChunkPublicationView source)) return false;
        OfflineDirectChunkSubscriptionView subscriptions = _recipient.Subscriptions;
        for (int index = 0; index < subscriptions.Length; index++)
        {
            OfflineDirectChunkSubscription subscription = subscriptions[index];
            if (subscription.Interest.Key != _key) continue;
            return subscription.HasKnowledge && subscription.Cursor.IsValid &&
                subscription.Cursor.Revision == source.Revision;
        }
        return false;
    }

    private bool HasAppliedPublicationCursor()
    {
        OfflineDirectChunkSubscriptionView subscriptions = _recipient.Subscriptions;
        for (int index = 0; index < subscriptions.Length; index++)
        {
            OfflineDirectChunkSubscription subscription = subscriptions[index];
            if (subscription.Interest.Key == _key)
                return subscription.HasKnowledge && subscription.Cursor.IsValid;
        }
        return false;
    }
}

sealed class ClientPhase : IDisposable
{
    private readonly HostedUdpProtocolClient _transport;
    private readonly int _targetTicks;
    private readonly ulong _applicationEpoch;
    private readonly BoundWorldPack _world;
    private readonly ClientWorldSemanticCache _cache;
    private readonly ClientSemanticChunkHandle _handle;
    private readonly byte[] _actionScratch = new byte[RemoteUiActionHeaderCodec.EncodedSize];
    private readonly byte[] _eventProofScratch =
        new byte[RemoteApplicationEventAppliedMessageCodec.EncodedSize];
    private ulong _participant;
    private ulong _lastOwnerTick;
    private ulong _eventsApplied;
    private ulong _pathRebinds;
    private ulong _disclosure;
    private bool _completionAcknowledged;

    internal ClientPhase(
        IPEndPoint server,
        ReadOnlySpan<byte> key,
        int targetTicks,
        ulong applicationEpoch,
        WorldContentFingerprint content)
    {
        _targetTicks = targetTicks;
        _applicationEpoch = applicationEpoch;
        Require(HostedUdpProtocolClient.TryCreate(
            server,
            key,
            NetworkProtocolLimits.Default,
            H05UdpLimits() with
            {
                IdleTimeoutMilliseconds = 60_000,
                MaximumHandshakePumps = 16_384,
            },
            out HostedUdpProtocolClient? client,
            UltraSimGameProtocol.Manifest,
            new IPEndPoint(IPAddress.Loopback, 0)));
        _transport = client!;
        _world = CreateWorld([Key(0, 0), Key(1, 0)], content);
        _cache = new ClientWorldSemanticCache(
            _world,
            new ClientWorldSemanticCacheLimits(2, 0, 0, 64, 2));
        Require(_cache.TryRequest(Key(0, 0), out _handle, out _));
        Require(_cache.TryStageFromBaseWorld(_handle, [], default, out _));
        Require(_cache.TryPublish(_handle, out _));
        Require(_cache.TryRequest(Key(1, 0), out ClientSemanticChunkHandle second, out _));
        Require(_cache.TryStageFromBaseWorld(second, [], default, out _));
        Require(_cache.TryPublish(second, out _));
        // The peer is established in Connect; construct the N1 client there.
    }

    internal ulong PathRebinds => _pathRebinds;
    internal ulong SnapshotsApplied => ChunkClient.GetSnapshot().AppliedSnapshots;
    internal ulong DeltasApplied => ChunkClient.GetSnapshot().AppliedDeltas;
    internal ulong OwnerStatesApplied => _lastOwnerTick;
    internal ulong EventsApplied => Math.Min(_eventsApplied, checked((ulong)_targetTicks));
    internal ulong DisclosureViolations => _disclosure;
    private RemoteChunkWorldStateClient ChunkClient { get; set; } = null!;

    internal void Connect(ref long socketTicks)
    {
        long deadline = Stopwatch.GetTimestamp() + checked(Stopwatch.Frequency * 20L);
        while (_transport.State == HostedUdpClientState.Connecting)
        {
            long started = Stopwatch.GetTimestamp();
            _transport.Pump();
            socketTicks += Stopwatch.GetTimestamp() - started;
            if (Stopwatch.GetTimestamp() >= deadline)
                throw new TimeoutException("The H0.5 client handshake did not converge.");
            Thread.Sleep(1);
        }
        Require(_transport.State == HostedUdpClientState.Established);
        _participant = checked((ulong)_transport.Connection.Slot + 1);
        ChunkClient = new RemoteChunkWorldStateClient(
            _transport.Peer!, _cache, _applicationEpoch, 64, 128);
        WaitForSnapshot(ref socketTicks);
    }

    internal void Run(
        List<double> tickTimes,
        ref long socketTicks,
        ref long applyTicks,
        int rebindAtTick)
    {
        for (int tick = 1; tick <= _targetTicks; tick++)
        {
            if (tick == rebindAtTick)
            {
                Require(_transport.TryBeginPathRebind(
                    new IPEndPoint(IPAddress.Loopback, 0)));
                PumpUntil(() =>
                {
                    return _transport.PathRebindState == HostedPathRebindState.None &&
                        _transport.PathGeneration.Value == 2;
                }, ref socketTicks, ref applyTicks);
                _pathRebinds++;
            }
            QueueControl(ref socketTicks, ref applyTicks);
            long started = Stopwatch.GetTimestamp();
            int expected = tick;
            PumpUntil(() =>
                AppliedPublicUpdates() >= checked((ulong)expected) &&
                _lastOwnerTick >= checked((ulong)expected) &&
                _eventsApplied >= checked((ulong)expected),
                ref socketTicks,
                ref applyTicks);
            tickTimes.Add(Microseconds(Stopwatch.GetTimestamp() - started));
        }
        QueueControl(ref socketTicks, ref applyTicks);
        PumpUntil(() => _completionAcknowledged, ref socketTicks, ref applyTicks);
    }

    public void Dispose()
    {
        _transport.Dispose();
    }

    private void WaitForSnapshot(ref long socketTicks)
    {
        long applyTicks = 0;
        PumpUntil(() => ChunkClient.GetSnapshot().AppliedSnapshots != 0,
            ref socketTicks, ref applyTicks);
    }

    private void PumpUntil(
        Func<bool> condition,
        ref long socketTicks,
        ref long applyTicks)
    {
        long deadline = Stopwatch.GetTimestamp() + checked(Stopwatch.Frequency * 20L);
        while (!condition())
        {
            PumpOnce(ref socketTicks, ref applyTicks);
            if (Stopwatch.GetTimestamp() >= deadline)
            {
                RemoteChunkWorldStateClientSnapshot chunk = ChunkClient.GetSnapshot();
                throw new TimeoutException(
                    $"The H0.5 client application did not converge " +
                    $"(snapshot={chunk.AppliedSnapshots}, delta={chunk.AppliedDeltas}, " +
                    $"owner={_lastOwnerTick}, event={_eventsApplied}, " +
                    $"completion={_completionAcknowledged}).");
            }
            Thread.Sleep(1);
        }
    }

    private void PumpOnce(ref long socketTicks, ref long applyTicks)
    {
        long started = Stopwatch.GetTimestamp();
        _transport.Pump();
        socketTicks += Stopwatch.GetTimestamp() - started;
        started = Stopwatch.GetTimestamp();
        NetworkProtocolPeer peer = _transport.Peer!;
        while (peer.TryPeekReceived(
            out NetworkDeliveredMessage message,
            out ReadOnlySpan<byte> payload))
        {
            bool handled = ChunkClient.TryProcess(in message, payload);
            if (!handled && message.MessageId == RemoteOwnerStateMessageCodec.Descriptor.Id)
            {
                if (!RemoteOwnerStateMessageCodec.TryReadExact(
                        payload, out RemoteOwnerStateMessage owner) ||
                    owner.ApplicationSessionEpoch != _applicationEpoch ||
                    owner.EntityIndex != checked((uint)_participant))
                    _disclosure++;
                else
                {
                    _lastOwnerTick = Math.Max(_lastOwnerTick, owner.SimulationTick);
                    if (owner.ClockRevision == 2 &&
                        owner.SimulationTick == checked((ulong)_targetTicks))
                        _completionAcknowledged = true;
                }
                handled = true;
            }
            if (!handled && message.MessageId == RemoteApplicationEventMessageCodec.Descriptor.Id)
            {
                if (!RemoteApplicationEventMessageCodec.TryReadExact(
                        payload, out RemoteApplicationEventMessage applicationEvent) ||
                    applicationEvent.ApplicationSessionEpoch != _applicationEpoch ||
                    applicationEvent.ParticipantId != _participant)
                    _disclosure++;
                else
                {
                    _eventsApplied = Math.Max(_eventsApplied, applicationEvent.Sequence);
                    if (applicationEvent.Kind == ushort.MaxValue &&
                        applicationEvent.Sequence == checked((ulong)_targetTicks + 1) &&
                        applicationEvent.Subject == _participant &&
                        applicationEvent.PayloadHash == 0x4830_3505)
                        _completionAcknowledged = true;
                    var proof = new RemoteApplicationEventAppliedMessage
                    {
                        ApplicationSessionEpoch = _applicationEpoch,
                        StateEpoch = applicationEvent.StateEpoch,
                        NextSequence = checked(applicationEvent.Sequence + 1),
                        Status = RemoteApplicationEventApplyStatus.Applied,
                    };
                    Require(RemoteApplicationEventAppliedMessageCodec.TryWriteExact(
                        _eventProofScratch, in proof));
                    Require(peer.TryQueue(
                        RemoteApplicationEventAppliedMessageCodec.Descriptor.Id,
                        _eventProofScratch) == NetworkQueueStatus.Accepted);
                }
                handled = true;
            }
            Require(handled);
            Require(peer.TryConsumeReceived());
        }
        _ = ChunkClient.TryProgress();
        applyTicks += Stopwatch.GetTimestamp() - started;
    }

    private void QueueControl(ref long socketTicks, ref long applyTicks)
    {
        var action = new RemoteUiActionHeader
        {
            ApplicationSessionEpoch = _applicationEpoch,
        };
        Require(RemoteUiActionHeaderCodec.TryWriteExact(_actionScratch, in action));
        while (_transport.Peer!.TryQueue(
            RemoteUiActionHeaderCodec.Descriptor.Id,
            _actionScratch) != NetworkQueueStatus.Accepted)
            PumpOnce(ref socketTicks, ref applyTicks);
    }

    private ulong AppliedPublicUpdates()
    {
        RemoteChunkWorldStateClientSnapshot snapshot = ChunkClient.GetSnapshot();
        return checked(snapshot.AppliedDeltas + snapshot.AppliedSnapshots - 1);
    }
}

static class H05Harness
{
internal static EcsWorld CreateEcs(out ArchetypeHandle empty)
{
        var world = new EcsWorld(
            GameSchema.CreateSchema(),
            new EcsWorldOptions(new EntityCapacity(1), 2, 4, lifecycleRecordCapacity: 2));
        empty = world.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
        world.Seal();
        world.Initialize();
        return world;
    }

    internal static BoundWorldPack CreateWorld(
    ReadOnlySpan<SpatialChunkKey> keys,
    WorldContentFingerprint content)
{
    var builder = new WorldPackBuilder(
        GameWorldState.DefaultSpace,
        content,
        new WorldPackLimits(64 * 1024 * 1024, keys.Length, 8, 0, 0, 0));
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    Array.Fill(materials, (ushort)1);
    var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    Require(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
    for (int index = 0; index < keys.Length; index++)
        Require(builder.TryAddBaseTerrain(keys[index], materials, heightBase, offsets, out _));
    Require(builder.TryBuild(out WorldPackArtifact? artifact, out _));
    WorldPack pack = artifact!.Open();
    Require(pack.TryBind(new WorldContentBinding(content, [1]),
        out BoundWorldPack? bound, out _));
    return bound!;
}

    internal static SpatialChunkKey Key(int x, int z) =>
    new(GameWorldState.DefaultSpace, x, z);

    internal static WorldPosition Position(int chunkX, ushort localX) =>
    new(chunkX, 0, localX, 8_000, 0);

    internal static UdpProtocolLimits H05UdpLimits() => new(
        MaximumDatagramBytes: 1_472,
        SocketSendBufferBytes: 262_144,
        SocketReceiveBufferBytes: 262_144,
        MaximumHandshakePumps: 4_096,
        CookieBucketMilliseconds: 30_000,
        IdleTimeoutMilliseconds: 30_000,
        DrainTimeoutMilliseconds: 5_000,
        KeepAliveIntervalMilliseconds: 5_000);

    internal static Process StartChild(string assembly, params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };
    start.ArgumentList.Add(assembly);
    for (int index = 0; index < arguments.Length; index++)
        start.ArgumentList.Add(arguments[index]);
    Process process = Process.Start(start) ??
        throw new InvalidOperationException("The H0.5 child process could not start.");
    process.OutputDataReceived += (_, eventArgs) =>
    {
        if (!string.IsNullOrEmpty(eventArgs.Data)) Console.WriteLine(eventArgs.Data);
    };
    process.ErrorDataReceived += (_, eventArgs) =>
    {
        if (!string.IsNullOrEmpty(eventArgs.Data)) Console.Error.WriteLine(eventArgs.Data);
    };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

    internal static void WaitForFile(string path, Process process, TimeSpan timeout)
{
    long deadline = Stopwatch.GetTimestamp() + checked((long)(timeout.TotalSeconds * Stopwatch.Frequency));
    while (!File.Exists(path))
    {
        if (process.HasExited)
            throw new InvalidOperationException(
                $"The H0.5 server exited before readiness ({process.ExitCode}).");
        if (Stopwatch.GetTimestamp() >= deadline)
            throw new TimeoutException("The H0.5 server readiness file timed out.");
        Thread.Sleep(5);
    }
}

    internal static void WaitForExit(Process process, TimeSpan timeout)
{
    if (!process.WaitForExit(checked((int)timeout.TotalMilliseconds)))
        throw new TimeoutException("An H0.5 child process timed out.");
    if (process.ExitCode != 0)
        throw new InvalidOperationException(
            $"An H0.5 child process failed with exit code {process.ExitCode}.");
}

    internal static T ReadJson<T>(string path) =>
    JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions()) ??
    throw new InvalidOperationException($"The H0.5 artifact {path} is invalid.");

    internal static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

    internal static IPEndPoint ParseEndpoint(string value)
{
    int split = value.LastIndexOf(':');
    if (split <= 0 || !IPAddress.TryParse(value[..split], out IPAddress? address) ||
        !int.TryParse(value[(split + 1)..], NumberStyles.None,
            CultureInfo.InvariantCulture, out int port))
        throw new ArgumentException("The H0.5 endpoint is invalid.", nameof(value));
    return new IPEndPoint(address, port);
}

    internal static bool Has(string[] values, string exact) =>
    values.Contains(exact, StringComparer.OrdinalIgnoreCase);

    internal static int ReadInt(string[] values, string prefix, int fallback) =>
    int.TryParse(ReadString(values, prefix), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

    internal static string? ReadString(string[] values, string prefix) =>
    values.FirstOrDefault(value =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    internal static string RequiredString(string[] values, string prefix) =>
    ReadString(values, prefix) ??
    throw new ArgumentException($"Missing H0.5 argument {prefix}", nameof(values));

    internal static double Milliseconds(long stopwatchTicks) =>
    stopwatchTicks * 1_000d / Stopwatch.Frequency;

    internal static double Microseconds(long stopwatchTicks) =>
    stopwatchTicks * 1_000_000d / Stopwatch.Frequency;

    internal static MetricEvidence Metric(double[] values)
{
    if (values.Length == 0) return default;
    Array.Sort(values);
    return new(
        values.Average(),
        values[Percentile(values.Length, .50)],
        values[Percentile(values.Length, .95)],
        values[Percentile(values.Length, .99)],
        values[^1]);
}

    private static int Percentile(int count, double fraction) =>
    Math.Clamp((int)Math.Ceiling(count * fraction) - 1, 0, count - 1);

    internal static void Require(bool condition)
{
    if (!condition) throw new InvalidOperationException("The H0.5 evidence guard failed.");
    }
}

readonly record struct ServerReady(string EndPoint, string ServerPublicKey);

readonly record struct ServerEvidence(
    int ExpectedApplications,
    int CompletedApplications,
    ulong ControlsReceived,
    ulong PublicDeltasQueued,
    ulong PublicSnapshotsStarted,
    ulong PublicStateRetriesQueued,
    ulong ChunkProofsApplied,
    ulong OwnerStatesQueued,
    ulong EventsQueued,
    ulong EventProofsApplied,
    ulong DisclosureViolations,
    double WallMilliseconds,
    double ProcessCpuMilliseconds,
    double ServerSemanticMilliseconds,
    double ListenerPumpMilliseconds,
    double OwnerProtocolMilliseconds,
    double ServerTransportMilliseconds,
    ulong ListenerPumps,
    long HostedReservedBytes,
    long ListenerReservedBytes,
    long OwnerRuntimeReservedBytes,
    ulong NativeSendCalls,
    ulong NativeReceiveCalls,
    HostedUdpConnectionOwnerRuntimeSnapshot OwnerRuntime);

readonly record struct ClientEvidence(
    int ClientIndex,
    int CompletedPhases,
    int TicksPerPhase,
    ulong SnapshotsApplied,
    ulong DeltasApplied,
    ulong OwnerStatesApplied,
    ulong EventsApplied,
    bool CompletionAcknowledged,
    ulong PathRebinds,
    ulong DisclosureViolations,
    double WallMilliseconds,
    double ProcessCpuMilliseconds,
    double SocketProtocolMilliseconds,
    double SemanticApplyMilliseconds,
    double[] EndToEndTickMicroseconds);

readonly record struct MetricEvidence(
    double Mean,
    double P50,
    double P95,
    double P99,
    double Maximum);

readonly record struct H05Scope(
    string ProcessTopology,
    string SemanticTraffic,
    string Exclusions);

readonly record struct H05Report(
    string Schema,
    DateTimeOffset CapturedAt,
    string RuntimeVersion,
    string Framework,
    string OperatingSystem,
    string Architecture,
    int ProcessorCount,
    bool ServerGc,
    int ClientWorkers,
    int ConnectionOwners,
    int TicksPerPhase,
    int ReconnectsPerClient,
    ServerEvidence Server,
    ClientEvidence[] Clients,
    H05Scope Scope);
