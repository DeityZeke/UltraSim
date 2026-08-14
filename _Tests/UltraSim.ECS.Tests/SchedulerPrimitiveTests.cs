using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using UltraSim.ECS;
using UltraSim.ECS.Tests;

internal static class SchedulerPrimitiveTests
{
    internal static void RunAll()
    {
        Run(nameof(Layouts_AndDisabledWorld_AreExplicit), Layouts_AndDisabledWorld_AreExplicit);
        Run(nameof(CapacityAccounting_SeparatesPlanFromPostSealOwnership), CapacityAccounting_SeparatesPlanFromPostSealOwnership);
        Run(nameof(SerialGeneratedSystem_HonorsLifecycleCadenceAndSnapshots), SerialGeneratedSystem_HonorsLifecycleCadenceAndSnapshots);
        Run(nameof(GeneratedMissingFamilies_ExecuteThroughPublicSurface), GeneratedMissingFamilies_ExecuteThroughPublicSurface);
        Run(nameof(ParallelAndSerialGeneratedExecution_AreCanonical), ParallelAndSerialGeneratedExecution_AreCanonical);
        Run(nameof(GeneratedColumns_DoNotAuthorizeRandomLookups), GeneratedColumns_DoNotAuthorizeRandomLookups);
        Run(nameof(DeclaredRandomLookups_RemainAuthorized), DeclaredRandomLookups_RemainAuthorized);
        Run(nameof(ParallelRandomAccess_RequiresCompleteOwnershipProof), ParallelRandomAccess_RequiresCompleteOwnershipProof);
        Run(nameof(ParallelRandomEnableWrites_PreserveSharedPhysicalWord), ParallelRandomEnableWrites_PreserveSharedPhysicalWord);
        Run(nameof(RetainedWorld_IsUnavailableDuringGraphExecution), RetainedWorld_IsUnavailableDuringGraphExecution);
        Run(nameof(RetainedStructuralArena_IsUnavailableDuringGraphExecution), RetainedStructuralArena_IsUnavailableDuringGraphExecution);
        Run(nameof(ManyArchetypes_RespectGlobalPartitionMaximum), ManyArchetypes_RespectGlobalPartitionMaximum);
        Run(nameof(SerialMode_WithoutCallerUsesOneWorker), SerialMode_WithoutCallerUsesOneWorker);
        Run(nameof(RangeJobs_PartitionExactlyOnceAcrossPersistentWorkers), RangeJobs_PartitionExactlyOnceAcrossPersistentWorkers);
        Run(nameof(AutoPolicy_BelowCrossoverDoesNotWakeBackgroundWorkers), AutoPolicy_BelowCrossoverDoesNotWakeBackgroundWorkers);
        Run(nameof(RegistrationIdentityLifecycleAndStageOrder_AreExplicit), RegistrationIdentityLifecycleAndStageOrder_AreExplicit);
        Run(nameof(GraphDependenciesAndStageValidation_AreExplicit), GraphDependenciesAndStageValidation_AreExplicit);
        Run(nameof(LateReadyForwardDependency_StillHonorsCompiledConflicts), LateReadyForwardDependency_StillHonorsCompiledConflicts);
        Run(nameof(RandomizedDagConflictCompilation_MatchesReference), RandomizedDagConflictCompilation_MatchesReference);
        Run(nameof(FailedRegistration_IsTransactional), FailedRegistration_IsTransactional);
        Run(nameof(DependencyCapacityFailure_DoesNotPoisonRegistration), DependencyCapacityFailure_DoesNotPoisonRegistration);
        Run(nameof(InvalidSchedulerEnums_AreRejectedCold), InvalidSchedulerEnums_AreRejectedCold);
        Run(nameof(RangeWorkItemExhaustion_HasBoundedFailureClassification), RangeWorkItemExhaustion_HasBoundedFailureClassification);
        Run(nameof(Cadence_DoesNotMakeDependenciesSameTickPrerequisites), Cadence_DoesNotMakeDependenciesSameTickPrerequisites);
        Run(nameof(ConflictsAndDependencies_CompileHardWaveBarriers), ConflictsAndDependencies_CompileHardWaveBarriers);
        Run(nameof(StructuralCapabilities_DoNotCreateFalseGraphHazards), StructuralCapabilities_DoNotCreateFalseGraphHazards);
        Run(nameof(ParallelTrackedWrites_PublishOneVersionAndExactRows), ParallelTrackedWrites_PublishOneVersionAndExactRows);
        Run(nameof(TrackedNoWrite_DoesNotAdvancePublication), TrackedNoWrite_DoesNotAdvancePublication);
        Run(nameof(ScheduledChangedRows_AcknowledgeOnlySuccessfulExecution), ScheduledChangedRows_AcknowledgeOnlySuccessfulExecution);
        Run(nameof(TrackedReadWrite_ProvidesDirectAndScheduledParity), TrackedReadWrite_ProvidesDirectAndScheduledParity);
        Run(nameof(PhaseStaggering_IsOptInDeterministicAndStageBounded), PhaseStaggering_IsOptInDeterministicAndStageBounded);
        Run(nameof(WorkerCapabilities_ExpireAndCrossSegmentDeferredTargetsFault), WorkerCapabilities_ExpireAndCrossSegmentDeferredTargetsFault);
        Run(nameof(StructuralLanes_MergeInLogicalOrderAndApplyAtPostBarrier), StructuralLanes_MergeInLogicalOrderAndApplyAtPostBarrier);
        Run(nameof(Lifecycle_PreBarrierAndPostBarrierFollowCommitOrder), Lifecycle_PreBarrierAndPostBarrierFollowCommitOrder);
        Run(nameof(Tick_PreBarrierMakesExternalCommandsVisibleToSystems), Tick_PreBarrierMakesExternalCommandsVisibleToSystems);
        Run(nameof(TypedStructuralRoutes_HandleAddReplaceRemoveTagsAndPayloadExhaustion), TypedStructuralRoutes_HandleAddReplaceRemoveTagsAndPayloadExhaustion);
        Run(nameof(StructuralRunKeys_DiscriminateTypedRoutesFromPublicTransitions), StructuralRunKeys_DiscriminateTypedRoutesFromPublicTransitions);
        Run(nameof(TagAdd_ConsumesZeroPayloadCapacity), TagAdd_ConsumesZeroPayloadCapacity);
        Run(nameof(ManualSystems_LeaveStructuralOutputForNextExternalEpoch), ManualSystems_LeaveStructuralOutputForNextExternalEpoch);
        Run(nameof(WorkerFailure_FaultsWorldAndSkipsStructuralPublication), WorkerFailure_FaultsWorldAndSkipsStructuralPublication);
        Run(nameof(WorkerDisposal_JoinsEveryBackgroundThread), WorkerDisposal_JoinsEveryBackgroundThread);
        Run(nameof(PartialWorkerStartFailure_IsCleanedUp), PartialWorkerStartFailure_IsCleanedUp);
        Run(nameof(WarmedSerialTick_AllocatesZeroManagedBytes), WarmedSerialTick_AllocatesZeroManagedBytes);
    }

    private static void Layouts_AndDisabledWorld_AreExplicit()
    {
        Assert.Equal(4, Unsafe.SizeOf<SystemId>());
        Assert.Equal(4, Marshal.SizeOf<SystemId>());
        Assert.Equal(16, Unsafe.SizeOf<SystemSchedule>());
        Assert.Equal(44, Unsafe.SizeOf<EcsSchedulerOptions>());
        Assert.Equal(16, Unsafe.SizeOf<SchedulerChunkPartition>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<SystemId>());
        using EcsWorld world = CreateWorld(EcsSchedulerOptions.Disabled, entityCapacity: 1, out _, out _, out _);
        Assert.False(world.HasScheduler);
        Assert.Equal(0L, world.SchedulerReservedBytes);
        Assert.Throws<InvalidOperationException>(() => world.Tick(1d / 60d));
    }

    private static void CapacityAccounting_SeparatesPlanFromPostSealOwnership()
    {
        using EcsWorld world = CreateBareWorld(Options(2), 64, out _);
        world.Seal();
        world.Initialize();
        WorldReservedPayloadSnapshot snapshot = world.GetReservedPayloadSnapshot();
        Assert.Equal(world.SchedulerReservedBytes, snapshot.SchedulerBytes);
        Assert.Equal(world.StructuralCommands.ReservedBytes, snapshot.MainStructuralCommandBytes);
        Assert.True(snapshot.SchedulerBytes > 0);
        Assert.True(snapshot.MainStructuralCommandBytes > 0);
        Assert.True(snapshot.NativeChunkPayloadBytes > 0);
        Assert.Equal(
            snapshot.KnownManagedPayloadBytes + snapshot.NativeChunkPayloadBytes,
            snapshot.TotalKnownPayloadBytes);

        WorldCapacityPlan plan = WorldCapacityPlanner.Plan(
            new EntityCapacity(64), ReadOnlySpan<ArchetypeCapacityPlan>.Empty,
            managedSchedulerBytes: snapshot.SchedulerBytes);
        Assert.Equal(plan.TotalReservedBytes, plan.TotalPlannedPayloadBytes);
    }

    private static void SerialGeneratedSystem_HonorsLifecycleCadenceAndSnapshots()
    {
        EcsSchedulerOptions scheduler = Options(backgroundWorkers: 0);
        using EcsWorld world = CreateWorld(scheduler, 512, out ArchetypeHandle archetype,
            out ReadWriteJobPlan<LayoutWide, LayoutInt> plan, out ComponentLookup<LayoutInt> integers);
        var runtime = new IncrementRuntime();
        SystemId id = world.RegisterReadWriteSystem<LayoutWide, LayoutInt, IncrementRuntime, IncrementJob>(
            new SystemDefinition("test.increment", SystemStage.Movement,
                new SystemSchedule(enabled: true, periodTicks: 2, startOffset: 1),
                SystemExecutionPolicy.Serial, persistenceId: 1001, stateSchemaVersion: 1),
            plan, runtime);
        Start(world, archetype, 512);

        world.Tick(0.01);
        Assert.Equal(0, runtime.CompletedRows);
        world.Tick(0.02);
        Assert.Equal(512, runtime.CompletedRows);
        world.Tick(0.03);
        Assert.Equal(512, runtime.CompletedRows);
        world.Tick(0.04);
        Assert.Equal(1_024, runtime.CompletedRows);
        Assert.Equal(4UL, world.TickCount);
        Assert.Equal(1, runtime.InitializeCount);

        var snapshots = new SystemRuntimeSnapshot[1];
        Assert.Equal(1, world.CopySystemRuntimeSnapshots(snapshots));
        Assert.Equal(id, snapshots[0].Id);
        Assert.Equal(2UL, snapshots[0].RunCount);
        Assert.False(snapshots[0].LastExecutionWasParallel);
        Assert.Equal(512, snapshots[0].LastRows);

        Assert.True(world.TryGetEntity(0, out Entity first));
        Assert.True(world.TryRead(first, integers, out LayoutInt value));
        Assert.Equal(2, value.Value);
    }

    private static void GeneratedMissingFamilies_ExecuteThroughPublicSurface()
    {
        using EcsWorld world = CreateBareWorld(Options(0), 128, out ArchetypeHandle archetype);
        ReadJobPlan<LayoutWide> read = world.PrepareReadJob<LayoutWide>();
        WriteJobPlan<LayoutInt> write = world.PrepareWriteJob<LayoutInt>();
        ReadReadReadJobPlan<LayoutWide, LayoutInt, LayoutEnableLong> read3 =
            world.PrepareReadReadReadJob<LayoutWide, LayoutInt, LayoutEnableLong>();
        var readRuntime = new ReadOnlyRuntime();
        var writeRuntime = new WriteOnlyRuntime();
        var read3Runtime = new ReadThreeRuntime();
        world.RegisterReadSystem<LayoutWide, ReadOnlyRuntime, ReadOnlyJob>(
            new SystemDefinition("test.1r", SystemStage.Input), read, readRuntime);
        world.RegisterWriteSystem<LayoutInt, WriteOnlyRuntime, WriteOnlyJob>(
            new SystemDefinition("test.1w", SystemStage.Movement), write, writeRuntime);
        world.RegisterReadReadReadSystem<LayoutWide, LayoutInt, LayoutEnableLong, ReadThreeRuntime, ReadThreeJob>(
            new SystemDefinition("test.3r", SystemStage.WorldSim), read3, read3Runtime);
        Start(world, archetype, 128);
        world.Tick(1d / 60d);
        Assert.Equal(128, readRuntime.Rows);
        Assert.Equal(128, writeRuntime.Rows);
        Assert.Equal(128, read3Runtime.Rows);
    }

    private static void ParallelAndSerialGeneratedExecution_AreCanonical()
    {
        using EcsWorld serial = CreateCanonicalWorld(Options(2), SchedulerRunMode.Serial, out IncrementRuntime serialRuntime);
        using EcsWorld parallel = CreateCanonicalWorld(Options(2), SchedulerRunMode.Parallel, out IncrementRuntime parallelRuntime);
        for (int tick = 0; tick < 16; tick++)
        {
            serial.Tick(1d / 60d);
            parallel.Tick(1d / 60d);
        }
        Assert.Equal(serialRuntime.CompletedRows, parallelRuntime.CompletedRows);
        Assert.Equal(HashIntegers(serial, 4_096), HashIntegers(parallel, 4_096));
        var snapshot = new SystemRuntimeSnapshot[1];
        parallel.CopySystemRuntimeSnapshots(snapshot);
        Assert.True(snapshot[0].LastExecutionWasParallel);
        Assert.True(snapshot[0].LastWorkItems > 1);
    }

    private static void GeneratedColumns_DoNotAuthorizeRandomLookups()
    {
        using EcsWorld world = CreateWorld(Options(0), 1, out ArchetypeHandle archetype,
            out ReadWriteJobPlan<LayoutWide, LayoutInt> plan, out ComponentLookup<LayoutInt> integers);
        var runtime = new UndeclaredLookupRuntime(integers);
        world.RegisterReadWriteSystem<LayoutWide, LayoutInt, UndeclaredLookupRuntime, UndeclaredLookupJob>(
            new SystemDefinition("capability.generated-is-not-random", SystemStage.Movement,
                executionPolicy: SystemExecutionPolicy.Serial), plan, runtime);
        Start(world, archetype, 1);
        Assert.True(world.TryGetEntity(0, out runtime.Entity));
        world.Tick(0.1);
        Assert.Equal(1, runtime.ReadRejections);
        Assert.Equal(1, runtime.WriteRejections);
        Assert.True(world.TryRead(runtime.Entity, integers, out LayoutInt unchanged));
        Assert.Equal(0, unchanged.Value);
    }

