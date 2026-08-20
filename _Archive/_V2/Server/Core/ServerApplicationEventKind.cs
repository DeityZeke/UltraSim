namespace UltraSim.Server;

public enum ServerApplicationEventKind : byte
{
    StateChanged = 0,
    Faulted = 1,
    Log = 2,
    SaveRequested = 3,
    SaveCompleted = 4,
    CommandHandled = 5,
}
