namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
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

    public SystemId RegisterRangeSystem<TSystem, TJob>(
        SystemDefinition definition,
        TSystem runtime)
        where TSystem : class, IRangeSystemRuntime<TJob>
        where TJob : struct, IRangeJob
    {
        EnsureOwnerThread();
        return GetSchedulerForRegistration().RegisterRangeSystem<TSystem, TJob>(definition, runtime);
    }

    public void Tick(double deltaSeconds)
    {
        EnsureOwnerThread();
        if (_core.State != EcsWorldState.Running)
            throw new InvalidOperationException("Tick requires a running ECS world.", _core.FaultException);
        RequireScheduler().Tick(deltaSeconds);
    }

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
