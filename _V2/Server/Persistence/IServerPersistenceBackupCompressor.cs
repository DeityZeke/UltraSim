namespace UltraSim.Server.Persistence;

public interface IServerPersistenceBackupCompressor
{
    string FileExtension { get; }

    void CompressDirectory(string sourceDirectory, string archivePath);

    void ExtractToDirectory(string archivePath, string destinationDirectory);
}
