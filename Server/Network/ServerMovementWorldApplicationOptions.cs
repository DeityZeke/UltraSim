using UltraSim.Server.Scripting;
using UltraSim.Server.Persistence;

namespace UltraSim.Server.Network;

public sealed record ServerMovementWorldApplicationOptions(
    string ProfileName,
    int Port = 42042,
    bool SaveOnStop = false,
    bool EnableGlobalMovementObserverState = true,
    bool EnableAsyncFlush = true,
    float MovementSpeed = 4.0f,
    int MaxClients = 0,
    double AutoSaveIntervalSeconds = 0.0,
    double SessionAuthTimeoutSeconds = 5.0,
    double SessionKeepAliveTimeoutSeconds = 10.0,
    string CommandPrefix = "/",
    int MaxAutoCreatedAccountsPerRemoteAddress = 1,
    DefaultUltraSimScriptContext? ScriptContext = null,
    ServerAccessRuleRegistry? AccessRules = null,
    IServerCommandAuditSink? CommandAudit = null,
    string? PersistenceRoot = null,
    string? PersistenceBackupsRoot = null,
    ServerPersistenceBackupStorageMode PersistenceBackupStorageMode = ServerPersistenceBackupStorageMode.Directory,
    int PersistenceBackupCount = 3,
    int PersistenceBackupZstdCompressionLevel = 3)
{
    public static ServerMovementWorldApplicationOptions Default { get; } = new("movement-world");
}
