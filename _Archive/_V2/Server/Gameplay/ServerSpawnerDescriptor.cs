namespace UltraSim.Server.Gameplay;

public sealed record ServerSpawnerDescriptor
{
    public ServerSpawnerDescriptor(
        string spawnSetKey,
        string displayName,
        string description = "",
        bool adminVisible = true)
    {
        SpawnSetKey = ServerSpawnerKey.Normalize(spawnSetKey);
        SpawnSetKeyHash = ServerSpawnerKey.ComputeHash(SpawnSetKey);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? SpawnSetKey : displayName.Trim();
        Description = description?.Trim() ?? string.Empty;
        AdminVisible = adminVisible;
    }

    public string SpawnSetKey { get; }

    public ulong SpawnSetKeyHash { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public bool AdminVisible { get; }
}
