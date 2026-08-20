namespace UltraSim.Server;

public enum ServerApplicationState : byte
{
    Created = 0,
    Initializing = 1,
    Initialized = 2,
    Starting = 3,
    Loading = 4,
    Running = 5,
    Saving = 6,
    Stopping = 7,
    Stopped = 8,
    Restarting = 9,
    Faulted = 10,
}
