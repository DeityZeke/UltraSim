using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

internal static class P03FourProfileComparison
{
    private const int DefaultWarmupTicks = 100;
    private const int DefaultTotalTicks = 5_000;
    private const int DrainTicks = 32;
    private const int ByteBudget = 16_384;
    private const int MaxEntities = 400;
    private const int MaxRecipients = 16;
    private const int MaxSubscriptionsPerRecipient = 1_024;
    private static int _dirtyEntitySpatialRegionEdgeMillimeters = 128_000;
    private static int _dirtyChunkSpatialRegionEdgeMillimeters = 128_000;

    internal static void Run(string[] args)
    {
        int requestedEntities = ReadInt(args, "--entities=", 128);
        int requestedRecipients = ReadInt(args, "--recipients=", 8);
        int warmup = ReadInt(args, "--warmup=", DefaultWarmupTicks);
        int totalTicks = ReadInt(args, "--ticks=", DefaultTotalTicks);
        _dirtyEntitySpatialRegionEdgeMillimeters = ReadInt(
            args, "--dirty-entity-region-edge=", 128_000);
        _dirtyChunkSpatialRegionEdgeMillimeters = ReadInt(
            args, "--dirty-chunk-region-edge=", 128_000);
        string? requestedWorkload = ReadString(args, "--workload=");
        string? requestedProfile = ReadString(args, "--profile=");
        string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../results/game-p03-four-profile-matrix.json"));
        if (requestedEntities <= 0 || requestedEntities > MaxEntities ||
            requestedRecipients <= 0 || requestedRecipients > MaxRecipients ||
            warmup < 1 || totalTicks <= warmup ||
            _dirtyEntitySpatialRegionEdgeMillimeters <= 0 ||
            _dirtyChunkSpatialRegionEdgeMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(args));

        Workload[] workloads = BuildWorkloads(requestedEntities, requestedRecipients, totalTicks);
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
            ProfileKind[] order = Enum.GetValues<ProfileKind>();
            if (!string.IsNullOrWhiteSpace(requestedProfile) &&
                Enum.TryParse(requestedProfile, ignoreCase: true, out ProfileKind selectedProfile))
                order = [selectedProfile];
            Shuffle(order, StableHash(workload.Id));
            ProfileReport[] profiles = order
                .Select(profile =>
                {
                    Console.WriteLine($"  profile={profile}");
                    return RunProfile(workload, profile, warmup);
                })
                .OrderBy(profile => profile.Profile, StringComparer.Ordinal)
                .ToArray();
            bool allConverged = profiles.All(profile => profile.Semantic.Converged);
            bool entityFamilyEquivalent = FamilyEquivalent(
                profiles, ProfileKind.MaintainedEntityRelation,
                ProfileKind.DirtyEntitySpatialFanout,
                ProfileKind.DirtyEntityChunkBatchFanout);
            bool chunkFamilyEquivalent = FamilyEquivalent(
                profiles, ProfileKind.ChunkPublication, ProfileKind.DirtyChunkSpatialFanout);
            WorkloadReport report = new(
                workload.Id,
                workload.Entities.Length,
                workload.Recipients.Length,
                workload.Ticks.Length,
                workload.CommonTraffic,
                allConverged,
                entityFamilyEquivalent,
                chunkFamilyEquivalent,
                profiles);
            reports.Add(report);
            Console.WriteLine(
                $"  converged={allConverged} entity_family={entityFamilyEquivalent} " +
                $"chunk_family={chunkFamilyEquivalent} " +
                string.Join(" | ", profiles.Select(profile =>
                    $"{profile.Profile}:p95={profile.Timing.P95Microseconds:F2}us " +
                    $"p99={profile.Timing.P99Microseconds:F2}us " +
                    $"records={profile.Work.RecordsMaterialized:N0} " +
                    $"payload={profile.Work.EncodedGamePayloadBytes:N0}B")));
        }

