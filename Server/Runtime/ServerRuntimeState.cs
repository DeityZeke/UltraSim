namespace UltraSim.Server.Runtime;

/// <summary>Lifecycle state of one authoritative server runtime.</summary>
public enum ServerRuntimeState : byte
{
    Created = 0,
    Initialized = 1,
    Running = 2,
    Stopped = 3,
    Faulted = 4,
    Disposed = 5,
}
