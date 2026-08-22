using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Spatial;

internal static class N14RemainingAdoptionMatrix
{
    internal static void Run(string[] args)
    {
        int clients = ReadInt(args, "--clients=", 400);
        int entities = ReadInt(args, "--entities=", 1_000);
        int ticks = ReadInt(args, "--ticks=", 120);
        int warmup = ReadInt(args, "--warmup=", 30);
        int dirtyPercent = ReadInt(args, "--dirty-percent=", 2);
        int subscriptionCapacity = ReadInt(args, "--subscription-capacity=", 640);
        int transportRecipients = ReadInt(args, "--transport-recipients=", 64);
        int transportWarmup = ReadInt(args, "--transport-warmup=", 10);
        int transportTicks = ReadInt(args, "--transport-ticks=", 60);
        int townChunks = ReadInt(args, "--town-chunks=", 30);
        int townRows = ReadInt(args, "--town-rows=", 64);
        string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../results/network-n1.4-remaining-adoption-matrix.json"));
        if (clients <= 0 || clients > 1_024 || entities <= 0 || ticks < 30 || warmup < 1 ||
            dirtyPercent is <= 0 or > 100 || subscriptionCapacity <= 0 ||
            transportRecipients <= 0 || transportWarmup < 1 || transportTicks < 5 ||
            townChunks <= 1 || townRows is <= 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(args));

        Console.WriteLine(
            $"N1.4 Dense semantic: clients={clients}, entities={entities}, dirty={dirtyPercent}%...");
        DenseSemanticEvidence dense;
        using (var fixture = new DenseSemanticFixture(
            clients, entities, dirtyPercent, subscriptionCapacity))
            dense = fixture.Measure(warmup, ticks);
        Console.WriteLine(
            $"dense tick p95={dense.TotalMicroseconds.P95:F3} us " +
            $"publication={dense.PublicationMicroseconds.P95:F3} us " +
            $"schedule={dense.ScheduleAndQueryMicroseconds.P95:F3} us " +
            $"queries={dense.PublicationQueries:N0} rows={dense.DeltaRows:N0}");

        Console.WriteLine(
            "N1.4 topology attribution: distributed/representative/teleport/cardinal/diagonal...");
        TopologyEvidence[] topology =
        [
            MeasureTopology(TopologyPath.DistributedCardinal, clients, ticks, subscriptionCapacity),
            MeasureTopology(TopologyPath.RepresentativeRoaming, clients, ticks,
                subscriptionCapacity),
            MeasureTopology(TopologyPath.StaggeredTeleports, clients, ticks,
                subscriptionCapacity),
            MeasureTopology(TopologyPath.SynchronizedCardinal, clients, Math.Min(ticks, 32),
                subscriptionCapacity),
            MeasureTopology(TopologyPath.SynchronizedDiagonal, clients, Math.Min(ticks, 32),
                subscriptionCapacity),
        ];
        foreach (TopologyEvidence item in topology)
            Console.WriteLine(
                $"topology={item.Path} total_p95={item.TotalMicroseconds.P95:F3} us " +
                $"reconcile_p95={item.ReconcileMicroseconds.P95:F3} us " +
                $"scheduler_p95={item.SchedulerMicroseconds.P95:F3} us " +
                $"changes={item.TopologyChanges:N0} exact={item.Attribution.ExactQueries:N0}");

        Console.WriteLine(
            $"N1.4 protocol replay: recipients={transportRecipients}, ticks={transportTicks}; " +
            $"dense entry={townChunks}x{townRows}...");
        TransportAdoptionEvidence transport = N14TransportAdoption.Capture(
            transportRecipients,
            transportWarmup,
            transportTicks,
            townChunks,
            townRows,
            subscriptionCapacity);
        Console.WriteLine(
            $"transport total_p95={transport.ProtocolReplay.TotalMicroseconds.P95:F3} us " +
            $"protocol_p95={transport.ProtocolReplay.ProtocolPumpMicroseconds.P95:F3} us " +
            $"mixed_p95={transport.MixedTraffic.TotalMicroseconds.P95:F3} us " +
            $"socket_p95={transport.SharedSocketReplay.TotalMicroseconds.P95:F3} us " +
            $"town_ms={transport.DenseTownEntry.TotalConvergenceMicroseconds / 1_000:F3} " +
            $"town_snapshots={transport.DenseTownEntry.SnapshotsApplied}");

        var report = new RemainingAdoptionReport(
            "network-n1.4-remaining-adoption-matrix-v3",
            DateTimeOffset.UtcNow,
            Environment.Version.ToString(),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            GCSettings.IsServerGC,
            Stopwatch.Frequency,
            new MatrixScope(
                "The report separates level-1 server semantic replication from level-2 secure " +
                "deterministic protocol replay. Dense-400 excludes protocol and client CPU; the " +
                "replay includes real framing, security, packetization, decode, semantic apply, " +
                "and proof without sockets. Neither is a controlled full-stack N0/N1 A/B.",
                subscriptionCapacity,
                Marshal.SizeOf<OfflineDirectChunkSubscription>(),
                "Mixed public/private/Event/Control lane traffic is included in deterministic " +
                "replay. Real shared sockets and sharded client processes remain separate " +
                "evidence. No N1.5 or 2K/4K work."),
            dense,
            topology,
            transport);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
            Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static TopologyEvidence MeasureTopology(
        TopologyPath path,
        int recipients,
        int ticks,
        int subscriptionCapacity)
    {
        using var fixture = new TopologyFixture(path, recipients, subscriptionCapacity);
        return fixture.Measure(ticks);
    }

    private static int ReadInt(string[] values, string prefix, int fallback) =>
        int.TryParse(values.FirstOrDefault(value =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..],
            out int parsed) ? parsed : fallback;

    private static string? ReadString(string[] values, string prefix) =>
        values.FirstOrDefault(value =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static WorldPosition Position(long x, long z)
    {
        Check(WorldPosition.TryFromGlobalMillimeters(x, 0, z, out WorldPosition position));
        return position;
    }

    private static int FindSubscription(
        OfflineDirectChunkSubscriptionView subscriptions,
        SpatialChunkKey key)
    {
        int low = 0;
        int high = subscriptions.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = subscriptions[middle].Interest.Key.CompareTo(key);
            if (comparison == 0) return middle;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("N1.4 remaining-adoption matrix guard failed.");
    }

    private sealed class DenseSemanticFixture : IDisposable
    {
        private const int SpacingMillimeters = 4_000;
        private readonly int _clients;
        private readonly int _entitiesCount;
        private readonly int _dirtyCount;
        private readonly int _subscriptionCapacity;
        private readonly EcsWorld _world;
        private readonly Entity[] _entities;
        private readonly WorldPosition[] _positions;
        private readonly bool[] _sides;
        private readonly WorldPublicChange[] _allChanges;
        private readonly WorldPublicChange[] _dirtyChanges;
        private readonly ChunkPublicationStore _source;
        private readonly OfflineDirectChunkRecipient[] _recipients;
        private readonly ChunkReplicationRecipientId[] _recipientIds;
        private readonly ChunkPublicationWorkScheduler _scheduler;
        private readonly OfflineDirectChunkSubscriptionChange[] _changes;
        private readonly WorldPublicDescriptor _descriptor = new(
            GameWorldPublicPrefabIds.Character,
            WorldYaw.Zero,
            WorldPublicStateFlags.Visible);
        private ulong _step;

        internal DenseSemanticFixture(
            int clients,
            int entities,
            int dirtyPercent,
            int subscriptionCapacity)
        {
            _clients = clients;
            _entitiesCount = entities;
            _dirtyCount = Math.Max(1, checked(entities * dirtyPercent / 100));
            _subscriptionCapacity = subscriptionCapacity;
            _world = new EcsWorld(
                GameSchema.CreateSchema(),
                new EcsWorldOptions(
                    new EntityCapacity(entities),
                    2,
                    4,
                    lifecycleRecordCapacity: checked(entities * 2)));
            ArchetypeHandle empty = _world.RegisterArchetype(
                ReadOnlySpan<ComponentId>.Empty,
                ChunkLayoutOptions.KiB16,
                Math.Max(1, (entities + 255) / 256));
            _world.Seal();
            _world.Initialize();
            _entities = new Entity[entities];
            _positions = new WorldPosition[entities];
            _sides = new bool[entities];
            _allChanges = new WorldPublicChange[entities];
            _dirtyChanges = new WorldPublicChange[_dirtyCount];
            int width = checked((int)Math.Ceiling(Math.Sqrt(entities)));
            for (int index = 0; index < entities; index++)
            {
                Check(_world.TryCreate(empty, out _entities[index]));
                _positions[index] = Position(
                    checked((long)(index % width) * SpacingMillimeters),
                    checked((long)(index / width) * SpacingMillimeters));
                _allChanges[index] = WorldPublicChange.Present(
                    _entities[index], _positions[index], _descriptor);
            }
            _source = new ChunkPublicationStore(
                PublicationEpoch.Create(),
                new ChunkPublicationLimits(
                    entities,
                    checked(entities * 2),
                    64,
                    checked(Math.Max(entities * 8, _dirtyCount * 16))));
            Check(_source.TryCommit(_allChanges, out _));

            _recipients = new OfflineDirectChunkRecipient[clients];
            _recipientIds = new ChunkReplicationRecipientId[clients];
            _scheduler = new ChunkPublicationWorkScheduler(
                _source,
                new ChunkPublicationWorkSchedulerLimits(
                    clients,
                    checked(clients * subscriptionCapacity)));
            _changes = new OfflineDirectChunkSubscriptionChange[subscriptionCapacity];
            for (int index = 0; index < clients; index++)
            {
                var recipient = new OfflineDirectChunkRecipient(
                    new OfflineDirectChunkRecipientLimits(subscriptionCapacity, 64, 64),
                    GameChunkInterestProfile.Default,
                    OfflineDirectChunkCadence.EvidenceProfile);
                _recipients[index] = recipient;
                Check(_scheduler.TryRegister(recipient, out _recipientIds[index]));
                int entity = clients == 1 ? 0 : checked(index * (entities - 1) / (clients - 1));
                Check(recipient.Reconcile(
                    _source,
                    GameWorldState.DefaultSpace,
                    _positions[entity],
                    _changes,
                    out OfflineDirectChunkReconcileResult result) ==
                    OfflineDirectChunkReconcileStatus.Reconciled);
                Check(_scheduler.TryApplyReconciliation(
                    _recipientIds[index],
                    _changes.AsSpan(0, result.ChangeCount),
                    result));
            }
            DrainInitial();
        }

        internal DenseSemanticEvidence Measure(int warmup, int ticks)
        {
            for (int tick = 0; tick < warmup; tick++)
                _ = RunTick(measure: false);

            var total = new double[ticks];
            var preparation = new double[ticks];
            var publication = new double[ticks];
            var observation = new double[ticks];
            var scheduling = new double[ticks];
            ChunkPublicationWorkSchedulerSnapshot schedulerBefore = _scheduler.GetSnapshot();
            ChunkPublicationStoreSnapshot publicationBefore = _source.GetSnapshot();
            ulong deltaReads = 0;
            ulong noChangeReads = 0;
            ulong snapshotReads = 0;
            ulong rows = 0;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < ticks; tick++)
            {
                DenseTick sample = RunTick(measure: true);
                total[tick] = sample.TotalMicroseconds;
                preparation[tick] = sample.PreparationMicroseconds;
                publication[tick] = sample.PublicationMicroseconds;
                observation[tick] = sample.ObservationMicroseconds;
                scheduling[tick] = sample.SchedulingMicroseconds;
                deltaReads += sample.DeltaReads;
                noChangeReads += sample.NoChangeReads;
                snapshotReads += sample.SnapshotReads;
                rows += sample.Rows;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Check(allocated == 0);

            for (int settle = 0; settle < 8 && _scheduler.GetSnapshot().PendingWorkCount != 0; settle++)
            {
                _step++;
                _ = DrainDue(_step);
            }
            Check(_scheduler.GetSnapshot().PendingWorkCount == 0);
            ChunkPublicationWorkSchedulerSnapshot schedulerAfter = _scheduler.GetSnapshot();
            ChunkPublicationStoreSnapshot publicationAfter = _source.GetSnapshot();
            long recipientReserved = 0;
            int minimumSubscriptions = int.MaxValue;
            int maximumSubscriptions = 0;
            for (int index = 0; index < _recipients.Length; index++)
            {
                OfflineDirectChunkRecipientSnapshot snapshot = _recipients[index].GetSnapshot();
                recipientReserved = checked(recipientReserved + snapshot.ReservedBytes);
                minimumSubscriptions = Math.Min(minimumSubscriptions, snapshot.SubscriptionCount);
                maximumSubscriptions = Math.Max(maximumSubscriptions, snapshot.SubscriptionCount);
            }
            return new DenseSemanticEvidence(
                _clients,
                _entitiesCount,
                _dirtyCount,
                warmup,
                ticks,
                SpacingMillimeters,
                "One approximately 126 m hotspot; recipient anchors are distributed through the " +
                "same entity grid and every entity remains in each recipient's Far envelope.",
                minimumSubscriptions,
                maximumSubscriptions,
                schedulerAfter.MembershipCount,
                schedulerAfter.SharedChunkCount,
                publicationAfter.ResidentChunkCount,
                schedulerAfter.ReservedBytes,
                recipientReserved,
                publicationAfter.ReservedBytes,
                allocated,
                schedulerAfter.DirtyChunksObserved - schedulerBefore.DirtyChunksObserved,
                schedulerAfter.DirtyRecipientFanout - schedulerBefore.DirtyRecipientFanout,
                schedulerAfter.DirtyWorkCoalesces - schedulerBefore.DirtyWorkCoalesces,
                schedulerAfter.DueWorkDequeued - schedulerBefore.DueWorkDequeued,
                publicationAfter.DeltaReads - publicationBefore.DeltaReads,
                deltaReads,
                noChangeReads,
                snapshotReads,
                rows,
                Metric(total),
                Metric(preparation),
                Metric(publication),
                Metric(observation),
                Metric(scheduling));
        }

        private DenseTick RunTick(bool measure)
        {
            _step++;
            int range = _entitiesCount - _dirtyCount + 1;
            int start = checked((int)((_step * (ulong)_dirtyCount) % (ulong)range));
            long started = Stopwatch.GetTimestamp();
            for (int row = 0; row < _dirtyCount; row++)
            {
                int entity = start + row;
                _sides[entity] = !_sides[entity];
                WorldPosition current = _positions[entity];
                long x = checked(current.GlobalXMillimeters + (_sides[entity] ? 1 : 0));
                _dirtyChanges[row] = WorldPublicChange.Present(
                    _entities[entity],
                    Position(x, current.GlobalZMillimeters),
                    _descriptor);
            }
            long prepared = Stopwatch.GetTimestamp();
            Check(_source.TryCommit(_dirtyChanges, out _));
            long published = Stopwatch.GetTimestamp();
            Check(_scheduler.ObserveCommit(_source) ==
                ChunkPublicationCommitObservationStatus.Observed);
            long observed = Stopwatch.GetTimestamp();
            DenseDrain drained = DrainDue(_step);
            long stopped = Stopwatch.GetTimestamp();
            if (!measure) return default;
            return new DenseTick(
                Microseconds(stopped - started),
                Microseconds(prepared - started),
                Microseconds(published - prepared),
                Microseconds(observed - published),
                Microseconds(stopped - observed),
                drained.DeltaReads,
                drained.NoChangeReads,
                drained.SnapshotReads,
                drained.Rows);
        }

        private DenseDrain DrainDue(ulong step)
        {
            ulong deltas = 0;
            ulong noChanges = 0;
            ulong snapshots = 0;
            ulong rows = 0;
            while (_scheduler.TryDequeueDue(step, out ChunkPublicationWork work))
            {
                OfflineDirectChunkRecipient recipient = _recipients[work.Recipient.Index];
                int subscription = FindSubscription(recipient.Subscriptions, work.Key);
                Check(subscription >= 0);
                OfflineDirectChunkReadStatus status = recipient.TryBeginRead(
                    _source, subscription, step, out OfflineDirectChunkRead read);
                switch (status)
                {
                    case OfflineDirectChunkReadStatus.Delta:
                        deltas++;
                        rows += checked((ulong)read.Deltas.Length);
                        break;
                    case OfflineDirectChunkReadStatus.NoChanges:
                        noChanges++;
                        break;
                    case OfflineDirectChunkReadStatus.Snapshot:
                        snapshots++;
                        rows += checked((ulong)read.Snapshot.Length);
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected dense read {status}.");
                }
                recipient.CommitRead();
            }
            return new DenseDrain(deltas, noChanges, snapshots, rows);
        }

        private void DrainInitial()
        {
            int drained = 0;
            while (_scheduler.TryDequeueDue(0, out ChunkPublicationWork work))
            {
                OfflineDirectChunkRecipient recipient = _recipients[work.Recipient.Index];
                int subscription = FindSubscription(recipient.Subscriptions, work.Key);
                Check(subscription >= 0);
                OfflineDirectChunkReadStatus status = recipient.TryBeginRead(
                    _source, subscription, 0, out _);
                Check(status is OfflineDirectChunkReadStatus.Snapshot or
                    OfflineDirectChunkReadStatus.ChunkAbsent);
                recipient.CommitRead();
                drained++;
            }
            Check(drained == _scheduler.GetSnapshot().MembershipCount);
        }

        public void Dispose() => _world.Dispose();
    }

    private enum TopologyPath : byte
    {
        DistributedCardinal = 1,
        RepresentativeRoaming = 2,
        StaggeredTeleports = 3,
        SynchronizedCardinal = 4,
        SynchronizedDiagonal = 5,
    }

    private sealed class TopologyFixture : IDisposable
    {
        private readonly TopologyPath _path;
        private readonly int _recipientsCount;
        private readonly int _changeCapacity;
        private readonly ChunkPublicationStore _source = new(
            PublicationEpoch.Create(),
            new ChunkPublicationLimits(1, 2, 1, 4));
        private readonly OfflineDirectChunkRecipient[] _recipients;
        private readonly ChunkReplicationRecipientId[] _ids;
        private readonly ChunkPublicationWorkScheduler _scheduler;
        private readonly OfflineDirectChunkSubscriptionChange[] _changes;
        private readonly OfflineDirectChunkReconcileResult[] _results;

        internal TopologyFixture(
            TopologyPath path,
            int recipients,
            int subscriptionCapacity)
        {
            _path = path;
            _recipientsCount = recipients;
            _changeCapacity = checked(subscriptionCapacity * 2);
            _recipients = new OfflineDirectChunkRecipient[recipients];
            _ids = new ChunkReplicationRecipientId[recipients];
            _scheduler = new ChunkPublicationWorkScheduler(
                _source,
                new ChunkPublicationWorkSchedulerLimits(
                    recipients,
                    checked(recipients * subscriptionCapacity)));
            _changes = new OfflineDirectChunkSubscriptionChange[
                checked(recipients * _changeCapacity)];
            _results = new OfflineDirectChunkReconcileResult[recipients];
            for (int index = 0; index < recipients; index++)
            {
                var recipient = new OfflineDirectChunkRecipient(
                    new OfflineDirectChunkRecipientLimits(subscriptionCapacity, 1, 1),
                    GameChunkInterestProfile.Default,
                    OfflineDirectChunkCadence.EvidenceProfile);
                _recipients[index] = recipient;
                Check(_scheduler.TryRegister(recipient, out _ids[index]));
                WorldPosition anchor = Anchor(index, 0);
                Span<OfflineDirectChunkSubscriptionChange> destination =
                    _changes.AsSpan(index * _changeCapacity, _changeCapacity);
                Check(recipient.Reconcile(
                    _source,
                    GameWorldState.DefaultSpace,
                    anchor,
                    destination,
                    out OfflineDirectChunkReconcileResult result) ==
                    OfflineDirectChunkReconcileStatus.Reconciled);
                Check(_scheduler.TryApplyReconciliation(
                    _ids[index], destination[..result.ChangeCount], result));
                recipient.ResetReconcileAttribution(enabled: true);
            }
            while (_scheduler.TryDequeueDue(0, out _)) { }
        }

        internal TopologyEvidence Measure(int ticks)
        {
            var total = new double[ticks];
            var reconcile = new double[ticks];
            var scheduler = new double[ticks];
            ulong changes = 0;
            int maximumChanges = 0;
            int ticksWithChanges = 0;
            int boundaryCrossings = 0;
            int cornerCrossings = 0;
            int teleports = 0;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 1; tick <= ticks; tick++)
            {
                long started = Stopwatch.GetTimestamp();
                int tickChanges = 0;
                for (int index = 0; index < _recipients.Length; index++)
                {
                    WorldPosition previousAnchor = Anchor(index, tick - 1);
                    WorldPosition currentAnchor = Anchor(index, tick);
                    ClassifyTransition(previousAnchor, currentAnchor,
                        ref boundaryCrossings, ref cornerCrossings, ref teleports);
                    Span<OfflineDirectChunkSubscriptionChange> destination =
                        _changes.AsSpan(index * _changeCapacity, _changeCapacity);
                    Check(_recipients[index].Reconcile(
                        _source,
                        GameWorldState.DefaultSpace,
                        currentAnchor,
                        destination,
                        out _results[index]) == OfflineDirectChunkReconcileStatus.Reconciled);
                    tickChanges += _results[index].ChangeCount;
                }
                long reconciled = Stopwatch.GetTimestamp();
                for (int index = 0; index < _recipients.Length; index++)
                {
                    Span<OfflineDirectChunkSubscriptionChange> destination =
                        _changes.AsSpan(index * _changeCapacity, _changeCapacity);
                    Check(_scheduler.TryApplyReconciliation(
                        _ids[index], destination[.._results[index].ChangeCount], _results[index]));
                }
                long scheduled = Stopwatch.GetTimestamp();
                while (_scheduler.TryDequeueDue(checked((ulong)tick), out _)) { }
                total[tick - 1] = Microseconds(scheduled - started);
                reconcile[tick - 1] = Microseconds(reconciled - started);
                scheduler[tick - 1] = Microseconds(scheduled - reconciled);
                changes += checked((ulong)tickChanges);
                maximumChanges = Math.Max(maximumChanges, tickChanges);
                if (tickChanges != 0) ticksWithChanges++;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Check(allocated == 0);
            OfflineDirectChunkReconcileAttribution attribution = SumAttribution();
            return new TopologyEvidence(
                _path.ToString(),
                _recipientsCount,
                ticks,
                changes,
                maximumChanges,
                ticksWithChanges,
                boundaryCrossings,
                cornerCrossings,
                teleports,
                _scheduler.GetSnapshot().MembershipCount,
                allocated,
                Metric(total),
                Metric(reconcile),
                Metric(scheduler),
                attribution,
                "Attribution is opt-in and therefore diagnostic. Phase counters are raw Stopwatch " +
                "ticks inside the recipient; scheduler index mutation is timed separately. Work " +
                "dequeue is excluded from TotalMicroseconds.");
        }

        private OfflineDirectChunkReconcileAttribution SumAttribution()
        {
            ulong stable = 0;
            ulong exact = 0;
            ulong copy = 0;
            ulong query = 0;
            ulong count = 0;
            ulong build = 0;
            ulong emit = 0;
            ulong publish = 0;
            for (int index = 0; index < _recipients.Length; index++)
            {
                OfflineDirectChunkReconcileAttribution item =
                    _recipients[index].GetReconcileAttribution();
                stable += item.StableFastPaths;
                exact += item.ExactQueries;
                copy += item.PreviousInterestCopyTicks;
                query += item.ExactQueryTicks;
                count += item.ChangeCountTicks;
                build += item.NextBankBuildTicks;
                emit += item.ChangeEmissionTicks;
                publish += item.PublicationTicks;
            }
            return new OfflineDirectChunkReconcileAttribution(
                true, stable, exact, copy, query, count, build, emit, publish);
        }

        private WorldPosition Anchor(int recipient, int tick)
        {
            if (_path is TopologyPath.RepresentativeRoaming or TopologyPath.StaggeredTeleports)
            {
                const long regionStride = 512_000L;
                long regionX = checked((recipient % 20) * regionStride);
                long regionZ = checked((recipient / 20) * regionStride);
                int direction = recipient & 7;
                long x = checked(regionX + (recipient * 83L) % 16_000L);
                long z = checked(regionZ + (recipient * 197L) % 16_000L);
                if (direction >= 4)
                {
                    long phase = 1L + (recipient * 9_973L) % 15_999L;
                    x = checked(regionX + phase);
                    z = checked(regionZ +
                        (direction is 4 or 7 ? phase : 16_000L - phase));
                }
                long cardinal = checked(tick * 100L);
                long diagonal = checked(tick * 71L);
                switch (direction)
                {
                    case 0: x += cardinal; break;
                    case 1: x -= cardinal; break;
                    case 2: z += cardinal; break;
                    case 3: z -= cardinal; break;
                    case 4: x += diagonal; z += diagonal; break;
                    case 5: x += diagonal; z -= diagonal; break;
                    case 6: x -= diagonal; z += diagonal; break;
                    default: x -= diagonal; z -= diagonal; break;
                }
                if (_path == TopologyPath.StaggeredTeleports)
                {
                    int phase = checked((recipient * 37) % 120);
                    long jumps = (tick + (long)phase) / 120L;
                    long sign = (recipient & 1) == 0 ? 1L : -1L;
                    x = checked(x + sign * jumps * 256_000L);
                    z = checked(z - sign * jumps * 128_000L);
                }
                return Position(x, z);
            }
            return _path switch
            {
                TopologyPath.DistributedCardinal => Position(
                    checked((recipient * 83L) % 32_000L + tick * 100L),
                    checked((recipient * 197L) % 32_000L)),
                TopologyPath.SynchronizedCardinal =>
                    (tick & 1) == 0 ? Position(8_000, 8_000) : Position(40_000, 8_000),
                TopologyPath.SynchronizedDiagonal =>
                    (tick & 1) == 0 ? Position(8_000, 8_000) : Position(40_000, 40_000),
                _ => throw new ArgumentOutOfRangeException(),
            };
        }

        private static void ClassifyTransition(
            in WorldPosition previous,
            in WorldPosition current,
            ref int boundaryCrossings,
            ref int cornerCrossings,
            ref int teleports)
        {
            long deltaX = Math.Abs((long)current.Chunk.X - previous.Chunk.X);
            long deltaZ = Math.Abs((long)current.Chunk.Z - previous.Chunk.Z);
            if (deltaX > 1 || deltaZ > 1)
            {
                teleports++;
                return;
            }
            if (deltaX != 0 && deltaZ != 0) cornerCrossings++;
            else if (deltaX != 0 || deltaZ != 0) boundaryCrossings++;
        }

        public void Dispose() { }
    }

    private static N14Metric Metric(double[] samples)
    {
        Array.Sort(samples);
        return new N14Metric(
            samples.Average(),
            samples[Percentile(samples.Length, .50)],
            samples[Percentile(samples.Length, .95)],
            samples[Percentile(samples.Length, .99)],
            samples[^1]);
    }

    private static int Percentile(int count, double fraction) =>
        Math.Clamp((int)Math.Ceiling(count * fraction) - 1, 0, count - 1);

    private static double Microseconds(long ticks) =>
        ticks * 1_000_000d / Stopwatch.Frequency;

    private readonly record struct DenseTick(
        double TotalMicroseconds,
        double PreparationMicroseconds,
        double PublicationMicroseconds,
        double ObservationMicroseconds,
        double SchedulingMicroseconds,
        ulong DeltaReads,
        ulong NoChangeReads,
        ulong SnapshotReads,
        ulong Rows);

    private readonly record struct DenseDrain(
        ulong DeltaReads,
        ulong NoChangeReads,
        ulong SnapshotReads,
        ulong Rows);
}

internal readonly record struct N14Metric(
    double Mean,
    double P50,
    double P95,
    double P99,
    double Maximum);

internal readonly record struct DenseSemanticEvidence(
    int Recipients,
    int Entities,
    int DirtyEntitiesPerTick,
    int WarmupTicks,
    int MeasuredTicks,
    int EntitySpacingMillimeters,
    string SpatialShape,
    int MinimumSubscriptionsPerRecipient,
    int MaximumSubscriptionsPerRecipient,
    int Memberships,
    int SharedChunks,
    int PopulatedPublicationChunks,
    long SchedulerReservedBytes,
    long RecipientReservedBytes,
    long PublicationReservedBytes,
    long AllocatedBytes,
    ulong DirtyChunksObserved,
    ulong DirtyRecipientFanout,
    ulong DirtyWorkCoalesces,
    ulong DueWorkDequeued,
    ulong PublicationQueries,
    ulong DeltaReads,
    ulong NoChangeReads,
    ulong SnapshotReads,
    ulong DeltaRows,
    N14Metric TotalMicroseconds,
    N14Metric ChangePreparationMicroseconds,
    N14Metric PublicationMicroseconds,
    N14Metric ObserveDirtyMicroseconds,
    N14Metric ScheduleAndQueryMicroseconds);

internal readonly record struct TopologyEvidence(
    string Path,
    int Recipients,
    int Ticks,
    ulong TopologyChanges,
    int MaximumTopologyChangesPerTick,
    int TicksWithTopologyChanges,
    int BoundaryCrossings,
    int CornerCrossings,
    int Teleports,
    int FinalMemberships,
    long AllocatedBytes,
    N14Metric TotalMicroseconds,
    N14Metric ReconcileMicroseconds,
    N14Metric SchedulerMicroseconds,
    OfflineDirectChunkReconcileAttribution Attribution,
    string Notes);

internal readonly record struct MatrixScope(
    string EvidenceLevel,
    int MaximumSubscriptionsPerRecipient,
    int PublicSubscriptionValueBytes,
    string DeferredEvidence);

internal readonly record struct RemainingAdoptionReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string RuntimeVersion,
    string Framework,
    string OperatingSystem,
    string Architecture,
    int LogicalProcessors,
    bool ServerGc,
    long StopwatchFrequency,
    MatrixScope Scope,
    DenseSemanticEvidence Dense400Semantic,
    TopologyEvidence[] Topology,
    TransportAdoptionEvidence Transport);
