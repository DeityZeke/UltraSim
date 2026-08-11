using System.Text.Json;

namespace UltraSim.Client;

public sealed class ClientSavedServerListStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    public ClientSavedServerListStore(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Saved server list path is required.", nameof(path))
            : path;
    }

    public async ValueTask<ClientSavedServerList> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return new ClientSavedServerList();

        await using FileStream stream = File.OpenRead(_path);
        SavedServerListDocument? document = await JsonSerializer.DeserializeAsync<SavedServerListDocument>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (document is null)
            return new ClientSavedServerList();

        return new ClientSavedServerList(document.Favorites, document.History);
    }

    public async ValueTask SaveAsync(ClientSavedServerList list, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var document = new SavedServerListDocument
        {
            Favorites = list.Favorites.ToArray(),
            History = list.History.ToArray(),
        };

        await using FileStream stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private sealed class SavedServerListDocument
    {
        public ClientSavedServerEntry[] Favorites { get; init; } = [];
        public ClientSavedServerEntry[] History { get; init; } = [];
    }
}
