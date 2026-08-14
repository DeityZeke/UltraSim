using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace UltraSim.ECS;

/// <summary>Bounded owner-coordinated system graph over persistent worker threads.</summary>
internal sealed class SystemScheduler : IDisposable
{
    internal static Action<int>? WorkerStartHookForTesting;
    private readonly EcsWorld _facade;
    private readonly EcsWorld<NativeChunkArena> _world;
    private readonly StructuralCommandBuffer _mainCommands;
    private readonly StructuralRecordingGate _structuralRecordingGate;
    private readonly EcsSchedulerOptions _options;
    private readonly SystemNode?[] _nodes;
    private readonly ulong[] _readMasks;
    private readonly ulong[] _writeMasks;
    private readonly ulong[] _lookupReadMasks;
    private readonly ulong[] _lookupWriteMasks;
    private readonly ulong[] _structuralMasks;
    private readonly uint[] _structuralRouteByComponent;
    private readonly int _maskWords;
    private readonly int[] _stageMaximumWave;
    private readonly SchedulerWorkItem[] _workItems;
    private readonly SchedulerChunkPartition[] _partitionScratch;
    private readonly StructuralSegment[] _segments;
    private readonly SystemNode?[] _activeNodes;
    private readonly int[] _activeWorkCounts;
    private readonly bool[] _activeParallel;
    private readonly long[] _activeStartTicks;
    private readonly long[] _participantEpochs;
    private readonly int[] _participantSystems;
    private readonly int[] _participantWorkOrdinals;
    private readonly int[] _participantThreadIds;
    private readonly int[] _participantSegmentStarts;
    private readonly StructuralCommandBuffer[] _structuralLanes;
    private PersistentWorkerRuntime? _workers;
    private int _nodeCount;
    private int _dependencyCount;
    private bool _sealed;
    private bool _initialized;
    private bool _executing;
    private bool _disposed;
    private long _executionEpoch;
    private ulong _tick;
    private double _deltaSeconds;
    private SchedulerRunMode _runMode;
    private Exception? _executionFault;
    private long _dispatchCount;
    private long _mergedSegments;
    private long _mergedCommands;
    private long _mergedPayloadBytes;
    private long _identityResolutions;
    private long _workerManagedAllocationBytes;
    private long _preBarrierTicks;
    private long _graphTicks;
    private long _structuralMergeTicks;
    private long _postBarrierTicks;
    private int _segmentCount;

    internal SystemScheduler(
        EcsWorld facade,
        EcsWorld<NativeChunkArena> world,
        StructuralCommandBuffer mainCommands,
        StructuralRecordingGate structuralRecordingGate,
        EcsSchedulerOptions options,
        int maximumArchetypes)
    {
        _facade = facade;
        _world = world;
        _mainCommands = mainCommands;
        _structuralRecordingGate = structuralRecordingGate;
        _options = options;
        _nodes = new SystemNode[options.MaximumSystems];
        _maskWords = (world.Schema.ComponentCount + 63) >> 6;
        _readMasks = new ulong[checked(options.MaximumSystems * _maskWords)];
        _writeMasks = new ulong[checked(options.MaximumSystems * _maskWords)];
        _lookupReadMasks = new ulong[checked(options.MaximumSystems * _maskWords)];
        _lookupWriteMasks = new ulong[checked(options.MaximumSystems * _maskWords)];
        _structuralMasks = new ulong[checked(options.MaximumSystems * _maskWords)];
        _structuralRouteByComponent = new uint[world.Schema.ComponentCount];
        _stageMaximumWave = new int[(int)SystemStage.Count];
        Array.Fill(_stageMaximumWave, -1);
        _workItems = new SchedulerWorkItem[options.MaximumWorkItems];
        _partitionScratch = new SchedulerChunkPartition[options.MaximumWorkItems];
        _segments = new StructuralSegment[options.MaximumStructuralSegments];
        _activeNodes = new SystemNode[options.MaximumSystems];
        _activeWorkCounts = new int[options.MaximumSystems];
        _activeParallel = new bool[options.MaximumSystems];
        _activeStartTicks = new long[options.MaximumSystems];
        int participants = options.ExecutionConcurrency;
        _participantEpochs = new long[participants];
        _participantSystems = new int[participants];
        _participantWorkOrdinals = new int[participants];
        _participantThreadIds = new int[participants];
        _participantSegmentStarts = new int[participants];
        Array.Fill(_participantSystems, -1);
        Array.Fill(_participantWorkOrdinals, -1);
        Array.Fill(_participantSegmentStarts, -1);
        _structuralLanes = new StructuralCommandBuffer[participants];
        for (int index = 0; index < participants; index++)
        {
            _structuralLanes[index] = new StructuralCommandBuffer(
                world.Id,
                ownerThreadId: 0,
                options.StructuralCommandsPerProducer,
                maximumArchetypes,
                schedulerLane: true,
                payloadCapacityBytes: options.StructuralPayloadBytesPerProducer);
        }
        _runMode = options.BackgroundWorkerCount == 0
            ? SchedulerRunMode.Serial
            : SchedulerRunMode.Parallel;
    }

    internal int ExecutionConcurrency => _options.ExecutionConcurrency;
    internal int StructuralCommandsPerProducer => _options.StructuralCommandsPerProducer;
    internal int Count => _nodeCount;
    internal bool IsExecuting => _executing;
    internal ulong TickCount => _tick;
    internal SchedulerRunMode RunMode => _runMode;
    internal long ReservedBytes => checked(
        (long)(_nodes.Length + _activeNodes.Length + _structuralLanes.Length) * IntPtr.Size +
        (long)(_readMasks.Length + _writeMasks.Length + _lookupReadMasks.Length +
            _lookupWriteMasks.Length + _structuralMasks.Length) * sizeof(ulong) +
        (long)_structuralRouteByComponent.Length * sizeof(uint) +
        (long)_stageMaximumWave.Length * sizeof(int) +
        (long)_workItems.Length * Unsafe.SizeOf<SchedulerWorkItem>() +
        (long)_partitionScratch.Length * Unsafe.SizeOf<SchedulerChunkPartition>() +
        (long)_segments.Length * Unsafe.SizeOf<StructuralSegment>() +
        (long)(_activeWorkCounts.Length * sizeof(int)) +
        (long)_activeParallel.Length * sizeof(bool) +
        (long)_activeStartTicks.Length * sizeof(long) +
        (long)(_participantEpochs.Length * sizeof(long)) +
        (long)(_participantSystems.Length + _participantWorkOrdinals.Length +
            _participantThreadIds.Length + _participantSegmentStarts.Length) * sizeof(int) +
        SumNodeReservations() +
        SumLaneReservations());

