using System.Text.Json;

namespace UltraSim.Server;

public sealed class ServerSettingsConfigStore
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public ServerSettingsConfigDocument LoadOrEmpty(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            return ServerSettingsConfigDocument.Empty();

        ServerSettingsConfigDocument? document = JsonSerializer.Deserialize<ServerSettingsConfigDocument>(
            File.ReadAllText(path),
            s_jsonOptions);
        document ??= ServerSettingsConfigDocument.Empty();
        document.Normalize();
        return document;
    }

    public void Save(string path, ServerSettingsConfigDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        document.Normalize();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(document, s_jsonOptions));
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
            return;
        }

        File.Move(tempPath, path);
    }
}
