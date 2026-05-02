using System.Globalization;
using System.Text.Json;

namespace UltraSim.Server.Accounting;

public sealed class JsonAccountingPersistenceStore : IAccountingPersistenceStore
{
    private const int IndexVersion = 1;
    private readonly string _rootDirectory;
    private readonly AccountingPersistenceOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonAccountingPersistenceStore(string runtimeRoot, AccountingPersistenceOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeRoot))
            throw new ArgumentException("Runtime root is required.", nameof(runtimeRoot));

        _rootDirectory = runtimeRoot;
        _options = options ?? new AccountingPersistenceOptions();
    }

    public AccountingPersistenceSnapshot Load()
    {
        string indexPath = IndexPath;
        if (!File.Exists(indexPath))
            return AccountingPersistenceSnapshot.Empty;

        AccountingUsernameIndexDocument index = ReadJson<AccountingUsernameIndexDocument>(indexPath);
        if (index.Version != IndexVersion)
            throw new InvalidOperationException($"Unsupported accounting index version '{index.Version}'.");

        var accounts = new List<ServerAccountRecord>(index.Usernames.Count);
        var characters = new List<ServerCharacterRecord>();
        foreach (ulong accountId in index.Usernames.Values.Distinct().OrderBy(static value => value))
        {
            string accountPath = GetAccountPath(accountId);
            if (!File.Exists(accountPath))
                throw new InvalidOperationException($"Accounting index references missing account file '{accountPath}'.");

            ServerAccountRecord account = ReadJson<ServerAccountRecord>(accountPath).Normalize();
            accounts.Add(account);

            string charactersDirectory = GetCharactersDirectory(accountId);
            if (!Directory.Exists(charactersDirectory))
                continue;

            foreach (string characterPath in Directory.EnumerateFiles(charactersDirectory, "*.json").OrderBy(static path => path))
            {
                ServerCharacterRecord character = ReadJson<ServerCharacterRecord>(characterPath).Normalize();
                characters.Add(character);
            }
        }

        return new AccountingPersistenceSnapshot(accounts, characters).Normalize();
    }

    public void Save(AccountingPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        AccountingPersistenceSnapshot normalized = snapshot.Normalize();
        Directory.CreateDirectory(AccountsRoot);

        foreach (ServerAccountRecord account in normalized.Accounts)
        {
            AtomicWriteJson(GetAccountPath(account.AccountId), account);
            string charactersDirectory = GetCharactersDirectory(account.AccountId);
            Directory.CreateDirectory(charactersDirectory);

            HashSet<string> expectedCharacterFiles = normalized.Characters
                .Where(character => character.AccountId == account.AccountId)
                .Select(character => GetCharacterPath(account.AccountId, character.CharacterId))
                .ToHashSet(StringComparer.Ordinal);

            foreach (ServerCharacterRecord character in normalized.Characters.Where(character => character.AccountId == account.AccountId))
            {
                AtomicWriteJson(GetCharacterPath(account.AccountId, character.CharacterId), character);
            }

            PruneDeletedCharacterFiles(charactersDirectory, expectedCharacterFiles);
        }

        var index = new AccountingUsernameIndexDocument(
            IndexVersion,
            normalized.Accounts.ToDictionary(
                static account => account.Username,
                static account => account.AccountId,
                StringComparer.OrdinalIgnoreCase));
        AtomicWriteJson(IndexPath, index);
    }

    private string AccountsRoot => Path.Combine(_rootDirectory, _options.AccountsDirectory);

    private string IndexPath => Path.Combine(AccountsRoot, "index.json");

    private string GetAccountPath(ulong accountId)
    {
        return Path.Combine(AccountsRoot, $"{FormatId(accountId)}.json");
    }

    private string GetCharactersDirectory(ulong accountId)
    {
        return Path.Combine(AccountsRoot, FormatId(accountId), "characters");
    }

    private string GetCharacterPath(ulong accountId, ulong characterId)
    {
        return Path.Combine(GetCharactersDirectory(accountId), $"{FormatId(characterId)}.json");
    }

    private void AtomicWriteJson<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(value, _jsonOptions));
        _ = ReadJson<T>(tempPath);

        if (File.Exists(path) && _options.ClampedBackupCount != 0)
            File.Copy(path, CreateBackupPath(path), overwrite: false);

        File.Move(tempPath, path, overwrite: true);
        PruneBackups(path);
    }

    private T ReadJson<T>(string path)
    {
        T? value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), _jsonOptions);
        return value ?? throw new InvalidOperationException($"JSON file '{path}' did not contain a {typeof(T).Name} value.");
    }

    private void PruneBackups(string path)
    {
        int backupCount = _options.ClampedBackupCount;
        if (backupCount < 0)
            return;

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        string fileName = Path.GetFileName(path);
        string[] backups = Directory.EnumerateFiles(directory, $"{fileName}.*.bak")
            .OrderByDescending(static value => value, StringComparer.Ordinal)
            .ToArray();
        for (int i = backupCount; i < backups.Length; i++)
            File.Delete(backups[i]);
    }

    private static void PruneDeletedCharacterFiles(string charactersDirectory, HashSet<string> expectedCharacterFiles)
    {
        foreach (string path in Directory.EnumerateFiles(charactersDirectory, "*.json"))
        {
            if (!expectedCharacterFiles.Contains(path))
                File.Delete(path);
        }
    }

    private static string CreateBackupPath(string path)
    {
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
        return $"{path}.{stamp}.bak";
    }

    private static string FormatId(ulong id)
    {
        return id.ToString("D20", CultureInfo.InvariantCulture);
    }

    private sealed record AccountingUsernameIndexDocument(
        int Version,
        Dictionary<string, ulong> Usernames);
}
