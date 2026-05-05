using UltraSim.Server.Persistence;

namespace UltraSim.Server.Headless;

public sealed record HeadlessServerShellOptions(
    string ProfileName,
    int TickMilliseconds = 16,
    int MaxCatchUpTicksPerPump = 8,
    bool AutoStart = true,
    bool SaveOnStop = false,
    bool Quiet = false,
    TimeSpan? RunFor = null,
    TimeSpan IdleDelay = default,
    bool EnableMovementWorld = true,
    int NetworkPort = 42042,
    double SessionAuthTimeoutSeconds = 5.0,
    double SessionKeepAliveTimeoutSeconds = 10.0,
    int MaxAutoCreatedAccountsPerRemoteAddress = 1,
    string? RuntimeRootPath = null,
    ServerPersistenceBackupStorageMode BackupStorageMode = ServerPersistenceBackupStorageMode.Directory,
    int BackupZstdCompressionLevel = 3)
{
    public static HeadlessServerShellOptions Default { get; } =
        new("headless", IdleDelay: TimeSpan.FromMilliseconds(1));
}
