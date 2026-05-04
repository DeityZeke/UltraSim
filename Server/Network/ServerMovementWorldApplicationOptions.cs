using UltraSim.Server.Scripting;

namespace UltraSim.Server.Network;

public sealed record ServerMovementWorldApplicationOptions(
    string ProfileName,
    int Port = 42042,
    bool SaveOnStop = false,
    bool EnableGlobalMovementObserverState = true,
    bool EnableAsyncFlush = true,
    float MovementSpeed = 4.0f,
    double SessionAuthTimeoutSeconds = 5.0,
    double SessionKeepAliveTimeoutSeconds = 10.0,
    string CommandPrefix = "/",
    int MaxAutoCreatedAccountsPerRemoteAddress = 1,
    DefaultUltraSimScriptContext? ScriptContext = null,
    ServerAccessRuleRegistry? AccessRules = null,
    string? PersistenceRoot = null,
    string? PersistenceBackupsRoot = null)
{
    public static ServerMovementWorldApplicationOptions Default { get; } = new("movement-world");
}
