namespace UltraSim.ECS;

/// <summary>
/// Optional extension seams supplied when constructing a <see cref="World"/>.
/// </summary>
public sealed class EcsWorldServices
{
    /// <summary>
    /// Gets an optional prefab resolver used by higher-level prefab/import features.
    /// </summary>
    public IEcsPrefabResolver? PrefabResolver { get; init; }

    /// <summary>
    /// Gets an optional persistence adapter used by <see cref="World.Save"/> and <see cref="World.Load"/>.
    /// </summary>
    public IEcsPersistenceAdapter? PersistenceAdapter { get; init; }
}

public interface IEcsPrefabResolver
{
    bool TryResolvePrefab(string key, out PrefabDefinition prefab);
}

/// <summary>
/// Defines world-level persistence operations outside the ECS core.
/// </summary>
public interface IEcsPersistenceAdapter
{
    void Save(World world, IDataWriter writer);
    void Load(World world, IDataReader reader);
}

/// <summary>
/// Marker interface for persistence writers consumed by ECS extensions and systems.
/// </summary>
public interface IDataWriter
{
}

/// <summary>
/// Marker interface for persistence readers consumed by ECS extensions and systems.
/// </summary>
public interface IDataReader
{
}