    internal PersistentSystemStateRecord[] CapturePersistentState(int maximumBytes)
    {
        if (_executing) throw new InvalidOperationException("System state cannot be captured during graph execution.");
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var records = new List<PersistentSystemStateRecord>();
        long totalBytes = 0;
        for (int index = 0; index < _nodeCount; index++)
        {
            SystemNode node = GetNode(index);
            if (node.Definition.PersistenceId == 0) continue;
            IPersistentSystemState state = node.PersistentState ?? throw new PersistenceCompatibilityException(
                $"System '{node.Definition.Key}' declares persistence identity but its runtime has no persistent-state codec.");
            int byteCount = state.PersistenceStateByteCount;
            if (byteCount < 0) throw new PersistenceValidationException("A system reported a negative persistence-state size.");
            totalBytes = checked(totalBytes + byteCount);
            if (totalBytes > maximumBytes)
                throw new PersistenceValidationException("Persistent system-state capture exceeds its configured byte bound.");
            byte[] bytes = new byte[byteCount];
            state.WritePersistenceState(bytes);
            records.Add(new PersistentSystemStateRecord(node.Definition.PersistenceId,
                node.Definition.StateSchemaVersion, state.PersistenceStateRequired, bytes));
        }
        records.Sort(static (left, right) => left.PersistenceId.CompareTo(right.PersistenceId));
        return records.ToArray();
    }

    internal void RestorePersistentState(ulong tick, ReadOnlySpan<ParsedSystemState> saved)
    {
        if (!_initialized || _executing) throw new InvalidOperationException("System state restore requires an idle initialized scheduler.");
        var current = new List<(ulong Id, string Key, IPersistentSystemState State)>();
        for (int nodeIndex = 0; nodeIndex < _nodeCount; nodeIndex++)
        {
            SystemNode node = GetNode(nodeIndex);
            ulong id = node.Definition.PersistenceId;
            if (id == 0) continue;
            IPersistentSystemState state = node.PersistentState ?? throw new PersistenceCompatibilityException(
                $"System '{node.Definition.Key}' has no persistent-state codec.");
            current.Add((id, node.Definition.Key, state));
        }
        current.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        int currentIndex = 0, savedIndex = 0;
        while (currentIndex < current.Count || savedIndex < saved.Length)
        {
            if (savedIndex < saved.Length &&
                (currentIndex == current.Count || saved[savedIndex].PersistenceId < current[currentIndex].Id))
            {
                if (saved[savedIndex].Required)
                    throw new PersistenceCompatibilityException(
                        $"Saved required system state {saved[savedIndex].PersistenceId} has no current owner.");
                savedIndex++;
                continue;
            }
            if (currentIndex < current.Count &&
                (savedIndex == saved.Length || current[currentIndex].Id < saved[savedIndex].PersistenceId))
            {
                if (current[currentIndex].State.PersistenceStateRequired)
                    throw new PersistenceCompatibilityException(
                        $"Required system state {current[currentIndex].Id} for '{current[currentIndex].Key}' is absent from the snapshot.");
                currentIndex++;
                continue;
            }
            current[currentIndex].State.ReadPersistenceState(saved[savedIndex].Version, saved[savedIndex].Bytes);
            currentIndex++;
            savedIndex++;
        }
        _tick = tick;
    }

    internal SystemId GetNextRegistrationId()
    {
        EnsureRegistrationOpen();
        if (_nodeCount == _nodes.Length)
            throw new InvalidOperationException($"System capacity {_nodes.Length} is exhausted.");
        return new SystemId(_nodeCount);
    }

    internal void RegisterNode(SystemNode node, ReadOnlySpan<ComponentId> reads, ReadOnlySpan<ComponentId> writes)
    {
        EnsureRegistrationOpen();
        ArgumentNullException.ThrowIfNull(node);
        if (node.Id.Value != _nodeCount)
            throw new InvalidOperationException("System registration IDs are not dense and ordered.");
        ValidateDefinitionIdentity(node.Definition);

        // Registration is a cold proof/commit transaction. Failed validation must
        // leave the reusable slot and dependency budget completely untouched.
        var proposed = new ulong[checked(_maskWords * 5)];
        Span<ulong> readMask = proposed.AsSpan(0, _maskWords);
        Span<ulong> writeMask = proposed.AsSpan(_maskWords, _maskWords);
        Span<ulong> lookupReadMask = proposed.AsSpan(_maskWords * 2, _maskWords);
        Span<ulong> lookupWriteMask = proposed.AsSpan(_maskWords * 3, _maskWords);
        Span<ulong> structuralMask = proposed.AsSpan(_maskWords * 4, _maskWords);
        AddComponents(readMask, reads, nameof(reads));
        AddComponents(writeMask, writes, nameof(writes));
        AddComponents(lookupReadMask, node.Definition.AdditionalReads,
            nameof(node.Definition.AdditionalReads));
        AddComponents(lookupWriteMask, node.Definition.AdditionalWrites,
            nameof(node.Definition.AdditionalWrites));
        AddComponents(readMask, node.Definition.AdditionalReads, nameof(node.Definition.AdditionalReads));
        AddComponents(writeMask, node.Definition.AdditionalWrites, nameof(node.Definition.AdditionalWrites));
        AddComponents(structuralMask, node.Definition.StructuralComponents, nameof(node.Definition.StructuralComponents));
        for (int word = 0; word < _maskWords; word++)
            readMask[word] &= ~writeMask[word];
        int proposedDependencyCount = checked(_dependencyCount + node.Definition.Requires.Count);
        if (proposedDependencyCount > _options.MaximumDependencies)
            throw new InvalidOperationException($"System dependency capacity {_options.MaximumDependencies} is exhausted.");

        readMask.CopyTo(GetMask(_readMasks, node.Id.Value));
        writeMask.CopyTo(GetMask(_writeMasks, node.Id.Value));
        lookupReadMask.CopyTo(GetMask(_lookupReadMasks, node.Id.Value));
        lookupWriteMask.CopyTo(GetMask(_lookupWriteMasks, node.Id.Value));
        structuralMask.CopyTo(GetMask(_structuralMasks, node.Id.Value));
        node.HasWrites = !IsEmpty(writeMask);
        _dependencyCount = proposedDependencyCount;
        _nodes[_nodeCount++] = node;
    }

    internal SystemId RegisterRangeSystem<TSystem, TJob>(SystemDefinition definition, TSystem runtime)
        where TSystem : class, IRangeSystemRuntime<TJob>
        where TJob : struct, IRangeJob
    {
        SystemId id = GetNextRegistrationId();
        var node = new RangeSystemNode<TSystem, TJob>(id, definition, runtime, ExecutionConcurrency);
        RegisterNode(node, ReadOnlySpan<ComponentId>.Empty, ReadOnlySpan<ComponentId>.Empty);
        return id;
    }

