using UltraSim.ECS;
using UltraSim.ECS.Persistence;

namespace UltraSim.Server.Persistence;

public sealed class EcsWorldPersistenceParticipant : IServerPersistenceParticipant
{
    private readonly World _world;
    private readonly string _relativePath;

    public EcsWorldPersistenceParticipant(World world, string relativePath = "world/entities.bin")
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _relativePath = string.IsNullOrWhiteSpace(relativePath)
            ? throw new ArgumentException("World persistence relative path is required.", nameof(relativePath))
            : relativePath.Replace('\\', '/');
    }

    public string Name => "world";

    public ServerPersistenceArtifact Save(string saveSetRoot, ServerPersistenceMetadata metadata)
    {
        string path = Path.Combine(saveSetRoot, _relativePath);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var writer = new BinaryDataWriter(path);
        var adapter = new BinaryWorldPersistenceAdapter(new PersistenceTypeRegistry());
        adapter.Save(_world, writer);
        return new ServerPersistenceArtifact(Name, _relativePath);
    }
}
