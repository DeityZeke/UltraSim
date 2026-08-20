using System.Text.Json;

namespace UltraSim.Client.Content;

public sealed class ContentCacheIndexStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ContentCacheIndexStore(string indexPath)
    {
        if (string.IsNullOrWhiteSpace(indexPath))
        {
            throw new ArgumentException("Cache index path is required.", nameof(indexPath));
        }

        IndexPath = Path.GetFullPath(indexPath);
    }

    public string IndexPath { get; }

    public ContentCacheIndex LoadOrCreate()
    {
        if (!File.Exists(IndexPath))
        {
            return new ContentCacheIndex();
        }

        string json = File.ReadAllText(IndexPath);
        if (!TryDeserialize(json, out ContentCacheIndex index, out string error))
        {
            throw new InvalidDataException(error);
        }

        return index;
    }

    public void Save(ContentCacheIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath) ?? ".");
        string json = Serialize(index);
        string tempPath = IndexPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(IndexPath))
        {
            File.Delete(IndexPath);
        }

        File.Move(tempPath, IndexPath);
    }

    public static string Serialize(ContentCacheIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        var document = new ContentCacheIndexDocument(CurrentVersion, index.SnapshotOrdered());
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static bool TryDeserialize(string? json, out ContentCacheIndex index, out string error)
    {
        index = new ContentCacheIndex();
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Cache index JSON is required.";
            return false;
        }

        ContentCacheIndexDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ContentCacheIndexDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"Cache index JSON is invalid: {ex.Message}";
            return false;
        }

        if (document is null)
        {
            error = "Cache index JSON is empty.";
            return false;
        }

        if (document.Version != CurrentVersion)
        {
            error = $"Unsupported cache index version {document.Version}.";
            return false;
        }

        index = new ContentCacheIndex();
        foreach (ContentCacheEntry entry in document.Entries ?? Array.Empty<ContentCacheEntry>())
        {
            if (!index.TryUpsert(entry, out error))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private sealed record ContentCacheIndexDocument(
        int Version,
        IReadOnlyList<ContentCacheEntry>? Entries);
}
