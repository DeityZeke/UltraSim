namespace UltraSim.Server;

public enum ServerCommandSource : byte
{
    Unknown = 0,
    ChatInput = 1,
    ServerConsole = 2,
    VisualWrapper = 3,
    Script = 4,
    System = 5,
}