    internal void Seal()
    {
        if (_sealed)
            return;
        EnsureNotDisposed();
        ValidateAndCompileGraph();
        if (_options.BackgroundWorkerCount != 0)
        {
            _workers = new PersistentWorkerRuntime(this, _options.BackgroundWorkerCount, _options.CallerParticipates);
            _workers.Start();
        }
        else if (_options.CallerParticipates)
        {
            _participantThreadIds[0] = Environment.CurrentManagedThreadId;
        }
        _sealed = true;
    }

    internal void PrepareForWorldSeal()
    {
        EnsureRegistrationOpen();
        if (_structuralRouteByComponent.Length == 0)
            return;
        var unique = new ComponentId[_structuralRouteByComponent.Length];
        int uniqueCount = 0;
        for (int system = 0; system < _nodeCount; system++)
        {
            ReadOnlySpan<ulong> mask = GetMask(_structuralMasks, system);
            for (ushort component = 0; component < _structuralRouteByComponent.Length; component++)
            {
                if ((mask[component >> 6] & (1UL << (component & 63))) == 0 ||
                    _structuralRouteByComponent[component] != 0)
                    continue;
                unique[uniqueCount++] = new ComponentId(component);
                // Temporary nonzero marker; replaced with the compiled route ID below.
                _structuralRouteByComponent[component] = uint.MaxValue;
            }
        }
        if (uniqueCount == 0)
            return;
        var routeIds = new uint[uniqueCount];
        _world.PrepareStructuralComponentRoutes(unique.AsSpan(0, uniqueCount), routeIds);
        for (int index = 0; index < uniqueCount; index++)
            _structuralRouteByComponent[unique[index].Value] = routeIds[index];
    }

    internal void Initialize()
    {
        if (_initialized)
            return;
        EnsureSealed();
        int initialized = 0;
        try
        {
            for (; initialized < _nodeCount; initialized++)
                GetNode(initialized).Initialize(_facade);
            _initialized = true;
        }
        catch
        {
            for (int index = initialized - 1; index >= 0; index--)
            {
                try { GetNode(index).Shutdown(_facade); }
                catch { }
            }
            throw;
        }
    }

