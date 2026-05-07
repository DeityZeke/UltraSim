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
    string BindAddress = "0.0.0.0",
    int MaxClients = 0,
    double AutoSaveIntervalSeconds = 0.0,
    double SessionAuthTimeoutSeconds = 5.0,
    double SessionKeepAliveTimeoutSeconds = 10.0,
    string CommandPrefix = "/",
    int MaxAutoCreatedAccountsPerRemoteAddress = 1,
    string? RuntimeRootPath = null,
    ServerPersistenceBackupStorageMode BackupStorageMode = ServerPersistenceBackupStorageMode.Directory,
    int BackupCount = 3,
    int BackupZstdCompressionLevel = 3,
    bool DiscoveryEnabled = false,
    string DiscoveryRegistryUrl = "http://127.0.0.1:5000",
    string DiscoveryPublishMode = "unverified",
    string DiscoveryAdvertisedHost = "127.0.0.1",
    string DiscoveryServerId = "",
    string DiscoveryDescription = "",
    string DiscoveryRegion = "",
    string DiscoveryLanguage = "en-US",
    string DiscoveryTags = "",
    double DiscoveryHeartbeatIntervalSeconds = 15.0,
    bool DiscoveryUnpublishOnStop = true,
    string DiscoveryStatusEndpointUrl = "")
{
    public static HeadlessServerShellOptions Default { get; } =
        new("headless", IdleDelay: TimeSpan.FromMilliseconds(1));
}
