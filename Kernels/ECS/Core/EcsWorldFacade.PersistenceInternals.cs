namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
    internal EcsWorld<NativeChunkArena> PersistenceCore => _core;
    internal StructuralCommandBuffer PersistenceStructuralCommands => _structuralCommands;
    internal void EnsurePersistenceOwnerRuntime() => EnsureRuntimeAccess();
    internal ulong PersistenceSchedulerTick => _scheduler?.TickCount ?? 0;
    internal PersistentSystemStateRecord[] CapturePersistenceSystemState(int maximumBytes) =>
        _scheduler?.CapturePersistentState(maximumBytes) ?? [];
    internal void RestorePersistenceSystemState(ulong tick, ReadOnlySpan<ParsedSystemState> states)
    {
        if (_scheduler is null)
        {
            if (states.Length != 0) throw new PersistenceCompatibilityException("Snapshot contains system state but staging world has no scheduler.");
            return;
        }
        _scheduler.RestorePersistentState(tick, states);
    }
}