    private static void DeclaredRandomLookups_RemainAuthorized()
    {
        using EcsWorld world = CreateBareWorld(Options(0), 1, out ArchetypeHandle archetype);
        ComponentLookup<LayoutInt> integers = world.PrepareLookup<LayoutInt>();
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        var runtime = new DeclaredLookupRuntime(integers);
        world.RegisterRangeSystem<DeclaredLookupRuntime, DeclaredLookupJob>(
            new SystemDefinition("capability.declared-random", SystemStage.Movement,
                executionPolicy: SystemExecutionPolicy.Serial,
                additionalWrites: [integer],
                parallelRandomAccessPolicy: ParallelRandomAccessPolicy.PartitionDisjoint), runtime);
        Start(world, archetype, 1);
        Assert.True(world.TryGetEntity(0, out runtime.Entity));
        world.Tick(0.1);
        Assert.True(runtime.ReadAccepted);
        Assert.True(runtime.WriteAccepted);
        Assert.True(world.TryRead(runtime.Entity, integers, out LayoutInt value));
        Assert.Equal(456, value.Value);
    }

    private static void ParallelRandomAccess_RequiresCompleteOwnershipProof()
    {
        ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        using (EcsWorld randomWrite = CreateBareWorld(Options(3), 1, out ArchetypeHandle randomWriteArchetype))
        {
            var marks = new int[4_096];
            randomWrite.RegisterRangeSystem<MarkRangeRuntime, MarkRangeJob>(
                new SystemDefinition("random-write.serial-default", SystemStage.AI,
                    executionPolicy: SystemExecutionPolicy.Parallel,
                    additionalWrites: [integer], parallelRowThreshold: 1,
                    minimumRowsPerPartition: 64), new MarkRangeRuntime(marks));
            Start(randomWrite, randomWriteArchetype, 0);
            randomWrite.Tick(0.1);
            var snapshot = new SystemRuntimeSnapshot[1];
            randomWrite.CopySystemRuntimeSnapshots(snapshot);
            Assert.False(snapshot[0].LastExecutionWasParallel);
        }

        using (EcsWorld randomRead = CreateBareWorld(Options(3), 1, out ArchetypeHandle randomReadArchetype))
        {
            var marks = new int[4_096];
            randomRead.RegisterRangeSystem<MarkRangeRuntime, MarkRangeJob>(
                new SystemDefinition("random-read.parallel", SystemStage.AI,
                    executionPolicy: SystemExecutionPolicy.Parallel,
                    additionalReads: [integer], parallelRowThreshold: 1,
                    minimumRowsPerPartition: 64), new MarkRangeRuntime(marks));
            Start(randomRead, randomReadArchetype, 0);
            randomRead.Tick(0.1);
            var snapshot = new SystemRuntimeSnapshot[1];
            randomRead.CopySystemRuntimeSnapshots(snapshot);
            Assert.True(snapshot[0].LastExecutionWasParallel);
        }

        using (EcsWorld serial = CreateWorld(Options(3), 4_096, out ArchetypeHandle serialArchetype,
            out ReadWriteJobPlan<LayoutWide, LayoutInt> serialPlan, out _))
        {
            serial.RegisterReadWriteSystem<LayoutWide, LayoutInt, IncrementRuntime, IncrementJob>(
                new SystemDefinition("random-overlap.serial", SystemStage.Movement,
                    executionPolicy: SystemExecutionPolicy.Parallel,
                    additionalReads: [integer], parallelRowThreshold: 1,
                    minimumRowsPerPartition: 64), serialPlan, new IncrementRuntime());
            Start(serial, serialArchetype, 4_096);
            serial.Tick(0.1);
            var snapshot = new SystemRuntimeSnapshot[1];
            serial.CopySystemRuntimeSnapshots(snapshot);
            Assert.False(snapshot[0].LastExecutionWasParallel);
        }

        using EcsWorld parallel = CreateWorld(Options(3), 4_096, out ArchetypeHandle parallelArchetype,
            out ReadWriteJobPlan<LayoutWide, LayoutInt> parallelPlan, out _);
        parallel.RegisterReadWriteSystem<LayoutWide, LayoutInt, IncrementRuntime, IncrementJob>(
            new SystemDefinition("random-overlap.proven", SystemStage.Movement,
                executionPolicy: SystemExecutionPolicy.Parallel,
                additionalReads: [integer], parallelRowThreshold: 1,
                minimumRowsPerPartition: 64,
                parallelRandomAccessPolicy: ParallelRandomAccessPolicy.PartitionDisjoint),
            parallelPlan, new IncrementRuntime());
        Start(parallel, parallelArchetype, 4_096);
        parallel.Tick(0.1);
        var parallelSnapshot = new SystemRuntimeSnapshot[1];
        parallel.CopySystemRuntimeSnapshots(parallelSnapshot);
        Assert.True(parallelSnapshot[0].LastExecutionWasParallel);

        // Lock every cross-capability intersection independently. These are
        // policy gates; the synchronized random-write fixture below exercises
        // the admitted physical path under actual overlap.
        AssertReadWriteRandomPolicy(
            "generated-read-random-write", additionalReads: null,
            additionalWrites: [wide], ParallelRandomAccessPolicy.SerialOnly,
            expectedParallel: false);
        AssertReadWriteRandomPolicy(
            "generated-write-random-write", additionalReads: null,
            additionalWrites: [integer], ParallelRandomAccessPolicy.SerialOnly,
            expectedParallel: false);
        AssertRangeRandomPolicy(
            "random-read-random-write", additionalReads: [integer],
            additionalWrites: [integer], ParallelRandomAccessPolicy.SerialOnly,
            expectedParallel: false);
        AssertRangeRandomPolicy(
            "random-read-random-write-proven", additionalReads: [integer],
            additionalWrites: [integer], ParallelRandomAccessPolicy.PartitionDisjoint,
            expectedParallel: true);
    }

    private static void AssertReadWriteRandomPolicy(
        string key,
        IReadOnlyList<ComponentId>? additionalReads,
        IReadOnlyList<ComponentId>? additionalWrites,
        ParallelRandomAccessPolicy policy,
        bool expectedParallel)
    {
        using EcsWorld world = CreateWorld(Options(3), 4_096, out ArchetypeHandle archetype,
            out ReadWriteJobPlan<LayoutWide, LayoutInt> plan, out _);
        world.RegisterReadWriteSystem<LayoutWide, LayoutInt, IncrementRuntime, IncrementJob>(
            new SystemDefinition($"random-policy.{key}", SystemStage.Movement,
                executionPolicy: SystemExecutionPolicy.Parallel,
                additionalReads: additionalReads,
                additionalWrites: additionalWrites,
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 64,
                parallelRandomAccessPolicy: policy), plan, new IncrementRuntime());
        Start(world, archetype, 4_096);
        world.Tick(0.1);
        var snapshot = new SystemRuntimeSnapshot[1];
        world.CopySystemRuntimeSnapshots(snapshot);
        Assert.Equal(expectedParallel, snapshot[0].LastExecutionWasParallel);
    }

    private static void AssertRangeRandomPolicy(
        string key,
        IReadOnlyList<ComponentId>? additionalReads,
        IReadOnlyList<ComponentId>? additionalWrites,
        ParallelRandomAccessPolicy policy,
        bool expectedParallel)
    {
        using EcsWorld world = CreateBareWorld(Options(3), 1, out ArchetypeHandle archetype);
        var marks = new int[4_096];
        world.RegisterRangeSystem<MarkRangeRuntime, MarkRangeJob>(
            new SystemDefinition($"random-policy.{key}", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Parallel,
                additionalReads: additionalReads,
                additionalWrites: additionalWrites,
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 64,
                parallelRandomAccessPolicy: policy), new MarkRangeRuntime(marks));
        Start(world, archetype, 0);
        world.Tick(0.1);
        var snapshot = new SystemRuntimeSnapshot[1];
        world.CopySystemRuntimeSnapshots(snapshot);
        Assert.Equal(expectedParallel, snapshot[0].LastExecutionWasParallel);
    }

    private static void ParallelRandomEnableWrites_PreserveSharedPhysicalWord()
    {
        const int count = 128;
        using EcsWorld world = CreateBareWorld(Options(3), count, out ArchetypeHandle archetype);
        ComponentLookup<LayoutEnableLong> lookup = world.PrepareLookup<LayoutEnableLong>();
        ChangedRowsJobPlan<LayoutEnableLong, LayoutEnableLong> changed =
            world.PrepareChangedRowsJob<LayoutEnableLong, LayoutEnableLong>();
        ComponentId component = LayoutTestSchema.GetComponentId<LayoutEnableLong>();
        var runtime = new RandomEnableRuntime(lookup, count);
        world.RegisterRangeSystem<RandomEnableRuntime, RandomEnableJob>(
            new SystemDefinition("random-enable.shared-word", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Parallel,
                additionalWrites: [component], parallelRowThreshold: 1,
                minimumRowsPerPartition: 1,
                parallelRandomAccessPolicy: ParallelRandomAccessPolicy.PartitionDisjoint), runtime);
        world.Seal();
        world.Initialize();
        for (int index = 0; index < count; index++)
        {
            Assert.True(world.TryCreate(archetype, out runtime.Entities[index]));
            Assert.True(world.TrySetEnabled(
                runtime.Entities[index], lookup, enabled: (index & 1) != 0));
        }
        ChangeCursor cursor = world.CreateChangeCursorFromNow();
        world.BeginRun();
        world.Tick(0.1);
        AssertEnablePattern(world, lookup, runtime.Entities, pattern: 0);
        var audit = new EnableChangedAuditJob();
        Assert.Equal(count, world.ExecuteChangedRowsJob(changed, ref cursor, ref audit));

        for (int iteration = 0; iteration < 256; iteration++)
        {
            // Alternating, all-set, and all-clear force mixed, set/set, and
            // clear/clear CAS contention inside both shared 64-bit words.
            runtime.Pattern = iteration % 3;
            for (int index = 0; index < count; index++)
            {
                bool desired = DesiredEnableState(index, runtime.Pattern);
                Assert.True(world.TrySetEnabled(runtime.Entities[index], lookup, !desired));
            }
            ChangeCursor iterationCursor = world.CreateChangeCursorFromNow();
            world.Tick(0.1);
            AssertEnablePattern(world, lookup, runtime.Entities, runtime.Pattern);
            var iterationAudit = new EnableChangedAuditJob();
            Assert.Equal(count,
                world.ExecuteChangedRowsJob(changed, ref iterationCursor, ref iterationAudit));
        }
    }

    private static void AssertEnablePattern(
        EcsWorld world,
        ComponentLookup<LayoutEnableLong> lookup,
        ReadOnlySpan<Entity> entities,
        int pattern)
    {
        for (int index = 0; index < entities.Length; index++)
        {
            Assert.True(world.TryGetEnabled(entities[index], lookup, out bool enabled));
            Assert.Equal(DesiredEnableState(index, pattern), enabled);
        }
    }

    private static bool DesiredEnableState(int index, int pattern) => pattern switch
    {
        0 => (index & 1) == 0,
        1 => true,
        2 => false,
        _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
    };

    private static void RetainedWorld_IsUnavailableDuringGraphExecution()
    {
        using EcsWorld world = CreateWorld(Options(0), 1, out ArchetypeHandle archetype,
            out ReadWriteJobPlan<LayoutWide, LayoutInt> plan, out ComponentLookup<LayoutInt> integers);
        var runtime = new RetainedWorldRuntime(integers);
        world.RegisterReadWriteSystem<LayoutWide, LayoutInt, RetainedWorldRuntime, RetainedWorldJob>(
            new SystemDefinition("capability.retained-world", SystemStage.Movement,
                executionPolicy: SystemExecutionPolicy.Serial), plan, runtime);
        world.Seal();
        world.Initialize();
        Assert.True(runtime.InitializeAccessSucceeded);
        Assert.True(world.TryCreate(archetype, out runtime.Entity));
        world.BeginRun();
        world.Tick(0.1);
        Assert.Equal(3, runtime.PrepareRejections);
        Assert.Equal(2, runtime.ExecuteRejections);
        Assert.Equal(3, runtime.CompleteRejections);
        world.Dispose();
        Assert.True(runtime.ShutdownAccessSucceeded);
    }

    private static void RetainedStructuralArena_IsUnavailableDuringGraphExecution()
    {
        AssertRetainedArenaRejected(Options(0), rangeCount: 1, expectedRejections: 3, "caller");
        var background = new EcsSchedulerOptions(
            maximumSystems: 1, maximumDependencies: 0, maximumWorkItems: 2,
            maximumStructuralSegments: 2, backgroundWorkerCount: 2,
            callerParticipates: false, structuralCommandsPerProducer: 8,
            structuralPayloadBytesPerProducer: 0, maximumPartitionsPerSystem: 2);
        AssertRetainedArenaRejected(background, rangeCount: 2, expectedRejections: 4, "background");
    }

    private static void AssertRetainedArenaRejected(
        EcsSchedulerOptions scheduler,
        int rangeCount,
        int expectedRejections,
        string suffix)
    {
        using EcsWorld world = CreateBareWorld(scheduler, 1, out ArchetypeHandle archetype);
        var runtime = new RetainedArenaRuntime(rangeCount);
        world.RegisterRangeSystem<RetainedArenaRuntime, RetainedArenaJob>(
            new SystemDefinition($"capability.retained-arena.{suffix}", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1, minimumRowsPerPartition: 1), runtime);
        world.Seal();
        world.Initialize();
        Assert.True(world.TryCreate(archetype, out runtime.Entity));
        world.BeginRun();
        world.Tick(0.1);
        Assert.Equal(expectedRejections, runtime.Rejections);
        Assert.Equal(1, world.AliveCount);
        Assert.Equal(0, world.StructuralCommands.Count);
    }

