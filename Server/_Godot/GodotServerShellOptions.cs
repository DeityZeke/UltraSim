namespace UltraSim.Server.Godot;

public sealed record GodotServerShellOptions(
    string ProfileName,
    int TickMilliseconds = 16,
    int MaxCatchUpTicksPerPump = 8,
    bool SaveOnStop = false,
    bool EnableMovementWorld = true,
    int NetworkPort = 42042)
{
    public static GodotServerShellOptions Default { get; } = new("godot");
}