        var matrix = new ComparisonReport(
            "ultrasim-game-p03-five-profile-shared-trace-v4",
            DateTimeOffset.UtcNow,
            ReadGitSha(),
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            new MatrixScope(
                "All five Game profiles replay the same authoritative entity, recipient, " +
                "mutation, movement, login/re-entry, and traffic trace. Measurements cover the " +
                "profile semantic core and a payload-derived synthesized client/proof boundary. " +
                "Every post-warmup tick is timed in a fresh adapter; trace preparation, semantic " +
                "oracle verification, diagnostics, and cold snapshots run in a separate evidence " +
                "replay outside the timer. The " +
                "secure Network transport is held constant but is not included in the timed core " +
                "figures.",
                $"Release; {totalTicks} trace ticks; {warmup} warmup ticks; " +
                $"{totalTicks - warmup} measured ticks per profile; designated primary event " +
                "ticks are reported separately; application proof is immediate " +
                "after each encoded delivery; public/private/Event/Control traffic counts are " +
                "identical in every profile row. Payload counters use canonical Game codec sizes " +
                "and count each semantic delivery once; transport framing/security are excluded.",
                "ChunkPublication remains selected; this matrix is evidence only.",
                _dirtyEntitySpatialRegionEdgeMillimeters,
                _dirtyChunkSpatialRegionEdgeMillimeters),
            totalTicks,
            warmup,
            totalTicks - warmup,
            reports.ToArray());
        string json = JsonSerializer.Serialize(matrix, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, json + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static ProfileReport RunProfile(
        Workload workload,
        ProfileKind profile,
        int warmup)
    {
        CumulativeWork work;
        MemoryBreakdown memory;
        int convergenceTicks = 0;
        SemanticSummary semantic;
        using (ProfileAdapter evidence = CreateAdapter(workload, profile))
        {
            evidence.DiagnosticsEnabled = true;
            evidence.Initialize();
            for (int tick = 0; tick < workload.Ticks.Length; tick++)
                evidence.ApplyVerified(workload.Ticks[tick], tick);
            while (!evidence.IsConverged && convergenceTicks < DrainTicks)
            {
                convergenceTicks++;
                evidence.Drain(workload.Ticks.Length + convergenceTicks);
                evidence.RefreshSemanticStatus();
            }
            evidence.AssertSemanticContract();
            work = evidence.GetWork();
            memory = evidence.GetMemoryBreakdown(work);
            semantic = new SemanticSummary(
                evidence.IsConverged,
                convergenceTicks,
                evidence.SemanticDigest,
                evidence.DeliveredEntities,
                evidence.ExpectedEntities,
                evidence.MaxPending,
                evidence.MaxOutstanding);
        }
        // The evidence replay owns large bounded stores. Reclaim it before constructing the
        // independent timing adapter so the timed pass is not measured under double reservation.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        double[] timings = new double[workload.Ticks.Length - warmup];
        var events = new List<EventTiming>(workload.PrimaryEventTicks.Length);
        long allocated = 0;
        using (ProfileAdapter timing = CreateAdapter(workload, profile))
        {
            timing.DiagnosticsEnabled = false;
            timing.Initialize();
            for (int tick = 0; tick < warmup; tick++)
                timing.ApplyTimed(workload.Ticks[tick], tick, measure: false, out _, out _);
            for (int tick = warmup; tick < workload.Ticks.Length; tick++)
            {
                timing.ApplyTimed(workload.Ticks[tick], tick, measure: true,
                    out double elapsed, out long tickAllocated);
                allocated = checked(allocated + tickAllocated);
                timings[tick - warmup] = elapsed;
                if (Array.BinarySearch(workload.PrimaryEventTicks, tick) >= 0)
                    events.Add(new EventTiming(tick, elapsed));
            }
        }
        return new ProfileReport(
            profile.ToString(),
            new TimingSummary(Stats(timings), timings.Length, events.ToArray(), allocated),
            work,
            memory,
            semantic);
    }

    private static bool FamilyEquivalent(
        ProfileReport[] profiles,
        params ProfileKind[] family)
    {
        string[] digests = family
            .Select(kind => profiles.FirstOrDefault(item => item.Profile == kind.ToString()))
            .Where(report => report is not null)
            .Select(report => report!.Semantic.Digest)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return digests.Length <= 1;
    }

    private static ProfileAdapter CreateAdapter(Workload workload, ProfileKind profile) => profile switch
    {
        ProfileKind.MaintainedEntityRelation => new MaintainedAdapter(workload),
        ProfileKind.ChunkPublication => new ChunkPublicationAdapter(workload),
        ProfileKind.DirtyEntitySpatialFanout => new DirtyEntityAdapter(workload),
        ProfileKind.DirtyEntityChunkBatchFanout => new DirtyEntityAdapter(workload, chunkBatched: true),
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
            CreateWorkload("one-state-only-dirty", entities, recipients, ticks, WorkloadShape.OneStateDirty),
            CreateWorkload("one-spatial-dirty", entities, recipients, ticks, WorkloadShape.OneDirty),
            CreateWorkload("clustered-state-churn", entities, recipients, ticks, WorkloadShape.ClusteredStateChurn),
            CreateWorkload("clustered-micro-movement", entities, recipients, ticks, WorkloadShape.ClusteredDirty),
            CreateWorkload("distributed-dirty-chunks", entities, recipients, ticks, WorkloadShape.DistributedDirty),
            CreateWorkload("mostly-clean-recipient-movement", entities, recipients, ticks, WorkloadShape.Movement),
            CreateWorkload("active-town-mixed", entities, recipients, ticks, WorkloadShape.Mixed),
            CreateWorkload("dense-400", denseEntities, denseRecipients, ticks, WorkloadShape.Dense),
            CreateWorkload("edge-corner", entities, recipients, ticks, WorkloadShape.EdgeCorner),
            CreateWorkload("teleport-login-reentry", entities, recipients, ticks, WorkloadShape.TeleportReentry),
            CreateWorkload("synchronized-teleport-stress", entities, recipients, ticks, WorkloadShape.SynchronizedTeleport),
            CreateWorkload("lifecycle-privacy", entities, recipients, ticks, WorkloadShape.LifecyclePrivacy),
            CreateWorkload("high-state-churn", entities, recipients, ticks, WorkloadShape.HighStateChurn),
            CreateWorkload("high-spatial-churn", entities, recipients, ticks, WorkloadShape.HighDirt),
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
        for (int index = 0; index < entities.Length; index++)
        {
            long x = (index - entityCount / 2) * 64_000L + 24_000L;
            long z = 8_000L;
            if ((shape is WorkloadShape.ClusteredDirty or WorkloadShape.ClusteredStateChurn) &&
                index < 32)
            {
                x = 2_000L + (index % 8) * 1_500L;
                z = 2_000L + (index / 8) * 1_500L;
            }
            else if (shape == WorkloadShape.Dense)
            {
                x = (index % 20) * 6_000L - 57_000L;
                z = (index / 20) * 6_000L - 57_000L;
            }
            else if (shape == WorkloadShape.EdgeCorner)
            {
                int cycle = index / 16;
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
                x += cycle * 1_000L;
                z += cycle * 700L;
            }
            bool isPublic = index % 17 != 0;
            entities[index] = new(index, true, isPublic, x, z, 1, 1,
                isPublic ? 1u : 0u, isPublic ? 1UL : 0UL);
        }
        var publicPositions = new HashSet<(long X, long Z)>();
        for (int index = 0; index < entities.Length; index++)
            if (entities[index].Public &&
                !publicPositions.Add((entities[index].X, entities[index].Z)))
                throw new InvalidOperationException(
                    $"P0.3 workload {id} contains ambiguous public payload positions.");

        var recipients = new TraceRecipient[recipientCount];
        for (int index = 0; index < recipients.Length; index++)
        {
            long x = (index - recipientCount / 2) * 128_000L;
            long z = 0;
            if (shape == WorkloadShape.Dense)
            {
                x = (index % 4) * 32_000L - 48_000L;
                z = (index / 4) * 32_000L - 48_000L;
            }
            recipients[index] = new(index, true, x, z);
        }

        TraceEntity[] workingEntities = entities.ToArray();
        TraceRecipient[] workingRecipients = recipients.ToArray();
        int measuredEventTick = Math.Max(4, ticks / 3);
        int firstPublicEntity = Enumerable.Range(0, workingEntities.Length)
            .Where(index => workingEntities[index].Public)
            .OrderBy(index => workingRecipients
                .Where(recipient => recipient.Active)
                .Select(recipient =>
                {
                    long dx = workingEntities[index].X - recipient.X;
                    long dz = workingEntities[index].Z - recipient.Z;
                    return dx * dx + dz * dz;
                })
                .DefaultIfEmpty(long.MaxValue)
                .Min())
            .DefaultIfEmpty(-1)
            .First();
        if (firstPublicEntity < 0)
            throw new InvalidOperationException("P0.3 requires at least one public trace entity.");

        var ticksData = new TraceTick[ticks];
        for (int tick = 0; tick < ticks; tick++)
        {
            var mutations = new List<EntityMutation>();
            var moves = new List<RecipientMove>();
            switch (shape)
            {
                case WorkloadShape.OneStateDirty when tick == Math.Max(4, ticks / 2):
                    AddStateMutation(mutations, workingEntities, firstPublicEntity,
                        workingEntities[firstPublicEntity].Present,
                        workingEntities[firstPublicEntity].Public);
                    break;
                case WorkloadShape.OneDirty when tick == Math.Max(4, ticks / 2):
                    AddMutation(mutations, workingEntities, firstPublicEntity, 1_000, 0);
                    break;
                case WorkloadShape.ClusteredStateChurn:
                    for (int index = 0; index < Math.Min(32, entityCount); index++)
                        AddStateMutation(mutations, workingEntities, index,
                            workingEntities[index].Present, workingEntities[index].Public);
                    break;
                case WorkloadShape.ClusteredDirty:
                    for (int index = 0; index < Math.Min(32, entityCount); index++)
                        AddMutation(mutations, workingEntities, index,
                            (tick & 1) == 0 ? 100 : -100,
                            (tick & 1) == 0 ? 75 : -75);
                    break;
                case WorkloadShape.DistributedDirty:
                    for (int offset = 0; offset < Math.Min(16, entityCount); offset++)
                    {
                        int index = (tick * 29 + offset * 17) % entityCount;
                        AddMutation(mutations, workingEntities, index,
                            (tick & 1) == 0 ? 100 : -100,
                            (tick & 1) == 0 ? -75 : 75);
                    }
                    break;
                case WorkloadShape.Movement:
                    AddMovement(moves, workingRecipients, tick, 500, 375);
                    break;
                case WorkloadShape.Mixed:
                    if (tick >= measuredEventTick)
                        AddMovement(moves, workingRecipients, tick, 500, 375);
                    if (tick > measuredEventTick)
                        for (int offset = 0; offset < Math.Min(12, entityCount); offset++)
                            AddMutation(mutations, workingEntities, (tick * 11 + offset * 7) % entityCount,
                                (tick & 1) == 0 ? 50 : -50,
                                (tick & 1) == 0 ? 25 : -25);
                    break;
                case WorkloadShape.Dense:
                    if (tick >= measuredEventTick)
                        AddMovement(moves, workingRecipients, tick, 500, 250);
                    if (tick > measuredEventTick)
                        for (int offset = 0; offset < Math.Min(32, entityCount); offset++)
                            AddMutation(mutations, workingEntities, (tick * 13 + offset * 3) % entityCount,
                                (tick & 1) == 0 ? 25 : -25,
                                (tick & 1) == 0 ? 25 : -25);
                    break;
                case WorkloadShape.EdgeCorner:
                    AddMovement(moves, workingRecipients, tick, 500, 375);
                    if (tick == ticks - 1)
                        for (int offset = 0; offset < Math.Min(4, entityCount); offset++)
                            AddMutation(mutations, workingEntities, offset, 800, 0);
                    break;
                case WorkloadShape.TeleportReentry:
                    if (tick == Math.Max(4, ticks / 3))
                    {
                        moves.Add(new RecipientMove(0, true, 1_000_000, 1_000_000));
                        workingRecipients[0] = new TraceRecipient(0, true, 1_000_000, 1_000_000);
                    }
                    if (tick == Math.Max(5, ticks / 3 + 1))
                    {
                        moves.Add(new RecipientMove(0, false, 1_000_000, 1_000_000));
                        workingRecipients[0] = new TraceRecipient(0, false, 1_000_000, 1_000_000);
                    }
                    if (tick == Math.Max(6, ticks / 3 + 2))
                    {
                        moves.Add(new RecipientMove(0, true, 0, 0));
                        workingRecipients[0] = new TraceRecipient(0, true, 0, 0);
                    }
                    if (tick == Math.Max(7, ticks / 3 + 3) && entityCount > 0)
                        AddMutation(mutations, workingEntities, firstPublicEntity, 2_000, 0);
                    break;
                case WorkloadShape.SynchronizedTeleport:
                    if (tick == Math.Max(4, ticks / 3))
                    {
                        for (int index = 0; index < workingRecipients.Length; index++)
                        {
                            if (!workingRecipients[index].Active) continue;
                            long x = 1_000_000L + index * 2_000L;
                            long z = 1_000_000L - index * 1_000L;
                            moves.Add(new RecipientMove(index, true, x, z));
                            workingRecipients[index] = new TraceRecipient(index, true, x, z);
                        }
                    }
                    if (tick == Math.Max(5, ticks / 3 + 1))
                    {
                        for (int index = 0; index < workingRecipients.Length; index++)
                        {
                            moves.Add(new RecipientMove(index, true, 0, 0));
                            workingRecipients[index] = new TraceRecipient(index, true, 0, 0);
                        }
                    }
                    break;
                case WorkloadShape.LifecyclePrivacy:
                    if (tick == measuredEventTick)
                        AddStateMutation(mutations, workingEntities, firstPublicEntity,
                            present: true, isPublic: false);
                    else if (tick == measuredEventTick + 1)
                        AddStateMutation(mutations, workingEntities, firstPublicEntity,
                            present: true, isPublic: true);
                    else if (tick == measuredEventTick + 2)
                        AddStateMutation(mutations, workingEntities, firstPublicEntity,
                            present: false, isPublic: true);
                    else if (tick == measuredEventTick + 3)
                        AddStateMutation(mutations, workingEntities, firstPublicEntity,
                            present: true, isPublic: true);
                    break;
                case WorkloadShape.HighDirt:
                    for (int offset = 0; offset < Math.Max(1, entityCount * 3 / 4); offset++)
                        AddMutation(mutations, workingEntities, (tick * 19 + offset) % entityCount,
                            (tick & 1) == 0 ? 25 : -25,
                            (tick & 1) == 0 ? 25 : -25);
                    break;
                case WorkloadShape.HighStateChurn:
                    for (int offset = 0; offset < Math.Max(1, entityCount * 3 / 4); offset++)
                    {
                        int index = (tick * 19 + offset) % entityCount;
                        AddStateMutation(mutations, workingEntities, index,
                            workingEntities[index].Present, workingEntities[index].Public);
                    }
                    break;
                case WorkloadShape.HighMovement:
                    AddMovement(moves, workingRecipients, tick, 500, 375);
                    if (tick == ticks - 1 && entityCount > 0)
                        AddMutation(mutations, workingEntities, tick % entityCount, 100, 0);
                    break;
            }
            ticksData[tick] = new TraceTick(mutations.ToArray(), moves.ToArray());
        }

        int publicFacts = entities.Count(entity => entity.Present && entity.Public);
        int[] primaryEventTicks = shape switch
        {
            WorkloadShape.OneDirty or WorkloadShape.OneStateDirty => [Math.Max(4, ticks / 2)],
            WorkloadShape.TeleportReentry or WorkloadShape.LifecyclePrivacy =>
                [measuredEventTick, measuredEventTick + 1, measuredEventTick + 2, measuredEventTick + 3],
            WorkloadShape.SynchronizedTeleport => [measuredEventTick, measuredEventTick + 1],
            WorkloadShape.Mixed or WorkloadShape.Dense => [measuredEventTick],
            WorkloadShape.EdgeCorner or WorkloadShape.HighMovement => [ticks - 1],
            _ => [],
        };
        var traffic = new CommonTraffic(
            PublicFacts: checked(publicFacts + ticksData.Sum(item =>
                item.Mutations.Count(mutation => mutation.Present && mutation.Public))),
            PrivateFacts: checked(entities.Count(entity => !entity.Public) + ticksData.Sum(item =>
                item.Mutations.Count(mutation => !mutation.Public))),
            EventFacts: checked(ticks * Math.Max(1, recipientCount / 4)),
            ControlFacts: checked(ticks * (recipientCount + 1)));
        return new Workload(id, entities, recipients, ticksData, primaryEventTicks, traffic);
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
        entities[index] = AdvanceWorkingPublicLineage(
            current with { X = current.X + dx, Z = current.Z + dz },
            current.Present && current.Public,
            current.Present && current.Public);
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
            int segment = ((tick + index * 137) / 400) & 7;
            (int xDirection, int zDirection) = segment switch
            {
                0 => (1, 0),
                1 => (1, 1),
                2 => (0, 1),
                3 => (-1, 1),
                4 => (-1, 0),
                5 => (-1, -1),
                6 => (0, -1),
                _ => (1, -1),
            };
            long nextX = current.X + xDirection * dx;
            long nextZ = current.Z + zDirection * dz;
            moves.Add(new RecipientMove(index, true, nextX, nextZ));
            recipients[index] = new TraceRecipient(index, true, nextX, nextZ);
        }
    }

