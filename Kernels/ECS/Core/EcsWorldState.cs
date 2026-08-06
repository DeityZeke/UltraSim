namespace UltraSim.ECS;

/// <summary>Explicit lifecycle state of one ECS world instance.</summary>
public enum EcsWorldState : byte
{
    Created = 0,
    Sealed = 1,
    Initialized = 2,
    Running = 3,
    Faulted = 4,
    Disposed = 5,
}
