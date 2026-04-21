namespace UltraSim.Server.Godot;

public sealed record GodotServerShellOptions(
    string ProfileName,
    int TickMilliseconds = 50,
    int MaxCatchUpTicksPerPump = 8,
    bool SaveOnStop = false)
{
    public static GodotServerShellOptions Default { get; } = new("godot");
}