    internal void SetRunMode(SchedulerRunMode mode)
    {
        EnsureNotExecuting();
        if ((uint)mode > (uint)SchedulerRunMode.Parallel)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == SchedulerRunMode.Parallel && _options.BackgroundWorkerCount == 0)
            throw new InvalidOperationException("This world reserved no scheduler background workers.");
        _runMode = mode;
    }

    internal void SetEnabled(SystemId id, bool enabled)
    {
        EnsureNotExecuting();
        GetNode(id).SetEnabled(enabled);
    }

    internal void SetSchedule(SystemId id, int periodTicks, int startOffset)
    {
        EnsureNotExecuting();
        GetNode(id).SetSchedule(periodTicks, startOffset);
    }

    internal void Tick(double deltaSeconds)
    {
        EnsureInitialized();
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (_executing)
            throw new InvalidOperationException("Scheduler execution is not reentrant.");
        if (_tick == ulong.MaxValue)
            throw new InvalidOperationException("The scheduler tick counter reached its terminal value.");

        bool graphEntered = false;
        bool structuralGateEntered = false;
        bool executionBegun = false;
        try
        {
            // The intake epoch is consumed before systems see topology.
            long phaseStart = _options.ProfilingEnabled ? Stopwatch.GetTimestamp() : 0;
            if (_mainCommands.Count != 0)
                _mainCommands.Playback(_world);
            if (_options.ProfilingEnabled)
                _preBarrierTicks += Stopwatch.GetTimestamp() - phaseStart;
            BeginExecution(deltaSeconds);
            executionBegun = true;
            _structuralRecordingGate.EnterSystemGraph();
            structuralGateEntered = true;
            _world.BeginSystemGraphExecution();
            graphEntered = true;
            phaseStart = _options.ProfilingEnabled ? Stopwatch.GetTimestamp() : 0;
            for (int stage = 0; stage < (int)SystemStage.Count; stage++)
            {
                int maxWave = _stageMaximumWave[stage];
                for (int wave = 0; wave <= maxWave; wave++)
                    ExecuteWave((SystemStage)stage, wave, manualId: SystemId.Invalid);
            }
            if (_options.ProfilingEnabled)
                _graphTicks += Stopwatch.GetTimestamp() - phaseStart;
            _world.EndSystemGraphExecution();
            graphEntered = false;
            _structuralRecordingGate.ExitSystemGraph();
            structuralGateEntered = false;

            phaseStart = _options.ProfilingEnabled ? Stopwatch.GetTimestamp() : 0;
            MergeStructuralSegments();
            if (_options.ProfilingEnabled)
                _structuralMergeTicks += Stopwatch.GetTimestamp() - phaseStart;
            phaseStart = _options.ProfilingEnabled ? Stopwatch.GetTimestamp() : 0;
            if (_mainCommands.Count != 0)
                _mainCommands.Playback(_world);
            if (_options.ProfilingEnabled)
                _postBarrierTicks += Stopwatch.GetTimestamp() - phaseStart;
            _tick++;
            EndExecution();
        }
        catch (Exception exception)
        {
            if (graphEntered)
            {
                try { _world.EndSystemGraphExecution(); }
                catch { }
            }
            if (structuralGateEntered)
                _structuralRecordingGate.ExitSystemGraph();
            if (executionBegun)
            {
                DiscardStructuralLanes();
                EndExecution();
            }
            Exception fault = _executionFault ?? exception;
            _world.MarkFaulted(fault);
            throw fault;
        }
    }

    internal void RunManual(SystemId id, double deltaSeconds)
    {
        EnsureInitialized();
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        SystemNode node = GetNode(id);
        if (!node.Definition.Manual)
            throw new InvalidOperationException("Only a manual system can be invoked explicitly.");
        BeginExecution(deltaSeconds);
        bool graphEntered = false;
        bool structuralGateEntered = false;
        try
        {
            _structuralRecordingGate.EnterSystemGraph();
            structuralGateEntered = true;
            _world.BeginSystemGraphExecution();
            graphEntered = true;
            long phaseStart = _options.ProfilingEnabled ? Stopwatch.GetTimestamp() : 0;
            ExecuteWave(node.Definition.Stage, node.CompiledWave, id);
            if (_options.ProfilingEnabled)
                _graphTicks += Stopwatch.GetTimestamp() - phaseStart;
            _world.EndSystemGraphExecution();
            graphEntered = false;
            _structuralRecordingGate.ExitSystemGraph();
            structuralGateEntered = false;
            phaseStart = _options.ProfilingEnabled ? Stopwatch.GetTimestamp() : 0;
            MergeStructuralSegments();
            if (_options.ProfilingEnabled)
                _structuralMergeTicks += Stopwatch.GetTimestamp() - phaseStart;
            EndExecution();
        }
        catch (Exception exception)
        {
            if (graphEntered)
            {
                try { _world.EndSystemGraphExecution(); }
                catch { }
            }
            if (structuralGateEntered)
                _structuralRecordingGate.ExitSystemGraph();
            DiscardStructuralLanes();
            EndExecution();
            Exception fault = _executionFault ?? exception;
            _world.MarkFaulted(fault);
            throw fault;
        }
    }

    internal int CopySystemSnapshots(Span<SystemRuntimeSnapshot> destination)
    {
        EnsureNotExecuting();
        int count = Math.Min(destination.Length, _nodeCount);
        for (int index = 0; index < count; index++)
            destination[index] = GetNode(index).Snapshot();
        return count;
    }

    internal SchedulerRuntimeSnapshot GetSnapshot()
    {
        EnsureNotExecuting();
        return new SchedulerRuntimeSnapshot(
            _options.BackgroundWorkerCount,
            _options.CallerParticipates,
            _dispatchCount,
            _workers?.WakeCount ?? 0,
            _workers?.ParkCount ?? 0,
            _mergedSegments,
            _mergedCommands,
            _mergedPayloadBytes,
            _identityResolutions,
            ReservedBytes,
            Interlocked.Read(ref _workerManagedAllocationBytes),
            _preBarrierTicks,
            _graphTicks,
            _structuralMergeTicks,
            _postBarrierTicks);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryResolveEntity(in SystemJobContext context, uint localIndex, out Entity entity)
    {
        ValidateContext(context);
        // Identity reconstruction is an explicitly paid capability, but scheduler
        // telemetry must not add an atomic operation to that hot path when
        // profiling is disabled.  The profiling mode is deliberately allowed to
        // pay for exact cross-worker accounting.
        if (_options.ProfilingEnabled)
            Interlocked.Increment(ref _identityResolutions);
        return _world.TryGetEntity(localIndex, out entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Has<T>(in SystemJobContext context, Entity entity, in ComponentLookup<T> lookup)
        where T : unmanaged, IComponent<T>
    {
        ValidateLookupContext(context, lookup.Plan.ComponentId, write: false, lookup.Plan.WorldId);
        return _world.HasComponentPrepared(entity, lookup.Plan);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryRead<T>(in SystemJobContext context, Entity entity, in ComponentLookup<T> lookup, out T value)
        where T : unmanaged, IComponent<T>
    {
        ValidateLookupContext(context, lookup.Plan.ComponentId, write: false, lookup.Plan.WorldId);
        return _world.TryGetComponentPrepared(entity, lookup.Plan, out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryWrite<T>(in SystemJobContext context, Entity entity, in ComponentLookup<T> lookup, in T value)
        where T : unmanaged, IComponent<T>
    {
        SystemNode node = ValidateLookupContext(context, lookup.Plan.ComponentId, write: true, lookup.Plan.WorldId);
        return _world.TrySetComponentScheduled(entity, lookup.Plan, value, node.Publication.Acquire());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetEnabled<T>(in SystemJobContext context, Entity entity,
        in ComponentLookup<T> lookup, out bool enabled)
        where T : unmanaged, IComponent<T>
    {
        ValidateLookupContext(context, lookup.Plan.ComponentId, write: false, lookup.Plan.WorldId);
        return _world.TryGetComponentEnabledPrepared(entity, lookup.Plan, out enabled);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TrySetEnabled<T>(in SystemJobContext context, Entity entity,
        in ComponentLookup<T> lookup, bool enabled)
        where T : unmanaged, IComponent<T>
    {
        SystemNode node = ValidateLookupContext(context, lookup.Plan.ComponentId, write: true, lookup.Plan.WorldId);
        return _world.TrySetComponentEnabledScheduled(entity, lookup.Plan, enabled, node.Publication.Acquire());
    }

    internal bool TryEnqueueCreate(in SystemJobContext context, ArchetypeHandle archetype,
        out DeferredEntity entity)
    {
        StructuralCommandBuffer lane = GetStructuralLane(context);
        return lane.TryEnqueueCreateDefault(archetype, out entity);
    }

    internal bool TryEnqueueInstantiate(
        in SystemJobContext context,
        PrefabHandle prefab,
        out DeferredEntity entity)
    {
        StructuralCommandBuffer lane = GetStructuralLane(context);
        return lane.TryEnqueueInstantiate(prefab, out entity);
    }

    internal bool TryEnqueueDestroy(in SystemJobContext context, Entity entity) =>
        GetStructuralLane(context).TryEnqueueDestroy(entity);
    internal bool TryEnqueueDestroy(in SystemJobContext context, DeferredEntity entity) =>
        GetStructuralLaneForDeferred(context, entity).TryEnqueueDestroy(entity);
    internal bool TryEnqueueTransition(in SystemJobContext context, Entity entity,
        in ArchetypeTransition transition) =>
        GetStructuralLane(context).TryEnqueueTransition(entity, transition);
    internal bool TryEnqueueTransition(in SystemJobContext context, DeferredEntity entity,
        in ArchetypeTransition transition) =>
        GetStructuralLaneForDeferred(context, entity).TryEnqueueTransition(entity, transition);

    internal bool TryEnqueueAdd<T>(in SystemJobContext context, Entity entity, in T value)
        where T : unmanaged, IComponent<T>
    {
        StructuralCommandBuffer lane = GetStructuralLane(context);
        GetStructuralRoute<T>(context, out uint routeId, out int size, out int alignment);
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1));
        return lane.TryEnqueueAddComponent(entity, routeId, bytes[..size], alignment);
    }
    internal bool TryEnqueueAdd<T>(in SystemJobContext context, DeferredEntity entity, in T value)
        where T : unmanaged, IComponent<T>
    {
        StructuralCommandBuffer lane = GetStructuralLaneForDeferred(context, entity);
        GetStructuralRoute<T>(context, out uint routeId, out int size, out int alignment);
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1));
        return lane.TryEnqueueAddComponent(entity, routeId, bytes[..size], alignment);
    }
    internal bool TryEnqueueRemove<T>(in SystemJobContext context, Entity entity)
        where T : unmanaged, IComponent<T>
    {
        StructuralCommandBuffer lane = GetStructuralLane(context);
        GetStructuralRoute<T>(context, out uint routeId, out _, out _);
        return lane.TryEnqueueRemoveComponent(entity, routeId);
    }
    internal bool TryEnqueueRemove<T>(in SystemJobContext context, DeferredEntity entity)
        where T : unmanaged, IComponent<T>
    {
        StructuralCommandBuffer lane = GetStructuralLaneForDeferred(context, entity);
        GetStructuralRoute<T>(context, out uint routeId, out _, out _);
        return lane.TryEnqueueRemoveComponent(entity, routeId);
    }

    internal bool IsContextActive(in SystemJobContext context)
    {
        int participant = context.Participant;
        return _executing &&
            context.Epoch == unchecked((ulong)Volatile.Read(ref _executionEpoch)) &&
            unchecked((uint)participant) < unchecked((uint)_participantEpochs.Length) &&
            Volatile.Read(ref _participantEpochs[participant]) == _executionEpoch &&
            Volatile.Read(ref _participantSystems[participant]) == context.SystemId.Value &&
            Volatile.Read(ref _participantWorkOrdinals[participant]) == context.LogicalWorkOrdinal &&
            _participantThreadIds[participant] == Environment.CurrentManagedThreadId;
    }

    private void ExecuteWave(SystemStage stage, int wave, SystemId manualId)
    {
        int activeCount = 0;
        int workCount = 0;
        ChangeVersion candidate = _world.CurrentChangeVersion;
        for (int index = 0; index < _nodeCount; index++)
        {
            SystemNode node = GetNode(index);
            if (node.Definition.Stage != stage || node.CompiledWave != wave)
                continue;
            bool manual = manualId.IsValid;
            if (manual ? node.Id != manualId : node.Definition.Manual || !node.IsDue(_tick))
                continue;

            candidate = candidate.Next();
            node.Prepare(_tick, _deltaSeconds, manual, candidate);
            SchedulerQueryShape shape = node.GetShape();
            bool parallel = ShouldParallelize(node, shape);
            int desired = 1;
            if (parallel)
            {
                int usefulPartitions = Math.Max(1,
                    ((shape.Rows - 1) / node.Definition.MinimumRowsPerPartition) + 1);
                desired = Math.Min(
                    Math.Min(_options.MaximumPartitionsPerSystem, Math.Max(1, ExecutionConcurrency * 2)),
                    usefulPartitions);
            }
            int produced = node.BuildPartitions(
                shape,
                _partitionScratch.AsSpan(0, _workItems.Length - workCount),
                desired);
            if (produced > _workItems.Length - workCount)
                throw new InvalidOperationException("Scheduler work-item capacity is exhausted.");
            int fixedParticipant = parallel ? -1 : SelectSerialParticipant(activeCount);
            for (int partitionIndex = 0; partitionIndex < produced; partitionIndex++)
            {
                ref SchedulerWorkItem item = ref _workItems[workCount];
                item.Node = node;
                item.Partition = _partitionScratch[partitionIndex];
                if (_segmentCount + workCount >= _segments.Length)
                    throw new InvalidOperationException("Scheduler structural-segment capacity is exhausted.");
                item.LogicalOrdinal = _segmentCount + workCount;
                item.Participant = parallel
                    ? partitionIndex % ExecutionConcurrency
                    : fixedParticipant;
                workCount++;
            }
            _activeNodes[activeCount] = node;
            _activeWorkCounts[activeCount] = produced;
            _activeParallel[activeCount] = parallel;
            _activeStartTicks[activeCount] = _options.ProfilingEnabled ? Stopwatch.GetTimestamp() : 0;
            activeCount++;
        }

        if (workCount != 0)
        {
            _dispatchCount++;
            if (_options.CallerParticipates &&
                (_runMode == SchedulerRunMode.Serial ||
                 !RequiresBackgroundParticipant(_workItems, workCount)))
                ExecuteAssignedWork(_workItems, workCount, participant: 0);
            else if (_workers is not null)
                _workers.Dispatch(_workItems, workCount);
            else
                ExecuteAssignedWork(_workItems, workCount, participant: 0);
            if (_executionFault is not null)
                throw _executionFault;
        }

        _segmentCount += workCount;

        for (int activeIndex = 0; activeIndex < activeCount; activeIndex++)
        {
            SystemNode node = _activeNodes[activeIndex]
                ?? throw new InvalidOperationException("An active scheduler node is missing.");
            int itemCount = _activeWorkCounts[activeIndex];
            SchedulerQueryShape shape = node.GetShape();
            int completedRows = node.GetCompletedRows(shape);
            node.Complete(_tick, _deltaSeconds, _activeParallel[activeIndex], manualId.IsValid,
                completedRows, shape.Chunks, itemCount);
            if (node.Publication.WasPublished)
                _world.PublishSchedulerChangeVersion(node.Publication.Candidate);
            long elapsed = _options.ProfilingEnabled
                ? Stopwatch.GetTimestamp() - _activeStartTicks[activeIndex]
                : 0;
            node.RecordRun(_tick, elapsed, _activeParallel[activeIndex], completedRows, shape.Chunks, itemCount);
        }
    }

    private static bool RequiresBackgroundParticipant(SchedulerWorkItem[] items, int count)
    {
        for (int index = 0; index < count; index++)
            if (items[index].Participant != 0)
                return true;
        return false;
    }

    private void ExecuteAssignedWork(SchedulerWorkItem[] items, int count, int participant)
    {
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            for (int index = 0; index < count; index++)
            {
                if (_executionFault is not null)
                    return;
                ref SchedulerWorkItem item = ref items[index];
                if (item.Participant != participant)
                    continue;
                ExecuteWorkItem(ref item, participant);
            }
        }
        finally
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            if (allocated != 0)
                Interlocked.Add(ref _workerManagedAllocationBytes, allocated);
        }
    }

    private void ExecuteWorkItem(ref SchedulerWorkItem item, int participant)
    {
        SystemNode node = item.Node ?? throw new InvalidOperationException("A scheduler work item has no system node.");
        int startCommands = _structuralLanes[participant].SchedulerCount;
        Volatile.Write(ref _participantSegmentStarts[participant], startCommands);
        Volatile.Write(ref _participantEpochs[participant], _executionEpoch);
        Volatile.Write(ref _participantSystems[participant], node.Id.Value);
        Volatile.Write(ref _participantWorkOrdinals[participant], item.LogicalOrdinal);
        try
        {
            var context = new SystemJobContext(this, unchecked((ulong)_executionEpoch), node.Id,
                _tick, _deltaSeconds, participant, item.LogicalOrdinal);
            _ = node.ExecutePartition(participant, item.Partition, context);
            int commandCount = _structuralLanes[participant].SchedulerCount - startCommands;
            _segments[item.LogicalOrdinal] = new StructuralSegment(participant, startCommands, commandCount);
        }
        catch (Exception exception)
        {
            var contextual = exception as SystemExecutionException ?? new SystemExecutionException(
                $"System '{node.Definition.Key}' failed in work item {item.LogicalOrdinal}.",
                exception, node.Id, node.Definition.Key, node.Definition.Stage,
                item.LogicalOrdinal, participant);
            Interlocked.CompareExchange(ref _executionFault, contextual, null);
        }
        finally
        {
            Volatile.Write(ref _participantWorkOrdinals[participant], -1);
            Volatile.Write(ref _participantSystems[participant], -1);
            Volatile.Write(ref _participantEpochs[participant], 0);
            Volatile.Write(ref _participantSegmentStarts[participant], -1);
        }
    }

    private void BeginExecution(double deltaSeconds)
    {
        _executing = true;
        _deltaSeconds = deltaSeconds;
        _executionFault = null;
        _executionEpoch = _executionEpoch == long.MaxValue ? 1 : _executionEpoch + 1;
        Array.Clear(_segments);
        _segmentCount = 0;
        for (int index = 0; index < _structuralLanes.Length; index++)
            _structuralLanes[index].ClearSchedulerLane();
        if (_options.CallerParticipates)
            _participantThreadIds[0] = Environment.CurrentManagedThreadId;
    }

    private void EndExecution()
    {
        _executing = false;
        _deltaSeconds = 0d;
        _executionFault = null;
    }

    private void MergeStructuralSegments()
    {
        int commands = 0;
        int segments = 0;
        for (int index = 0; index < _segmentCount; index++)
        {
            StructuralSegment segment = _segments[index];
            if (segment.Count == 0)
                continue;
            segments++;
            commands += segment.Count;
        }
        if (commands == 0)
        {
            DiscardStructuralLanes();
            return;
        }

        // A previous playback leaves the canonical arena in a completed epoch.
        // Open its next recording epoch before payload offsets are preflighted
        // or copied; otherwise BeginRecordingIfNeeded in the first append would
        // clear the payload count after the bytes had already been installed.
        _mainCommands.BeginSchedulerMerge();
        int payloadStart = _mainCommands.SchedulerPayloadCount;
        int payloadEnd = payloadStart;
        for (int index = 0; index < _segmentCount; index++)
        {
            StructuralSegment segment = _segments[index];
            if (segment.Count == 0)
                continue;
            payloadEnd = _structuralLanes[segment.Lane].GetSchedulerMergedPayloadEnd(
                _world,
                segment.Start,
                segment.Count,
                payloadEnd);
        }
        if (!_mainCommands.CanAppendSchedulerCommands(commands))
            throw new StructuralCommandCapacityExceededException(_mainCommands.Capacity);
        if (!_mainCommands.CanAppendSchedulerPayloadEnd(payloadEnd))
            throw new StructuralCommandCapacityExceededException(_mainCommands.Capacity);
        for (int index = 0; index < _segmentCount; index++)
        {
            StructuralSegment segment = _segments[index];
            if (segment.Count == 0)
                continue;
            _structuralLanes[segment.Lane].CopySchedulerSegmentTo(
                _mainCommands,
                _world,
                segment.Start,
                segment.Count);
        }
        _mergedSegments += segments;
        _mergedCommands += commands;
        _mergedPayloadBytes += payloadEnd - payloadStart;
        DiscardStructuralLanes();
    }

    private void DiscardStructuralLanes()
    {
        for (int index = 0; index < _structuralLanes.Length; index++)
            _structuralLanes[index].ClearSchedulerLane();
    }

    private bool ShouldParallelize(SystemNode node, SchedulerQueryShape shape)
    {
        if (!node.SupportsParallel || _runMode != SchedulerRunMode.Parallel ||
            ExecutionConcurrency <= 1 || shape.Rows == 0)
            return false;
        ReadOnlySpan<ulong> lookupWrites = GetMask(_lookupWriteMasks, node.Id.Value);
        ReadOnlySpan<ulong> lookupReads = GetMask(_lookupReadMasks, node.Id.Value);
        ReadOnlySpan<ulong> allWrites = GetMask(_writeMasks, node.Id.Value);
        bool needsRandomOwnershipProof = !IsEmpty(lookupWrites) || Intersects(lookupReads, allWrites);
        if (needsRandomOwnershipProof &&
            node.Definition.ParallelRandomAccessPolicy != ParallelRandomAccessPolicy.PartitionDisjoint)
            return false;
        return node.Definition.ExecutionPolicy switch
        {
            SystemExecutionPolicy.Serial => false,
            SystemExecutionPolicy.Parallel => shape.Rows > 1,
            SystemExecutionPolicy.Auto => (node.IsRangeWork || shape.Chunks > 1) &&
                shape.Rows >= node.Definition.ParallelRowThreshold,
            _ => throw new InvalidOperationException("A system has an invalid execution policy."),
        };
    }

    private int SelectSerialParticipant(int activeOrdinal)
    {
        if (_runMode == SchedulerRunMode.Serial || ExecutionConcurrency == 1)
            return 0;
        return activeOrdinal % ExecutionConcurrency;
    }

    private void ValidateAndCompileGraph()
    {
        ApplyConfiguredPhaseStaggering();
        var compiled = new bool[_nodeCount];
        for (int stage = 0; stage < (int)SystemStage.Count; stage++)
        {
            int remaining = 0;
            for (int index = 0; index < _nodeCount; index++)
                if ((int)GetNode(index).Definition.Stage == stage)
                    remaining++;
            while (remaining != 0)
            {
                bool progressed = false;
                for (int index = 0; index < _nodeCount; index++)
                {
                    SystemNode node = GetNode(index);
                    if (compiled[index] || (int)node.Definition.Stage != stage)
                        continue;
                    ValidateDependencies(node, compiled, out bool ready);
                    if (!ready)
                        continue;
                    int wave = 0;
                    foreach (SystemId required in node.Definition.Requires)
                    {
                        SystemNode dependency = GetNode(required);
                        if (dependency.Definition.Stage == node.Definition.Stage)
                            wave = Math.Max(wave, dependency.CompiledWave + 1);
                    }
                    // A forward dependency can make a lower-registration node
                    // ready after a higher-registration conflicting node. Scan
                    // every already-compiled peer, not merely lower ordinals.
                    for (int previous = 0; previous < _nodeCount; previous++)
                    {
                        SystemNode earlier = GetNode(previous);
                        if (compiled[previous] && earlier.Definition.Stage == node.Definition.Stage &&
                            Conflicts(previous, index))
                            wave = Math.Max(wave, earlier.CompiledWave + 1);
                    }
                    node.CompiledWave = wave;
                    _stageMaximumWave[stage] = Math.Max(_stageMaximumWave[stage], wave);
                    compiled[index] = true;
                    remaining--;
                    progressed = true;
                }
                if (!progressed)
                    throw new InvalidOperationException($"System dependency cycle detected in stage {(SystemStage)stage}.");
            }
        }
    }

    private void ApplyConfiguredPhaseStaggering()
    {
        if (!_options.PhaseStaggeringEnabled || _options.MaximumStaggerPhases <= 1)
            return;
        for (int index = 0; index < _nodeCount; index++)
        {
            SystemNode node = GetNode(index);
            SystemSchedule schedule = node.Schedule;
            if (!node.Definition.AutomaticallyStagger || node.Definition.Manual ||
                schedule.PeriodTicks <= 1 || schedule.StartOffset != 0 ||
                node.Definition.Stage is SystemStage.Movement or SystemStage.Combat)
            {
                continue;
            }
            int phases = Math.Min(schedule.PeriodTicks, _options.MaximumStaggerPhases);
            uint hash = 2166136261;
            foreach (char character in node.Definition.Key)
                hash = unchecked((hash ^ character) * 16777619);
            node.ApplyAutomaticStagger(unchecked((int)(hash % unchecked((uint)phases))));
        }
    }

    private void ValidateDependencies(SystemNode node, bool[] compiled, out bool ready)
    {
        ready = true;
        for (int index = 0; index < node.Definition.Requires.Count; index++)
        {
            SystemId required = node.Definition.Requires[index];
            if (!required.IsValid || required.Value >= _nodeCount)
                throw new InvalidOperationException($"System '{node.Definition.Key}' has a missing dependency {required}.");
            if (required == node.Id)
                throw new InvalidOperationException($"System '{node.Definition.Key}' depends on itself.");
            for (int previous = 0; previous < index; previous++)
                if (node.Definition.Requires[previous] == required)
                    throw new InvalidOperationException($"System '{node.Definition.Key}' repeats dependency {required}.");
            SystemNode dependency = GetNode(required);
            if (dependency.Definition.Stage > node.Definition.Stage)
                throw new InvalidOperationException(
                    $"System '{node.Definition.Key}' requires later-stage system '{dependency.Definition.Key}'.");
            if (dependency.Definition.Stage == node.Definition.Stage && !compiled[required.Value])
                ready = false;
        }
    }

    private bool Conflicts(int first, int second)
    {
        ReadOnlySpan<ulong> firstRead = GetMask(_readMasks, first);
        ReadOnlySpan<ulong> firstWrite = GetMask(_writeMasks, first);
        ReadOnlySpan<ulong> secondRead = GetMask(_readMasks, second);
        ReadOnlySpan<ulong> secondWrite = GetMask(_writeMasks, second);
        for (int word = 0; word < _maskWords; word++)
        {
            if ((firstWrite[word] & (secondRead[word] | secondWrite[word])) != 0 ||
                (secondWrite[word] & firstRead[word]) != 0)
                return true;
        }
        return false;
    }

    private SystemNode ValidateLookupContext(in SystemJobContext context, ComponentId component,
        bool write, WorldId lookupWorld)
    {
        ValidateContext(context);
        if (lookupWorld != _world.Id)
            throw new ArgumentException("The component lookup belongs to a different world.");
        SystemNode node = GetNode(context.SystemId);
        ReadOnlySpan<ulong> mask = GetMask(
            write ? _lookupWriteMasks : _lookupReadMasks,
            node.Id.Value);
        int word = component.Value >> 6;
        ulong bit = 1UL << (component.Value & 63);
        bool declared = word < mask.Length && (mask[word] & bit) != 0;
        if (!declared && !write)
        {
            ReadOnlySpan<ulong> writes = GetMask(_lookupWriteMasks, node.Id.Value);
            declared = word < writes.Length && (writes[word] & bit) != 0;
        }
        if (!declared)
            throw new InvalidOperationException($"System '{node.Definition.Key}' used an undeclared component lookup.");
        return node;
    }

    private StructuralCommandBuffer GetStructuralLane(in SystemJobContext context)
    {
        ValidateContext(context);
        return _structuralLanes[context.Participant];
    }

    private StructuralCommandBuffer GetStructuralLaneForDeferred(
        in SystemJobContext context,
        DeferredEntity entity)
    {
        StructuralCommandBuffer lane = GetStructuralLane(context);
        int start = Volatile.Read(ref _participantSegmentStarts[context.Participant]);
        if (start < 0 || !lane.OwnsSchedulerDeferredInSegment(entity, start))
            throw new InvalidOperationException(
                "A scheduler deferred entity may only be consumed by the work segment that created it.");
        return lane;
    }

    private void GetStructuralRoute<T>(in SystemJobContext context,
        out uint routeId, out int size, out int alignment)
        where T : unmanaged, IComponent<T>
    {
        ValidateContext(context);
        if (!_world.Schema.TryGetComponentId<T>(out ComponentId component))
            throw new ArgumentException($"Component type {typeof(T)} is not registered in this world schema.");
        ReadOnlySpan<ulong> mask = GetMask(_structuralMasks, context.SystemId.Value);
        if ((mask[component.Value >> 6] & (1UL << (component.Value & 63))) == 0)
            throw new InvalidOperationException("The system did not declare this structural component capability.");
        routeId = _structuralRouteByComponent[component.Value];
        if (routeId is 0 or uint.MaxValue)
            throw new InvalidOperationException("The structural component route was not compiled at Seal.");
        _world.GetStructuralComponentPayloadLayout(routeId, out size, out alignment);
        if (size > Unsafe.SizeOf<T>())
            throw new InvalidOperationException("Structural payload metadata exceeds the generated component value size.");
    }

    private void ValidateContext(in SystemJobContext context)
    {
        if (!ReferenceEquals(context.GetScheduler(), this) || !IsContextActive(context))
            throw new InvalidOperationException("The scheduler worker capability is stale, foreign, or used outside its work item.");
    }

    private void ValidateDefinitionIdentity(SystemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        for (int index = 0; index < _nodeCount; index++)
        {
            SystemDefinition existing = GetNode(index).Definition;
            if (string.Equals(existing.Key, definition.Key, StringComparison.Ordinal))
                throw new ArgumentException($"Duplicate system key '{definition.Key}'.", nameof(definition));
            if (definition.PersistenceId != 0 && existing.PersistenceId == definition.PersistenceId)
                throw new ArgumentException($"Duplicate system persistence ID {definition.PersistenceId}.", nameof(definition));
        }
    }

    private void AddComponents(Span<ulong> mask, IReadOnlyList<ComponentId> components, string name)
    {
        for (int index = 0; index < components.Count; index++)
            AddComponent(mask, components[index], name);
    }

    private void AddComponents(Span<ulong> mask, ReadOnlySpan<ComponentId> components, string name)
    {
        for (int index = 0; index < components.Length; index++)
            AddComponent(mask, components[index], name);
    }

    private void AddComponent(Span<ulong> mask, ComponentId component, string name)
    {
        if (!component.IsValid || component.Value >= _world.Schema.ComponentCount)
            throw new ArgumentException("A system access component is not in this world schema.", name);
        int word = component.Value >> 6;
        mask[word] |= 1UL << (component.Value & 63);
    }

    private Span<ulong> GetMask(ulong[] masks, int systemIndex) =>
        masks.AsSpan(systemIndex * _maskWords, _maskWords);
    private static bool IsEmpty(ReadOnlySpan<ulong> mask)
    {
        for (int index = 0; index < mask.Length; index++)
            if (mask[index] != 0)
                return false;
        return true;
    }
    private static bool Intersects(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        System.Diagnostics.Debug.Assert(left.Length == right.Length);
        for (int index = 0; index < left.Length; index++)
            if ((left[index] & right[index]) != 0)
                return true;
        return false;
    }

    private SystemNode GetNode(SystemId id)
    {
        if (!id.IsValid || id.Value >= _nodeCount)
            throw new ArgumentOutOfRangeException(nameof(id));
        return GetNode(id.Value);
    }
    private SystemNode GetNode(int index) => _nodes[index]
        ?? throw new InvalidOperationException("A registered system slot is empty.");
    private void EnsureRegistrationOpen()
    {
        EnsureNotDisposed();
        if (_sealed)
            throw new InvalidOperationException("System registration is closed after Seal.");
    }
    private void EnsureSealed()
    {
        EnsureNotDisposed();
        if (!_sealed)
            throw new InvalidOperationException("The scheduler has not been sealed.");
    }
    private void EnsureInitialized()
    {
        EnsureSealed();
        if (!_initialized)
            throw new InvalidOperationException("The scheduler has not been initialized.");
    }
    private void EnsureNotExecuting()
    {
        EnsureNotDisposed();
        if (_executing)
            throw new InvalidOperationException("Scheduler controls cannot change during execution.");
    }
    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private long SumLaneReservations()
    {
        long bytes = 0;
        for (int index = 0; index < _structuralLanes.Length; index++)
            bytes = checked(bytes + _structuralLanes[index].ReservedBytes);
        return bytes;
    }
    private long SumNodeReservations()
    {
        long bytes = 0;
        for (int index = 0; index < _nodeCount; index++)
            bytes = checked(bytes + GetNode(index).ReservedPayloadBytes);
        return bytes;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Exception? shutdownFault = null;
        if (_initialized)
        {
            for (int index = _nodeCount - 1; index >= 0; index--)
            {
                try { GetNode(index).Shutdown(_facade); }
                catch (Exception exception) { shutdownFault ??= exception; }
            }
        }
        try { _workers?.Dispose(); }
        catch (Exception exception) { shutdownFault ??= exception; }
        for (int index = 0; index < _structuralLanes.Length; index++)
            _structuralLanes[index].Invalidate();
        _disposed = true;
        if (shutdownFault is not null)
            throw new InvalidOperationException("One or more ECS systems failed during shutdown.", shutdownFault);
    }

    private struct SchedulerWorkItem
    {
        internal SystemNode? Node;
        internal SchedulerChunkPartition Partition;
        internal int LogicalOrdinal;
        internal int Participant;
    }

    private readonly struct StructuralSegment
    {
        internal StructuralSegment(int lane, int start, int count) { Lane = lane; Start = start; Count = count; }
        internal int Lane { get; }
        internal int Start { get; }
        internal int Count { get; }
    }

    private sealed class PersistentWorkerRuntime : IDisposable
    {
        private readonly SystemScheduler _scheduler;
        private readonly Thread[] _threads;
        private readonly AutoResetEvent[] _events;
        private readonly ManualResetEvent _complete = new(false);
        private readonly bool _callerParticipates;
        private SchedulerWorkItem[]? _items;
        private int _count;
        private int _remaining;
        private int _completionPublished;
        private volatile bool _shutdown;
        private long _wakeCount;
        private long _parkCount;
        private int _startedCount;
        private bool _disposed;

        internal PersistentWorkerRuntime(SystemScheduler scheduler, int backgroundWorkers, bool callerParticipates)
        {
            _scheduler = scheduler;
            _callerParticipates = callerParticipates;
            _threads = new Thread[backgroundWorkers];
            _events = new AutoResetEvent[backgroundWorkers];
            for (int index = 0; index < backgroundWorkers; index++)
            {
                _events[index] = new AutoResetEvent(false);
                int captured = index;
                _threads[index] = new Thread(() => WorkerLoop(captured))
                {
                    IsBackground = true,
                    Name = $"UltraSim.ECS.Worker.{index}",
                };
            }
        }

        internal long WakeCount => Interlocked.Read(ref _wakeCount);
        internal long ParkCount => Interlocked.Read(ref _parkCount);

        internal void Start()
        {
            try
            {
                for (int index = 0; index < _threads.Length; index++)
                {
                    WorkerStartHookForTesting?.Invoke(index);
                    _threads[index].Start();
                    _startedCount++;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Dispatch(SchedulerWorkItem[] items, int count)
        {
            _items = items;
            _count = count;
            _remaining = _threads.Length;
            _complete.Reset();
            Volatile.Write(ref _completionPublished, 0);
            for (int index = 0; index < _events.Length; index++)
                _events[index].Set();
            if (_callerParticipates)
                _scheduler.ExecuteAssignedWork(items, count, participant: 0);
            if (_threads.Length != 0)
                WaitForCompletion();
            _items = null;
            _count = 0;
        }

        private void WaitForCompletion()
        {
            // Fixed spin-then-park avoids both an unconditional kernel transition
            // for short jobs and ManualResetEventSlim's lazy lock/wait allocations.
            for (int spin = 0; spin < 64; spin++)
            {
                if (Volatile.Read(ref _completionPublished) != 0)
                    return;
                Thread.SpinWait(64);
            }
            if (Volatile.Read(ref _completionPublished) == 0)
            {
                _complete.WaitOne();
                while (Volatile.Read(ref _completionPublished) == 0)
                    Thread.SpinWait(1);
            }
        }

        private void WorkerLoop(int workerIndex)
        {
            int participant = _callerParticipates ? workerIndex + 1 : workerIndex;
            _scheduler._participantThreadIds[participant] = Environment.CurrentManagedThreadId;
            while (true)
            {
                Interlocked.Increment(ref _parkCount);
                _events[workerIndex].WaitOne();
                if (_shutdown)
                    return;
                Interlocked.Increment(ref _wakeCount);
                SchedulerWorkItem[] items = _items
                    ?? throw new InvalidOperationException("A worker woke without a dispatch array.");
                _scheduler.ExecuteAssignedWork(items, _count, participant);
                if (Interlocked.Decrement(ref _remaining) == 0)
                {
                    _complete.Set();
                    Volatile.Write(ref _completionPublished, 1);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _shutdown = true;
            for (int index = 0; index < _startedCount; index++)
                _events[index].Set();
            for (int index = 0; index < _startedCount; index++)
                _threads[index].Join();
            for (int index = 0; index < _events.Length; index++)
                _events[index].Dispose();
            _complete.Dispose();
            _disposed = true;
        }
    }
}
