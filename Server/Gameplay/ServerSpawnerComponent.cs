using UltraSim.ECS;

namespace UltraSim.Server.Gameplay;

public readonly record struct ServerSpawnerComponent(
    ulong SpawnSetKeyHash,
    float Radius,
    int MaxActive,
    uint RespawnSeconds,
    ServerSpawnerFlags Flags) : IComponent<ServerSpawnerComponent>
{
    public static ServerSpawnerComponent Create(
        string spawnSetKey,
        float radius,
        int maxActive,
        uint respawnSeconds,
        ServerSpawnerFlags flags = ServerSpawnerFlags.Enabled | ServerSpawnerFlags.AdminVisible | ServerSpawnerFlags.SmartActivate)
    {
        if (!float.IsFinite(radius) || radius < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (maxActive < 0)
            throw new ArgumentOutOfRangeException(nameof(maxActive));

        return new ServerSpawnerComponent(
            ServerSpawnerKey.ComputeHash(spawnSetKey),
            radius,
            maxActive,
            respawnSeconds,
            flags);
    }

    public bool IsEnabled => (Flags & ServerSpawnerFlags.Enabled) != 0;

    public bool IsAdminVisible => (Flags & ServerSpawnerFlags.AdminVisible) != 0;

    public bool UsesSmartActivation => (Flags & ServerSpawnerFlags.SmartActivate) != 0;
}
