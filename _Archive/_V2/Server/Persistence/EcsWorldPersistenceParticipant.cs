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

    public void Load(string saveSetRoot, ServerPersistenceMetadata metadata, ServerPersistenceArtifact artifact)
    {
        if (!string.Equals(artifact.Name, Name, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected world artifact, got '{artifact.Name}'.");
        if (!string.Equals(artifact.RelativePath, _relativePath, StringComparison.Ordinal))
            throw new InvalidOperationException($"World artifact path '{artifact.RelativePath}' does not match expected path '{_relativePath}'.");

        string path = Path.Combine(saveSetRoot, artifact.RelativePath);
        using var reader = new BinaryDataReader(path);
        var adapter = new BinaryWorldPersistenceAdapter(new PersistenceTypeRegistry());
        adapter.Load(_world, reader);
    }
}
