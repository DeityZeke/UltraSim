using UltraSim.ECS;

namespace UltraSim.Server.Gameplay;

public readonly record struct ServerSpawnerRuntimeState(
    int ActiveCount,
    ulong LastSpawnTick,
    ulong NextSpawnTick) : IComponent<ServerSpawnerRuntimeState>
{
    public static ServerSpawnerRuntimeState Empty => new(0, 0, 0);
}
