namespace UltraSim.Server.Gameplay;

[Flags]
public enum ServerSpawnerFlags : uint
{
    None = 0,
    Enabled = 1 << 0,
    AdminVisible = 1 << 1,
    SmartActivate = 1 << 2,
}
