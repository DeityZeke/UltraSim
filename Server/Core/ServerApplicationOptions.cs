namespace UltraSim.Server;

public sealed record ServerApplicationOptions(
    string ProfileName,
    bool SaveOnStop = false)
{
    public static ServerApplicationOptions Default { get; } = new("default");
}
