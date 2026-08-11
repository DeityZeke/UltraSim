using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UltraSim.ECS;
using UltraSim.ECS.Tests;

internal static class SchedulerPrimitiveTests
{
    internal static void RunAll()
    {
        Run(nameof(Layouts_AndDisabledWorld_AreExplicit), Layouts_AndDisabledWorld_AreExplicit);
        Run(nameof(SerialGeneratedSystem_HonorsLifecycleCadenceAndSnapshots), SerialGeneratedSystem_HonorsLifecycleCadenceAndSnapshots);
        Run(nameof(GeneratedMissingFamilies_ExecuteThroughPublicSurface), GeneratedMissingFamilies_ExecuteThroughPublicSurface);
        Run(nameof(ParallelAndSerialGeneratedExecution_AreCanonical), ParallelAndSerialGeneratedExecution_AreCanonical);
        Run(nameof(SerialMode_WithoutCallerUsesOneWorker), SerialMode_WithoutCallerUsesOneWorker);
        Run(nameof(RangeJobs_PartitionExactlyOnceAcrossPersistentWorkers), RangeJobs_PartitionExactlyOnceAcrossPersistentWorkers);
        Run(nameof(AutoPolicy_BelowCrossoverDoesNotWakeBackgroundWorkers), AutoPolicy_BelowCrossoverDoesNotWakeBackgroundWorkers);
        Run(nameof(RegistrationIdentityLifecycleAndStageOrder_AreExplicit), RegistrationIdentityLifecycleAndStageOrder_AreExplicit);
        Run(nameof(GraphDependenciesAndStageValidation_AreExplicit), GraphDependenciesAndStageValidation_AreExplicit);
        Run(nameof(Cadence_DoesNotMakeDependenciesSameTickPrerequisites), Cadence_DoesNotMakeDependenciesSameTickPrerequisites);
        Run(nameof(ConflictsAndDependencies_CompileHardWaveBarriers), ConflictsAndDependencies_CompileHardWaveBarriers);
        Run(nameof(ParallelTrackedWrites_PublishOneVersionAndExactRows), ParallelTrackedWrites_PublishOneVersionAndExactRows);
        Run(nameof(TrackedNoWrite_DoesNotAdvancePublication), TrackedNoWrite_DoesNotAdvancePublication);
        Run(nameof(ScheduledChangedRows_AcknowledgeOnlySuccessfulExecution), ScheduledChangedRows_AcknowledgeOnlySuccessfulExecution);
        Run(nameof(PhaseStaggering_IsOptInDeterministicAndStageBounded), PhaseStaggering_IsOptInDeterministicAndStageBounded);
        Run(nameof(WorkerCapabilities_ExpireAndCrossSegmentDeferredTargetsFault), WorkerCapabilities_ExpireAndCrossSegmentDeferredTargetsFault);
        Run(nameof(StructuralLanes_MergeInLogicalOrderAndApplyAtPostBarrier), StructuralLanes_MergeInLogicalOrderAndApplyAtPostBarrier);
        Run(nameof(Tick_PreBarrierMakesExternalCommandsVisibleToSystems), Tick_PreBarrierMakesExternalCommandsVisibleToSystems);
        Run(nameof(TypedStructuralRoutes_HandleAddReplaceRemoveTagsAndPayloadExhaustion), TypedStructuralRoutes_HandleAddReplaceRemoveTagsAndPayloadExhaustion);
        Run(nameof(ManualSystems_LeaveStructuralOutputForNextExternalEpoch), ManualSystems_LeaveStructuralOutputForNextExternalEpoch);
        Run(nameof(WorkerFailure_FaultsWorldAndSkipsStructuralPublication), WorkerFailure_FaultsWorldAndSkipsStructuralPublication);
        Run(nameof(WorkerDisposal_JoinsEveryBackgroundThread), WorkerDisposal_JoinsEveryBackgroundThread);
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

    private static void RegistrationIdentityLifecycleAndStageOrder_AreExplicit()
    {
        using (EcsWorld duplicateKey = CreateBareWorld(Options(0), 1, out _))
        {
            duplicateKey.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("identity.duplicate", SystemStage.Input), new NoOpRangeRuntime());
            Assert.Throws<ArgumentException>(() =>
                duplicateKey.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                    new SystemDefinition("identity.duplicate", SystemStage.AI), new NoOpRangeRuntime()));
        }

        using (EcsWorld duplicatePersistence = CreateBareWorld(Options(0), 1, out _))
        {
            duplicatePersistence.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                new SystemDefinition("persistence.first", SystemStage.Input,
                    persistenceId: 7001, stateSchemaVersion: 1), new NoOpRangeRuntime());
            Assert.Throws<ArgumentException>(() =>
                duplicatePersistence.RegisterRangeSystem<NoOpRangeRuntime, NoOpRangeJob>(
                    new SystemDefinition("persistence.second", SystemStage.AI,
                        persistenceId: 7001, stateSchemaVersion: 2), new NoOpRangeRuntime()));
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
            maximumStructuralCommands: 8, scheduler));
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
        world.BeginRun();
        world.Tick(0.1);

        Assert.True(world.TryRead(runtime.AddInteger, integers, out LayoutInt added));
        Assert.Equal(42, added.Value);
        Assert.True(world.TryRead(runtime.ReplaceInteger, integers, out LayoutInt replaced));
        Assert.Equal(99, replaced.Value);
        Assert.False(world.Has(runtime.RemoveInteger, integers));
        Assert.True(world.Has(runtime.AddTag, tags));
        Assert.False(world.Has(runtime.RemoveTag, tags));

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
        int delivered = runtime.Rows;
        world.Tick(0.1);
        Assert.Equal(delivered, runtime.Rows);
    }

    private static void ManualSystems_LeaveStructuralOutputForNextExternalEpoch()
    {
        using EcsWorld world = CreateStructuralWorld(Options(0), manual: true,
            out ArchetypeHandle source, out _, out AddRuntime runtime);
        Start(world, source, 0);
        world.RunManual(runtime.Id, 0.25);
        Assert.Equal(0, world.AliveCount);
        Assert.Equal(128, world.StructuralCommands.Count);
        world.Tick(0.25);
        Assert.Equal(64, world.AliveCount);
        Assert.Equal(1UL, world.TickCount);
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
            new EcsWorldOptions(new EntityCapacity(128), 2, 8, 8, 4_096, scheduler));
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
    private struct ChangedAuditJob : IReadOnlyChunkJob<LayoutInt>
    {
        internal int Rows;
        public void Execute(in ReadOnlyComponentChunk<LayoutInt> chunk) => Rows += chunk.Count;
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
