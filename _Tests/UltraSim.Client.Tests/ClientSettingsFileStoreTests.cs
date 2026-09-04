using UltraSim.Client.Core;

static class ClientSettingsFileStoreTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ultrasim-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(root, "settings", "client.json");
        try
        {
            var store = new ClientSettingsFileStore(path);
            ClientSettingsLoadResult missing = store.Load();
            Equal(ClientSettingsLoadStatus.MissingDefaults, missing.Status);

            ClientPhysicalInput.TryCreate(
                ClientPhysicalInputKind.Keyboard, "Q", ClientInputModifiers.None,
                out ClientPhysicalInput input);
            var document = new ClientSettingsDocument(new ClientSettingsLayer(
                uiScale: 1.35f,
                bindings: [new ClientPhysicalBinding(ClientSemanticAction.Interact, input)],
                renderDistanceChunks: 2));
            store.Save(document);
            True(File.Exists(path));
            True(!File.Exists(path + ".tmp"));
            string readable = File.ReadAllText(path);
            True(readable.Contains("\"uiScale\": 1.35", StringComparison.Ordinal));
            True(readable.Contains("\"Interact\"", StringComparison.Ordinal));
            True(readable.Contains("\"renderDistanceChunks\": 2", StringComparison.Ordinal));
            ClientSettingsLoadResult loaded = store.Load();
            Equal(ClientSettingsLoadStatus.Loaded, loaded.Status);
            Equal(1.35f, ClientSettingsResolver.Resolve(loaded.Document).UiScale);
            Equal(2, ClientSettingsResolver.Resolve(loaded.Document).RenderDistanceChunks);

            File.WriteAllText(
                path,
                readable.Replace("\"code\": \"Q\"", "\"code\": \"ESCAPE\"", StringComparison.Ordinal));
            ClientSettingsLoadResult conflicting = store.Load();
            Equal(ClientSettingsLoadStatus.InvalidFallback, conflicting.Status);
            Equal(1f, ClientSettingsResolver.Resolve(conflicting.Document).UiScale);
            True(!string.IsNullOrWhiteSpace(conflicting.Error));

            File.WriteAllText(path, "{ invalid settings");
            ClientSettingsLoadResult invalid = store.Load();
            Equal(ClientSettingsLoadStatus.InvalidFallback, invalid.Status);
            Equal(1f, ClientSettingsResolver.Resolve(invalid.Document).UiScale);
            True(!string.IsNullOrWhiteSpace(invalid.Error));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
