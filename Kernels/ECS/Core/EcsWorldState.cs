namespace UltraSim.ECS;

/// <summary>Current lifecycle states of the minimal composed ECS world.</summary>
internal enum EcsWorldState : byte
{
    Created = 0,
    Sealed = 1,
    Disposed = 2,
}
