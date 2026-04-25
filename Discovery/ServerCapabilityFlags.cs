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
}