    private static void AddStateMutation(
        List<EntityMutation> mutations,
        TraceEntity[] entities,
        int index,
        bool present,
        bool isPublic)
    {
        TraceEntity current = entities[index];
        mutations.Add(new EntityMutation(index, current.X, current.Z, present, isPublic));
        TraceEntity updated = current with
        {
            Present = present,
            Public = isPublic,
            Generation = !current.Present && present
                ? checked(current.Generation + 1)
                : current.Generation,
        };
        entities[index] = AdvanceWorkingPublicLineage(
            updated, current.Present && current.Public, present && isPublic);
    }

    private static TraceEntity AdvanceWorkingPublicLineage(
        in TraceEntity prior,
        bool wasDisclosed,
        bool isDisclosed)
    {
        if (isDisclosed && !wasDisclosed)
            return prior with
            {
                PublicGeneration = checked(prior.PublicGeneration + 1),
                PublicVersion = 1,
            };
        if (wasDisclosed)
            return prior with { PublicVersion = checked(prior.PublicVersion + 1) };
        return prior;
    }

    private static double Microseconds(long ticks) =>
        ticks * 1_000_000.0 / Stopwatch.Frequency;

    private static StatsSummary Stats(double[] values)
    {
        if (values.Length == 0)
            throw new InvalidOperationException("P0.3 produced no timing samples.");
        Array.Sort(values);
        return new(
            Percentile(values, 0.50),
            Percentile(values, 0.95),
            Percentile(values, 0.99),
            values[^1]);
    }

