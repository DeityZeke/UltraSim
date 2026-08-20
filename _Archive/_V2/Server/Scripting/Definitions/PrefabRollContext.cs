namespace UltraSim.Server.Scripting;

public sealed class PrefabRollContext
{
    public PrefabRollContext(Random random, long spawnSequence = 0, string? prefabKey = null)
    {
        Random = random ?? throw new ArgumentNullException(nameof(random));
        SpawnSequence = spawnSequence;
        PrefabKey = prefabKey ?? string.Empty;
    }

    public Random Random { get; }
    public long SpawnSequence { get; }
    public string PrefabKey { get; }
}
