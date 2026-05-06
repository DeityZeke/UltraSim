using UltraSim.Server.Persistence;

namespace UltraSim.Server.Godot;

public sealed record GodotServerShellOptions(
    string ProfileName,
    int TickMilliseconds = 16,
    int MaxCatchUpTicksPerPump = 8,
    bool SaveOnStop = false,
    bool EnableMovementWorld = true,
    int NetworkPort = 42042,
    int MaxClients = 0,
    double SessionAuthTimeoutSeconds = 5.0,
    double SessionKeepAliveTimeoutSeconds = 10.0,
    string CommandPrefix = "/",
    int MaxAutoCreatedAccountsPerRemoteAddress = 1,
    string? RuntimeRootPath = null,
    ServerPersistenceBackupStorageMode BackupStorageMode = ServerPersistenceBackupStorageMode.Directory,
    int BackupCount = 3,
    int BackupZstdCompressionLevel = 3)
{
    public static GodotServerShellOptions Default { get; } = new("godot");
}
