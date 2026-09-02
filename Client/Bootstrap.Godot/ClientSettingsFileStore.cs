using System.Text;
using UltraSim.Client.Core;

namespace UltraSim.Client.Bootstrap.Godot;

public enum ClientSettingsLoadStatus : byte
{
    MissingDefaults = 1,
    Loaded = 2,
    InvalidFallback = 3,
}

public readonly record struct ClientSettingsLoadResult(
    ClientSettingsLoadStatus Status,
    ClientSettingsDocument Document,
    string? Error);

/// <summary>
/// Cold platform-local persistence for the human-readable user settings
/// document. Server recommendations are cached elsewhere by later remote work.
/// </summary>
public sealed class ClientSettingsFileStore
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _path;

    public ClientSettingsFileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = System.IO.Path.GetFullPath(path);
    }

    public string Path => _path;

    public ClientSettingsLoadResult Load()
    {
        if (!File.Exists(_path))
            return new ClientSettingsLoadResult(
                ClientSettingsLoadStatus.MissingDefaults,
                ClientSettingsDocument.Empty,
                null);
        try
        {
            string json = File.ReadAllText(_path, Utf8);
            if (ClientSettingsJsonCodec.TryRead(json, out ClientSettingsDocument document, out string? error))
                return new ClientSettingsLoadResult(ClientSettingsLoadStatus.Loaded, document, null);
            return new ClientSettingsLoadResult(
                ClientSettingsLoadStatus.InvalidFallback,
                ClientSettingsDocument.Empty,
                error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            DecoderFallbackException)
        {
            return new ClientSettingsLoadResult(
                ClientSettingsLoadStatus.InvalidFallback,
                ClientSettingsDocument.Empty,
                exception.Message);
        }
    }

    public void Save(ClientSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("The client settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = _path + ".tmp";
        try
        {
            File.WriteAllText(temporary, ClientSettingsJsonCodec.Write(document), Utf8);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
