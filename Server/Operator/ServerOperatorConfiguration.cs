using System.Text.Json;

namespace UltraSim.Server.Operator;

/// <summary>Locally owned cold configuration; not Game world state or a live Control setting.</summary>
public sealed record ServerOperatorConfiguration(
    string SaveRoot, string BackupRoot, ServerOperatorPolicy Policy)
{
    public static ServerOperatorConfiguration LoadOrCreate(string identityDirectory)
    {
        string root = Path.GetFullPath(identityDirectory);
        Directory.CreateDirectory(root);
        RejectReparse(root);
        string path = Path.Combine(root, "operator-policy.json");
        if (File.Exists(path))
        {
            RejectReparse(path);
            if (new FileInfo(path).Length is <= 0 or > 4_096)
                throw new InvalidDataException("Operator policy file size is invalid.");
            ServerOperatorConfiguration configured = JsonSerializer.Deserialize<ServerOperatorConfiguration>(
                File.ReadAllBytes(path)) ?? throw new InvalidDataException("Operator policy is empty.");
            configured.Validate();
            return configured;
        }
        var initial = new ServerOperatorConfiguration(
            Path.Combine(root, "saves"), Path.Combine(root, "backups"), ServerOperatorPolicy.Default);
        initial.Validate();
        string staging = path + "." + Environment.ProcessId + ".tmp";
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, initial);
                stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(staging, path, overwrite: false);
            return initial;
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SaveRoot) || string.IsNullOrWhiteSpace(BackupRoot) ||
            !Path.IsPathFullyQualified(SaveRoot) || !Path.IsPathFullyQualified(BackupRoot))
            throw new InvalidDataException("Operator roots must be absolute paths.");
        Policy.Validate();
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Operator configuration path is a reparse point.");
    }
}
