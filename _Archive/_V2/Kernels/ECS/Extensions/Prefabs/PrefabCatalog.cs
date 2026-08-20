namespace UltraSim.ECS;

/// <summary>
/// Simple in-memory prefab catalog for resolver-backed spawn paths.
/// </summary>
public sealed class PrefabCatalog : IEcsPrefabResolver
{
    private readonly Dictionary<string, PrefabDefinition> _byKey = new(StringComparer.Ordinal);

    public int Count => _byKey.Count;

    public void Add(string key, PrefabDefinition prefab)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Prefab key is required.", nameof(key));

        _byKey[key] = prefab;
    }

    public void Add<TPrefab>(string key)
        where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        Add(key, PrefabDefinition.From<TPrefab>());
    }

    public bool TryResolvePrefab(string key, out PrefabDefinition prefab)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        return _byKey.TryGetValue(key, out prefab);
    }
}