    private static void ManyArchetypes_RespectGlobalPartitionMaximum()
    {
        const int archetypeCount = 16;
        ComponentSchema schema = LayoutTestSchema.CreateSchema();
        ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
        ComponentId[] optional =
        [
            LayoutTestSchema.GetComponentId<LayoutByte>(),
            LayoutTestSchema.GetComponentId<LayoutInt>(),
            LayoutTestSchema.GetComponentId<LayoutEnableLong>(),
            LayoutTestSchema.GetComponentId<LayoutTag>(),
        ];
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 1, maximumDependencies: 0, maximumWorkItems: 8,
            maximumStructuralSegments: 8, backgroundWorkerCount: 3,
            callerParticipates: true, structuralCommandsPerProducer: 0,
            structuralPayloadBytesPerProducer: 0, maximumPartitionsPerSystem: 8,
            profilingEnabled: true);
        using EcsWorld world = new(schema, new EcsWorldOptions(
            new EntityCapacity(archetypeCount), archetypeCount, 4,
            maximumStructuralCommands: 0, scheduler: scheduler));
        var archetypes = new ArchetypeHandle[archetypeCount];
        for (int bits = 0; bits < archetypeCount; bits++)
        {
            var signature = new ComponentId[1 + BitOperations.PopCount(unchecked((uint)bits))];
            signature[0] = wide;
            int next = 1;
            for (int bit = 0; bit < optional.Length; bit++)
                if ((bits & (1 << bit)) != 0) signature[next++] = optional[bit];
            Array.Sort(signature, static (left, right) => left.CompareTo(right));
            archetypes[bits] = world.RegisterArchetype(signature, ChunkLayoutOptions.KiB16, 1);
        }
        ReadJobPlan<LayoutWide> plan = world.PrepareReadJob<LayoutWide>();
        var runtime = new ReadOnlyRuntime();
        world.RegisterReadSystem<LayoutWide, ReadOnlyRuntime, ReadOnlyJob>(
            new SystemDefinition("partition.global-cap", SystemStage.Movement,
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1, minimumRowsPerPartition: 1), plan, runtime);
        world.Seal();
        world.Initialize();
        for (int index = 0; index < archetypes.Length; index++)
            Assert.True(world.TryCreate(archetypes[index], out _));
        world.BeginRun();
        world.Tick(0.1);
        Assert.Equal(archetypeCount, runtime.Rows);
        var snapshots = new SystemRuntimeSnapshot[1];
        Assert.Equal(1, world.CopySystemRuntimeSnapshots(snapshots));
        Assert.True(snapshots[0].LastExecutionWasParallel);
        Assert.Equal(8, snapshots[0].LastWorkItems);
    }

    private static void SerialMode_WithoutCallerUsesOneWorker()
    {
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 2, maximumDependencies: 0, maximumWorkItems: 2,
            maximumStructuralSegments: 2, backgroundWorkerCount: 2,
            callerParticipates: false, structuralCommandsPerProducer: 0,
            structuralPayloadBytesPerProducer: 0, maximumPartitionsPerSystem: 1);
        using EcsWorld world = CreateBareWorld(scheduler, 1, out ArchetypeHandle archetype);
        var first = new ThreadCaptureRuntime();
        var second = new ThreadCaptureRuntime();
        world.RegisterRangeSystem<ThreadCaptureRuntime, ThreadCaptureJob>(
            new SystemDefinition("serial.worker.first", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Serial), first);
        world.RegisterRangeSystem<ThreadCaptureRuntime, ThreadCaptureJob>(
            new SystemDefinition("serial.worker.second", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Serial), second);
        Start(world, archetype, 0);
        world.SchedulerRunMode = SchedulerRunMode.Serial;
        world.Tick(0.1);
        Assert.True(first.ThreadId != 0);
        Assert.Equal(first.ThreadId, second.ThreadId);
    }

    private static void RangeJobs_PartitionExactlyOnceAcrossPersistentWorkers()
    {
        const int count = 10_003;
        var marks = new int[count];
        using EcsWorld world = CreateBareWorld(Options(3), 1, out ArchetypeHandle archetype);
        var runtime = new MarkRangeRuntime(marks);
        world.RegisterRangeSystem<MarkRangeRuntime, MarkRangeJob>(
            new SystemDefinition("test.range", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1, minimumRowsPerPartition: 1), runtime);
        Start(world, archetype, 0);
        world.Tick(0.5);
        for (int index = 0; index < marks.Length; index++)
            Assert.Equal(1, marks[index]);
        Assert.Equal(count, runtime.Rows);
        SchedulerRuntimeSnapshot snapshot = world.GetSchedulerRuntimeSnapshot();
        Assert.Equal(3, snapshot.BackgroundWorkerCount);
        Assert.Equal(4, snapshot.ExecutionConcurrency);
        Assert.True(snapshot.WorkerWakeCount > 0);
    }

    private static void AutoPolicy_BelowCrossoverDoesNotWakeBackgroundWorkers()
    {
        var values = new int[128];
        using EcsWorld world = CreateBareWorld(Options(3), 1, out ArchetypeHandle archetype);
        world.RegisterRangeSystem<MarkRangeRuntime, MarkRangeJob>(
            new SystemDefinition("test.auto.serial", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Auto,
                parallelRowThreshold: 4_096,
                minimumRowsPerPartition: 64),
            new MarkRangeRuntime(values));
        Start(world, archetype, 0);
        SchedulerRuntimeSnapshot before = world.GetSchedulerRuntimeSnapshot();
        world.Tick(0.1);
        SchedulerRuntimeSnapshot after = world.GetSchedulerRuntimeSnapshot();
        Assert.Equal(before.WorkerWakeCount, after.WorkerWakeCount);
        var systems = new SystemRuntimeSnapshot[1];
        world.CopySystemRuntimeSnapshots(systems);
        Assert.False(systems[0].LastExecutionWasParallel);
        for (int index = 0; index < values.Length; index++) Assert.Equal(1, values[index]);
    }

    private static void GraphDependenciesAndStageValidation_AreExplicit()
    {
        using (EcsWorld missing = CreateBareWorld(Options(0), 1, out _))
        {
            missing.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("missing", SystemStage.Input,
                    requires: [SystemId.FromRegistrationIndex(7)]), new NoOpRangeRuntime());
            Assert.Throws<InvalidOperationException>(missing.Seal);
        }

        using (EcsWorld reverse = CreateBareWorld(Options(0), 1, out _))
        {
            SystemId later = reverse.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("later", SystemStage.Cleanup), new NoOpRangeRuntime());
            reverse.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("earlier", SystemStage.Input, requires: [later]), new NoOpRangeRuntime());
            Assert.Throws<InvalidOperationException>(reverse.Seal);
        }

        using (EcsWorld cycle = CreateBareWorld(Options(0), 1, out _))
        {
            cycle.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("a", SystemStage.Input,
                    requires: [SystemId.FromRegistrationIndex(1)]), new NoOpRangeRuntime());
            cycle.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("b", SystemStage.Input,
                    requires: [SystemId.FromRegistrationIndex(0)]), new NoOpRangeRuntime());
            Assert.Throws<InvalidOperationException>(cycle.Seal);
        }
    }

    private static void LateReadyForwardDependency_StillHonorsCompiledConflicts()
    {
        using EcsWorld world = CreateBareWorld(Options(3), 1, out ArchetypeHandle archetype);
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        var overlap = new ConcurrentOverlapProbe();
        world.RegisterRangeSystem<ConcurrentOverlapRuntime, ConcurrentOverlapJob>(
            new SystemDefinition("late.a", SystemStage.AI,
                requires: [SystemId.FromRegistrationIndex(3)],
                executionPolicy: SystemExecutionPolicy.Parallel,
                additionalWrites: [integer], parallelRowThreshold: 1,
                minimumRowsPerPartition: 1), new ConcurrentOverlapRuntime(overlap));
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("late.e", SystemStage.AI), new NoOpRangeRuntime());
        world.RegisterRangeSystem<ConcurrentOverlapRuntime, ConcurrentOverlapJob>(
            new SystemDefinition("late.b", SystemStage.AI,
                requires: [SystemId.FromRegistrationIndex(1)],
                executionPolicy: SystemExecutionPolicy.Parallel,
                additionalWrites: [integer], parallelRowThreshold: 1,
                minimumRowsPerPartition: 1), new ConcurrentOverlapRuntime(overlap));
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("late.c", SystemStage.AI), new NoOpRangeRuntime());
        Start(world, archetype, 0);
        var snapshots = new SystemRuntimeSnapshot[4];
        Assert.Equal(4, world.CopySystemRuntimeSnapshots(snapshots));
        Assert.NotEqual(snapshots[0].CompiledWave, snapshots[2].CompiledWave);
        Assert.True(snapshots[0].CompiledWave > snapshots[3].CompiledWave);
        Assert.True(snapshots[2].CompiledWave > snapshots[1].CompiledWave);
        for (int iteration = 0; iteration < 16; iteration++)
            world.Tick(0.1);
        Assert.Equal(32, overlap.Executions);
        Assert.Equal(1, overlap.MaximumConcurrent);
    }

    private static void RandomizedDagConflictCompilation_MatchesReference()
    {
        const int nodeCount = 8;
        ComponentId[] components =
        [
            LayoutTestSchema.GetComponentId<LayoutWide>(),
            LayoutTestSchema.GetComponentId<LayoutInt>(),
            LayoutTestSchema.GetComponentId<LayoutEnableLong>(),
            LayoutTestSchema.GetComponentId<LayoutTag>(),
        ];
        for (int seed = 1; seed <= 64; seed++)
        {
            var random = new Random(seed);
            int[] topological = Enumerable.Range(0, nodeCount).ToArray();
            random.Shuffle(topological);
            var dependencies = new List<SystemId>[nodeCount];
            var writeComponents = new ComponentId[nodeCount];
            for (int index = 0; index < nodeCount; index++)
            {
                dependencies[index] = [];
                writeComponents[index] = components[random.Next(components.Length)];
            }
            for (int position = 1; position < nodeCount; position++)
            {
                int target = topological[position];
                int dependencyCount = random.Next(Math.Min(3, position + 1));
                while (dependencies[target].Count < dependencyCount)
                {
                    int source = topological[random.Next(position)];
                    var id = SystemId.FromRegistrationIndex(source);
                    if (!dependencies[target].Contains(id))
                        dependencies[target].Add(id);
                }
            }

            var scheduler = new EcsSchedulerOptions(
                maximumSystems: nodeCount, maximumDependencies: nodeCount * 3,
                maximumWorkItems: nodeCount, maximumStructuralSegments: nodeCount,
                backgroundWorkerCount: 0, structuralCommandsPerProducer: 0,
                structuralPayloadBytesPerProducer: 0, maximumPartitionsPerSystem: 1);
            using EcsWorld world = CreateBareWorld(scheduler, 1, out _);
            for (int index = 0; index < nodeCount; index++)
            {
                world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                    new SystemDefinition($"dag.{seed}.{index}", SystemStage.AI,
                        requires: dependencies[index],
                        additionalWrites: [writeComponents[index]]),
                    new NoOpRangeRuntime());
            }
            world.Seal();
            var snapshots = new SystemRuntimeSnapshot[nodeCount];
            world.CopySystemRuntimeSnapshots(snapshots);
            int[] expected = CompileReferenceWaves(dependencies, writeComponents);
            for (int index = 0; index < nodeCount; index++)
                Assert.Equal(expected[index], snapshots[index].CompiledWave);
        }
    }

    private static int[] CompileReferenceWaves(
        IReadOnlyList<SystemId>[] dependencies,
        ComponentId[] writeComponents)
    {
        int count = dependencies.Length;
        var waves = new int[count];
        var compiled = new bool[count];
        int remaining = count;
        while (remaining != 0)
        {
            bool progressed = false;
            for (int index = 0; index < count; index++)
            {
                if (compiled[index])
                    continue;
                bool ready = true;
                for (int dependency = 0; dependency < dependencies[index].Count; dependency++)
                    ready &= compiled[dependencies[index][dependency].Value];
                if (!ready)
                    continue;
                int wave = 0;
                for (int dependency = 0; dependency < dependencies[index].Count; dependency++)
                    wave = Math.Max(wave, waves[dependencies[index][dependency].Value] + 1);
                for (int prior = 0; prior < count; prior++)
                    if (compiled[prior] && writeComponents[prior] == writeComponents[index])
                        wave = Math.Max(wave, waves[prior] + 1);
                waves[index] = wave;
                compiled[index] = true;
                remaining--;
                progressed = true;
            }
            Assert.True(progressed);
        }
        return waves;
    }

    private static void FailedRegistration_IsTransactional()
    {
        AssertFailedRegistrationDoesNotLeak("read");
        AssertFailedRegistrationDoesNotLeak("write");
        AssertFailedRegistrationDoesNotLeak("structural");
    }

    private static void AssertFailedRegistrationDoesNotLeak(string declaration)
    {
        ComponentSchema schema = LayoutTestSchema.CreateSchema();
        ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        ComponentId invalid = new(60_000);
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 1, maximumDependencies: 0, maximumWorkItems: 1,
            maximumStructuralSegments: 1, backgroundWorkerCount: 0,
            structuralCommandsPerProducer: 1, structuralPayloadBytesPerProducer: sizeof(int),
            maximumPartitionsPerSystem: 1);
        using EcsWorld world = new(schema, new EcsWorldOptions(
            new EntityCapacity(1), 2, 4, maximumPreparedTransitions: 2,
            maximumStructuralCommands: 1, scheduler: scheduler));
        ArchetypeHandle source = world.RegisterArchetype([wide], ChunkLayoutOptions.KiB16, 1);
        ComponentId[] withInteger = [wide, integer];
        Array.Sort(withInteger, static (left, right) => left.CompareTo(right));
        _ = world.RegisterArchetype(withInteger, ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<LayoutInt> lookup = world.PrepareLookup<LayoutInt>();
        var invalidDefinition = new SystemDefinition(
            $"registration.invalid-{declaration}", SystemStage.AI,
            additionalReads: declaration == "read" ? [integer, invalid] : null,
            additionalWrites: declaration == "write" ? [integer, invalid] : null,
            structuralComponents: declaration == "structural" ? [integer, invalid] : null);
        Assert.Throws<ArgumentException>(() =>
            world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                invalidDefinition, new NoOpRangeRuntime()));

        var probe = new LeakedCapabilityProbeRuntime(lookup);
        SystemId retry = world.RegisterRangeSystem<LeakedCapabilityProbeRuntime, LeakedCapabilityProbeJob>(
            new SystemDefinition($"registration.retry-{declaration}", SystemStage.AI), probe);
        Assert.Equal(SystemId.FromRegistrationIndex(0), retry);
        world.Seal();
        world.Initialize();
        Assert.True(world.TryCreate(source, out probe.Entity));
        world.BeginRun();
        world.Tick(0.1);
        Assert.Equal(3, probe.Rejections);
        Assert.False(world.Has(probe.Entity, lookup));
        Assert.Equal(1, world.SystemCount);
    }

    private static void DependencyCapacityFailure_DoesNotPoisonRegistration()
    {
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 3, maximumDependencies: 1, maximumWorkItems: 3,
            maximumStructuralSegments: 3, backgroundWorkerCount: 0,
            structuralCommandsPerProducer: 0, structuralPayloadBytesPerProducer: 0,
            maximumPartitionsPerSystem: 1);
        using EcsWorld world = CreateBareWorld(scheduler, 1, out ArchetypeHandle archetype);
        SystemId root = world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("dependency.root", SystemStage.AI), new NoOpRangeRuntime());
        Assert.Throws<InvalidOperationException>(() =>
            world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("dependency.too-many", SystemStage.AI,
                    requires: [root, root]), new NoOpRangeRuntime()));
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("dependency.zero-retry", SystemStage.AI), new NoOpRangeRuntime());
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("dependency.one-retry", SystemStage.AI,
                requires: [root]), new NoOpRangeRuntime());
        Start(world, archetype, 0);
        world.Tick(0.1);
        Assert.Equal(3, world.SystemCount);
    }

    private static void InvalidSchedulerEnums_AreRejectedCold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SystemDefinition("invalid.stage", (SystemStage)255));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SystemDefinition("invalid.execution", SystemStage.AI,
                executionPolicy: (SystemExecutionPolicy)255));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SystemDefinition("invalid.random-policy", SystemStage.AI,
                parallelRandomAccessPolicy: (ParallelRandomAccessPolicy)255));
        using EcsWorld world = CreateBareWorld(Options(0), 1, out _);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            world.SchedulerRunMode = (SchedulerRunMode)255);
    }

    private static void RangeWorkItemExhaustion_HasBoundedFailureClassification()
    {
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 2, maximumDependencies: 0, maximumWorkItems: 1,
            maximumStructuralSegments: 1, backgroundWorkerCount: 0,
            structuralCommandsPerProducer: 0, structuralPayloadBytesPerProducer: 0,
            maximumPartitionsPerSystem: 1);
        using EcsWorld world = CreateBareWorld(scheduler, 1, out ArchetypeHandle archetype);
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("capacity.first", SystemStage.AI), new NoOpRangeRuntime());
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("capacity.second", SystemStage.AI), new NoOpRangeRuntime());
        Start(world, archetype, 0);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => world.Tick(0.1));
        Assert.Equal(typeof(InvalidOperationException), exception.GetType());
        Assert.True(exception.Message.Contains("work-item capacity", StringComparison.Ordinal));
    }

    private static void RegistrationIdentityLifecycleAndStageOrder_AreExplicit()
    {
        using (EcsWorld duplicateKey = CreateBareWorld(Options(0), 1, out _))
        {
            SystemId first = duplicateKey.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("identity.duplicate", SystemStage.Input), new NoOpRangeRuntime());
            Assert.Throws<ArgumentException>(() =>
                duplicateKey.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                    new SystemDefinition("identity.duplicate", SystemStage.AI), new NoOpRangeRuntime()));
            SystemId retry = duplicateKey.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("identity.clean-retry", SystemStage.AI), new NoOpRangeRuntime());
            Assert.Equal(SystemId.FromRegistrationIndex(0), first);
            Assert.Equal(SystemId.FromRegistrationIndex(1), retry);
            Assert.Equal(2, duplicateKey.SystemCount);
        }

        using (EcsWorld duplicatePersistence = CreateBareWorld(Options(0), 1, out _))
        {
            SystemId first = duplicatePersistence.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("persistence.first", SystemStage.Input,
                    persistenceId: 7001, stateSchemaVersion: 1), new NoOpRangeRuntime());
            Assert.Throws<ArgumentException>(() =>
                duplicatePersistence.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                    new SystemDefinition("persistence.second", SystemStage.AI,
                        persistenceId: 7001, stateSchemaVersion: 2), new NoOpRangeRuntime()));
            SystemId retry = duplicatePersistence.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("persistence.clean-retry", SystemStage.AI,
                    persistenceId: 7002, stateSchemaVersion: 1), new NoOpRangeRuntime());
            Assert.Equal(SystemId.FromRegistrationIndex(0), first);
            Assert.Equal(SystemId.FromRegistrationIndex(1), retry);
            Assert.Equal(2, duplicatePersistence.SystemCount);
        }

        using (EcsWorld sealedWorld = CreateBareWorld(Options(0), 1, out _))
        {
            sealedWorld.Seal();
            Assert.Throws<InvalidOperationException>(() =>
                sealedWorld.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                    new SystemDefinition("registration.after-seal", SystemStage.Input),
                    new NoOpRangeRuntime()));
        }

        var lifecycle = new LifecycleRangeRuntime();
        EcsWorld lifetimeWorld = CreateBareWorld(Options(0), 1, out _);
        lifetimeWorld.RegisterRangeSystem<LifecycleRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("lifecycle.once", SystemStage.Input), lifecycle);
        lifetimeWorld.Seal();
        lifetimeWorld.Initialize();
        Assert.Equal(1, lifecycle.InitializeCount);
        lifetimeWorld.Dispose();
        Assert.Equal(1, lifecycle.ShutdownCount);

        using EcsWorld stageWorld = CreateBareWorld(Options(0), 1, out ArchetypeHandle archetype);
        var order = new BarrierOrder();
        stageWorld.RegisterRangeSystem<BarrierRuntime, BarrierJob>(
            new SystemDefinition("stage.cleanup", SystemStage.Cleanup), new BarrierRuntime(order, 2));
        stageWorld.RegisterRangeSystem<BarrierRuntime, BarrierJob>(
            new SystemDefinition("stage.input", SystemStage.Input), new BarrierRuntime(order, 1));
        Start(stageWorld, archetype, 0);
        stageWorld.Tick(0.1);
        Assert.Equal(12, order.Value);
    }

    private static void StructuralLanes_MergeInLogicalOrderAndApplyAtPostBarrier()
    {
        using EcsWorld world = CreateStructuralWorld(Options(2), manual: false,
            out ArchetypeHandle source, out _, out AddRuntime runtime);
        Start(world, source, 0);
        LifecycleCursor lifecycle = world.CaptureLifecycleCursorFromNow();
        world.Tick(1d / 60d);
        Assert.Equal(64, world.AliveCount);
        Assert.Equal(64, runtime.Completed);
        for (uint index = 0; index < 64; index++)
        {
            Assert.True(world.TryGetEntity(index, out Entity entity));
            Assert.True(world.TryRead(entity, runtime.Lookup, out LayoutInt value));
            Assert.Equal(unchecked((int)index + 100), value.Value);
        }
        SchedulerRuntimeSnapshot snapshot = world.GetSchedulerRuntimeSnapshot();
        Assert.True(snapshot.MergedSegmentCount > 1);
        Assert.Equal(128L, snapshot.MergedCommandCount);
        Assert.True(snapshot.MergedPayloadBytes >= 64 * sizeof(int));
        Span<LifecycleRecord> lifecycleRecords = stackalloc LifecycleRecord[128];
        LifecycleReadResult lifecycleRead = world.ReadLifecycle(ref lifecycle, lifecycleRecords);
        Assert.Equal(128, lifecycleRead.Count);
        for (int index = 0; index < 64; index++)
            Assert.Equal(LifecycleKind.Created, lifecycleRecords[index].Kind);
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        for (int index = 64; index < 128; index++)
        {
            Assert.Equal(LifecycleKind.ComponentAdded, lifecycleRecords[index].Kind);
            Assert.Equal(integer, lifecycleRecords[index].ComponentId);
        }
    }

    private static void Tick_PreBarrierMakesExternalCommandsVisibleToSystems()
    {
        using EcsWorld world = CreateBareWorld(Options(0), 1, out ArchetypeHandle archetype);
        ReadJobPlan<LayoutWide> plan = world.PrepareReadJob<LayoutWide>();
        var runtime = new ReadOnlyRuntime();
        world.RegisterReadSystem<LayoutWide, ReadOnlyRuntime, ReadOnlyJob>(
            new SystemDefinition("prebarrier.read", SystemStage.Input), plan, runtime);
        Start(world, archetype, 0);
        world.StructuralCommands.EnqueueCreateDefault(archetype);
        world.Tick(0.1);
        Assert.Equal(1, runtime.Rows);
        Assert.Equal(1, world.AliveCount);
        Assert.Equal(0, world.StructuralCommands.Count);
    }

    private static void Lifecycle_PreBarrierAndPostBarrierFollowCommitOrder()
    {
        using EcsWorld world = CreateStructuralWorld(Options(2), manual: false,
            out ArchetypeHandle source, out _, out _);
        Start(world, source, 0);
        LifecycleCursor cursor = world.CaptureLifecycleCursorFromNow();
        world.StructuralCommands.EnqueueCreateDefault(source);
        Assert.Equal(
            LifecycleReadStatus.NoData,
            world.ReadLifecycle(ref cursor, Span<LifecycleRecord>.Empty).Status);

        world.Tick(1d / 60d);
        var records = new LifecycleRecord[129];
        LifecycleReadResult read = world.ReadLifecycle(ref cursor, records);
        Assert.Equal(LifecycleReadStatus.Success, read.Status);
        Assert.Equal(129, read.Count);
        Assert.Equal(LifecycleKind.Created, records[0].Kind);
        for (int index = 1; index <= 64; index++)
            Assert.Equal(LifecycleKind.Created, records[index].Kind);
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        for (int index = 65; index < records.Length; index++)
        {
            Assert.Equal(LifecycleKind.ComponentAdded, records[index].Kind);
            Assert.Equal(integer, records[index].ComponentId);
        }
    }

    private static void TypedStructuralRoutes_HandleAddReplaceRemoveTagsAndPayloadExhaustion()
    {
        ComponentSchema schema = LayoutTestSchema.CreateSchema();
        ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        ComponentId tag = LayoutTestSchema.GetComponentId<LayoutTag>();
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 1, maximumDependencies: 0, maximumWorkItems: 4,
            maximumStructuralSegments: 4, backgroundWorkerCount: 0,
            structuralCommandsPerProducer: 8, structuralPayloadBytesPerProducer: 64,
            maximumPartitionsPerSystem: 1);
        using EcsWorld world = new(schema, new EcsWorldOptions(
            new EntityCapacity(8), 3, 4, maximumPreparedTransitions: 4,
            maximumStructuralCommands: 8, scheduler,
            lifecycleRecordCapacity: 64));
        ArchetypeHandle source = world.RegisterArchetype([wide], ChunkLayoutOptions.KiB16, 1);
        ComponentId[] withIntegerSignature = [wide, integer];
        Array.Sort(withIntegerSignature, static (a, b) => a.CompareTo(b));
        ArchetypeHandle withInteger = world.RegisterArchetype(withIntegerSignature, ChunkLayoutOptions.KiB16, 1);
        ComponentId[] withTagSignature = [wide, tag];
        Array.Sort(withTagSignature, static (a, b) => a.CompareTo(b));
        ArchetypeHandle withTag = world.RegisterArchetype(withTagSignature, ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<LayoutInt> integers = world.PrepareLookup<LayoutInt>();
        ComponentLookup<LayoutTag> tags = world.PrepareLookup<LayoutTag>();
        var runtime = new TypedStructuralRuntime();
        world.RegisterRangeSystem<TypedStructuralRuntime, TypedStructuralJob>(
            new SystemDefinition("typed.routes", SystemStage.WorldSim,
                structuralComponents: [integer, tag]), runtime);
        world.Seal();
        world.Initialize();
        Assert.True(world.TryCreate(source, out runtime.AddInteger));
        Assert.True(world.TryCreate(withInteger, out runtime.ReplaceInteger));
        Assert.True(world.TryCreate(withInteger, out runtime.RemoveInteger));
        Assert.True(world.TryCreate(source, out runtime.AddTag));
        Assert.True(world.TryCreate(withTag, out runtime.RemoveTag));
        Assert.True(world.TryWrite(runtime.ReplaceInteger, integers, new LayoutInt { Value = 7 }));
        LifecycleCursor lifecycle = world.CaptureLifecycleCursorFromNow();
        world.BeginRun();
        world.Tick(0.1);

        Assert.True(world.TryRead(runtime.AddInteger, integers, out LayoutInt added));
        Assert.Equal(42, added.Value);
        Assert.True(world.TryRead(runtime.ReplaceInteger, integers, out LayoutInt replaced));
        Assert.Equal(99, replaced.Value);
        Assert.False(world.Has(runtime.RemoveInteger, integers));
        Assert.True(world.Has(runtime.AddTag, tags));
        Assert.False(world.Has(runtime.RemoveTag, tags));
        Span<LifecycleRecord> lifecycleRecords = stackalloc LifecycleRecord[8];
        LifecycleReadResult lifecycleRead = world.ReadLifecycle(ref lifecycle, lifecycleRecords);
        Assert.Equal(4, lifecycleRead.Count);
        Assert.Equal(LifecycleKind.ComponentRemoved, lifecycleRecords[0].Kind);
        Assert.Equal(integer, lifecycleRecords[0].ComponentId);
        Assert.Equal(LifecycleKind.ComponentRemoved, lifecycleRecords[1].Kind);
        Assert.Equal(tag, lifecycleRecords[1].ComponentId);
        Assert.Equal(LifecycleKind.ComponentAdded, lifecycleRecords[2].Kind);
        Assert.Equal(integer, lifecycleRecords[2].ComponentId);
        Assert.Equal(LifecycleKind.ComponentAdded, lifecycleRecords[3].Kind);
        Assert.Equal(tag, lifecycleRecords[3].ComponentId);

        // A second tick opens a new canonical command-buffer epoch. Typed Add
        // payload must be installed after that epoch opens; otherwise the first
        // append clears the just-merged payload count and playback faults.
        world.Tick(0.1);
        Assert.True(world.TryRead(runtime.AddInteger, integers, out LayoutInt secondTickAdded));
        Assert.Equal(42, secondTickAdded.Value);
        Assert.True(world.TryRead(runtime.ReplaceInteger, integers, out LayoutInt secondTickReplaced));
        Assert.Equal(99, secondTickReplaced.Value);

        var tinyScheduler = new EcsSchedulerOptions(
            maximumSystems: 1, maximumDependencies: 0, maximumWorkItems: 1,
            maximumStructuralSegments: 1, backgroundWorkerCount: 0,
            structuralCommandsPerProducer: 1, structuralPayloadBytesPerProducer: 3,
            maximumPartitionsPerSystem: 1);
        using EcsWorld tiny = new(schema, new EcsWorldOptions(
            new EntityCapacity(1), 1, 2, maximumStructuralCommands: 1,
            scheduler: tinyScheduler));
        ArchetypeHandle tinyArchetype = tiny.RegisterArchetype([integer], ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<LayoutInt> tinyLookup = tiny.PrepareLookup<LayoutInt>();
        var exhaustion = new PayloadExhaustionRuntime();
        tiny.RegisterRangeSystem<PayloadExhaustionRuntime, PayloadExhaustionJob>(
            new SystemDefinition("typed.exhaustion", SystemStage.WorldSim,
                structuralComponents: [integer]), exhaustion);
        tiny.Seal(); tiny.Initialize();
        Assert.True(tiny.TryCreate(tinyArchetype, out exhaustion.Entity));
        Assert.True(tiny.TryWrite(exhaustion.Entity, tinyLookup, new LayoutInt { Value = 5 }));
        tiny.BeginRun(); tiny.Tick(0.1);
        Assert.False(exhaustion.Accepted);
        Assert.True(tiny.TryRead(exhaustion.Entity, tinyLookup, out LayoutInt unchanged));
        Assert.Equal(5, unchanged.Value);
    }

    private static void StructuralRunKeys_DiscriminateTypedRoutesFromPublicTransitions()
    {
        int[] boundaries = [7, 8, 9, 255, 256, 257];
        for (int index = 0; index < boundaries.Length; index++)
        {
            RunStructuralRouteCollisionCase(boundaries[index], removePhase: false);
            RunStructuralRouteCollisionCase(boundaries[index], removePhase: true);
        }
    }

    private static void RunStructuralRouteCollisionCase(int count, bool removePhase)
    {
        RouteCollisionOutcome production = ExecuteStructuralRouteCollisionCase(
            count, removePhase, scalarOracle: false);
        RouteCollisionOutcome scalar = ExecuteStructuralRouteCollisionCase(
            count, removePhase, scalarOracle: true);
        Assert.Equal(production.CommandCount, scalar.CommandCount);
        Assert.Equal(production.AddedCount, scalar.AddedCount);
        Assert.Equal(production.RemovedCount, scalar.RemovedCount);
        Assert.Equal(production.RejectedCount, scalar.RejectedCount);
        Assert.Equal(production.CanonicalHash, scalar.CanonicalHash);
    }

    private static RouteCollisionOutcome ExecuteStructuralRouteCollisionCase(
        int count,
        bool removePhase,
        bool scalarOracle)
    {
        ComponentSchema schema = LayoutTestSchema.CreateSchema();
        ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        ComponentId tag = LayoutTestSchema.GetComponentId<LayoutTag>();
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 1, maximumDependencies: 0, maximumWorkItems: 1,
            maximumStructuralSegments: 1, backgroundWorkerCount: 0,
            structuralCommandsPerProducer: count, structuralPayloadBytesPerProducer: sizeof(int),
            maximumPartitionsPerSystem: 1);
        using EcsWorld world = new(schema, new EcsWorldOptions(
            new EntityCapacity(count), 3, 8, maximumPreparedTransitions: 8,
            maximumStructuralCommands: count, scheduler: scheduler));

        ComponentId[] sourceSignature = removePhase ? [wide, integer, tag] : [wide];
        ComponentId[] transitionSignature = removePhase ? [wide, tag] : [wide, tag];
        ComponentId[] typedSignature = removePhase ? [wide, integer] : [wide, integer];
        Array.Sort(sourceSignature, static (a, b) => a.CompareTo(b));
        Array.Sort(transitionSignature, static (a, b) => a.CompareTo(b));
        Array.Sort(typedSignature, static (a, b) => a.CompareTo(b));
        ArchetypeHandle source = world.RegisterArchetype(
            sourceSignature, ChunkLayoutOptions.KiB16, count);
        ArchetypeHandle transitionDestination = world.RegisterArchetype(
            transitionSignature, ChunkLayoutOptions.KiB16, count);
        _ = world.RegisterArchetype(typedSignature, ChunkLayoutOptions.KiB16, count);
        ArchetypeTransition transition = world.PrepareTransition(source, transitionDestination);
        ComponentLookup<LayoutInt> integers = world.PrepareLookup<LayoutInt>();
        ComponentLookup<LayoutTag> tags = world.PrepareLookup<LayoutTag>();
        var runtime = new RouteCollisionRuntime(count, transition, removePhase);
        SystemId systemId = world.RegisterRangeSystem<RouteCollisionRuntime, RouteCollisionJob>(
            new SystemDefinition($"run-key.{removePhase}.{count}", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Serial,
                manual: true,
                structuralComponents: [removePhase ? tag : integer]), runtime);
        world.Seal();
        world.Initialize();
        for (int index = 0; index < count; index++)
            Assert.True(world.TryCreate(source, out runtime.Entities[index]));
        world.BeginRun();
        world.RunManual(systemId, 0.1);
        StructuralPlaybackResult playback = scalarOracle
            ? world.PlaybackStructuralCommandsScalarOracle()
            : world.PlaybackStructuralCommands();

        ulong canonicalHash = 1469598103934665603UL;
        for (int index = 0; index < count - 1; index++)
        {
            Assert.False(world.Has(runtime.Entities[index], integers));
            Assert.True(world.Has(runtime.Entities[index], tags));
            canonicalHash = unchecked((canonicalHash ^ runtime.Entities[index].Index) * 1099511628211UL);
            canonicalHash = unchecked((canonicalHash ^ runtime.Entities[index].Generation) * 1099511628211UL);
            canonicalHash = unchecked((canonicalHash ^ 2UL) * 1099511628211UL);
        }
        Entity typed = runtime.Entities[count - 1];
        Assert.True(world.Has(typed, integers));
        Assert.False(world.Has(typed, tags));
        Assert.True(world.TryRead(typed, integers, out LayoutInt typedValue));
        Assert.Equal(removePhase ? 0 : 73, typedValue.Value);
        canonicalHash = unchecked((canonicalHash ^ typed.Index) * 1099511628211UL);
        canonicalHash = unchecked((canonicalHash ^ typed.Generation) * 1099511628211UL);
        canonicalHash = unchecked((canonicalHash ^ unchecked((uint)typedValue.Value)) * 1099511628211UL);
        return new RouteCollisionOutcome(
            playback.CommandCount,
            playback.AddedCount,
            playback.RemovedCount,
            playback.RejectedCount,
            canonicalHash);
    }

    private static void ConflictsAndDependencies_CompileHardWaveBarriers()
    {
        using EcsWorld world = CreateBareWorld(Options(2), 1, out ArchetypeHandle archetype);
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        var order = new BarrierOrder();
        SystemId first = world.RegisterRangeSystem<BarrierRuntime, BarrierJob>(
            new SystemDefinition("barrier.first", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Parallel,
                additionalWrites: [integer], parallelRowThreshold: 1,
                minimumRowsPerPartition: 1), new BarrierRuntime(order, 1));
        SystemId second = world.RegisterRangeSystem<BarrierRuntime, BarrierJob>(
            new SystemDefinition("barrier.conflict", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Parallel,
                requires: [first], additionalWrites: [integer], parallelRowThreshold: 1,
                minimumRowsPerPartition: 1), new BarrierRuntime(order, 2));
        world.RegisterRangeSystem<BarrierRuntime, BarrierJob>(
            new SystemDefinition("barrier.requires", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Parallel,
                requires: [second], parallelRowThreshold: 1,
                minimumRowsPerPartition: 1), new BarrierRuntime(order, 3));
        Start(world, archetype, 0);
        world.Tick(0.1);
        Assert.Equal(123, order.Value);
        var snapshots = new SystemRuntimeSnapshot[3];
        world.CopySystemRuntimeSnapshots(snapshots);
        Assert.Equal(0, snapshots[0].CompiledWave);
        Assert.True(snapshots[1].CompiledWave > snapshots[0].CompiledWave);
        Assert.True(snapshots[2].CompiledWave > snapshots[1].CompiledWave);
    }

    private static void StructuralCapabilities_DoNotCreateFalseGraphHazards()
    {
        using EcsWorld world = CreateBareWorld(Options(0), 1, out ArchetypeHandle archetype);
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("structural.no-hazard.first", SystemStage.AI,
                structuralComponents: [integer]), new NoOpRangeRuntime());
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("structural.no-hazard.second", SystemStage.AI,
                structuralComponents: [integer]), new NoOpRangeRuntime());
        Start(world, archetype, 0);
        world.Tick(0.1);
        var snapshots = new SystemRuntimeSnapshot[2];
        Assert.Equal(2, world.CopySystemRuntimeSnapshots(snapshots));
        Assert.Equal(0, snapshots[0].CompiledWave);
        Assert.Equal(0, snapshots[1].CompiledWave);
    }

    private static void TagAdd_ConsumesZeroPayloadCapacity()
    {
        ComponentSchema schema = LayoutTestSchema.CreateSchema();
        ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
        ComponentId tag = LayoutTestSchema.GetComponentId<LayoutTag>();
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 1, maximumDependencies: 0, maximumWorkItems: 1,
            maximumStructuralSegments: 1, backgroundWorkerCount: 0,
            structuralCommandsPerProducer: 1, structuralPayloadBytesPerProducer: 0,
            maximumPartitionsPerSystem: 1);
        using EcsWorld world = new(schema, new EcsWorldOptions(
            new EntityCapacity(1), 2, 2, maximumPreparedTransitions: 2,
            maximumStructuralCommands: 1, scheduler: scheduler));
        ArchetypeHandle source = world.RegisterArchetype([wide], ChunkLayoutOptions.KiB16, 1);
        ComponentId[] taggedSignature = [wide, tag];
        Array.Sort(taggedSignature, static (left, right) => left.CompareTo(right));
        _ = world.RegisterArchetype(taggedSignature, ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<LayoutTag> tags = world.PrepareLookup<LayoutTag>();
        var runtime = new TagOnlyRuntime();
        world.RegisterRangeSystem<TagOnlyRuntime, TagOnlyJob>(
            new SystemDefinition("typed.zero-payload-tag", SystemStage.WorldSim,
                structuralComponents: [tag]), runtime);
        world.Seal();
        world.Initialize();
        Assert.True(world.TryCreate(source, out runtime.Entity));
        world.BeginRun();
        world.Tick(0.1);
        Assert.True(runtime.Accepted);
        Assert.True(world.Has(runtime.Entity, tags));
        Assert.Equal(0L, world.GetSchedulerRuntimeSnapshot().MergedPayloadBytes);
    }

    private static void Cadence_DoesNotMakeDependenciesSameTickPrerequisites()
    {
        using EcsWorld world = CreateBareWorld(Options(0), 1, out ArchetypeHandle archetype);
        SystemId periodic = world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("cadence.periodic", SystemStage.AI,
                new SystemSchedule(enabled: true, periodTicks: 2, startOffset: 0)),
            new NoOpRangeRuntime());
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("cadence.dependent", SystemStage.AI,
                requires: [periodic]),
            new NoOpRangeRuntime());
        Start(world, archetype, 0);

        world.Tick(0.1);
        world.Tick(0.1);
        world.SetSystemEnabled(periodic, false);
        world.Tick(0.1);

        var snapshots = new SystemRuntimeSnapshot[2];
        Assert.Equal(2, world.CopySystemRuntimeSnapshots(snapshots));
        Assert.Equal(1UL, snapshots[0].RunCount);
        Assert.Equal(3UL, snapshots[1].RunCount);
        Assert.True(snapshots[1].CompiledWave > snapshots[0].CompiledWave);
    }

    private static void ParallelTrackedWrites_PublishOneVersionAndExactRows()
    {
        const int count = 4_096;
        using EcsWorld world = CreateBareWorld(Options(3), count, out ArchetypeHandle archetype);
        WriteJobPlan<LayoutInt> write = world.PrepareWriteJob<LayoutInt>();
        ChangedRowsJobPlan<LayoutInt, LayoutInt> changed = world.PrepareChangedRowsJob<LayoutInt, LayoutInt>();
        var runtime = new SparseTrackedRuntime(write: true, divisor: 17);
        world.RegisterTrackedWriteSystem<LayoutInt, SparseTrackedRuntime, SparseTrackedJob>(
            new SystemDefinition("tracked.parallel", SystemStage.Movement,
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1, minimumRowsPerPartition: 64), write, runtime);
        Start(world, archetype, count);
        ChangeCursor cursor = world.CreateChangeCursorFromNow();
        ChangeVersion before = world.CurrentChangeVersion;
        world.Tick(0.1);
        Assert.Equal(before.Next(), world.CurrentChangeVersion);
        var audit = new ChangedAuditJob();
        int rows = world.ExecuteChangedRowsJob(changed, ref cursor, ref audit);
        int expected = (count + 16) / 17;
        Assert.Equal(expected, rows);
        Assert.Equal(expected, audit.Rows);
    }

    private static void TrackedNoWrite_DoesNotAdvancePublication()
    {
        using EcsWorld world = CreateBareWorld(Options(2), 512, out ArchetypeHandle archetype);
        WriteJobPlan<LayoutInt> write = world.PrepareWriteJob<LayoutInt>();
        var runtime = new SparseTrackedRuntime(write: false, divisor: 1);
        world.RegisterTrackedWriteSystem<LayoutInt, SparseTrackedRuntime, SparseTrackedJob>(
            new SystemDefinition("tracked.no-write", SystemStage.Movement,
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1, minimumRowsPerPartition: 32), write, runtime);
        Start(world, archetype, 512);
        ChangeVersion before = world.CurrentChangeVersion;
        world.Tick(0.1);
        Assert.Equal(before, world.CurrentChangeVersion);
        Assert.Equal(0, runtime.Writes);
    }

    private static void ScheduledChangedRows_AcknowledgeOnlySuccessfulExecution()
    {
        const int count = 257;
        using EcsWorld world = CreateBareWorld(Options(2), count, out ArchetypeHandle archetype);
        ChangedRowsJobPlan<LayoutInt, LayoutInt> changed = world.PrepareChangedRowsJob<LayoutInt, LayoutInt>();
        ComponentLookup<LayoutInt> lookup = world.PrepareLookup<LayoutInt>();
        var runtime = new ChangedRowsRuntime();
        world.RegisterChangedRowsSystem<LayoutInt, LayoutInt, ChangedRowsRuntime, ChangedRowsScheduledJob>(
            new SystemDefinition("changed.serial", SystemStage.Networking,
                executionPolicy: SystemExecutionPolicy.Parallel), changed, runtime);
        Start(world, archetype, count);
        runtime.Arm(world.CreateChangeCursorFromNow());
        for (uint index = 0; index < count; index += 5)
        {
            Assert.True(world.TryGetEntity(index, out Entity entity));
            var value = new LayoutInt { Value = unchecked((int)index) };
            Assert.True(world.TryWrite(entity, lookup, value));
        }
        world.Tick(0.1);
        Assert.Equal((count + 4) / 5, runtime.Rows);
        var snapshots = new SystemRuntimeSnapshot[1];
        world.CopySystemRuntimeSnapshots(snapshots);
        Assert.False(snapshots[0].LastExecutionWasParallel);
        Assert.Equal((count + 4) / 5, snapshots[0].LastRows);
        int delivered = runtime.Rows;
        world.Tick(0.1);
        Assert.Equal(delivered, runtime.Rows);
        world.CopySystemRuntimeSnapshots(snapshots);
        Assert.Equal(0, snapshots[0].LastRows);
    }

    private static void TrackedReadWrite_ProvidesDirectAndScheduledParity()
    {
        const int count = 513;
        using EcsWorld world = CreateWorld(Options(2), count, out ArchetypeHandle archetype,
            out ReadWriteJobPlan<LayoutWide, LayoutInt> plan, out ComponentLookup<LayoutInt> lookup);
        ChangedRowsJobPlan<LayoutInt, LayoutInt> changed =
            world.PrepareChangedRowsJob<LayoutInt, LayoutInt>();
        var runtime = new TrackedReadWriteRuntime(writeOdd: true);
        world.RegisterTrackedReadWriteSystem<LayoutWide, LayoutInt,
            TrackedReadWriteRuntime, ScheduledTrackedReadWriteJob>(
            new SystemDefinition("tracked.1r1w", SystemStage.Movement,
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1, minimumRowsPerPartition: 32), plan, runtime);
        Start(world, archetype, count);
        ChangeCursor cursor = world.CreateChangeCursorFromNow();
        ChangeVersion before = world.CurrentChangeVersion;
        var direct = new DirectTrackedReadWriteJob(writeEven: true);
        Assert.Equal(count, world.ExecuteTrackedJob(plan, ref direct));
        Assert.Equal((count + 1) / 2, direct.Writes);
        Assert.Equal(before.Next(), world.CurrentChangeVersion);
        var audit = new ChangedAuditJob();
        Assert.Equal(direct.Writes, world.ExecuteChangedRowsJob(changed, ref cursor, ref audit));

        before = world.CurrentChangeVersion;
        world.Tick(0.1);
        Assert.Equal(before.Next(), world.CurrentChangeVersion);
        audit = default;
        Assert.Equal(count / 2, world.ExecuteChangedRowsJob(changed, ref cursor, ref audit));
        Assert.Equal(count / 2, runtime.Writes);
        for (uint index = 0; index < count; index++)
        {
            Assert.True(world.TryGetEntity(index, out Entity entity));
            Assert.True(world.TryRead(entity, lookup, out LayoutInt value));
            Assert.Equal(index % 2 == 0 ? 1 : 2, value.Value);
        }
    }

    private static void ManualSystems_LeaveStructuralOutputForNextExternalEpoch()
    {
        using EcsWorld world = CreateStructuralWorld(Options(0), manual: true,
            out ArchetypeHandle source, out _, out AddRuntime runtime);
        Start(world, source, 0);
        LifecycleCursor lifecycle = world.CaptureLifecycleCursorFromNow();
        world.RunManual(runtime.Id, 0.25);
        Assert.Equal(0, world.AliveCount);
        Assert.Equal(128, world.StructuralCommands.Count);
        Span<LifecycleRecord> lifecycleRecords = stackalloc LifecycleRecord[128];
        Assert.Equal(
            LifecycleReadStatus.NoData,
            world.ReadLifecycle(ref lifecycle, lifecycleRecords).Status);
        world.Tick(0.25);
        Assert.Equal(64, world.AliveCount);
        Assert.Equal(1UL, world.TickCount);
        Assert.Equal(128, world.ReadLifecycle(ref lifecycle, lifecycleRecords).Count);
    }

    private static void PhaseStaggering_IsOptInDeterministicAndStageBounded()
    {
        EcsSchedulerOptions scheduler = new(
            maximumSystems: 8, maximumDependencies: 8, maximumWorkItems: 16,
            maximumStructuralSegments: 16, backgroundWorkerCount: 0,
            phaseStaggeringEnabled: true, maximumStaggerPhases: 10);
        using EcsWorld world = CreateBareWorld(scheduler, 1, out ArchetypeHandle archetype);
        var staggered = new NoOpRangeRuntime();
        var excluded = new NoOpRangeRuntime();
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("phase.world", SystemStage.WorldSim,
                new SystemSchedule(true, 7, 0), automaticallyStagger: true), staggered);
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("phase.movement", SystemStage.Movement,
                new SystemSchedule(true, 7, 0), automaticallyStagger: true), excluded);
        Start(world, archetype, 0);
        var snapshots = new SystemRuntimeSnapshot[2];
        world.CopySystemRuntimeSnapshots(snapshots);
        Assert.True(snapshots[0].StartOffset is >= 0 and < 7);
        Assert.Equal(0, snapshots[1].StartOffset);
        for (int tick = 0; tick < 21; tick++) world.Tick(0.1);
        world.CopySystemRuntimeSnapshots(snapshots);
        Assert.Equal(3UL, snapshots[0].RunCount);
        Assert.Equal(3UL, snapshots[1].RunCount);
    }

    private static void WorkerCapabilities_ExpireAndCrossSegmentDeferredTargetsFault()
    {
        using (EcsWorld expiry = CreateBareWorld(Options(0), 1, out ArchetypeHandle archetype))
        {
            var capture = new CaptureContextRuntime();
            expiry.RegisterRangeSystem<CaptureContextRuntime, CaptureContextJob>(
                new SystemDefinition("context.capture", SystemStage.Input), capture);
            Start(expiry, archetype, 0);
            expiry.Tick(0.1);
            SystemJobContext stale = capture.Context;
            Assert.Throws<InvalidOperationException>(() => stale.TryGetEntity(0, out _));
        }

        using (EcsWorld crossing = CreateBareWorld(Options(0), 2, out ArchetypeHandle archetype))
        {
            var holder = new DeferredHolder();
            SystemId create = crossing.RegisterRangeSystem<CreateTokenRuntime, CreateTokenJob>(
                new SystemDefinition("token.create", SystemStage.Input),
                new CreateTokenRuntime(holder, archetype));
            crossing.RegisterRangeSystem<ConsumeTokenRuntime, ConsumeTokenJob>(
                new SystemDefinition("token.consume", SystemStage.Input, requires: [create]),
                new ConsumeTokenRuntime(holder));
            Start(crossing, archetype, 0);
            Assert.Throws<SystemExecutionException>(() => crossing.Tick(0.1));
            Assert.True(crossing.IsFaulted);
        }
    }

    private static void WorkerFailure_FaultsWorldAndSkipsStructuralPublication()
    {
        using EcsWorld world = CreateBareWorld(Options(2), 8, out ArchetypeHandle archetype);
        world.RegisterRangeSystem<ThrowingRangeRuntime, ThrowingRangeJob>(
            new SystemDefinition("test.throw", SystemStage.Input,
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1, minimumRowsPerPartition: 1),
            new ThrowingRangeRuntime(archetype));
        Start(world, archetype, 0);
        Assert.Throws<SystemExecutionException>(() => world.Tick(0.1));
        Assert.True(world.IsFaulted);
        Assert.Equal(0, world.AliveCount);
        Assert.Equal(0UL, world.TickCount);
    }

    private static void WorkerDisposal_JoinsEveryBackgroundThread()
    {
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 1, maximumDependencies: 0, maximumWorkItems: 4,
            maximumStructuralSegments: 4, backgroundWorkerCount: 2,
            callerParticipates: false, structuralCommandsPerProducer: 0,
            structuralPayloadBytesPerProducer: 0, maximumPartitionsPerSystem: 2);
        EcsWorld world = CreateBareWorld(scheduler, 1, out ArchetypeHandle archetype);
        var runtime = new ThreadArrayRuntime(2);
        world.RegisterRangeSystem<ThreadArrayRuntime, ThreadArrayJob>(
            new SystemDefinition("dispose.workers", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1, minimumRowsPerPartition: 1), runtime);
        Start(world, archetype, 0);
        world.Tick(0.1);
        world.Dispose();
        for (int index = 0; index < runtime.Threads.Length; index++)
        {
            Assert.True(runtime.Threads[index] is not null);
            Assert.False(runtime.Threads[index]!.IsAlive);
        }
    }

    private static void PartialWorkerStartFailure_IsCleanedUp()
    {
        EcsWorld world = CreateBareWorld(Options(3), 1, out _);
        world.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
            new SystemDefinition("workers.partial-start", SystemStage.AI), new NoOpRangeRuntime());
        SystemScheduler.WorkerStartHookForTesting = static index =>
        {
            if (index == 1)
                throw new InvalidOperationException("injected worker-start failure");
        };
        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(world.Seal);
            Assert.True(exception.Message.Contains("injected worker-start failure", StringComparison.Ordinal));
        }
        finally
        {
            SystemScheduler.WorkerStartHookForTesting = null;
            world.Dispose();
        }
    }

    private static void WarmedSerialTick_AllocatesZeroManagedBytes()
    {
        using EcsWorld world = CreateCanonicalWorld(Options(0), SchedulerRunMode.Serial, out _);
        for (int index = 0; index < 128; index++)
            world.Tick(1d / 60d);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 2_000; index++)
            world.Tick(1d / 60d);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static EcsWorld CreateCanonicalWorld(EcsSchedulerOptions options, SchedulerRunMode mode,
        out IncrementRuntime runtime)
    {
        EcsWorld world = CreateWorld(options, 4_096, out ArchetypeHandle archetype,
            out ReadWriteJobPlan<LayoutWide, LayoutInt> plan, out _);
        runtime = new IncrementRuntime();
        world.RegisterReadWriteSystem<LayoutWide, LayoutInt, IncrementRuntime, IncrementJob>(
            new SystemDefinition("canonical.increment", SystemStage.Movement,
                executionPolicy: SystemExecutionPolicy.Auto,
                parallelRowThreshold: 1, minimumRowsPerPartition: 128), plan, runtime);
        Start(world, archetype, 4_096);
        world.SchedulerRunMode = mode;
        return world;
    }

    private static EcsWorld CreateWorld(EcsSchedulerOptions scheduler, int entityCapacity,
        out ArchetypeHandle archetype, out ReadWriteJobPlan<LayoutWide, LayoutInt> plan,
        out ComponentLookup<LayoutInt> integers)
    {
        EcsWorld world = CreateBareWorld(scheduler, entityCapacity, out archetype);
        plan = world.PrepareReadWriteJob<LayoutWide, LayoutInt>();
        integers = world.PrepareLookup<LayoutInt>();
        world.Remember(integers);
        return world;
    }

    private static EcsWorld CreateBareWorld(EcsSchedulerOptions scheduler, int entityCapacity,
        out ArchetypeHandle archetype)
    {
        ComponentSchema schema = LayoutTestSchema.CreateSchema();
        ComponentId[] signature =
        [
            LayoutTestSchema.GetComponentId<LayoutWide>(),
            LayoutTestSchema.GetComponentId<LayoutInt>(),
            LayoutTestSchema.GetComponentId<LayoutEnableLong>(),
        ];
        Array.Sort(signature, static (a, b) => a.CompareTo(b));
        var world = new EcsWorld(schema,
            new EcsWorldOptions(new EntityCapacity(Math.Max(1, entityCapacity)), 4, 16,
                maximumPreparedTransitions: 8, maximumStructuralCommands: 4_096, scheduler));
        archetype = world.RegisterArchetype(signature, ChunkLayoutOptions.KiB16,
            Math.Max(1, entityCapacity / 64 + 4));
        return world;
    }

    private static EcsWorld CreateStructuralWorld(EcsSchedulerOptions scheduler, bool manual,
        out ArchetypeHandle source, out ArchetypeHandle destination, out AddRuntime runtime)
    {
        ComponentSchema schema = LayoutTestSchema.CreateSchema();
        ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        var world = new EcsWorld(schema,
            new EcsWorldOptions(
                new EntityCapacity(128),
                2,
                8,
                8,
                4_096,
                scheduler,
                lifecycleRecordCapacity: 512));
        source = world.RegisterArchetype([wide], ChunkLayoutOptions.KiB16, 2);
        ComponentId[] signature = [wide, integer];
        Array.Sort(signature, static (a, b) => a.CompareTo(b));
        destination = world.RegisterArchetype(signature, ChunkLayoutOptions.KiB16, 2);
        ComponentLookup<LayoutInt> lookup = world.PrepareLookup<LayoutInt>();
        runtime = new AddRuntime(source, lookup, 64);
        SystemId id = world.RegisterRangeSystem<AddRuntime, AddJob>(
            new SystemDefinition("test.structural", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Parallel, manual: manual,
                structuralComponents: [integer], parallelRowThreshold: 1,
                minimumRowsPerPartition: 1), runtime);
        runtime.Id = id;
        return world;
    }

    private static void Start(EcsWorld world, ArchetypeHandle archetype, int entities)
    {
        world.Seal();
        world.Initialize();
        for (int index = 0; index < entities; index++)
            Assert.True(world.TryCreate(archetype, out _));
        world.BeginRun();
    }

    private static EcsSchedulerOptions Options(int backgroundWorkers) => new(
        maximumSystems: 16,
        maximumDependencies: 32,
        maximumWorkItems: 128,
        maximumStructuralSegments: 256,
        backgroundWorkerCount: backgroundWorkers,
        callerParticipates: true,
        structuralCommandsPerProducer: 512,
        structuralPayloadBytesPerProducer: 8 * 1024,
        maximumPartitionsPerSystem: 32,
        profilingEnabled: true);

    private static ulong HashIntegers(EcsWorld world, int count)
    {
        ComponentLookup<LayoutInt> lookup = world.PrepareLookupAfterSealForTest();
        ulong hash = 1469598103934665603UL;
        for (uint index = 0; index < count; index++)
        {
            Assert.True(world.TryGetEntity(index, out Entity entity));
            Assert.True(world.TryRead(entity, lookup, out LayoutInt value));
            hash = unchecked((hash ^ unchecked((uint)value.Value)) * 1099511628211UL);
        }
        return hash;
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }

    private sealed class IncrementRuntime : ISystemRuntime<IncrementJob>
    {
        internal int InitializeCount;
        internal int CompletedRows;
        public void Initialize(in SystemInitializationContext context) => InitializeCount++;
        public IncrementJob Prepare(in SystemPrepareContext context) => default;
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<IncrementJob> results)
        {
            for (int i = 0; i < results.Length; i++) CompletedRows += results[i].Rows;
        }
    }

    private struct IncrementJob : IScheduledReadWriteChunkJob<LayoutWide, LayoutInt>
    {
        internal int Rows;
        public void Execute(in SystemJobContext context, in ReadWriteComponentChunk<LayoutWide, LayoutInt> chunk)
        {
            Span<LayoutInt> values = chunk.WriteComponents;
            for (int row = 0; row < chunk.Count; row++) values[row].Value++;
            Rows += chunk.Count;
        }
    }

    private sealed class UndeclaredLookupRuntime : ISystemRuntime<UndeclaredLookupJob>
    {
        private readonly ComponentLookup<LayoutInt> _lookup;
        internal Entity Entity;
        internal int ReadRejections;
        internal int WriteRejections;
        internal UndeclaredLookupRuntime(ComponentLookup<LayoutInt> lookup) => _lookup = lookup;
        public UndeclaredLookupJob Prepare(in SystemPrepareContext context) => new(Entity, _lookup);
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<UndeclaredLookupJob> jobs)
        {
            for (int index = 0; index < jobs.Length; index++)
            {
                ReadRejections += jobs[index].ReadRejections;
                WriteRejections += jobs[index].WriteRejections;
            }
        }
    }

    private struct UndeclaredLookupJob : IScheduledReadWriteChunkJob<LayoutWide, LayoutInt>
    {
        private readonly Entity _entity;
        private readonly ComponentLookup<LayoutInt> _lookup;
        internal int ReadRejections;
        internal int WriteRejections;
        internal UndeclaredLookupJob(Entity entity, ComponentLookup<LayoutInt> lookup) =>
            (_entity, _lookup, ReadRejections, WriteRejections) = (entity, lookup, 0, 0);
        public void Execute(in SystemJobContext context,
            in ReadWriteComponentChunk<LayoutWide, LayoutInt> chunk)
        {
            try { _ = context.TryRead(_entity, _lookup, out LayoutInt _); }
            catch (InvalidOperationException) { ReadRejections++; }
            try { _ = context.TryWrite(_entity, _lookup, new LayoutInt { Value = 123 }); }
            catch (InvalidOperationException) { WriteRejections++; }
        }
    }

    private sealed class DeclaredLookupRuntime : IRangeSystemRuntime<DeclaredLookupJob>
    {
        private readonly ComponentLookup<LayoutInt> _lookup;
        internal Entity Entity;
        internal bool ReadAccepted;
        internal bool WriteAccepted;
        internal DeclaredLookupRuntime(ComponentLookup<LayoutInt> lookup) => _lookup = lookup;
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public DeclaredLookupJob Prepare(in SystemPrepareContext context) => new(Entity, _lookup);
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<DeclaredLookupJob> jobs)
        {
            ReadAccepted = jobs.Length == 1 && jobs[0].ReadAccepted;
            WriteAccepted = jobs.Length == 1 && jobs[0].WriteAccepted;
        }
    }

    private struct DeclaredLookupJob : IRangeJob
    {
        private readonly Entity _entity;
        private readonly ComponentLookup<LayoutInt> _lookup;
        internal bool ReadAccepted;
        internal bool WriteAccepted;
        internal DeclaredLookupJob(Entity entity, ComponentLookup<LayoutInt> lookup) =>
            (_entity, _lookup, ReadAccepted, WriteAccepted) = (entity, lookup, false, false);
        public void Execute(in SystemJobContext context, int start, int count)
        {
            ReadAccepted = context.TryRead(_entity, _lookup, out _);
            WriteAccepted = context.TryWrite(_entity, _lookup, new LayoutInt { Value = 456 });
        }
    }

    private sealed class LookupProbeRangeRuntime : IRangeSystemRuntime<LookupProbeRangeJob>
    {
        private readonly ComponentLookup<LayoutInt> _lookup;
        internal Entity Entity = Entity.Null;
        internal int Rejections;
        internal LookupProbeRangeRuntime(ComponentLookup<LayoutInt> lookup) => _lookup = lookup;
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public LookupProbeRangeJob Prepare(in SystemPrepareContext context) => new(Entity, _lookup);
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<LookupProbeRangeJob> jobs) =>
            Rejections = jobs.Length == 0 ? 0 : jobs[0].Rejections;
    }

    private struct LookupProbeRangeJob : IRangeJob
    {
        private readonly Entity _entity;
        private readonly ComponentLookup<LayoutInt> _lookup;
        internal int Rejections;
        internal LookupProbeRangeJob(Entity entity, ComponentLookup<LayoutInt> lookup) =>
            (_entity, _lookup, Rejections) = (entity, lookup, 0);
        public void Execute(in SystemJobContext context, int start, int count)
        {
            try { _ = context.TryRead(_entity, _lookup, out LayoutInt _); }
            catch (InvalidOperationException) { Rejections++; }
        }
    }

    private sealed class LeakedCapabilityProbeRuntime : IRangeSystemRuntime<LeakedCapabilityProbeJob>
    {
        private readonly ComponentLookup<LayoutInt> _lookup;
        internal Entity Entity;
        internal int Rejections;
        internal LeakedCapabilityProbeRuntime(ComponentLookup<LayoutInt> lookup) => _lookup = lookup;
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public LeakedCapabilityProbeJob Prepare(in SystemPrepareContext context) => new(Entity, _lookup);
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<LeakedCapabilityProbeJob> jobs) =>
            Rejections = jobs.Length == 0 ? 0 : jobs[0].Rejections;
    }

    private struct LeakedCapabilityProbeJob : IRangeJob
    {
        private readonly Entity _entity;
        private readonly ComponentLookup<LayoutInt> _lookup;
        internal int Rejections;
        internal LeakedCapabilityProbeJob(Entity entity, ComponentLookup<LayoutInt> lookup) =>
            (_entity, _lookup, Rejections) = (entity, lookup, 0);
        public void Execute(in SystemJobContext context, int start, int count)
        {
            try { _ = context.TryRead(_entity, _lookup, out LayoutInt _); }
            catch (InvalidOperationException) { Rejections++; }
            try { _ = context.TryWrite(_entity, _lookup, new LayoutInt { Value = 7 }); }
            catch (InvalidOperationException) { Rejections++; }
            try { _ = context.Commands.TryAdd(_entity, new LayoutInt { Value = 9 }); }
            catch (InvalidOperationException) { Rejections++; }
        }
    }

    private sealed class RandomEnableRuntime : IRangeSystemRuntime<RandomEnableJob>
    {
        private readonly ComponentLookup<LayoutEnableLong> _lookup;
        internal RandomEnableRuntime(ComponentLookup<LayoutEnableLong> lookup, int count)
        {
            _lookup = lookup;
            Entities = new Entity[count];
        }
        internal Entity[] Entities { get; }
        internal int Pattern { get; set; }
        public int GetRangeCount(in SystemPrepareContext context) => Entities.Length;
        public RandomEnableJob Prepare(in SystemPrepareContext context) => new(Entities, _lookup, Pattern);
    }

    private readonly struct RandomEnableJob : IRangeJob
    {
        private readonly Entity[] _entities;
        private readonly ComponentLookup<LayoutEnableLong> _lookup;
        private readonly int _pattern;
        internal RandomEnableJob(Entity[] entities, ComponentLookup<LayoutEnableLong> lookup, int pattern) =>
            (_entities, _lookup, _pattern) = (entities, lookup, pattern);
        public void Execute(in SystemJobContext context, int start, int count)
        {
            for (int index = start; index < start + count; index++)
                Assert.True(context.TrySetEnabled(
                    _entities[index], _lookup, DesiredEnableState(index, _pattern)));
        }
    }

    private sealed class RetainedWorldRuntime : ISystemRuntime<RetainedWorldJob>
    {
        private readonly ComponentLookup<LayoutInt> _lookup;
        private EcsWorld? _world;
        internal Entity Entity;
        internal bool InitializeAccessSucceeded;
        internal bool ShutdownAccessSucceeded;
        internal int PrepareRejections;
        internal int ExecuteRejections;
        internal int CompleteRejections;
        internal RetainedWorldRuntime(ComponentLookup<LayoutInt> lookup) => _lookup = lookup;
        public void Initialize(in SystemInitializationContext context)
        {
            _world = context.World;
            _ = context.World.StructuralCommands.Count;
            InitializeAccessSucceeded = true;
        }
        public RetainedWorldJob Prepare(in SystemPrepareContext context)
        {
            EcsWorld world = _world ?? throw new InvalidOperationException();
            PrepareRejections = ProbeDirectWorld(world, Entity, _lookup);
            return new RetainedWorldJob(world, Entity, _lookup);
        }
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<RetainedWorldJob> jobs)
        {
            for (int index = 0; index < jobs.Length; index++)
                ExecuteRejections += jobs[index].Rejections;
            CompleteRejections = ProbeDirectWorld(
                _world ?? throw new InvalidOperationException(), Entity, _lookup);
        }
        public void Shutdown(in SystemShutdownContext context)
        {
            _ = context.World.AliveCount;
            ShutdownAccessSucceeded = true;
        }
        internal static int ProbeDirectWorld(EcsWorld world, Entity entity,
            ComponentLookup<LayoutInt> lookup)
        {
            int rejected = 0;
            try { _ = world.TryRead(entity, lookup, out LayoutInt _); }
            catch (InvalidOperationException) { rejected++; }
            try { _ = world.TryWrite(entity, lookup, new LayoutInt { Value = 99 }); }
            catch (InvalidOperationException) { rejected++; }
            try { _ = world.StructuralCommands.Count; }
            catch (InvalidOperationException) { rejected++; }
            return rejected;
        }
    }

    private struct RetainedWorldJob : IScheduledReadWriteChunkJob<LayoutWide, LayoutInt>
    {
        private readonly EcsWorld _world;
        private readonly Entity _entity;
        private readonly ComponentLookup<LayoutInt> _lookup;
        internal int Rejections;
        internal RetainedWorldJob(EcsWorld world, Entity entity, ComponentLookup<LayoutInt> lookup) =>
            (_world, _entity, _lookup, Rejections) = (world, entity, lookup, 0);
        public void Execute(in SystemJobContext context,
            in ReadWriteComponentChunk<LayoutWide, LayoutInt> chunk)
        {
            int rejected = 0;
            try { _ = _world.TryRead(_entity, _lookup, out LayoutInt _); }
            catch (InvalidOperationException) { rejected++; }
            try { _ = _world.StructuralCommands.Count; }
            catch (InvalidOperationException) { rejected++; }
            Rejections = rejected;
        }
    }

    private sealed class RetainedArenaRuntime : IRangeSystemRuntime<RetainedArenaJob>
    {
        private StructuralCommandBuffer? _commands;
        private readonly int _rangeCount;
        internal Entity Entity;
        internal int Rejections;
        internal RetainedArenaRuntime(int rangeCount) => _rangeCount = rangeCount;
        public void Initialize(in SystemInitializationContext context) =>
            _commands = context.World.StructuralCommands;
        public int GetRangeCount(in SystemPrepareContext context) => _rangeCount;
        public RetainedArenaJob Prepare(in SystemPrepareContext context)
        {
            StructuralCommandBuffer commands = _commands ?? throw new InvalidOperationException();
            try { _ = commands.TryEnqueueDestroy(Entity); }
            catch (InvalidOperationException) { Rejections++; }
            return new RetainedArenaJob(commands, Entity);
        }
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<RetainedArenaJob> jobs)
        {
            for (int index = 0; index < jobs.Length; index++)
                Rejections += jobs[index].Rejections;
            try { _ = (_commands ?? throw new InvalidOperationException()).TryEnqueueDestroy(Entity); }
            catch (InvalidOperationException) { Rejections++; }
        }
    }

    private struct RetainedArenaJob : IRangeJob
    {
        private readonly StructuralCommandBuffer _commands;
        private readonly Entity _entity;
        internal int Rejections;
        internal RetainedArenaJob(StructuralCommandBuffer commands, Entity entity) =>
            (_commands, _entity, Rejections) = (commands, entity, 0);
        public void Execute(in SystemJobContext context, int start, int count)
        {
            try { _ = _commands.TryEnqueueDestroy(_entity); }
            catch (InvalidOperationException) { Rejections++; }
        }
    }

    private sealed class ReadOnlyRuntime : ISystemRuntime<ReadOnlyJob>
    {
        internal int Rows;
        public ReadOnlyJob Prepare(in SystemPrepareContext context) => default;
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<ReadOnlyJob> jobs)
        { for (int i = 0; i < jobs.Length; i++) Rows += jobs[i].Rows; }
    }
    private struct ReadOnlyJob : IScheduledReadChunkJob<LayoutWide>
    {
        internal int Rows;
        public void Execute(in SystemJobContext context, in ReadComponentChunk<LayoutWide> chunk) => Rows += chunk.Count;
    }

    private sealed class WriteOnlyRuntime : ISystemRuntime<WriteOnlyJob>
    {
        internal int Rows;
        public WriteOnlyJob Prepare(in SystemPrepareContext context) => default;
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<WriteOnlyJob> jobs)
        { for (int i = 0; i < jobs.Length; i++) Rows += jobs[i].Rows; }
    }
    private struct WriteOnlyJob : IScheduledWriteChunkJob<LayoutInt>
    {
        internal int Rows;
        public void Execute(in SystemJobContext context, in WriteComponentChunk<LayoutInt> chunk)
        { Rows += chunk.Count; }
    }

    private sealed class ReadThreeRuntime : ISystemRuntime<ReadThreeJob>
    {
        internal int Rows;
        public ReadThreeJob Prepare(in SystemPrepareContext context) => default;
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<ReadThreeJob> jobs)
        { for (int i = 0; i < jobs.Length; i++) Rows += jobs[i].Rows; }
    }
    private struct ReadThreeJob : IScheduledReadReadReadChunkJob<LayoutWide, LayoutInt, LayoutEnableLong>
    {
        internal int Rows;
        public void Execute(in SystemJobContext context,
            in ReadReadReadComponentChunk<LayoutWide, LayoutInt, LayoutEnableLong> chunk) => Rows += chunk.Count;
    }

    private sealed class MarkRangeRuntime : IRangeSystemRuntime<MarkRangeJob>
    {
        internal readonly int[] Marks;
        internal int Rows;
        internal MarkRangeRuntime(int[] marks) => Marks = marks;
        public int GetRangeCount(in SystemPrepareContext context) => Marks.Length;
        public MarkRangeJob Prepare(in SystemPrepareContext context) => new(Marks);
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<MarkRangeJob> jobs)
        { for (int i = 0; i < jobs.Length; i++) Rows += jobs[i].Rows; }
    }
    private struct MarkRangeJob : IRangeJob
    {
        private readonly int[] _marks;
        internal int Rows;
        internal MarkRangeJob(int[] marks) => (_marks, Rows) = (marks, 0);
        public void Execute(in SystemJobContext context, int start, int count)
        { for (int i = start; i < start + count; i++) Interlocked.Increment(ref _marks[i]); Rows += count; }
    }

    private sealed class NoOpRangeRuntime : IRangeSystemRuntime<NoOpRangeJob>
    {
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public NoOpRangeJob Prepare(in SystemPrepareContext context) => default;
    }
    private struct NoOpRangeJob : IRangeJob
    { public void Execute(in SystemJobContext context, int start, int count) { } }

    private sealed class LifecycleRangeRuntime : IRangeSystemRuntime<NoOpRangeJob>
    {
        internal int InitializeCount;
        internal int ShutdownCount;
        public void Initialize(in SystemInitializationContext context) => InitializeCount++;
        public int GetRangeCount(in SystemPrepareContext context) => 0;
        public NoOpRangeJob Prepare(in SystemPrepareContext context) => default;
        public void Shutdown(in SystemShutdownContext context) => ShutdownCount++;
    }

    private sealed class ThreadCaptureRuntime : IRangeSystemRuntime<ThreadCaptureJob>
    {
        internal int ThreadId;
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public ThreadCaptureJob Prepare(in SystemPrepareContext context) => new(this);
    }
    private readonly struct ThreadCaptureJob : IRangeJob
    {
        private readonly ThreadCaptureRuntime _runtime;
        internal ThreadCaptureJob(ThreadCaptureRuntime runtime) => _runtime = runtime;
        public void Execute(in SystemJobContext context, int start, int count) =>
            _runtime.ThreadId = Environment.CurrentManagedThreadId;
    }

    private sealed class ThreadArrayRuntime : IRangeSystemRuntime<ThreadArrayJob>
    {
        internal ThreadArrayRuntime(int count) => Threads = new Thread?[count];
        internal Thread?[] Threads { get; }
        public int GetRangeCount(in SystemPrepareContext context) => Threads.Length;
        public ThreadArrayJob Prepare(in SystemPrepareContext context) => new(Threads);
    }
    private readonly struct ThreadArrayJob : IRangeJob
    {
        private readonly Thread?[] _threads;
        internal ThreadArrayJob(Thread?[] threads) => _threads = threads;
        public void Execute(in SystemJobContext context, int start, int count) =>
            _threads[context.ParticipantIndex] = Thread.CurrentThread;
    }

    private sealed class CaptureContextRuntime : IRangeSystemRuntime<CaptureContextJob>
    {
        internal SystemJobContext Context;
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public CaptureContextJob Prepare(in SystemPrepareContext context) => new(this);
    }
    private struct CaptureContextJob : IRangeJob
    {
        private readonly CaptureContextRuntime _runtime;
        internal CaptureContextJob(CaptureContextRuntime runtime) => _runtime = runtime;
        public void Execute(in SystemJobContext context, int start, int count) => _runtime.Context = context;
    }
    private sealed class DeferredHolder { internal DeferredEntity Entity; }
    private sealed class CreateTokenRuntime : IRangeSystemRuntime<CreateTokenJob>
    {
        private readonly DeferredHolder _holder;
        private readonly ArchetypeHandle _archetype;
        internal CreateTokenRuntime(DeferredHolder holder, ArchetypeHandle archetype) =>
            (_holder, _archetype) = (holder, archetype);
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public CreateTokenJob Prepare(in SystemPrepareContext context) => new(_holder, _archetype);
    }
    private struct CreateTokenJob : IRangeJob
    {
        private readonly DeferredHolder _holder;
        private readonly ArchetypeHandle _archetype;
        internal CreateTokenJob(DeferredHolder holder, ArchetypeHandle archetype) =>
            (_holder, _archetype) = (holder, archetype);
        public void Execute(in SystemJobContext context, int start, int count) =>
            _holder.Entity = context.Commands.CreateDefault(_archetype);
    }
    private sealed class ConsumeTokenRuntime : IRangeSystemRuntime<ConsumeTokenJob>
    {
        private readonly DeferredHolder _holder;
        internal ConsumeTokenRuntime(DeferredHolder holder) => _holder = holder;
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public ConsumeTokenJob Prepare(in SystemPrepareContext context) => new(_holder);
    }
    private struct ConsumeTokenJob : IRangeJob
    {
        private readonly DeferredHolder _holder;
        internal ConsumeTokenJob(DeferredHolder holder) => _holder = holder;
        public void Execute(in SystemJobContext context, int start, int count) =>
            context.Commands.TryDestroy(_holder.Entity);
    }

    private sealed class ConcurrentOverlapProbe
    {
        internal int Active;
        internal int MaximumConcurrent;
        internal int Executions;
    }
    private sealed class ConcurrentOverlapRuntime : IRangeSystemRuntime<ConcurrentOverlapJob>
    {
        private readonly ConcurrentOverlapProbe _probe;
        internal ConcurrentOverlapRuntime(ConcurrentOverlapProbe probe) => _probe = probe;
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public ConcurrentOverlapJob Prepare(in SystemPrepareContext context) => new(_probe);
    }
    private readonly struct ConcurrentOverlapJob : IRangeJob
    {
        private readonly ConcurrentOverlapProbe _probe;
        internal ConcurrentOverlapJob(ConcurrentOverlapProbe probe) => _probe = probe;
        public void Execute(in SystemJobContext context, int start, int count)
        {
            int active = Interlocked.Increment(ref _probe.Active);
            int observed = Volatile.Read(ref _probe.MaximumConcurrent);
            while (observed < active)
            {
                int prior = Interlocked.CompareExchange(
                    ref _probe.MaximumConcurrent, active, observed);
                if (prior == observed)
                    break;
                observed = prior;
            }
            Thread.Sleep(2);
            Interlocked.Increment(ref _probe.Executions);
            Interlocked.Decrement(ref _probe.Active);
        }
    }

    private sealed class BarrierOrder { internal int Value; }
    private sealed class BarrierRuntime : IRangeSystemRuntime<BarrierJob>
    {
        private readonly BarrierOrder _order;
        private readonly int _digit;
        internal BarrierRuntime(BarrierOrder order, int digit) => (_order, _digit) = (order, digit);
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public BarrierJob Prepare(in SystemPrepareContext context) => new(_order, _digit);
    }
    private struct BarrierJob : IRangeJob
    {
        private readonly BarrierOrder _order;
        private readonly int _digit;
        internal BarrierJob(BarrierOrder order, int digit) => (_order, _digit) = (order, digit);
        public void Execute(in SystemJobContext context, int start, int count) =>
            _order.Value = checked(_order.Value * 10 + _digit);
    }

    private sealed class SparseTrackedRuntime : ISystemRuntime<SparseTrackedJob>
    {
        private readonly bool _write;
        private readonly int _divisor;
        internal int Writes;
        internal SparseTrackedRuntime(bool write, int divisor) => (_write, _divisor) = (write, divisor);
        public SparseTrackedJob Prepare(in SystemPrepareContext context) => new(_write, _divisor);
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<SparseTrackedJob> jobs)
        { for (int i = 0; i < jobs.Length; i++) Writes += jobs[i].Writes; }
    }
    private struct SparseTrackedJob : IScheduledTrackedWriteChunkJob<LayoutInt>
    {
        private readonly bool _write;
        private readonly int _divisor;
        internal int Writes;
        internal SparseTrackedJob(bool write, int divisor) => (_write, _divisor, Writes) = (write, divisor, 0);
        public void Execute(in SystemJobContext context, in TrackedWriteComponentChunk<LayoutInt> chunk)
        {
            if (!_write) return;
            for (int row = 0; row < chunk.Count; row++)
            {
                if (chunk.EntityIndices[row] % unchecked((uint)_divisor) != 0) continue;
                ref LayoutInt value = ref chunk.Write.GetTracked(row);
                value.Value++;
                Writes++;
            }
        }
    }

    private sealed class TrackedReadWriteRuntime : ISystemRuntime<ScheduledTrackedReadWriteJob>
    {
        private readonly bool _writeOdd;
        internal int Writes;
        internal TrackedReadWriteRuntime(bool writeOdd) => _writeOdd = writeOdd;
        public ScheduledTrackedReadWriteJob Prepare(in SystemPrepareContext context) => new(_writeOdd);
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<ScheduledTrackedReadWriteJob> jobs)
        {
            for (int index = 0; index < jobs.Length; index++)
                Writes += jobs[index].Writes;
        }
    }

    private struct ScheduledTrackedReadWriteJob :
        IScheduledTrackedReadWriteChunkJob<LayoutWide, LayoutInt>
    {
        private readonly bool _writeOdd;
        internal int Writes;
        internal ScheduledTrackedReadWriteJob(bool writeOdd) => (_writeOdd, Writes) = (writeOdd, 0);
        public void Execute(in SystemJobContext context,
            in TrackedReadWriteComponentChunk<LayoutWide, LayoutInt> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                if (((chunk.EntityIndices[row] & 1) != 0) != _writeOdd)
                    continue;
                ref LayoutInt value = ref chunk.Write.GetTracked(row);
                value.Value += 2;
                Writes++;
            }
        }
    }

    private struct DirectTrackedReadWriteJob : ITrackedReadWriteChunkJob<LayoutWide, LayoutInt>
    {
        private readonly bool _writeEven;
        internal int Writes;
        internal DirectTrackedReadWriteJob(bool writeEven) => (_writeEven, Writes) = (writeEven, 0);
        public void Execute(in TrackedReadWriteComponentChunk<LayoutWide, LayoutInt> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                if (((chunk.EntityIndices[row] & 1) == 0) != _writeEven)
                    continue;
                ref LayoutInt value = ref chunk.Write.GetTracked(row);
                value.Value++;
                Writes++;
            }
        }
    }
    private struct ChangedAuditJob : IReadOnlyChunkJob<LayoutInt>
    {
        internal int Rows;
        public void Execute(in ReadOnlyComponentChunk<LayoutInt> chunk) => Rows += chunk.Count;
    }
    private struct EnableChangedAuditJob : IReadOnlyChunkJob<LayoutEnableLong>
    {
        internal int Rows;
        public void Execute(in ReadOnlyComponentChunk<LayoutEnableLong> chunk) => Rows += chunk.Count;
    }
    private sealed class ChangedRowsRuntime : IChangedRowsSystemRuntime<ChangedRowsScheduledJob>
    {
        private bool _cursorInitialized;
        internal int Rows;
        public ChangeCursor Cursor { get; set; }
        internal void Arm(ChangeCursor cursor) { Cursor = cursor; _cursorInitialized = true; }
        public ChangedRowsScheduledJob Prepare(in SystemPrepareContext context)
        {
            if (!_cursorInitialized)
                throw new InvalidOperationException("The changed-row test runtime was not armed.");
            return default;
        }
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<ChangedRowsScheduledJob> jobs)
        { for (int i = 0; i < jobs.Length; i++) Rows += jobs[i].Rows; }
    }
    private struct ChangedRowsScheduledJob : IScheduledChangedRowsChunkJob<LayoutInt>
    {
        internal int Rows;
        public void Execute(in SystemJobContext context, in ReadOnlyComponentChunk<LayoutInt> chunk) =>
            Rows += chunk.Count;
    }

    private sealed class AddRuntime : IRangeSystemRuntime<AddJob>
    {
        internal AddRuntime(ArchetypeHandle source, ComponentLookup<LayoutInt> lookup, int count)
        { Source = source; Lookup = lookup; Count = count; }
        internal ArchetypeHandle Source;
        internal ComponentLookup<LayoutInt> Lookup;
        internal int Count;
        internal int Completed;
        internal SystemId Id;
        public int GetRangeCount(in SystemPrepareContext context) => Count;
        public AddJob Prepare(in SystemPrepareContext context) => new(Source);
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<AddJob> jobs)
        { for (int i = 0; i < jobs.Length; i++) Completed += jobs[i].Completed; }
    }
    private struct AddJob : IRangeJob
    {
        private readonly ArchetypeHandle _source;
        internal int Completed;
        internal AddJob(ArchetypeHandle source) => (_source, Completed) = (source, 0);
        public void Execute(in SystemJobContext context, int start, int count)
        {
            for (int i = start; i < start + count; i++)
            {
                DeferredEntity entity = context.Commands.CreateDefault(_source);
                var value = new LayoutInt { Value = i + 100 };
                Assert.True(context.Commands.TryAdd(entity, value));
                Completed++;
            }
        }
    }

    private sealed class ThrowingRangeRuntime : IRangeSystemRuntime<ThrowingRangeJob>
    {
        private readonly ArchetypeHandle _archetype;
        internal ThrowingRangeRuntime(ArchetypeHandle archetype) => _archetype = archetype;
        public int GetRangeCount(in SystemPrepareContext context) => 8;
        public ThrowingRangeJob Prepare(in SystemPrepareContext context) => new(_archetype);
    }
    private struct ThrowingRangeJob : IRangeJob
    {
        private readonly ArchetypeHandle _archetype;
        internal ThrowingRangeJob(ArchetypeHandle archetype) => _archetype = archetype;
        public void Execute(in SystemJobContext context, int start, int count)
        {
            context.Commands.CreateDefault(_archetype);
            throw new SchedulerTestException();
        }
    }
    private sealed class SchedulerTestException : Exception { }

    private sealed class TypedStructuralRuntime : IRangeSystemRuntime<TypedStructuralJob>
    {
        internal Entity AddInteger;
        internal Entity ReplaceInteger;
        internal Entity RemoveInteger;
        internal Entity AddTag;
        internal Entity RemoveTag;
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public TypedStructuralJob Prepare(in SystemPrepareContext context) => new(
            AddInteger, ReplaceInteger, RemoveInteger, AddTag, RemoveTag);
    }

    private sealed class RouteCollisionRuntime : IRangeSystemRuntime<RouteCollisionJob>
    {
        private readonly ArchetypeTransition _transition;
        private readonly bool _removePhase;
        internal RouteCollisionRuntime(int count, ArchetypeTransition transition, bool removePhase)
        {
            Entities = new Entity[count];
            _transition = transition;
            _removePhase = removePhase;
        }
        internal Entity[] Entities { get; }
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public RouteCollisionJob Prepare(in SystemPrepareContext context) =>
            new(Entities, _transition, _removePhase);
    }

    private readonly record struct RouteCollisionOutcome(
        int CommandCount,
        int AddedCount,
        int RemovedCount,
        int RejectedCount,
        ulong CanonicalHash);

    private readonly struct RouteCollisionJob : IRangeJob
    {
        private readonly Entity[] _entities;
        private readonly ArchetypeTransition _transition;
        private readonly bool _removePhase;
        internal RouteCollisionJob(Entity[] entities, ArchetypeTransition transition, bool removePhase) =>
            (_entities, _transition, _removePhase) = (entities, transition, removePhase);
        public void Execute(in SystemJobContext context, int start, int count)
        {
            for (int index = 0; index < _entities.Length - 1; index++)
                Assert.True(context.Commands.TryTransition(_entities[index], _transition));
            Entity final = _entities[^1];
            if (_removePhase)
                Assert.True(context.Commands.TryRemove<LayoutTag>(final));
            else
                Assert.True(context.Commands.TryAdd(final, new LayoutInt { Value = 73 }));
        }
    }

    private readonly struct TypedStructuralJob : IRangeJob
    {
        private readonly Entity _addInteger;
        private readonly Entity _replaceInteger;
        private readonly Entity _removeInteger;
        private readonly Entity _addTag;
        private readonly Entity _removeTag;
        internal TypedStructuralJob(Entity addInteger, Entity replaceInteger, Entity removeInteger,
            Entity addTag, Entity removeTag) =>
            (_addInteger, _replaceInteger, _removeInteger, _addTag, _removeTag) =
            (addInteger, replaceInteger, removeInteger, addTag, removeTag);
        public void Execute(in SystemJobContext context, int start, int count)
        {
            Assert.True(context.Commands.TryAdd(_addInteger, new LayoutInt { Value = 42 }));
            Assert.True(context.Commands.TryAdd(_replaceInteger, new LayoutInt { Value = 99 }));
            Assert.True(context.Commands.TryRemove<LayoutInt>(_removeInteger));
            Assert.True(context.Commands.TryAdd(_addTag, new LayoutTag()));
            Assert.True(context.Commands.TryRemove<LayoutTag>(_removeTag));
        }
    }

    private sealed class PayloadExhaustionRuntime : IRangeSystemRuntime<PayloadExhaustionJob>
    {
        internal Entity Entity;
        internal bool Accepted;
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public PayloadExhaustionJob Prepare(in SystemPrepareContext context) => new(Entity);
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<PayloadExhaustionJob> jobs) =>
            Accepted = jobs.Length != 0 && jobs[0].Accepted;
    }

    private struct PayloadExhaustionJob : IRangeJob
    {
        private readonly Entity _entity;
        internal bool Accepted;
        internal PayloadExhaustionJob(Entity entity) => (_entity, Accepted) = (entity, false);
        public void Execute(in SystemJobContext context, int start, int count) =>
            Accepted = context.Commands.TryAdd(_entity, new LayoutInt { Value = 17 });
    }

    private sealed class TagOnlyRuntime : IRangeSystemRuntime<TagOnlyJob>
    {
        internal Entity Entity;
        internal bool Accepted;
        public int GetRangeCount(in SystemPrepareContext context) => 1;
        public TagOnlyJob Prepare(in SystemPrepareContext context) => new(Entity);
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<TagOnlyJob> jobs) =>
            Accepted = jobs.Length != 0 && jobs[0].Accepted;
    }

    private struct TagOnlyJob : IRangeJob
    {
        private readonly Entity _entity;
        internal bool Accepted;
        internal TagOnlyJob(Entity entity) => (_entity, Accepted) = (entity, false);
        public void Execute(in SystemJobContext context, int start, int count) =>
            Accepted = context.Commands.TryAdd(_entity, new LayoutTag());
    }
}

internal static class SchedulerTestWorldExtensions
{
    // Tests that hash after Seal retain their already-prepared lookup through a tiny registry.
    private static readonly ConditionalWeakTable<EcsWorld, LookupHolder> Lookups = new();
    internal static void Remember(this EcsWorld world, ComponentLookup<LayoutInt> lookup) =>
        Lookups.Add(world, new LookupHolder(lookup));
    internal static ComponentLookup<LayoutInt> PrepareLookupAfterSealForTest(this EcsWorld world) =>
        Lookups.TryGetValue(world, out LookupHolder? holder)
            ? holder.Lookup
            : throw new InvalidOperationException("Test lookup was not retained.");
    private sealed class LookupHolder
    {
        internal LookupHolder(ComponentLookup<LayoutInt> lookup) => Lookup = lookup;
        internal ComponentLookup<LayoutInt> Lookup { get; }
    }
}
