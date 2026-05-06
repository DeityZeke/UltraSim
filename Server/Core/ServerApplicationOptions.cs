namespace UltraSim.Server;

public sealed record ServerApplicationOptions(
    string ProfileName,
    bool SaveOnStop = false,
    double AutoSaveIntervalSeconds = 0.0)
{
    public static ServerApplicationOptions Default { get; } = new("default");
}
