namespace UltraSim.ECS;

[Flags]
public enum ComponentFlags : byte
{
    None = 0,
    Tag = 1 << 0,
    Enableable = 1 << 1,
    Persistent = 1 << 2,
    PersistEnableState = 1 << 3,
}
