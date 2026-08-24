using System.Diagnostics;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

internal static class P03FourProfileComparison
{
    private const int WarmupTicks = 4;
    private const int MeasuredTicks = 12;
    private const int DrainTicks = 32;
    private const int ByteBudget = 16_384;
    private const int EntityRecordBytes = 40;
    private const int ChunkRecordBytes = ChunkPublicationSemanticCodec.RecordBytes;
    private const int MaxEntities = 400;
    private const int MaxRecipients = 16;
    private const int MaxSubscriptionsPerRecipient = 1_024;

    internal static void Run(string[] args)
    {
        int requestedEntities = ReadInt(args, "--entities=", 128);
        int requestedRecipients = ReadInt(args, "--recipients=", 8);
        int warmup = ReadInt(args, "--warmup=", WarmupTicks);
        int samples = ReadInt(args, "--samples=", MeasuredTicks);
        string? requestedWorkload = ReadString(args, "--workload=");
        string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../results/game-p03-four-profile-matrix.json"));
        if (requestedEntities <= 0 || requestedEntities > MaxEntities ||
            requestedRecipients <= 0 || requestedRecipients > MaxRecipients ||
            warmup < 1 || samples < 3)
            throw new ArgumentOutOfRangeException(nameof(args));

        Workload[] workloads = BuildWorkloads(requestedEntities, requestedRecipients, warmup + samples);
        if (!string.IsNullOrWhiteSpace(requestedWorkload))
            workloads = workloads.Where(item =>
                string.Equals(item.Id, requestedWorkload, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (workloads.Length == 0)
            throw new ArgumentException("The requested P0.3 workload was not found.", nameof(args));

        var reports = new List<WorkloadReport>(workloads.Length);
        foreach (Workload workload in workloads)
        {
            Console.WriteLine($"P0.3 workload={workload.Id} entities={workload.Entities.Length} " +
                $"recipients={workload.Recipients.Length} ticks={workload.Ticks.Length}");
            ProfileReport[] profiles = Enum.GetValues<ProfileKind>()
                .Select(profile => RunProfile(workload, profile, warmup, samples))
                .ToArray();
            bool allConverged = profiles.All(profile => profile.Semantic.Converged);
            bool equivalent = profiles.All(profile =>
                string.Equals(profile.Semantic.Digest, profiles[0].Semantic.Digest,
                    StringComparison.Ordinal));
            WorkloadReport report = new(
                workload.Id,
                workload.Entities.Length,
                workload.Recipients.Length,
                workload.Ticks.Length,
                workload.CommonTraffic,
                allConverged,
                equivalent,
                profiles);
            reports.Add(report);
            Console.WriteLine(
                $"  converged={allConverged} equivalent={equivalent} " +
                string.Join(" | ", profiles.Select(profile =>
                    $"{profile.Profile}:p95={profile.Timing.P95Microseconds:F2}us " +
                    $"work={profile.Work.DiscoveryOperations + profile.Work.PublicationReads:N0} " +
                    $"wire={profile.Work.WireBytes:N0}B")));
        }

        var matrix = new ComparisonReport(
            "ultrasim-game-p03-four-profile-shared-trace-v1",
            DateTimeOffset.UtcNow,
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            new MatrixScope(
                "All four frozen Game profiles replay the same authoritative entity, recipient, " +
                "mutation, movement, login/re-entry, and traffic trace. Measurements cover the " +
                "profile semantic core and a common synthesized client/proof boundary; the " +
                "secure Network transport is held constant but is not included in the timed core " +
                "figures.",
                "Release; fixed trace cadence; warmup excluded; application proof is immediate " +
                "after each materialized delivery; public/private/Event/Control traffic counts " +
                "are identical in every profile row.",
                "ChunkPublication remains selected; this matrix is evidence only."),
            reports.ToArray());
        string json = JsonSerializer.Serialize(matrix, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, json + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static ProfileReport RunProfile(
        Workload workload,
        ProfileKind profile,
        int warmup,
        int samples)
    {
        using ProfileAdapter adapter = CreateAdapter(workload, profile);
        adapter.Initialize();
        for (int tick = 0; tick < warmup; tick++)
            adapter.Apply(workload.Ticks[tick], tick);

        var timings = new double[samples];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int sample = 0; sample < samples; sample++)
        {
            int tick = warmup + sample;
            long started = Stopwatch.GetTimestamp();
            adapter.Apply(workload.Ticks[tick], tick);
            timings[sample] = Microseconds(Stopwatch.GetTimestamp() - started);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        int convergenceTicks = 0;
        while (!adapter.IsConverged && convergenceTicks < DrainTicks)
        {
            convergenceTicks++;
            adapter.Drain(warmup + samples + convergenceTicks);
            adapter.RefreshSemanticStatus();
        }
        adapter.AssertSemanticContract();
        CumulativeWork work = adapter.GetWork();
        return new ProfileReport(
            profile.ToString(),
            new TimingSummary(Stats(timings), allocated),
            work,
            new SemanticSummary(
                adapter.IsConverged,
                convergenceTicks,
                adapter.SemanticDigest,
                adapter.DeliveredEntities,
                adapter.ExpectedEntities,
                adapter.MaxPending,
                adapter.MaxOutstanding));
    }

    private static ProfileAdapter CreateAdapter(Workload workload, ProfileKind profile) => profile switch
    {
        ProfileKind.MaintainedEntityRelation => new MaintainedAdapter(workload),
        ProfileKind.ChunkPublication => new ChunkPublicationAdapter(workload),
        ProfileKind.DirtyEntitySpatialFanout => new DirtyEntityAdapter(workload),
        ProfileKind.DirtyChunkSpatialFanout => new DirtyChunkAdapter(workload),
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    private static Workload[] BuildWorkloads(int entities, int recipients, int ticks)
    {
        int denseEntities = Math.Min(MaxEntities, Math.Max(entities, 400));
        int denseRecipients = Math.Min(MaxRecipients, Math.Max(recipients, 16));
        return
        [
            CreateWorkload("stable-world", entities, recipients, ticks, WorkloadShape.Stable),
            CreateWorkload("one-dirty-entity", entities, recipients, ticks, WorkloadShape.OneDirty),
            CreateWorkload("many-dirty-one-chunk", entities, recipients, ticks, WorkloadShape.ClusteredDirty),
            CreateWorkload("distributed-dirty-chunks", entities, recipients, ticks, WorkloadShape.DistributedDirty),
            CreateWorkload("mostly-clean-recipient-movement", entities, recipients, ticks, WorkloadShape.Movement),
            CreateWorkload("active-town-mixed", entities, recipients, ticks, WorkloadShape.Mixed),
            CreateWorkload("dense-400", denseEntities, denseRecipients, ticks, WorkloadShape.Dense),
            CreateWorkload("edge-corner", entities, recipients, ticks, WorkloadShape.EdgeCorner),
            CreateWorkload("teleport-login-reentry", entities, recipients, ticks, WorkloadShape.TeleportReentry),
            CreateWorkload("high-dirt-low-movement", entities, recipients, ticks, WorkloadShape.HighDirt),
            CreateWorkload("high-movement-low-dirt", entities, recipients, ticks, WorkloadShape.HighMovement),
        ];
    }

    private static Workload CreateWorkload(
        string id,
        int entityCount,
        int recipientCount,
        int ticks,
        WorkloadShape shape)
    {
        var entities = new TraceEntity[entityCount];
        int width = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(entityCount)));
        for (int index = 0; index < entities.Length; index++)
        {
            long x = (index - entityCount / 2) * 64_000L + 24_000L;
            long z = 8_000L;
            if (shape == WorkloadShape.EdgeCorner)
            {
                x = (index % 4) switch
                {
                    0 => -320_000,
                    1 => 320_000,
                    2 => -160_000,
                    _ => 160_000,
                };
                z = ((index / 4) % 4) switch
                {
                    0 => -320_000,
                    1 => 320_000,
                    2 => -160_000,
                    _ => 160_000,
                };
            }
            entities[index] = new(index, true, index % 17 != 0, x, z, 1);
        }

        var recipients = new TraceRecipient[recipientCount];
        for (int index = 0; index < recipients.Length; index++)
        {
            long x = (index - recipientCount / 2) * 128_000L;
            long z = 0;
            recipients[index] = new(index, true, x, z);
        }

        TraceEntity[] workingEntities = entities.ToArray();
        TraceRecipient[] workingRecipients = recipients.ToArray();

        var ticksData = new TraceTick[ticks];
        for (int tick = 0; tick < ticks; tick++)
        {
            var mutations = new List<EntityMutation>();
            var moves = new List<RecipientMove>();
            switch (shape)
            {
                case WorkloadShape.OneDirty when tick == 2:
                    AddMutation(mutations, workingEntities, 0, 1_000, 0);
                    break;
                case WorkloadShape.ClusteredDirty:
                    for (int index = 0; index < Math.Min(32, entityCount); index++)
                        AddMutation(mutations, workingEntities, index, 750 + tick * 5L, 500);
                    break;
                case WorkloadShape.DistributedDirty:
                    for (int offset = 0; offset < Math.Min(16, entityCount); offset++)
                    {
                        int index = (tick * 29 + offset * 17) % entityCount;
                        AddMutation(mutations, workingEntities, index, 1_000, -750);
                    }
                    break;
                case WorkloadShape.Movement:
                    AddMovement(moves, workingRecipients, tick, 512_000, 0);
                    break;
                case WorkloadShape.Mixed:
                    if (tick == 0)
                        AddMovement(moves, workingRecipients, tick, 512_000, 0);
                    for (int offset = 0; offset < Math.Min(12, entityCount); offset++)
                        AddMutation(mutations, workingEntities, (tick * 11 + offset * 7) % entityCount, 500, 250);
                    break;
                case WorkloadShape.Dense:
                    for (int offset = 0; offset < Math.Min(32, entityCount); offset++)
                        AddMutation(mutations, workingEntities, (tick * 13 + offset * 3) % entityCount, 400, 100);
                    if (tick == 0) AddMovement(moves, workingRecipients, tick, 512_000, 0);
                    break;
                case WorkloadShape.EdgeCorner:
                    AddMovement(moves, workingRecipients, tick, tick % 2 == 0 ? 512_000 : -512_000, 0);
                    for (int offset = 0; offset < Math.Min(4, entityCount); offset++)
                        AddMutation(mutations, workingEntities, offset, tick % 2 == 0 ? 800 : -800, 0);
                    break;
                case WorkloadShape.TeleportReentry:
                    if (tick == 1)
                    {
                        moves.Add(new RecipientMove(0, true, 1_000_000, 1_000_000));
                        workingRecipients[0] = new TraceRecipient(0, true, 1_000_000, 1_000_000);
                    }
                    if (tick == 2)
                    {
                        moves.Add(new RecipientMove(0, false, 1_000_000, 1_000_000));
                        workingRecipients[0] = new TraceRecipient(0, false, 1_000_000, 1_000_000);
                    }
                    if (tick == 3)
                    {
                        moves.Add(new RecipientMove(0, true, 0, 0));
                        workingRecipients[0] = new TraceRecipient(0, true, 0, 0);
                    }
                    if (tick == 4 && entityCount > 0)
                        AddMutation(mutations, workingEntities, 0, 2_000, 0);
                    break;
                case WorkloadShape.HighDirt:
                    for (int offset = 0; offset < Math.Max(1, entityCount * 3 / 4); offset++)
                        AddMutation(mutations, workingEntities, (tick * 19 + offset) % entityCount, 250, 250);
                    break;
                case WorkloadShape.HighMovement:
                    AddMovement(moves, workingRecipients, tick, 512_000, 0);
                    if ((tick & 3) == 0 && entityCount > 0)
                        AddMutation(mutations, workingEntities, tick % entityCount, 1_000, 0);
                    break;
            }
            ticksData[tick] = new TraceTick(mutations.ToArray(), moves.ToArray());
        }

        int publicFacts = entities.Count(entity => entity.Present && entity.Public);
        var traffic = new CommonTraffic(
            PublicFacts: checked(publicFacts + ticksData.Sum(item => item.Mutations.Length)),
            PrivateFacts: checked(entities.Count(entity => !entity.Public) + ticksData.Sum(item =>
                item.Mutations.Count(mutation => !entities[mutation.EntityIndex].Public))),
            EventFacts: checked(ticks * Math.Max(1, recipientCount / 4)),
            ControlFacts: checked(ticks * (recipientCount + 1)));
        return new Workload(id, entities, recipients, ticksData, traffic);
    }

    private static void AddMutation(
        List<EntityMutation> mutations,
        TraceEntity[] entities,
        int index,
        long dx,
        long dz)
    {
        TraceEntity current = entities[index];
        mutations.Add(new EntityMutation(index, current.X + dx, current.Z + dz,
            current.Present, current.Public));
        entities[index] = current with { X = current.X + dx, Z = current.Z + dz };
    }

    private static void AddMovement(
        List<RecipientMove> moves,
        TraceRecipient[] recipients,
        int tick,
        long dx,
        long dz)
    {
        for (int index = 0; index < recipients.Length; index++)
        {
            TraceRecipient current = recipients[index];
            if (!current.Active) continue;
            long nextX = current.X + dx * (index + 1);
            long nextZ = current.Z + dz * (tick + 1);
            moves.Add(new RecipientMove(index, true, nextX, nextZ));
            recipients[index] = new TraceRecipient(index, true, nextX, nextZ);
        }
    }

    private static double Microseconds(long ticks) =>
        ticks * 1_000_000.0 / Stopwatch.Frequency;

    private static StatsSummary Stats(double[] values)
    {
        Array.Sort(values);
        return new(
            values[values.Length / 2],
            values[(int)Math.Ceiling(values.Length * 0.95) - 1],
            values[^1]);
    }

    private static int ReadInt(string[] args, string prefix, int fallback) =>
        int.TryParse(ReadString(args, prefix), out int value) ? value : fallback;

    private static string? ReadString(string[] args, string prefix) =>
        args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
            [prefix.Length..];

    private enum ProfileKind : byte
    {
        MaintainedEntityRelation,
        ChunkPublication,
        DirtyEntitySpatialFanout,
        DirtyChunkSpatialFanout,
    }

    private enum WorkloadShape : byte
    {
        Stable,
        OneDirty,
        ClusteredDirty,
        DistributedDirty,
        Movement,
        Mixed,
        Dense,
        EdgeCorner,
        TeleportReentry,
        HighDirt,
        HighMovement,
    }

    private readonly record struct TraceEntity(
        int Index,
        bool Present,
        bool Public,
        long X,
        long Z,
        ulong Version);

    private readonly record struct TraceRecipient(int Index, bool Active, long X, long Z);

    private readonly record struct EntityMutation(
        int EntityIndex,
        long X,
        long Z,
        bool Present,
        bool Public);

    private readonly record struct RecipientMove(
        int RecipientIndex,
        bool Active,
        long X,
        long Z);

    private readonly record struct TraceTick(
        EntityMutation[] Mutations,
        RecipientMove[] Moves);

    private sealed record Workload(
        string Id,
        TraceEntity[] Entities,
        TraceRecipient[] Recipients,
        TraceTick[] Ticks,
        CommonTraffic CommonTraffic);

    private readonly record struct CommonTraffic(
        int PublicFacts,
        int PrivateFacts,
        int EventFacts,
        int ControlFacts);

    private readonly record struct MatrixScope(
        string MeasurementScope,
        string SharedTraceContract,
        string SelectionPolicy);

    private sealed record ComparisonReport(
        string WorkloadIdentity,
        DateTimeOffset CapturedAtUtc,
        string Runtime,
        string OperatingSystem,
        int ProcessorCount,
        long StopwatchFrequency,
        MatrixScope Scope,
        WorkloadReport[] Workloads);

    private sealed record WorkloadReport(
        string Workload,
        int EntityCount,
        int RecipientCount,
        int TraceTicks,
        CommonTraffic CommonTraffic,
        bool AllProfilesConverged,
        bool SemanticallyEquivalent,
        ProfileReport[] Profiles);

    private sealed record ProfileReport(
        string Profile,
        TimingSummary Timing,
        CumulativeWork Work,
        SemanticSummary Semantic);

    private readonly record struct TimingSummary(
        StatsSummary P50P95MaxMicroseconds,
        long MeasuredManagedBytes)
    {
        public double P50Microseconds => P50P95MaxMicroseconds.P50;
        public double P95Microseconds => P50P95MaxMicroseconds.P95;
    }

    private readonly record struct StatsSummary(double P50, double P95, double Max);

    private readonly record struct SemanticSummary(
        bool Converged,
        int DrainTicks,
        string Digest,
        int DeliveredEntities,
        int ExpectedEntities,
        int MaxPending,
        int MaxOutstanding);

    private readonly record struct CumulativeWork(
        ulong DiscoveryOperations,
        ulong CandidateExamined,
        ulong TopologyOperations,
        ulong PublicationReads,
        ulong SourceReads,
        ulong RecordsMaterialized,
        ulong WireBytes,
        ulong Deliveries,
        ulong Proofs,
        int MaxPending,
        int MaxOutstanding,
        long ReservedBytes);

    private abstract class ProfileAdapter : IDisposable
    {
        protected readonly Workload Workload;
        protected readonly TraceEntity[] State;
        protected readonly TraceRecipient[] Recipients;
        private readonly bool[] _appliedPresent;
        private readonly ulong[] _appliedVersion;
        private readonly int[] _publicSlotEntities;
        private readonly uint[] _publicSlotGenerations;
        private int _deliveredEntities;
        private int _expectedEntities;
        private ulong _wireBytes;
        private ulong _deliveries;
        private ulong _proofs;
        private int _maxPending;
        private int _maxOutstanding;

        protected ProfileAdapter(Workload workload)
        {
            Workload = workload;
            State = workload.Entities.ToArray();
            Recipients = workload.Recipients.ToArray();
            _appliedPresent = new bool[checked(Recipients.Length * State.Length)];
            _appliedVersion = new ulong[_appliedPresent.Length];
            _publicSlotEntities = Enumerable.Repeat(-1, State.Length).ToArray();
            _publicSlotGenerations = new uint[State.Length];
        }

        public abstract void Initialize();

        public void Apply(TraceTick tick, int cadenceStep)
        {
            for (int index = 0; index < tick.Moves.Length; index++)
            {
                RecipientMove move = tick.Moves[index];
                Recipients[move.RecipientIndex] = new(
                    move.RecipientIndex, move.Active, move.X, move.Z);
            }
            for (int index = 0; index < tick.Mutations.Length; index++)
            {
                EntityMutation mutation = tick.Mutations[index];
                TraceEntity prior = State[mutation.EntityIndex];
                State[mutation.EntityIndex] = new(
                    prior.Index,
                    mutation.Present,
                    mutation.Public,
                    mutation.X,
                    mutation.Z,
                    checked(prior.Version + 1));
            }
            ApplyCore(tick, cadenceStep);
            UpdateSemanticStatus();
        }

        public abstract void Drain(int cadenceStep);

        public abstract CumulativeWork GetWork();

        public abstract void AssertSemanticContract();

        public abstract void Dispose();

        protected abstract void ApplyCore(TraceTick tick, int cadenceStep);

        public bool IsConverged { get; private set; }
        public string SemanticDigest { get; private set; } = string.Empty;
        public int DeliveredEntities => _deliveredEntities;
        public int ExpectedEntities => _expectedEntities;
        public int MaxPending => _maxPending;
        public int MaxOutstanding => _maxOutstanding;

        protected void RecordEntity(
            int recipientIndex,
            int entityIndex,
            ulong version,
            bool present)
        {
            if ((uint)recipientIndex >= (uint)Recipients.Length ||
                (uint)entityIndex >= (uint)State.Length)
                return;
            int offset = checked(recipientIndex * State.Length + entityIndex);
            _appliedPresent[offset] = present;
            _appliedVersion[offset] = version;
            _wireBytes = checked(_wireBytes + EntityRecordBytes);
            _deliveries++;
            _proofs++;
        }

        protected void RecordChunkRecord(
            int recipientIndex,
            in WorldPublicRecord record,
            bool present = true)
        {
            int entityIndex = ResolveKnownPublicSlot(record.Id);
            if (entityIndex < 0)
                entityIndex = FindEntityIndex(record.Position);
            RememberPublicSlot(record.Id, entityIndex);
            RecordEntity(recipientIndex, entityIndex,
                entityIndex >= 0 ? State[entityIndex].Version : record.EntityPublicVersion, present);
            _wireBytes = checked(_wireBytes - EntityRecordBytes + ChunkRecordBytes);
        }

        private int FindEntityIndex(in WorldPosition position)
        {
            long x = position.GlobalXMillimeters;
            long z = position.GlobalZMillimeters;
            for (int index = 0; index < State.Length; index++)
            {
                TraceEntity entity = State[index];
                if (entity.Present && entity.Public && entity.X == x && entity.Z == z &&
                    !IsPublicEntityMapped(entityIndex: index))
                    return index;
            }
            throw new InvalidOperationException(
                $"P0.3 publication record position ({x},{z}) has no current trace entity.");
        }

        private int ResolveKnownPublicSlot(in WorldPublicEntityId id)
        {
            if ((uint)id.Index >= (uint)_publicSlotEntities.Length ||
                _publicSlotGenerations[id.Index] != id.Generation)
                return -1;
            return _publicSlotEntities[id.Index];
        }

        private bool IsPublicEntityMapped(int entityIndex)
        {
            for (int index = 0; index < _publicSlotEntities.Length; index++)
                if (_publicSlotEntities[index] == entityIndex)
                    return true;
            return false;
        }

        protected void RecordChunkDelta(
            int recipientIndex,
            in WorldPublicDeltaRecord record)
        {
            if (record.Operation == WorldPublicOperation.Present)
            {
                RecordChunkRecord(recipientIndex, record.Current);
                return;
            }
            int entityIndex = ResolvePublicSlot(record.Id);
            RecordEntity(recipientIndex, entityIndex, State[entityIndex].Version, false);
            _wireBytes = checked(_wireBytes - EntityRecordBytes + ChunkRecordBytes);
        }

        private void RememberPublicSlot(in WorldPublicEntityId id, int entityIndex)
        {
            if ((uint)id.Index >= (uint)_publicSlotEntities.Length)
                throw new InvalidOperationException("P0.3 publication slot exceeded trace capacity.");
            _publicSlotEntities[id.Index] = entityIndex;
            _publicSlotGenerations[id.Index] = id.Generation;
        }

        private int ResolvePublicSlot(in WorldPublicEntityId id)
        {
            if ((uint)id.Index >= (uint)_publicSlotEntities.Length ||
                _publicSlotGenerations[id.Index] != id.Generation ||
                _publicSlotEntities[id.Index] < 0)
                throw new InvalidOperationException("P0.3 publication removal lost its entity identity.");
            return _publicSlotEntities[id.Index];
        }

        protected void ClearChunk(
            int recipientIndex,
            in SpatialChunkKey key)
        {
            for (int entityIndex = 0; entityIndex < State.Length; entityIndex++)
            {
                if (!State[entityIndex].Present ||
                    ToChunkKey(State[entityIndex]) != key) continue;
                RecordEntity(recipientIndex, entityIndex, State[entityIndex].Version, false);
            }
        }

        protected void ClearRecipient(int recipientIndex)
        {
            for (int entityIndex = 0; entityIndex < State.Length; entityIndex++)
            {
                int offset = recipientIndex * State.Length + entityIndex;
                if (_appliedPresent[offset])
                    RecordEntity(recipientIndex, entityIndex, State[entityIndex].Version, false);
            }
        }

        protected void NotePending(int pending, int outstanding)
        {
            _maxPending = Math.Max(_maxPending, pending);
            _maxOutstanding = Math.Max(_maxOutstanding, outstanding);
        }

        protected void AssertConverged()
        {
            if (IsConverged) return;
            for (int recipient = 0; recipient < Recipients.Length; recipient++)
            {
                for (int entity = 0; entity < State.Length; entity++)
                {
                    int offset = recipient * State.Length + entity;
                    bool expected = ExpectedVisible(Recipients[recipient], State[entity]);
                    bool actual = Recipients[recipient].Active && _appliedPresent[offset];
                    if (actual != expected || actual && _appliedVersion[offset] != State[entity].Version)
                        throw new InvalidOperationException(
                            $"P0.3 convergence failed for {GetType().Name}: recipient={recipient}, " +
                            $"entity={entity}, expectedPresent={expected}, actualPresent={actual}, " +
                            $"recipientPosition=({Recipients[recipient].X},{Recipients[recipient].Z}), " +
                            $"entityPosition=({State[entity].X},{State[entity].Z}), " +
                            $"expectedVersion={State[entity].Version}, actualVersion={_appliedVersion[offset]}, " +
                            $"expectedCount={_expectedEntities}, actualCount={_deliveredEntities}.");
                }
            }
            throw new InvalidOperationException($"P0.3 convergence failed for {GetType().Name}.");
        }

        internal void RefreshSemanticStatus() => UpdateSemanticStatus();

        protected ulong Deliveries => _deliveries;
        protected ulong Proofs => _proofs;
        protected ulong WireBytes => _wireBytes;

        protected void RecordDeliveryBytes(int bytes)
        {
            _wireBytes = checked(_wireBytes + checked((ulong)bytes));
            _deliveries++;
            _proofs++;
        }

        protected static ReplicationEntityId ReplicationId(int entityIndex) =>
            new(checked((uint)entityIndex), 1);

        protected static ReplicationSpatialPoint Point(in TraceEntity entity) =>
            new(checked((int)entity.X), checked((int)entity.Z));

        protected static ReplicationSpatialPoint Point(in TraceRecipient recipient) =>
            new(checked((int)recipient.X), checked((int)recipient.Z));

        protected static WorldPosition WorldPosition(in TraceEntity entity)
        {
            if (!global::UltraSim.Game.World.WorldPosition.TryFromGlobalMillimeters(
                    entity.X, 0, entity.Z, out WorldPosition position))
                throw new InvalidOperationException("The P0.3 trace exceeded world bounds.");
            return position;
        }

        protected static WorldPosition WorldPosition(in TraceRecipient recipient)
        {
            if (!global::UltraSim.Game.World.WorldPosition.TryFromGlobalMillimeters(
                    recipient.X, 0, recipient.Z, out WorldPosition position))
                throw new InvalidOperationException("The P0.3 trace exceeded world bounds.");
            return position;
        }

        protected static SpatialChunkKey ToChunkKey(in TraceEntity entity) =>
            WorldPosition(entity).ToChunkKey(GameWorldState.DefaultSpace);

        private void UpdateSemanticStatus()
        {
            _expectedEntities = 0;
            _deliveredEntities = 0;
            bool equal = true;
            ulong hash = 14_695_981_039_346_656_003UL;
            for (int recipient = 0; recipient < Recipients.Length; recipient++)
            {
                for (int entity = 0; entity < State.Length; entity++)
                {
                    bool expected = ExpectedVisible(Recipients[recipient], State[entity]);
                    int offset = recipient * State.Length + entity;
                    bool actual = Recipients[recipient].Active && _appliedPresent[offset];
                    if (expected) _expectedEntities++;
                    if (actual) _deliveredEntities++;
                    if (actual != expected || actual && _appliedVersion[offset] != State[entity].Version)
                        equal = false;
                    hash ^= (ulong)(actual ? 1 : 0);
                    hash = unchecked(hash * 1_099_511_628_211UL);
                    if (actual) hash ^= State[entity].Version;
                    hash = unchecked(hash * 1_099_511_628_211UL);
                }
            }
            IsConverged = equal;
            SemanticDigest = hash.ToString("X16");
        }

        private static bool ExpectedVisible(in TraceRecipient recipient, in TraceEntity entity)
        {
            if (!recipient.Active || !entity.Present || !entity.Public) return false;
            long dx = entity.X - recipient.X;
            long dz = entity.Z - recipient.Z;
            return dx * dx + dz * dz <= 192_000L * 192_000L;
        }
    }

    private sealed class MaintainedAdapter : ProfileAdapter
    {
        private readonly GameInterestService _service;
        private readonly GameWorldStateRecord[] _records;
        private readonly bool[] _serviceActive;

        internal MaintainedAdapter(Workload workload) : base(workload)
        {
            _service = new GameInterestService(new StateEpoch(0xF031),
                CreateInterestLimits(workload), GameInterestProfile.Reference,
                GameInterestAdmissionMode.ExactDistance);
            _records = new GameWorldStateRecord[32];
            _serviceActive = new bool[Recipients.Length];
        }

        public override void Initialize()
        {
            for (int index = 0; index < Recipients.Length; index++)
            {
                if (!Recipients[index].Active) continue;
                Require(_service.SetRecipientAnchor(
                    Participant(index), Point(Recipients[index]), 0) is
                    GameInterestUpdateStatus.Success or GameInterestUpdateStatus.NoChange);
                _serviceActive[index] = true;
            }
            for (int index = 0; index < State.Length; index++)
                ApplyEntity(index);
            Drain(0);
        }

        protected override void ApplyCore(TraceTick tick, int cadenceStep)
        {
            foreach (RecipientMove move in tick.Moves) ApplyRecipient(move.RecipientIndex);
            foreach (EntityMutation mutation in tick.Mutations) ApplyEntity(mutation.EntityIndex);
            Drain(cadenceStep);
        }

        public override void Drain(int cadenceStep)
        {
            while (true)
            {
                GameWorldStateBuildStatus status = _service.TryBuildNextBundle(
                    cadenceStep * 16L, ByteBudget, _records, out GameWorldStateBundle bundle);
                if (status == GameWorldStateBuildStatus.NoData) break;
                Require(status == GameWorldStateBuildStatus.Success);
                int recipientIndex = ParticipantIndex(bundle.Participant);
                for (int index = 0; index < bundle.RecordCount; index++)
                {
                    GameWorldStateRecord record = _records[index];
                    RecordEntity(recipientIndex,
                        checked((int)record.Entity.Index), record.CanonicalVersion, record.IsPresent);
                }
                Require(_service.TryAcknowledgeBundle(
                    bundle.Participant, bundle.BundleId, applied: true));
            }
            GameInterestSnapshot snapshot = _service.GetSnapshot();
            NotePending(snapshot.PendingRelationCount, snapshot.OutstandingBundleCount);
        }

        public override CumulativeWork GetWork()
        {
            GameInterestSnapshot snapshot = _service.GetSnapshot();
            return new(
                snapshot.PendingObligationsCreated,
                snapshot.RelationSequenceAllocations,
                checked((ulong)(snapshot.RelationCount + snapshot.RegionInterestEdgeCount)),
                snapshot.SemanticRecordCopies,
                snapshot.SemanticRecordCopies,
                snapshot.SemanticRecordCopies,
                WireBytes,
                Deliveries,
                Proofs,
                MaxPending,
                MaxOutstanding,
                snapshot.ReservedBytes);
        }

        public override void AssertSemanticContract() => AssertConverged();

        public override void Dispose() { }

        private void ApplyRecipient(int index)
        {
            TraceRecipient recipient = Recipients[index];
            if (!recipient.Active)
            {
                if (_serviceActive[index])
                    Require(_service.RemoveRecipient(Participant(index)));
                _serviceActive[index] = false;
                return;
            }
            GameInterestUpdateStatus status = _service.SetRecipientAnchor(
                Participant(index), Point(recipient), index + 1L);
            if (status is not (GameInterestUpdateStatus.Success or GameInterestUpdateStatus.NoChange))
                throw new InvalidOperationException(
                    $"P0.3 maintained recipient {index} update returned {status} at ({recipient.X},{recipient.Z}).");
            _serviceActive[index] = true;
        }

        private void ApplyEntity(int index)
        {
            TraceEntity entity = State[index];
            if (!entity.Public) return;
            GameReplicationHorizon horizon = entity.Public
                ? GameReplicationHorizon.Far
                : GameReplicationHorizon.None;
            var projected = new GameProjectedEntity(
                ReplicationId(index), true,
                new GameSimulationState { TickMarker = checked((int)entity.Version) });
            if (entity.Present)
                Require(_service.UpsertEntity(
                    projected, entity.Version, Point(entity), horizon, index + 1L) is
                    GameInterestUpdateStatus.Success or GameInterestUpdateStatus.NoChange);
            else
                Require(_service.RemoveEntity(
                    ReplicationId(index), entity.Version, index + 1L) is
                    GameInterestUpdateStatus.Success or GameInterestUpdateStatus.StaleVersion);
        }
    }

    private sealed class DirtyEntityAdapter : ProfileAdapter
    {
        private readonly DirtyEntitySpatialFanout _service;
        private readonly DirtyEntityDeliveryRecord[] _records;
        private readonly bool[] _serviceActive;

        internal DirtyEntityAdapter(Workload workload) : base(workload)
        {
            _service = new DirtyEntitySpatialFanout(
                new StateEpoch(0xF032),
                new DirtyEntitySpatialFanoutLimits(
                    workload.Entities.Length,
                    workload.Recipients.Length,
                    CreateInterestLimits(workload).MaximumRegions,
                    checked(workload.Entities.Length * workload.Recipients.Length),
                    Math.Max(64, workload.Recipients.Length * 4),
                    32),
                GameInterestProfile.Reference);
            _records = new DirtyEntityDeliveryRecord[32];
            _serviceActive = new bool[Recipients.Length];
        }

        public override void Initialize()
        {
            for (int index = 0; index < Recipients.Length; index++)
            {
                if (!Recipients[index].Active) continue;
                Require(_service.SetRecipientAnchor(
                    Participant(index), Point(Recipients[index]), 0) is
                    DirtyEntityUpdateStatus.Success or DirtyEntityUpdateStatus.NoChange);
                _serviceActive[index] = true;
            }
            for (int index = 0; index < State.Length; index++)
                ApplyEntity(index);
            Drain(0);
        }

        protected override void ApplyCore(TraceTick tick, int cadenceStep)
        {
            foreach (RecipientMove move in tick.Moves) ApplyRecipient(move.RecipientIndex);
            foreach (EntityMutation mutation in tick.Mutations) ApplyEntity(mutation.EntityIndex);
            Drain(cadenceStep);
        }

        public override void Drain(int cadenceStep)
        {
            Require(_service.TryProcessDirtyEntities(cadenceStep * 16L, out _) is
                DirtyEntityProcessStatus.Success or DirtyEntityProcessStatus.NoData);
            while (true)
            {
                DirtyEntityBuildStatus status = _service.TryBuildNextBundle(
                    cadenceStep * 16L, ByteBudget, _records, out DirtyEntityBundle bundle);
                if (status == DirtyEntityBuildStatus.NoData) break;
                Require(status == DirtyEntityBuildStatus.Success);
                for (int index = 0; index < bundle.RecordCount; index++)
                {
                    DirtyEntityDeliveryRecord record = _records[index];
                    RecordEntity(ParticipantIndex(bundle.Participant),
                        checked((int)record.Entity.Index), record.CanonicalVersion, record.IsPresent);
                }
                Require(_service.TryAcknowledgeBundle(
                    bundle.Participant, bundle.DeliveryBundleId, applied: true));
            }
            DirtyEntitySpatialFanoutSnapshot snapshot = _service.GetSnapshot();
            NotePending(snapshot.PendingObligationCount, snapshot.OutstandingBundleCount);
        }

        public override CumulativeWork GetWork()
        {
            DirtyEntitySpatialFanoutSnapshot snapshot = _service.GetSnapshot();
            return new(
                snapshot.DirtyEntitiesProcessed,
                snapshot.CandidateRecipientsExamined,
                checked((ulong)(snapshot.SpatialRegionCount + snapshot.SemanticKnowledgeRowCount)),
                snapshot.BundlesBuilt,
                snapshot.DirtyEntitiesProcessed,
                snapshot.BundlesBuilt * 32,
                WireBytes,
                Deliveries,
                Proofs,
                MaxPending,
                MaxOutstanding,
                snapshot.ReservedBytes);
        }

        public override void AssertSemanticContract() => AssertConverged();

        public override void Dispose() { }

        private void ApplyRecipient(int index)
        {
            TraceRecipient recipient = Recipients[index];
            if (!recipient.Active)
            {
                if (_serviceActive[index])
                    Require(_service.RemoveRecipient(Participant(index)));
                _serviceActive[index] = false;
                return;
            }
            Require(_service.SetRecipientAnchor(
                Participant(index), Point(recipient), index + 1L) is
                DirtyEntityUpdateStatus.Success or DirtyEntityUpdateStatus.NoChange);
            _serviceActive[index] = true;
        }

        private void ApplyEntity(int index)
        {
            TraceEntity entity = State[index];
            var projected = new GameProjectedEntity(
                ReplicationId(index), true,
                new GameSimulationState { TickMarker = checked((int)entity.Version) });
            Require(_service.CommitEntity(
                new DirtyEntityLatestState(
                    ReplicationId(index), entity.Version, entity.Present, entity.Public,
                    entity.Present ? GameReplicationHorizon.Far : GameReplicationHorizon.None,
                    Point(entity), projected.SimulationState), index + 1L) is
                DirtyEntityUpdateStatus.Success or DirtyEntityUpdateStatus.NoChange);
        }
    }

    private sealed class ChunkPublicationAdapter : ProfileAdapter
    {
        private readonly PublicationFixture _publication;
        private readonly OfflineDirectChunkRecipient?[] _recipients;
        private readonly ChunkReplicationRecipientId[] _recipientIds;
        private readonly ChunkPublicationWorkScheduler _scheduler;
        private readonly OfflineDirectChunkSubscriptionChange[] _changes;
        private readonly bool[] _registered;

        internal ChunkPublicationAdapter(Workload workload) : base(workload)
        {
            _publication = new PublicationFixture(workload);
            _recipients = new OfflineDirectChunkRecipient[Recipients.Length];
            _recipientIds = new ChunkReplicationRecipientId[Recipients.Length];
            _registered = new bool[Recipients.Length];
            _scheduler = new ChunkPublicationWorkScheduler(
                _publication.Source,
                new ChunkPublicationWorkSchedulerLimits(
                    Recipients.Length,
                    checked(Recipients.Length * MaxSubscriptionsPerRecipient)));
            _changes = new OfflineDirectChunkSubscriptionChange[MaxSubscriptionsPerRecipient];
        }

        public override void Initialize()
        {
            for (int index = 0; index < Recipients.Length; index++)
                ApplyRecipient(index);
            Drain(0);
        }

        protected override void ApplyCore(TraceTick tick, int cadenceStep)
        {
            foreach (RecipientMove move in tick.Moves) ApplyRecipient(move.RecipientIndex);
            _publication.Commit(State, tick.Mutations);
            Require(_scheduler.ObserveCommit(_publication.Source) is
                ChunkPublicationCommitObservationStatus.Observed or
                ChunkPublicationCommitObservationStatus.NoNewCommit);
            Drain(cadenceStep);
        }

        public override void Drain(int cadenceStep)
        {
            int pendingBudget = _scheduler.GetSnapshot().PendingWorkCount;
            while (pendingBudget-- > 0 &&
                   _scheduler.TryDequeueDue(checked((ulong)cadenceStep), out ChunkPublicationWork work))
            {
                int recipientIndex = work.Recipient.Index;
                OfflineDirectChunkRecipient recipient = _recipients[recipientIndex] ??
                    throw new InvalidOperationException("A scheduler work item lost its recipient.");
                int subscription = FindSubscription(recipient, work.Key);
                if (subscription < 0) continue;
                OfflineDirectChunkReadStatus status = recipient.TryBeginRead(
                    _publication.Source, subscription, checked((ulong)cadenceStep), out OfflineDirectChunkRead read);
                if (status == OfflineDirectChunkReadStatus.NotDue)
                {
                    Require(_scheduler.Requeue(work));
                    continue;
                }
                if (status is not (OfflineDirectChunkReadStatus.Snapshot or
                    OfflineDirectChunkReadStatus.Delta or
                    OfflineDirectChunkReadStatus.ChunkAbsent or
                    OfflineDirectChunkReadStatus.NoChanges))
                    throw new InvalidOperationException(
                        $"P0.3 publication read returned {status} for recipient {recipientIndex} " +
                        $"at cadence {cadenceStep} key {work.Key}.");
                if (status == OfflineDirectChunkReadStatus.Snapshot)
                {
                    foreach (WorldPublicRecord record in read.Snapshot)
                        RecordChunkRecord(recipientIndex, record);
                    RecordDeliveryBytes(ChunkPublicationSemanticCodec.MeasureSnapshot(read.Snapshot.Length));
                }
                else if (status == OfflineDirectChunkReadStatus.Delta)
                {
                    foreach (WorldPublicDeltaRecord record in read.Deltas)
                        RecordChunkDelta(recipientIndex, record);
                    RecordDeliveryBytes(48 + read.Deltas.Length * ChunkRecordBytes);
                }
                else if (status == OfflineDirectChunkReadStatus.ChunkAbsent)
                {
                    ClearChunk(recipientIndex, work.Key);
                    RecordDeliveryBytes(48);
                }
                recipient.CommitRead();
            }
            ChunkPublicationWorkSchedulerSnapshot scheduler = _scheduler.GetSnapshot();
            NotePending(scheduler.PendingWorkCount, 0);
        }

        public override CumulativeWork GetWork()
        {
            ChunkPublicationWorkSchedulerSnapshot scheduler = _scheduler.GetSnapshot();
            ulong reads = 0;
            ulong records = 0;
            for (int index = 0; index < _recipients.Length; index++)
            {
                OfflineDirectChunkRecipient? recipient = _recipients[index];
                if (recipient is null) continue;
                OfflineDirectChunkRecipientSnapshot snapshot = recipient.GetSnapshot();
                reads = checked(reads + snapshot.SnapshotReads + snapshot.DeltaReads + snapshot.NoChangeReads);
                records = checked(records + snapshot.SnapshotReads + snapshot.DeltaReads);
            }
            return new(
                scheduler.DirtyChunksObserved + scheduler.IncomingQueued,
                scheduler.DirtyRecipientFanout + scheduler.PendingMembershipsExamined,
                checked((ulong)(scheduler.MembershipCount + scheduler.SharedChunkCount)),
                reads,
                scheduler.DirtyChunksObserved,
                records,
                WireBytes,
                Deliveries,
                Proofs,
                MaxPending,
                MaxOutstanding,
                scheduler.ReservedBytes + RecipientReservedBytes());
        }

        public override void AssertSemanticContract() => AssertConverged();

        public override void Dispose() => _publication.Dispose();

        private void ApplyRecipient(int index)
        {
            TraceRecipient recipientState = Recipients[index];
            if (!recipientState.Active)
            {
                ClearRecipient(index);
                if (_registered[index])
                {
                    Require(_scheduler.RemoveRecipient(_recipientIds[index]));
                    _registered[index] = false;
                    _recipients[index] = null;
                }
                return;
            }
            if (_recipients[index] is null)
            {
                var recipient = new OfflineDirectChunkRecipient(
                    new OfflineDirectChunkRecipientLimits(MaxSubscriptionsPerRecipient, MaxEntities, MaxEntities),
                    GameChunkInterestProfile.Default,
                    OfflineDirectChunkCadence.EvidenceProfile);
                _recipients[index] = recipient;
                Require(_scheduler.TryRegister(recipient, out _recipientIds[index]));
                _registered[index] = true;
            }
            OfflineDirectChunkRecipient active = _recipients[index]!;
            OfflineDirectChunkReconcileStatus reconcileStatus = active.Reconcile(
                _publication.Source,
                GameWorldState.DefaultSpace,
                WorldPosition(recipientState),
                _changes,
                out OfflineDirectChunkReconcileResult result);
            if (reconcileStatus != OfflineDirectChunkReconcileStatus.Reconciled)
                throw new InvalidOperationException(
                    $"P0.3 publication recipient {index} reconciliation returned {reconcileStatus} " +
                    $"at ({recipientState.X},{recipientState.Z}).");
            for (int changeIndex = 0; changeIndex < result.ChangeCount; changeIndex++)
                if (_changes[changeIndex].Kind == OfflineDirectChunkSubscriptionChangeKind.Left)
                    ClearChunk(index, _changes[changeIndex].Key);
            Require(_scheduler.TryApplyReconciliation(
                _recipientIds[index], _changes.AsSpan(0, result.ChangeCount), result));
        }

        private long RecipientReservedBytes()
        {
            long reserved = 0;
            for (int index = 0; index < _recipients.Length; index++)
                if (_recipients[index] is not null)
                    reserved = checked(reserved + _recipients[index]!.GetStorageSnapshot().TotalReservedBytes);
            return reserved;
        }
    }

    private sealed class DirtyChunkAdapter : ProfileAdapter
    {
        private readonly PublicationFixture _publication;
        private readonly DirtyChunkSpatialFanout _service;
        private readonly bool[] _serviceActive;

        internal DirtyChunkAdapter(Workload workload) : base(workload)
        {
            _publication = new PublicationFixture(workload);
            _service = new DirtyChunkSpatialFanout(
                _publication.Source,
                new DirtyChunkSpatialFanoutLimits(
                    Recipients.Length,
                    checked(Recipients.Length * MaxSubscriptionsPerRecipient),
                    16_384,
                    8_192,
                    Math.Max(64, Recipients.Length * 4),
                    64,
                    64,
                    16_000),
                GameChunkInterestProfile.Default,
                OfflineDirectChunkCadence.EvidenceProfile);
            _serviceActive = new bool[Recipients.Length];
        }

        public override void Initialize()
        {
            for (int index = 0; index < Recipients.Length; index++)
                ApplyRecipient(index);
            Drain(0);
        }

        protected override void ApplyCore(TraceTick tick, int cadenceStep)
        {
            foreach (RecipientMove move in tick.Moves) ApplyRecipient(move.RecipientIndex);
            _publication.Commit(State, tick.Mutations);
            Drain(cadenceStep);
        }

        public override void Drain(int cadenceStep)
        {
            Require(_service.TryProcessDirtyChunks(
                checked((ulong)cadenceStep), out _) is
                DirtyChunkProcessStatus.Success or DirtyChunkProcessStatus.NoData);
            for (int recipientIndex = 0; recipientIndex < Recipients.Length; recipientIndex++)
            {
                if (!_serviceActive[recipientIndex]) continue;
                ParticipantId participant = Participant(recipientIndex);
                while (true)
                {
                    DirtyChunkBuildStatus status = _service.TryBuildDelivery(
                        participant, checked((ulong)cadenceStep), out DirtyChunkDelivery delivery);
                    if (status == DirtyChunkBuildStatus.NoData) break;
                    if (status != DirtyChunkBuildStatus.Success)
                        throw new InvalidOperationException(
                            $"P0.3 dirty-chunk build returned {status} for recipient {recipientIndex} " +
                            $"at cadence {cadenceStep}.");
                    if (delivery.Status == OfflineDirectChunkReadStatus.Snapshot)
                    {
                        foreach (WorldPublicRecord record in delivery.Snapshot)
                            RecordChunkRecord(recipientIndex, record);
                        RecordDeliveryBytes(ChunkPublicationSemanticCodec.MeasureSnapshot(
                            delivery.Snapshot.Length));
                    }
                    else if (delivery.Status == OfflineDirectChunkReadStatus.Delta)
                    {
                        foreach (WorldPublicDeltaRecord record in delivery.Deltas)
                            RecordChunkDelta(recipientIndex, record);
                        RecordDeliveryBytes(48 + delivery.Deltas.Length * ChunkRecordBytes);
                    }
                    else if (delivery.Status == OfflineDirectChunkReadStatus.ChunkAbsent)
                    {
                        ClearChunk(recipientIndex, delivery.Key);
                        RecordDeliveryBytes(48);
                    }
                    Require(_service.TryAcknowledgeDelivery(
                        participant, delivery.DeliveryId, applied: true) ==
                        DirtyChunkAppliedProofStatus.Applied);
                }
            }
            DirtyChunkSpatialFanoutSnapshot snapshot = _service.GetSnapshot();
            NotePending(snapshot.PendingChunkObligationCount, snapshot.OutstandingDeliveryCount);
        }

        public override CumulativeWork GetWork()
        {
            DirtyChunkSpatialFanoutSnapshot snapshot = _service.GetSnapshot();
            return new(
                snapshot.DirtyChunksProcessed + snapshot.IncomingCurrentTruthScopesQueued,
                snapshot.CandidateRecipientsExamined,
                checked((ulong)(snapshot.RecipientChunkKnowledgeRowCount +
                    snapshot.RecipientSpatialRegionCount)),
                snapshot.PublicationReads + snapshot.DeltaReads,
                snapshot.DirtyChunksObserved,
                snapshot.PublicationReads + snapshot.DeltaReads,
                WireBytes,
                Deliveries,
                Proofs,
                MaxPending,
                MaxOutstanding,
                snapshot.ReservedBytes);
        }

        public override void AssertSemanticContract() => AssertConverged();

        public override void Dispose() => _publication.Dispose();

        private void ApplyRecipient(int index)
        {
            TraceRecipient recipient = Recipients[index];
            if (!recipient.Active)
            {
                if (_serviceActive[index]) ClearRecipient(index);
                if (_serviceActive[index])
                    Require(_service.RemoveRecipient(Participant(index)));
                _serviceActive[index] = false;
                return;
            }
            if (_serviceActive[index]) ClearRecipient(index);
            Require(_service.SetRecipientAnchor(
                Participant(index), GameWorldState.DefaultSpace, WorldPosition(recipient),
                checked((ulong)(index + 1))) is
                DirtyChunkRecipientUpdateStatus.Reconciled or
                DirtyChunkRecipientUpdateStatus.NoChange);
            _serviceActive[index] = true;
        }
    }

    private sealed class PublicationFixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly Entity[] _handles;
        private readonly WorldPublicChange[] _changes;
        private readonly bool[] _touched;
        private readonly WorldPublicDescriptor _descriptor = new(
            GameWorldPublicPrefabIds.Character,
            WorldYaw.Zero,
            WorldPublicStateFlags.Visible);

        internal PublicationFixture(Workload workload)
        {
            _world = new EcsWorld(
                GameSchema.CreateSchema(),
                new EcsWorldOptions(
                    new EntityCapacity(workload.Entities.Length),
                    2,
                    4,
                    lifecycleRecordCapacity: Math.Max(32, workload.Entities.Length * 2)));
            ArchetypeHandle empty = _world.RegisterArchetype(
                ReadOnlySpan<ComponentId>.Empty,
                ChunkLayoutOptions.KiB16,
                Math.Max(1, (workload.Entities.Length + 255) / 256));
            _world.Seal();
            _world.Initialize();
            _handles = new Entity[workload.Entities.Length];
            for (int index = 0; index < _handles.Length; index++)
                Require(_world.TryCreate(empty, out _handles[index]));
            Source = new ChunkPublicationStore(
                new PublicationEpoch(0xF040),
                new ChunkPublicationLimits(
                    workload.Entities.Length,
                    checked(workload.Entities.Length * 2),
                    8,
                    Math.Max(128, workload.Entities.Length * 8)));
            _changes = new WorldPublicChange[workload.Entities.Length];
            _touched = new bool[workload.Entities.Length];
            Commit(workload.Entities, ReadOnlySpan<EntityMutation>.Empty, initial: true);
        }

        internal ChunkPublicationStore Source { get; }

        internal void Commit(
            TraceEntity[] state,
            ReadOnlySpan<EntityMutation> mutations,
            bool initial = false)
        {
            if (initial)
            {
                for (int index = 0; index < state.Length; index++)
                    _touched[index] = state[index].Present && state[index].Public;
            }
            else
            {
                for (int index = 0; index < mutations.Length; index++)
                    _touched[mutations[index].EntityIndex] = true;
            }

            int count = 0;
            for (int index = 0; index < state.Length; index++)
            {
                if (!_touched[index]) continue;
                TraceEntity entity = state[index];
                _changes[count++] = entity.Present && entity.Public
                    ? WorldPublicChange.Present(_handles[index],
                        CreateWorldPosition(entity), _descriptor)
                    : WorldPublicChange.Absent(_handles[index]);
                _touched[index] = false;
            }
            if (count != 0)
                Require(Source.TryCommit(_changes.AsSpan(0, count), out _));
        }

        public void Dispose() => _world.Dispose();
    }

    private static GameInterestLimits CreateInterestLimits(Workload workload)
    {
        int farCells = (192_500 + 16_000 - 1) / 16_000;
        int edgeWidth = farCells * 2 + 1;
        int maximumEdges = checked(edgeWidth * edgeWidth * workload.Recipients.Length + edgeWidth);
        return new GameInterestLimits(
            workload.Entities.Length,
            workload.Recipients.Length,
            Math.Max(16_384, checked(workload.Entities.Length + maximumEdges)),
            checked(maximumEdges + workload.Entities.Length),
            checked(workload.Entities.Length * workload.Recipients.Length),
            Math.Max(64, workload.Recipients.Length * 4),
            32);
    }

    private static WorldPosition CreateWorldPosition(in TraceEntity entity)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(
                entity.X, 0, entity.Z, out WorldPosition position))
            throw new InvalidOperationException("P0.3 trace position is outside world bounds.");
        return position;
    }

    private static ParticipantId Participant(int index) =>
        new(checked((ulong)index + 1));

    private static int ParticipantIndex(ParticipantId participant) =>
        checked((int)participant.Value - 1);

    private static int FindSubscription(
        OfflineDirectChunkRecipient recipient,
        SpatialChunkKey key)
    {
        OfflineDirectChunkSubscriptionView subscriptions = recipient.Subscriptions;
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

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("P0.3 comparison contract failed.");
    }
}
