namespace UltraSim.Server;

public enum ServerControlCommandKind : byte
{
    Start = 0,
    Stop = 1,
    Restart = 2,
    Save = 3,
    Status = 4,
    Shutdown = 5,
}
