namespace UltraSim.Discovery;

[Flags]
public enum ServerCapabilityFlags : uint
{
    None = 0,
    DirectConnect = 1 << 0,
    PasswordRequired = 1 << 1,
    WhitelistOnly = 1 << 2,
    ServerDrivenUi = 1 << 3,
    ContentPatching = 1 << 4,
    GatewayAuth = 1 << 5,
    CharacterCreationAtJoin = 1 << 6,
    Mods = 1 << 7,
    RequiresLauncher = 1 << 8,
    Voice = 1 << 9,

    SupportsDirectConnect = DirectConnect,
    SupportsPassword = PasswordRequired,
    SupportsGatewayAuth = GatewayAuth,
    SupportsCharacterCreationAtJoin = CharacterCreationAtJoin,
    SupportsMods = Mods,
    SupportsVoice = Voice,
}
