namespace UltraSim.ECS;

public sealed class EcsWorldServices
{
    public IEcsPrefabResolver? PrefabResolver { get; init; }
    public IEcsPersistenceAdapter? PersistenceAdapter { get; init; }
}

public interface IEcsPrefabResolver
{
}

public interface IEcsPersistenceAdapter
{
    void Save(World world, IDataWriter writer);
    void Load(World world, IDataReader reader);
}

public interface IDataWriter
{
}

public interface IDataReader
{
}
