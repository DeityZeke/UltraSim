namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
    /// <summary>Gets whether this world reserved and owns a scheduler.</summary>
    public bool HasScheduler
    {
        get { EnsureOwnerThread(); return _scheduler is not null; }
    }

    public ulong TickCount
    {
        get { EnsureOwnerThread(); return RequireScheduler().TickCount; }
    }

    public int SystemCount
    {
        get { EnsureOwnerThread(); return RequireScheduler().Count; }
    }

    public SchedulerRunMode SchedulerRunMode
    {
        get { EnsureOwnerThread(); return RequireScheduler().RunMode; }
        set { EnsureOwnerThread(); RequireScheduler().SetRunMode(value); }
    }

    public long SchedulerReservedBytes
    {
        get { EnsureOwnerThread(); return _scheduler?.ReservedBytes ?? 0; }
    }

    /// <summary>Returns post-seal owned-payload categories without double-counting the main arena.</summary>
    public WorldReservedPayloadSnapshot GetReservedPayloadSnapshot()
    {
        EnsureOwnerThread();
        if (!IsSealed)
            throw new InvalidOperationException("Reserved payload observation requires a sealed world.");
        return _core.GetReservedPayloadSnapshot(
            _structuralCommands.ReservedBytes,
            _scheduler?.ReservedBytes ?? 0);
    }

    /// <summary>Registers bounded non-query range work on the scheduler worker runtime.</summary>
    /// <remarks>
    /// Use this for external arrays, spatial buckets, preselected entity lists,
    /// and other bounded index spaces. Do not scan the full entity population
    /// through <see cref="ComponentLookup{T}"/> when component composition can
    /// select the rows; use a generated chunk system instead.
    ///
    /// Supplemental component access must be declared by
    /// <see cref="SystemDefinition.AdditionalReads"/> and
    /// <see cref="SystemDefinition.AdditionalWrites"/>. Random mutation is serial
    /// unless the definition supplies the complete partition-disjoint proof.
    /// </remarks>
    /// <example>Process 512 targets already selected by a combat or spatial index.</example>
    public SystemId RegisterRangeSystem<TSystem, TJob>(
        SystemDefinition definition,
        TSystem runtime)
        where TSystem : class, IRangeSystemRuntime<TJob>
        where TJob : struct, IRangeJob
    {
        EnsureOwnerThread();
        return GetSchedulerForRegistration().RegisterRangeSystem<TSystem, TJob>(definition, runtime);
    }

    /// <summary>Executes one complete scheduled tick and its structural barriers.</summary>
    /// <remarks>
    /// The graph observes one stable topology. Scheduler-recorded structural
    /// commands merge deterministically and become visible at the post-graph
    /// barrier. Direct component writes are visible according to graph ordering.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The world is not running, has no scheduler, or faults.</exception>
    public void Tick(double deltaSeconds)
    {
        EnsureOwnerThread();
        if (_core.State != EcsWorldState.Running)
            throw new InvalidOperationException("Tick requires a running ECS world.", _core.FaultException);
        RequireScheduler().Tick(deltaSeconds);
    }

    /// <summary>Executes one system registered as manual through normal scheduler semantics.</summary>
    /// <remarks>
    /// Manual execution retains declared capabilities and deterministically merges
    /// recorded structural segments into the main arena. It does not play them
    /// back immediately; they become visible at the next explicit structural
    /// playback or the next scheduled tick's pre-barrier.
    /// </remarks>
    public void RunManual(SystemId systemId, double deltaSeconds)
    {
        EnsureOwnerThread();
        if (_core.State != EcsWorldState.Running)
            throw new InvalidOperationException("Manual system execution requires a running ECS world.", _core.FaultException);
        RequireScheduler().RunManual(systemId, deltaSeconds);
    }

    public void SetSystemEnabled(SystemId systemId, bool enabled)
    {
        EnsureOwnerThread();
        RequireScheduler().SetEnabled(systemId, enabled);
    }

    public void SetSystemSchedule(SystemId systemId, int periodTicks, int startOffset)
    {
        EnsureOwnerThread();
        RequireScheduler().SetSchedule(systemId, periodTicks, startOffset);
    }

    public int CopySystemRuntimeSnapshots(Span<SystemRuntimeSnapshot> destination)
    {
        EnsureOwnerThread();
        return RequireScheduler().CopySystemSnapshots(destination);
    }

    public SchedulerRuntimeSnapshot GetSchedulerRuntimeSnapshot()
    {
        EnsureOwnerThread();
        return RequireScheduler().GetSnapshot();
    }

    private SystemScheduler RequireScheduler() => _scheduler ??
        throw new InvalidOperationException("This world was constructed with scheduler reservations disabled.");
}
