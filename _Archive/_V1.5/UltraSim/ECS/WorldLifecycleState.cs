namespace UltraSim.ECS
{
    public enum WorldLifecycleState : byte
    {
        Created = 0,
        Sealed = 1,
        Initialized = 2,
        Running = 3,
        Shutdown = 4,
    }
}
