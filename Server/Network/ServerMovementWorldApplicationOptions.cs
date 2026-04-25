namespace UltraSim.Server.Network;

public sealed record ServerMovementWorldApplicationOptions(
    string ProfileName,
    int Port = 42042,
    bool SaveOnStop = false,
    bool EnableGlobalMovementObserverState = true,
    bool EnableAsyncFlush = true)
{
    public static ServerMovementWorldApplicationOptions Default { get; } = new("movement-world");
}