    private static double Percentile(double[] values, double percentile)
    {
        int index = Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1);
        return values[index];
    }

    private static uint StableHash(string value)
    {
        uint hash = 2_166_136_261;
        foreach (char character in value)
            hash = unchecked((hash ^ character) * 16_777_619);
        return hash;
    }

    private static void Shuffle<T>(T[] values, uint seed)
    {
        for (int index = values.Length - 1; index > 0; index--)
        {
            seed = unchecked(seed * 1_664_525 + 1_013_904_223);
            int swap = (int)(seed % (uint)(index + 1));
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }

    private static string ReadGitSha()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = FindRepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return "unavailable";
            string sha = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0 || sha.Length == 0) return "unavailable";
            using var statusProcess = Process.Start(new ProcessStartInfo(
                "git", "status --porcelain --untracked-files=no")
            {
                WorkingDirectory = FindRepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (statusProcess is null) return sha;
            string status = statusProcess.StandardOutput.ReadToEnd();
            statusProcess.WaitForExit();
            return status.Length == 0 ? sha : sha + "-dirty";
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return Directory.GetCurrentDirectory();
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
        DirtyEntityChunkBatchFanout,
        DirtyChunkSpatialFanout,
    }

    private enum WorkloadShape : byte
    {
        Stable,
        OneStateDirty,
        OneDirty,
        ClusteredStateChurn,
        ClusteredDirty,
        DistributedDirty,
        Movement,
        Mixed,
        Dense,
        EdgeCorner,
        TeleportReentry,
        SynchronizedTeleport,
        LifecyclePrivacy,
        HighStateChurn,
        HighDirt,
        HighMovement,
    }

    private readonly record struct TraceEntity(
        int Index,
        bool Present,
        bool Public,
        long X,
        long Z,
        ulong Version,
        uint Generation,
        uint PublicGeneration,
        ulong PublicVersion);

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
        int[] PrimaryEventTicks,
        CommonTraffic CommonTraffic);

    private readonly record struct CommonTraffic(
        int PublicFacts,
        int PrivateFacts,
        int EventFacts,
        int ControlFacts);

    private readonly record struct MatrixScope(
        string MeasurementScope,
        string SharedTraceContract,
        string SelectionPolicy,
        int DirtyEntitySpatialRegionEdgeMillimeters,
        int DirtyChunkSpatialRegionEdgeMillimeters);

    private sealed record ComparisonReport(
        string WorkloadIdentity,
        DateTimeOffset CapturedAtUtc,
        string GitSha,
        string Runtime,
        string OperatingSystem,
        int ProcessorCount,
        long StopwatchFrequency,
        MatrixScope Scope,
        int TraceTicks,
        int WarmupTicks,
        int TimingSamples,
        WorkloadReport[] Workloads);

    private sealed record WorkloadReport(
        string Workload,
        int EntityCount,
        int RecipientCount,
        int TraceTicks,
        CommonTraffic CommonTraffic,
        bool AllProfilesConverged,
        bool EntityFamilyEquivalent,
        bool ChunkFamilyEquivalent,
        ProfileReport[] Profiles);

    private sealed record ProfileReport(
        string Profile,
        TimingSummary Timing,
        CumulativeWork Work,
        MemoryBreakdown Memory,
        SemanticSummary Semantic);

    private readonly record struct TimingSummary(
        StatsSummary Percentiles,
        int SampleCount,
        EventTiming[] PrimaryEvents,
        long MeasuredManagedBytes)
    {
        public double P50Microseconds => Percentiles.P50;
        public double P95Microseconds => Percentiles.P95;
        public double P99Microseconds => Percentiles.P99;
    }

    private readonly record struct StatsSummary(double P50, double P95, double P99, double Max);

    private readonly record struct EventTiming(int Tick, double Microseconds);

    private readonly record struct SemanticSummary(
        bool Converged,
        int DrainTicks,
        string Digest,
        int DeliveredEntities,
        int ExpectedEntities,
        int MaxPending,
        int MaxOutstanding);

    private readonly record struct CumulativeWork(
        ulong DirtyUnitsProcessed,
        ulong SpatialDiscoveryOperations,
        ulong CandidateTests,
        ulong RecipientReconciliations,
        ulong SourceReads,
        ulong RecordsMaterialized,
        ulong EncodedGamePayloadBytes,
        ulong EncodedGameMessages,
        ulong ApplicationProofs,
        int MaxPending,
        int MaxOutstanding,
        long ReservedBytes);

    private readonly record struct MemoryBreakdown(
        long ProfileMechanismReservedBytes,
        long AuthoritativeSourceHistoryBytes,
        long HarnessSemanticStateBytes,
        long TotalKnownReservedBytes);

    private abstract class ProfileAdapter : IDisposable
    {
        protected readonly Workload Workload;
        protected readonly TraceEntity[] State;
        protected readonly TraceRecipient[] Recipients;
        private readonly bool _usesChunkInterest;
        private readonly bool[] _appliedPresent;
        private readonly uint[] _appliedIdentityIndex;
        private readonly uint[] _appliedIdentityGeneration;
        private readonly ulong[] _appliedVersion;
        private readonly long[] _appliedX;
        private readonly long[] _appliedZ;
        private readonly SpatialChunkKey[] _lastDeliveredChunk;
        private readonly bool[] _hasLastDeliveredChunk;
        private readonly int[] _publicSlotEntities;
        private readonly uint[] _publicSlotGenerations;
        private readonly ChunkInterestEntry[][]? _expectedChunkInterest;
        private readonly int[]? _expectedChunkInterestCounts;
        private readonly ChunkInterestEntry[]? _expectedChunkInterestScratch;
        private int _deliveredEntities;
        private int _expectedEntities;
        private ulong _encodedGamePayloadBytes;
        private ulong _encodedGameMessages;
        private ulong _proofs;
        private ulong _materializedRecords;
        private int _maxPending;
        private int _maxOutstanding;

        protected ProfileAdapter(Workload workload, bool usesChunkInterest)
        {
            Workload = workload;
            _usesChunkInterest = usesChunkInterest;
            State = workload.Entities.ToArray();
            Recipients = workload.Recipients.ToArray();
            _appliedPresent = new bool[checked(Recipients.Length * State.Length)];
            _appliedIdentityIndex = new uint[_appliedPresent.Length];
            _appliedIdentityGeneration = new uint[_appliedPresent.Length];
            _appliedVersion = new ulong[_appliedPresent.Length];
            _appliedX = new long[_appliedPresent.Length];
            _appliedZ = new long[_appliedPresent.Length];
            _lastDeliveredChunk = new SpatialChunkKey[_appliedPresent.Length];
            _hasLastDeliveredChunk = new bool[_appliedPresent.Length];
            _publicSlotEntities = Enumerable.Repeat(-1, State.Length).ToArray();
            _publicSlotGenerations = new uint[State.Length];
            if (usesChunkInterest)
            {
                _expectedChunkInterest = new ChunkInterestEntry[Recipients.Length][];
                _expectedChunkInterestCounts = new int[Recipients.Length];
                _expectedChunkInterestScratch = new ChunkInterestEntry[MaxSubscriptionsPerRecipient];
                for (int index = 0; index < Recipients.Length; index++)
                    _expectedChunkInterest[index] = new ChunkInterestEntry[MaxSubscriptionsPerRecipient];
                RefreshAllChunkInterestOracles();
            }
        }

        internal bool DiagnosticsEnabled { get; set; }

        public abstract void Initialize();

        public void ApplyVerified(TraceTick tick, int cadenceStep)
        {
            PrepareTick(tick);
            ApplyCore(tick, cadenceStep);
            UpdateSemanticStatus();
        }

        public void ApplyTimed(
            TraceTick tick,
            int cadenceStep,
            bool measure,
            out double elapsedMicroseconds,
            out long allocatedBytes)
        {
            PrepareTick(tick);
            long allocatedBefore = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
            long started = measure ? Stopwatch.GetTimestamp() : 0;
            ApplyCore(tick, cadenceStep);
            elapsedMicroseconds = measure
                ? Microseconds(Stopwatch.GetTimestamp() - started)
                : 0;
            allocatedBytes = measure
                ? GC.GetAllocatedBytesForCurrentThread() - allocatedBefore
                : 0;
        }

        private void PrepareTick(TraceTick tick)
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
                bool wasDisclosed = prior.Present && prior.Public;
                bool isDisclosed = mutation.Present && mutation.Public;
                uint publicGeneration = isDisclosed && !wasDisclosed
                    ? checked(prior.PublicGeneration + 1)
                    : prior.PublicGeneration;
                ulong publicVersion = isDisclosed && !wasDisclosed
                    ? 1
                    : wasDisclosed
                        ? checked(prior.PublicVersion + 1)
                        : prior.PublicVersion;
                State[mutation.EntityIndex] = new(
                    prior.Index,
                    mutation.Present,
                    mutation.Public,
                    mutation.X,
                    mutation.Z,
                    checked(prior.Version + 1),
                    mutation.Present && !prior.Present
                        ? checked(prior.Generation + 1)
                        : prior.Generation,
                    publicGeneration,
                    publicVersion);
            }
            if (_usesChunkInterest)
                for (int index = 0; index < tick.Moves.Length; index++)
                    RefreshChunkInterestOracle(tick.Moves[index].RecipientIndex);
        }

        public abstract void Drain(int cadenceStep);

        public abstract CumulativeWork GetWork();

        public abstract MemoryBreakdown GetMemoryBreakdown(CumulativeWork work);

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
            in ReplicationEntityId identity,
            ulong version,
            bool present,
            long x,
            long z,
            int tickMarker)
        {
            int entityIndex = checked((int)identity.Index);
            if ((uint)recipientIndex >= (uint)Recipients.Length ||
                (uint)entityIndex >= (uint)State.Length)
                throw new InvalidOperationException("P0.3 entity payload identity exceeded trace capacity.");
            if (!identity.IsValid || version == 0 ||
                present && tickMarker != checked((int)version))
                throw new InvalidOperationException("P0.3 entity payload identity/version/state was invalid.");
            int offset = checked(recipientIndex * State.Length + entityIndex);
            _appliedPresent[offset] = present;
            _appliedIdentityIndex[offset] = identity.Index;
            _appliedIdentityGeneration[offset] = identity.Generation;
            _appliedVersion[offset] = version;
            _appliedX[offset] = present ? x : 0;
            _appliedZ[offset] = present ? z : 0;
            if (present)
            {
                _lastDeliveredChunk[offset] = ToChunkKey(x, z);
                _hasLastDeliveredChunk[offset] = true;
            }
            _materializedRecords++;
        }

        protected void RecordEntity(
            int recipientIndex,
            in ReplicationEntityId identity,
            ulong version,
            bool present,
            in ReplicationSpatialPoint position,
            in GameSimulationState state) =>
            RecordEntity(recipientIndex, identity, version, present,
                present ? position.XMillimeters : 0,
                present ? position.ZMillimeters : 0,
                present ? state.TickMarker : 0);

        protected void RecordChunkRecord(
            int recipientIndex,
            in WorldPublicRecord record,
            bool present = true)
        {
            int entityIndex = ResolveKnownPublicSlot(record.Id);
            if (entityIndex < 0)
                entityIndex = FindEntityIndex(record.Position);
            RememberPublicSlot(record.Id, entityIndex);
            if (record.PrefabId != GameWorldPublicPrefabIds.Character ||
                record.Orientation != new WorldYaw(unchecked((ushort)State[entityIndex].Version)) ||
                record.StateFlags != WorldPublicStateFlags.Visible)
                throw new InvalidOperationException("P0.3 publication payload fields diverged from source truth.");
            RecordPublicEntity(recipientIndex, entityIndex, record.Id,
                record.EntityPublicVersion, present,
                record.Position.GlobalXMillimeters, record.Position.GlobalZMillimeters);
        }

        private int FindEntityIndex(in WorldPosition position)
        {
            long x = position.GlobalXMillimeters;
            long z = position.GlobalZMillimeters;
            int match = -1;
            for (int index = 0; index < State.Length; index++)
            {
                TraceEntity entity = State[index];
                if (entity.Present && entity.Public && entity.X == x && entity.Z == z)
                {
                    if (match >= 0)
                        throw new InvalidOperationException(
                            $"P0.3 publication record position ({x},{z}) is ambiguous.");
                    match = index;
                }
            }
            if (match >= 0) return match;
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
            if (entityIndex < 0)
            {
                // A subscribed chunk may legitimately carry a removal for an identity this
                // recipient never observed. It is a materialized, semantically empty operation.
                _materializedRecords++;
                return;
            }
            RecordPublicEntity(recipientIndex, entityIndex, record.Id,
                record.EntityPublicVersion, false, 0, 0);
        }

        private void RecordPublicEntity(
            int recipientIndex,
            int entityIndex,
            in WorldPublicEntityId identity,
            ulong version,
            bool present,
            long x,
            long z)
        {
            if ((uint)recipientIndex >= (uint)Recipients.Length ||
                (uint)entityIndex >= (uint)State.Length || !identity.IsValid || version == 0)
                throw new InvalidOperationException("P0.3 chunk payload identity/version was invalid.");
            int offset = checked(recipientIndex * State.Length + entityIndex);
            _appliedPresent[offset] = present;
            _appliedIdentityIndex[offset] = identity.Index;
            _appliedIdentityGeneration[offset] = identity.Generation;
            _appliedVersion[offset] = version;
            _appliedX[offset] = present ? x : 0;
            _appliedZ[offset] = present ? z : 0;
            if (present)
            {
                _lastDeliveredChunk[offset] = ToChunkKey(x, z);
                _hasLastDeliveredChunk[offset] = true;
            }
            _materializedRecords++;
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
                return -1;
            return _publicSlotEntities[id.Index];
        }

        protected void ClearChunk(
            int recipientIndex,
            in SpatialChunkKey key)
        {
            for (int entityIndex = 0; entityIndex < State.Length; entityIndex++)
            {
                int offset = checked(recipientIndex * State.Length + entityIndex);
                if (!_appliedPresent[offset] || !_hasLastDeliveredChunk[offset] ||
                    _lastDeliveredChunk[offset] != key) continue;
                _appliedPresent[offset] = false;
                _appliedX[offset] = 0;
                _appliedZ[offset] = 0;
                _materializedRecords++;
            }
        }

        protected void ClearRecipient(int recipientIndex)
        {
            for (int entityIndex = 0; entityIndex < State.Length; entityIndex++)
            {
                int offset = recipientIndex * State.Length + entityIndex;
                if (_appliedPresent[offset])
                {
                    _appliedPresent[offset] = false;
                    _appliedX[offset] = 0;
                    _appliedZ[offset] = 0;
                    _materializedRecords++;
                }
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
                    bool expected = ExpectedVisible(recipient, entity);
                    bool actual = Recipients[recipient].Active && _appliedPresent[offset];
                    ulong expectedVersion = _usesChunkInterest
                        ? State[entity].PublicVersion
                        : State[entity].Version;
                    if (actual != expected || actual &&
                        (_appliedVersion[offset] != expectedVersion ||
                         _appliedX[offset] != State[entity].X ||
                         _appliedZ[offset] != State[entity].Z ||
                         (_usesChunkInterest
                             ? _appliedIdentityGeneration[offset] != State[entity].PublicGeneration
                             : _appliedIdentityIndex[offset] != (uint)entity ||
                               _appliedIdentityGeneration[offset] != State[entity].Generation)))
                        throw new InvalidOperationException(
                            $"P0.3 convergence failed for {GetType().Name}: recipient={recipient}, " +
                            $"entity={entity}, expectedPresent={expected}, actualPresent={actual}, " +
                            $"recipientPosition=({Recipients[recipient].X},{Recipients[recipient].Z}), " +
                            $"entityPosition=({State[entity].X},{State[entity].Z}), " +
                            $"expectedVersion={expectedVersion}, actualVersion={_appliedVersion[offset]}, " +
                            $"expectedCount={_expectedEntities}, actualCount={_deliveredEntities}.");
                }
            }
            throw new InvalidOperationException($"P0.3 convergence failed for {GetType().Name}.");
        }

        internal void RefreshSemanticStatus() => UpdateSemanticStatus();

        protected ulong Deliveries => _encodedGameMessages;
        protected ulong Proofs => _proofs;
        protected ulong WireBytes => _encodedGamePayloadBytes;
        protected ulong MaterializedRecords => _materializedRecords;

        protected MemoryBreakdown BuildMemoryBreakdown(
            long profileStorageBytes,
            long sourceHistoryBytes)
        {
            long semanticStateBytes = checked((long)_appliedPresent.Length *
                (sizeof(byte) + sizeof(uint) + sizeof(uint) + sizeof(ulong) +
                 sizeof(long) + sizeof(long) + Unsafe.SizeOf<SpatialChunkKey>() + sizeof(byte)) +
                (long)State.Length * Unsafe.SizeOf<TraceEntity>() +
                (long)Recipients.Length * Unsafe.SizeOf<TraceRecipient>() +
                (long)_publicSlotEntities.Length * sizeof(int) +
                (long)_publicSlotGenerations.Length * sizeof(uint));
            if (_expectedChunkInterest is not null)
                semanticStateBytes = checked(semanticStateBytes +
                    (long)_expectedChunkInterest.Length * MaxSubscriptionsPerRecipient *
                        Unsafe.SizeOf<ChunkInterestEntry>() +
                    (long)_expectedChunkInterestCounts!.Length * sizeof(int) +
                    (long)_expectedChunkInterestScratch!.Length * Unsafe.SizeOf<ChunkInterestEntry>());
            return new(
                profileStorageBytes,
                sourceHistoryBytes,
                semanticStateBytes,
                checked(profileStorageBytes + sourceHistoryBytes + semanticStateBytes));
        }

        protected void RecordMessageBytes(int bytes)
        {
            _encodedGamePayloadBytes = checked(_encodedGamePayloadBytes + checked((ulong)bytes));
            _encodedGameMessages++;
            _proofs++;
        }

        protected static ReplicationEntityId ReplicationId(in TraceEntity entity) =>
            new(checked((uint)entity.Index), entity.Generation);

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

        private static SpatialChunkKey ToChunkKey(long x, long z)
        {
            if (!global::UltraSim.Game.World.WorldPosition.TryFromGlobalMillimeters(
                    x, 0, z, out WorldPosition position))
                throw new InvalidOperationException("A delivered P0.3 position exceeded world bounds.");
            return position.ToChunkKey(GameWorldState.DefaultSpace);
        }

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
                    bool expected = ExpectedVisible(recipient, entity);
                    int offset = recipient * State.Length + entity;
                    bool actual = Recipients[recipient].Active && _appliedPresent[offset];
                    ulong expectedVersion = _usesChunkInterest
                        ? State[entity].PublicVersion
                        : State[entity].Version;
                    if (expected) _expectedEntities++;
                    if (actual) _deliveredEntities++;
                    if (actual != expected || actual &&
                        (_appliedVersion[offset] != expectedVersion ||
                         _appliedX[offset] != State[entity].X ||
                         _appliedZ[offset] != State[entity].Z ||
                         (_usesChunkInterest
                             ? _appliedIdentityGeneration[offset] != State[entity].PublicGeneration
                             : _appliedIdentityIndex[offset] != (uint)entity ||
                               _appliedIdentityGeneration[offset] != State[entity].Generation)))
                        equal = false;
                    hash ^= (ulong)(actual ? 1 : 0);
                    hash = unchecked(hash * 1_099_511_628_211UL);
                    hash ^= unchecked((ulong)recipient);
                    hash = unchecked(hash * 1_099_511_628_211UL);
                    hash ^= unchecked((ulong)entity);
                    hash = unchecked(hash * 1_099_511_628_211UL);
                    if (actual)
                    {
                        hash ^= _appliedIdentityIndex[offset];
                        hash = unchecked(hash * 1_099_511_628_211UL);
                        hash ^= _appliedIdentityGeneration[offset];
                        hash = unchecked(hash * 1_099_511_628_211UL);
                        hash ^= _appliedVersion[offset];
                        hash = unchecked(hash * 1_099_511_628_211UL);
                        hash ^= unchecked((ulong)_appliedX[offset]);
                        hash = unchecked(hash * 1_099_511_628_211UL);
                        hash ^= unchecked((ulong)_appliedZ[offset]);
                    }
                    hash = unchecked(hash * 1_099_511_628_211UL);
                }
            }
            IsConverged = equal;
            SemanticDigest = hash.ToString("X16");
        }

        private bool ExpectedVisible(int recipientIndex, int entityIndex)
        {
            TraceRecipient recipient = Recipients[recipientIndex];
            TraceEntity entity = State[entityIndex];
            if (!recipient.Active || !entity.Present || !entity.Public) return false;
            if (_usesChunkInterest)
            {
                SpatialChunkKey key = ToChunkKey(entity);
                ChunkInterestEntry[] entries = _expectedChunkInterest![recipientIndex];
                int low = 0;
                int high = _expectedChunkInterestCounts![recipientIndex] - 1;
                while (low <= high)
                {
                    int middle = low + ((high - low) >> 1);
                    int comparison = entries[middle].Key.CompareTo(key);
                    if (comparison == 0) return true;
                    if (comparison < 0) low = middle + 1; else high = middle - 1;
                }
                return false;
            }
            long dx = entity.X - recipient.X;
            long dz = entity.Z - recipient.Z;
            return dx * dx + dz * dz <= 192_000L * 192_000L;
        }

        private void RefreshAllChunkInterestOracles()
        {
            for (int index = 0; index < Recipients.Length; index++)
                RefreshChunkInterestOracle(index);
        }

        private void RefreshChunkInterestOracle(int recipientIndex)
        {
            if (!Recipients[recipientIndex].Active)
            {
                _expectedChunkInterestCounts![recipientIndex] = 0;
                return;
            }
            ChunkInterestEntry[] current = _expectedChunkInterest![recipientIndex];
            int currentCount = _expectedChunkInterestCounts![recipientIndex];
            ChunkInterestQueryStatus status = ChunkInterestQuery.TryQuery(
                GameWorldState.DefaultSpace,
                WorldPosition(Recipients[recipientIndex]),
                GameChunkInterestProfile.Default,
                current.AsSpan(0, currentCount),
                _expectedChunkInterestScratch!,
                out int count,
                out int required);
            if (status != ChunkInterestQueryStatus.Success || count != required)
                throw new InvalidOperationException("P0.3 chunk-interest oracle exceeded its fixed capacity.");
            _expectedChunkInterestScratch.AsSpan(0, count).CopyTo(current);
            _expectedChunkInterestCounts[recipientIndex] = count;
        }
    }

    private sealed class MaintainedAdapter : ProfileAdapter
    {
        private readonly GameInterestService _service;
        private readonly GameWorldStateRecord[] _records;
        private readonly bool[] _serviceActive;
        private readonly bool[] _entityServicePresent;

        internal MaintainedAdapter(Workload workload) : base(workload, usesChunkInterest: false)
        {
            _service = new GameInterestService(new StateEpoch(0xF031),
                CreateInterestLimits(workload), GameInterestProfile.Reference,
                GameInterestAdmissionMode.ExactDistance);
            _records = new GameWorldStateRecord[32];
            _serviceActive = new bool[Recipients.Length];
            _entityServicePresent = new bool[State.Length];
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
                ApplyEntity(index, 0);
            Drain(0);
        }

        protected override void ApplyCore(TraceTick tick, int cadenceStep)
        {
            foreach (RecipientMove move in tick.Moves) ApplyRecipient(move.RecipientIndex, cadenceStep);
            foreach (EntityMutation mutation in tick.Mutations) ApplyEntity(mutation.EntityIndex, cadenceStep);
            if (DiagnosticsEnabled)
            {
                GameInterestSnapshot snapshot = _service.GetSnapshot();
                NotePending(snapshot.PendingRelationCount, snapshot.OutstandingBundleCount);
            }
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
                    RecordEntity(recipientIndex, record.Entity, record.CanonicalVersion,
                        record.IsPresent, record.Position, record.SimulationState);
                }
                RecordMessageBytes(bundle.EstimatedEncodedBytes);
                Require(_service.TryAcknowledgeBundle(
                    bundle.Participant, bundle.BundleId, applied: true));
            }
            if (DiagnosticsEnabled)
            {
                GameInterestSnapshot snapshot = _service.GetSnapshot();
                NotePending(snapshot.PendingRelationCount, snapshot.OutstandingBundleCount);
            }
        }

        public override CumulativeWork GetWork()
        {
            GameInterestSnapshot snapshot = _service.GetSnapshot();
            return new(
                snapshot.UniqueSemanticConstructions,
                0,
                0,
                snapshot.RelationSequenceAllocations,
                snapshot.SemanticRecordCopies,
                snapshot.SemanticRecordCopies,
                WireBytes,
                Deliveries,
                Proofs,
                MaxPending,
                MaxOutstanding,
                snapshot.ReservedBytes);
        }

        public override MemoryBreakdown GetMemoryBreakdown(CumulativeWork work)
        {
            GameInterestSnapshot snapshot = _service.GetSnapshot();
            return BuildMemoryBreakdown(
                snapshot.ReservedBytes,
                0);
        }

        public override void AssertSemanticContract() => AssertConverged();

        public override void Dispose() { }

        private void ApplyRecipient(int index, int cadenceStep)
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
                Participant(index), Point(recipient), checked(cadenceStep * 16L + index));
            if (status is not (GameInterestUpdateStatus.Success or GameInterestUpdateStatus.NoChange))
                throw new InvalidOperationException(
                    $"P0.3 maintained recipient {index} update returned {status} at ({recipient.X},{recipient.Z}).");
            _serviceActive[index] = true;
        }

        private void ApplyEntity(int index, int cadenceStep)
        {
            TraceEntity entity = State[index];
            if (!entity.Present)
            {
                if (_entityServicePresent[index])
                {
                    Require(_service.RemoveEntity(
                        ReplicationId(entity), entity.Version,
                        checked(cadenceStep * 16L + index)) is
                        GameInterestUpdateStatus.Success or GameInterestUpdateStatus.StaleVersion);
                    _entityServicePresent[index] = false;
                }
                return;
            }
            var projected = new GameProjectedEntity(
                ReplicationId(entity), true,
                new GameSimulationState { TickMarker = checked((int)entity.Version) });
            Require(_service.UpsertEntity(
                projected, entity.Version, Point(entity),
                entity.Public ? GameReplicationHorizon.Far : GameReplicationHorizon.None,
                checked(cadenceStep * 16L + index)) is
                GameInterestUpdateStatus.Success or GameInterestUpdateStatus.NoChange);
            _entityServicePresent[index] = true;
        }
    }

    private sealed class DirtyEntityAdapter : ProfileAdapter
    {
        private readonly DirtyEntitySpatialFanout _service;
        private readonly DirtyEntityDeliveryRecord[] _records;
        private readonly bool[] _serviceActive;

        internal DirtyEntityAdapter(Workload workload, bool chunkBatched = false)
            : base(workload, usesChunkInterest: false)
        {
            DirtyEntitySpatialFanoutLimits limits = new(
                workload.Entities.Length,
                workload.Recipients.Length,
                CreateInterestLimits(workload).MaximumRegions,
                checked(workload.Entities.Length * workload.Recipients.Length),
                Math.Max(64, workload.Recipients.Length * 4),
                32,
                _dirtyEntitySpatialRegionEdgeMillimeters);
            _service = chunkBatched
                ? new DirtyEntityChunkBatchFanout(
                    new StateEpoch(0xF033), limits, GameInterestProfile.Reference)
                : new DirtyEntitySpatialFanout(
                    new StateEpoch(0xF032), limits, GameInterestProfile.Reference);
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
                ApplyEntity(index, 0);
            Drain(0);
        }

        protected override void ApplyCore(TraceTick tick, int cadenceStep)
        {
            foreach (RecipientMove move in tick.Moves) ApplyRecipient(move.RecipientIndex, cadenceStep);
            foreach (EntityMutation mutation in tick.Mutations) ApplyEntity(mutation.EntityIndex, cadenceStep);
            Drain(cadenceStep);
        }

        public override void Drain(int cadenceStep)
        {
            Require(_service.TryProcessDirtyEntities(cadenceStep * 16L, out _) is
                DirtyEntityProcessStatus.Success or DirtyEntityProcessStatus.NoData);
            if (DiagnosticsEnabled)
            {
                DirtyEntitySpatialFanoutSnapshot snapshot = _service.GetSnapshot();
                NotePending(snapshot.PendingObligationCount, snapshot.OutstandingBundleCount);
            }
            int bundleGuard = 0;
            while (true)
            {
                if (++bundleGuard > State.Length * Recipients.Length * 2 + 64)
                    throw new InvalidOperationException(
                        $"P0.3 dirty-entity drain did not converge at cadence {cadenceStep}: {_service.GetSnapshot()}.");
                DirtyEntityBuildStatus status = _service.TryBuildNextBundle(
                    cadenceStep * 16L, ByteBudget, _records, out DirtyEntityBundle bundle);
                if (status == DirtyEntityBuildStatus.NoData) break;
                Require(status == DirtyEntityBuildStatus.Success);
                for (int index = 0; index < bundle.RecordCount; index++)
                {
                    DirtyEntityDeliveryRecord record = _records[index];
                    RecordEntity(ParticipantIndex(bundle.Participant), record.Entity,
                        record.CanonicalVersion, record.IsPresent,
                        record.Position, record.SimulationState);
                }
                RecordMessageBytes(bundle.EstimatedEncodedBytes);
                Require(_service.TryAcknowledgeBundle(
                    bundle.Participant, bundle.DeliveryBundleId, applied: true));
            }
            if (DiagnosticsEnabled)
            {
                DirtyEntitySpatialFanoutSnapshot snapshot = _service.GetSnapshot();
                NotePending(snapshot.PendingObligationCount, snapshot.OutstandingBundleCount);
            }
        }

        public override CumulativeWork GetWork()
        {
            DirtyEntitySpatialFanoutSnapshot snapshot = _service.GetSnapshot();
            return new(
                snapshot.DirtyEntitiesProcessed,
                snapshot.SpatialRecipientDiscoveryOperations,
                snapshot.CandidateRecipientsExamined,
                snapshot.BundlesBuilt,
                snapshot.DirtyEntitiesProcessed,
                MaterializedRecords,
                WireBytes,
                Deliveries,
                Proofs,
                MaxPending,
                MaxOutstanding,
                snapshot.ReservedBytes);
        }

        public override MemoryBreakdown GetMemoryBreakdown(CumulativeWork work)
        {
            DirtyEntitySpatialFanoutSnapshot snapshot = _service.GetSnapshot();
            return BuildMemoryBreakdown(
                snapshot.ReservedBytes,
                0);
        }

        public override void AssertSemanticContract() => AssertConverged();

        public override void Dispose() { }

        private void ApplyRecipient(int index, int cadenceStep)
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
                Participant(index), Point(recipient), checked(cadenceStep * 16L + index)) is
                DirtyEntityUpdateStatus.Success or DirtyEntityUpdateStatus.NoChange);
            _serviceActive[index] = true;
        }

        private void ApplyEntity(int index, int cadenceStep)
        {
            TraceEntity entity = State[index];
            var projected = new GameProjectedEntity(
                ReplicationId(entity), true,
                new GameSimulationState { TickMarker = checked((int)entity.Version) });
            Require(_service.CommitEntity(
                new DirtyEntityLatestState(
                    ReplicationId(entity), entity.Version, entity.Present, entity.Public,
                    entity.Present ? GameReplicationHorizon.Far : GameReplicationHorizon.None,
                    Point(entity), projected.SimulationState),
                    checked(cadenceStep * 16L + index)) is
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

        internal ChunkPublicationAdapter(Workload workload) : base(workload, usesChunkInterest: true)
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
            for (int index = 0; index < _recipients.Length; index++)
                _recipients[index] = CreateRecipient();
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
            if (DiagnosticsEnabled)
            {
                ChunkPublicationWorkSchedulerSnapshot snapshot = _scheduler.GetSnapshot();
                NotePending(snapshot.PendingWorkCount, 0);
            }
            Drain(cadenceStep);
        }

        public override void Drain(int cadenceStep)
        {
            while (_scheduler.TryDequeueDue(
                       checked((ulong)cadenceStep), out ChunkPublicationWork work))
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
                    RecordMessageBytes(UltraSimGameProtocol.GetChunkSnapshotEncodedBytes(read.Snapshot.Length));
                }
                else if (status == OfflineDirectChunkReadStatus.Delta)
                {
                    foreach (WorldPublicDeltaRecord record in read.Deltas)
                        RecordChunkDelta(recipientIndex, record);
                    RecordMessageBytes(UltraSimGameProtocol.GetChunkDeltaEncodedBytes(read.Deltas.Length));
                }
                else if (status == OfflineDirectChunkReadStatus.ChunkAbsent)
                {
                    ClearChunk(recipientIndex, work.Key);
                    RecordMessageBytes(RemoteChunkAbsenceMessageCodec.EncodedSize);
                }
                recipient.CommitRead();
            }
            if (DiagnosticsEnabled)
            {
                ChunkPublicationWorkSchedulerSnapshot scheduler = _scheduler.GetSnapshot();
                NotePending(scheduler.PendingWorkCount, 0);
            }
        }

        public override CumulativeWork GetWork()
        {
            ChunkPublicationWorkSchedulerSnapshot scheduler = _scheduler.GetSnapshot();
            ulong reads = 0;
            for (int index = 0; index < _recipients.Length; index++)
            {
                OfflineDirectChunkRecipient? recipient = _recipients[index];
                if (recipient is null) continue;
                OfflineDirectChunkRecipientSnapshot snapshot = recipient.GetSnapshot();
                reads = checked(reads + snapshot.SnapshotReads + snapshot.DeltaReads + snapshot.NoChangeReads);
            }
            return new(
                scheduler.DirtyChunksObserved + scheduler.IncomingQueued,
                0,
                scheduler.DirtyRecipientFanout + scheduler.PendingMembershipsExamined,
                scheduler.IncomingQueued,
                reads,
                MaterializedRecords,
                WireBytes,
                Deliveries,
                Proofs,
                MaxPending,
                MaxOutstanding,
                scheduler.ReservedBytes + RecipientReservedBytes());
        }

        public override MemoryBreakdown GetMemoryBreakdown(CumulativeWork work)
        {
            ChunkPublicationWorkSchedulerSnapshot scheduler = _scheduler.GetSnapshot();
            return BuildMemoryBreakdown(
                checked(scheduler.ReservedBytes + RecipientReservedBytes()),
                _publication.Source.GetSnapshot().ReservedBytes);
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
                    _recipients[index]!.ResetForReuse();
                }
                return;
            }
            if (!_registered[index])
            {
                Require(_scheduler.TryRegister(_recipients[index]!, out _recipientIds[index]));
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
                reserved = checked(reserved + _recipients[index]!.GetStorageSnapshot().TotalReservedBytes);
            return reserved;
        }

        private static OfflineDirectChunkRecipient CreateRecipient() => new(
            new OfflineDirectChunkRecipientLimits(MaxSubscriptionsPerRecipient, MaxEntities, MaxEntities),
            GameChunkInterestProfile.Default,
            OfflineDirectChunkCadence.EvidenceProfile);
    }

    private sealed class DirtyChunkAdapter : ProfileAdapter
    {
        private readonly PublicationFixture _publication;
        private readonly DirtyChunkSpatialFanout _service;
        private readonly bool[] _serviceActive;
        private readonly OfflineDirectChunkSubscriptionChange[] _changes;

        internal DirtyChunkAdapter(Workload workload) : base(workload, usesChunkInterest: true)
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
                    _dirtyChunkSpatialRegionEdgeMillimeters),
                GameChunkInterestProfile.Default,
                OfflineDirectChunkCadence.EvidenceProfile);
            _serviceActive = new bool[Recipients.Length];
            _changes = new OfflineDirectChunkSubscriptionChange[MaxSubscriptionsPerRecipient * 2];
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
            if (DiagnosticsEnabled)
            {
                DirtyChunkSpatialFanoutSnapshot beforeDelivery = _service.GetSnapshot();
                NotePending(beforeDelivery.PendingChunkObligationCount,
                    beforeDelivery.OutstandingDeliveryCount);
            }
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
                        RecordMessageBytes(UltraSimGameProtocol.GetChunkSnapshotEncodedBytes(
                            delivery.Snapshot.Length));
                    }
                    else if (delivery.Status == OfflineDirectChunkReadStatus.Delta)
                    {
                        foreach (WorldPublicDeltaRecord record in delivery.Deltas)
                        {
                            RecordChunkDelta(recipientIndex, record);
                        }
                        RecordMessageBytes(UltraSimGameProtocol.GetChunkDeltaEncodedBytes(
                            delivery.Deltas.Length));
                    }
                    else if (delivery.Status == OfflineDirectChunkReadStatus.ChunkAbsent)
                    {
                        ClearChunk(recipientIndex, delivery.Key);
                        RecordMessageBytes(RemoteChunkAbsenceMessageCodec.EncodedSize);
                    }
                    Require(_service.TryAcknowledgeDelivery(
                        participant, delivery.DeliveryId, applied: true) ==
                        DirtyChunkAppliedProofStatus.Applied);
                }
            }
            if (DiagnosticsEnabled)
            {
                DirtyChunkSpatialFanoutSnapshot snapshot = _service.GetSnapshot();
                NotePending(snapshot.PendingChunkObligationCount, snapshot.OutstandingDeliveryCount);
            }
        }

        public override CumulativeWork GetWork()
        {
            DirtyChunkSpatialFanoutSnapshot snapshot = _service.GetSnapshot();
            return new(
                snapshot.DirtyChunksProcessed + snapshot.IncomingCurrentTruthScopesQueued,
                snapshot.SpatialRecipientDiscoveryOperations,
                snapshot.CandidateRecipientsExamined,
                snapshot.RecipientTopologyReconciliations,
                snapshot.DirtyChunksObserved,
                MaterializedRecords,
                WireBytes,
                Deliveries,
                Proofs,
                MaxPending,
                MaxOutstanding,
                snapshot.ReservedBytes);
        }

        public override MemoryBreakdown GetMemoryBreakdown(CumulativeWork work)
        {
            DirtyChunkSpatialFanoutSnapshot snapshot = _service.GetSnapshot();
            return BuildMemoryBreakdown(
                snapshot.ReservedBytes,
                _publication.Source.GetSnapshot().ReservedBytes);
        }

        public override void AssertSemanticContract() => AssertConverged();

        public override void Dispose() => _publication.Dispose();

        private void ApplyRecipient(int index)
        {
            TraceRecipient recipient = Recipients[index];
            if (!recipient.Active)
            {
                if (_serviceActive[index])
                {
                    ClearRecipient(index);
                    Require(_service.RemoveRecipient(Participant(index)));
                }
                _serviceActive[index] = false;
                return;
            }
            DirtyChunkRecipientUpdateStatus status = _service.SetRecipientAnchor(
                Participant(index), GameWorldState.DefaultSpace, WorldPosition(recipient),
                checked((ulong)(index + 1)), _changes,
                out OfflineDirectChunkReconcileResult result);
            Require(status is DirtyChunkRecipientUpdateStatus.Reconciled or
                DirtyChunkRecipientUpdateStatus.NoChange);
            for (int changeIndex = 0; changeIndex < result.ChangeCount; changeIndex++)
                if (_changes[changeIndex].Kind == OfflineDirectChunkSubscriptionChangeKind.Left)
                    ClearChunk(index, _changes[changeIndex].Key);
            _serviceActive[index] = true;
        }
    }

    private sealed class PublicationFixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly Entity[] _handles;
        private readonly WorldPublicChange[] _changes;
        private readonly bool[] _touched;
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
                        CreateWorldPosition(entity), Descriptor(entity))
                    : WorldPublicChange.Absent(_handles[index]);
                _touched[index] = false;
            }
            if (count != 0)
                Require(Source.TryCommit(_changes.AsSpan(0, count), out _));
        }

        private static WorldPublicDescriptor Descriptor(in TraceEntity entity) => new(
            GameWorldPublicPrefabIds.Character,
            new WorldYaw(unchecked((ushort)entity.Version)),
            WorldPublicStateFlags.Visible);

        public void Dispose() => _world.Dispose();
    }

    private static GameInterestLimits CreateInterestLimits(Workload workload)
    {
        int farCells = (192_500 + 16_000 - 1) / 16_000;
        int edgeWidth = farCells * 2 + 1;
        // Recipient envelope rebuilds allocate the new region/edge set before retiring the old
        // one. Reserve two simultaneous envelopes so teleport/re-entry measures the profile,
        // rather than tripping a harness-only transient-capacity limit.
        int maximumEdges = checked(edgeWidth * edgeWidth * workload.Recipients.Length * 2 + edgeWidth);
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
