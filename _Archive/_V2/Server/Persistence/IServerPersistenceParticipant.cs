namespace UltraSim.Server.Persistence;

public interface IServerPersistenceParticipant
{
    string Name { get; }

    ServerPersistenceArtifact Save(string saveSetRoot, ServerPersistenceMetadata metadata);

    void Load(string saveSetRoot, ServerPersistenceMetadata metadata, ServerPersistenceArtifact artifact);
}
