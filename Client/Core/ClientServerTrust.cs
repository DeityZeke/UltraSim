using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UltraSim.Client.Core;

public enum ClientServerTrustLoadStatus : byte
{
    Missing = 1,
    Loaded = 2,
    Invalid = 3,
}

public enum ClientServerTrustCommitStatus : byte
{
    Added = 1,
    Existing = 2,
    EndpointUpdated = 3,
    Conflict = 4,
    Invalid = 5,
}

/// <summary>Canonical SHA-256 fingerprint of one deployment signing key.</summary>
public readonly record struct ClientDeploymentKeyFingerprint
{
    private ClientDeploymentKeyFingerprint(string value) => Value = value;

    public string? Value { get; }
    public bool IsValid => IsCanonical(Value);

    public static ClientDeploymentKeyFingerprint Compute(ReadOnlySpan<byte> publicKey)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(publicKey, hash);
        return new ClientDeploymentKeyFingerprint(Convert.ToHexString(hash).ToLowerInvariant());
    }

    public static bool TryParse(string? value, out ClientDeploymentKeyFingerprint fingerprint)
    {
        fingerprint = default;
        if (value is null) return false;
        Span<char> canonical = stackalloc char[SHA256.HashSizeInBytes * 2];
        int written = 0;
        foreach (char character in value.Trim())
        {
            if (character is ':' or '-' or ' ') continue;
            if (!char.IsAsciiHexDigit(character) || written == canonical.Length)
                return false;
            canonical[written++] = char.ToLowerInvariant(character);
        }
        if (written != canonical.Length) return false;
        fingerprint = new ClientDeploymentKeyFingerprint(new string(canonical));
        return true;
    }

    public override string ToString() => Value ?? string.Empty;

    private static bool IsCanonical(string? value)
    {
        if (value is null || value.Length != SHA256.HashSizeInBytes * 2) return false;
        foreach (char character in value)
            if (!(character is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }
}

/// <summary>
/// Durable client trust for one verified application realm. Endpoint is only the
/// most recently authenticated location hint; the deployment key is the trust anchor.
/// </summary>
public sealed class ClientTrustedDeployment
{
    public const int MaximumEndpointCharacters = 64;
    private readonly byte[] _publicKey;

    internal ClientTrustedDeployment(
        UltraSim.Game.Application.ApplicationRealmId realm,
        string endpoint,
        ReadOnlySpan<byte> publicKey)
    {
        Realm = realm;
        Endpoint = endpoint;
        _publicKey = publicKey.ToArray();
        Fingerprint = ClientDeploymentKeyFingerprint.Compute(publicKey);
    }

    public UltraSim.Game.Application.ApplicationRealmId Realm { get; }
    public string Endpoint { get; }
    public ClientDeploymentKeyFingerprint Fingerprint { get; }
    public int PublicKeyBytes => _publicKey.Length;

    public bool TryCopyPublicKey(Span<byte> destination, out int written)
    {
        written = 0;
        if (destination.Length < _publicKey.Length) return false;
        _publicKey.CopyTo(destination);
        written = _publicKey.Length;
        return true;
    }

    internal bool KeyEquals(ReadOnlySpan<byte> candidate) =>
        candidate.Length == _publicKey.Length &&
        CryptographicOperations.FixedTimeEquals(candidate, _publicKey);
}

/// <summary>Bounded in-memory trust registry committed only after realm authentication.</summary>
public sealed class ClientServerTrustRegistry
{
    public const int MaximumRecords = 128;
    private readonly List<ClientTrustedDeployment> _records;

    public ClientServerTrustRegistry() => _records = new List<ClientTrustedDeployment>();

    private ClientServerTrustRegistry(List<ClientTrustedDeployment> records) => _records = records;

    public int Count => _records.Count;
    public IReadOnlyList<ClientTrustedDeployment> Records => _records;

    public bool TryFindByEndpoint(string endpoint, out ClientTrustedDeployment? deployment)
    {
        deployment = null;
        if (!IsEndpoint(endpoint)) return false;
        for (int index = 0; index < _records.Count; index++)
            if (string.Equals(_records[index].Endpoint, endpoint, StringComparison.Ordinal))
            {
                deployment = _records[index];
                return true;
            }
        return false;
    }

    public bool TryFindByRealm(
        UltraSim.Game.Application.ApplicationRealmId realm,
        out ClientTrustedDeployment? deployment)
    {
        deployment = null;
        if (!realm.IsValid) return false;
        for (int index = 0; index < _records.Count; index++)
            if (_records[index].Realm == realm)
            {
                deployment = _records[index];
                return true;
            }
        return false;
    }

    public ClientServerTrustCommitStatus TryCommitAuthenticated(
        UltraSim.Game.Application.ApplicationRealmId realm,
        string endpoint,
        ReadOnlySpan<byte> publicKey)
    {
        if (!realm.IsValid || !IsEndpoint(endpoint) || !IsCanonicalP256Key(publicKey))
            return ClientServerTrustCommitStatus.Invalid;
        int realmIndex = -1;
        for (int index = 0; index < _records.Count; index++)
        {
            ClientTrustedDeployment current = _records[index];
            if (string.Equals(current.Endpoint, endpoint, StringComparison.Ordinal) &&
                (current.Realm != realm || !current.KeyEquals(publicKey)))
                return ClientServerTrustCommitStatus.Conflict;
            if (current.Realm == realm) realmIndex = index;
        }
        if (realmIndex >= 0)
        {
            ClientTrustedDeployment current = _records[realmIndex];
            if (!current.KeyEquals(publicKey)) return ClientServerTrustCommitStatus.Conflict;
            if (string.Equals(current.Endpoint, endpoint, StringComparison.Ordinal))
                return ClientServerTrustCommitStatus.Existing;
            _records[realmIndex] = new ClientTrustedDeployment(realm, endpoint, publicKey);
            return ClientServerTrustCommitStatus.EndpointUpdated;
        }
        if (_records.Count == MaximumRecords)
            return ClientServerTrustCommitStatus.Invalid;
        _records.Add(new ClientTrustedDeployment(realm, endpoint, publicKey));
        _records.Sort(static (left, right) => left.Realm.CompareTo(right.Realm));
        return ClientServerTrustCommitStatus.Added;
    }

    /// <summary>
    /// Explicit trust replacement begins by forgetting the old record. It is never
    /// performed implicitly as part of mismatch handling.
    /// </summary>
    public bool TryForgetEndpoint(string endpoint)
    {
        for (int index = 0; index < _records.Count; index++)
            if (string.Equals(_records[index].Endpoint, endpoint, StringComparison.Ordinal))
            {
                _records.RemoveAt(index);
                return true;
            }
        return false;
    }

    internal static bool TryCreate(
        IEnumerable<(UltraSim.Game.Application.ApplicationRealmId Realm,
            string Endpoint, byte[] PublicKey)> source,
        out ClientServerTrustRegistry registry)
    {
        registry = new ClientServerTrustRegistry();
        foreach ((UltraSim.Game.Application.ApplicationRealmId realm,
                     string endpoint, byte[] publicKey) in source)
        {
            ClientServerTrustCommitStatus status = registry.TryCommitAuthenticated(
                realm, endpoint, publicKey);
            if (status is not ClientServerTrustCommitStatus.Added)
            {
                registry = new ClientServerTrustRegistry();
                return false;
            }
        }
        return true;
    }

    private static bool IsEndpoint(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > ClientTrustedDeployment.MaximumEndpointCharacters)
            return false;
        foreach (char character in value)
            if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or ':' or '-'))
                return false;
        return true;
    }

    private static bool IsCanonicalP256Key(ReadOnlySpan<byte> value)
    {
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(value, out int read);
            return read == value.Length && key.KeySize == 256;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

public readonly record struct ClientServerTrustLoadResult(
    ClientServerTrustLoadStatus Status,
    ClientServerTrustRegistry Registry,
    string? Error);

/// <summary>Strict, atomic local persistence for verified deployment/realm trust.</summary>
public sealed class ClientServerTrustFileStore
{
    private const int CurrentVersion = 1;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _path;

    public ClientServerTrustFileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = System.IO.Path.GetFullPath(path);
    }

    public string Path => _path;

    public ClientServerTrustLoadResult Load()
    {
        if (!File.Exists(_path))
            return new(ClientServerTrustLoadStatus.Missing, new ClientServerTrustRegistry(), null);
        try
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(_path, Utf8));
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("version", out JsonElement version) ||
                version.GetInt32() != CurrentVersion ||
                !root.TryGetProperty("deployments", out JsonElement deployments) ||
                deployments.ValueKind != JsonValueKind.Array ||
                deployments.GetArrayLength() > ClientServerTrustRegistry.MaximumRecords)
                throw new InvalidDataException("The server trust document envelope is invalid.");
            var records = new List<(UltraSim.Game.Application.ApplicationRealmId, string, byte[])>();
            foreach (JsonElement item in deployments.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 4 ||
                    !item.TryGetProperty("realm", out JsonElement realmText) ||
                    realmText.GetString() is not string encodedRealm ||
                    !UltraSim.Game.Application.ApplicationRealmId.TryParse(encodedRealm, out var realm) ||
                    !string.Equals(encodedRealm, realm.ToString(), StringComparison.Ordinal) ||
                    !item.TryGetProperty("endpoint", out JsonElement endpointText) ||
                    endpointText.GetString() is not string endpoint ||
                    !item.TryGetProperty("publicKey", out JsonElement keyText) ||
                    keyText.GetString() is not string encodedKey ||
                    !item.TryGetProperty("fingerprint", out JsonElement fingerprintText) ||
                    fingerprintText.GetString() is not string encodedFingerprint ||
                    !ClientDeploymentKeyFingerprint.TryParse(encodedFingerprint, out var fingerprint) ||
                    !string.Equals(encodedFingerprint, fingerprint.ToString(), StringComparison.Ordinal))
                    throw new InvalidDataException("A server trust record is invalid.");
                byte[] key = Convert.FromBase64String(encodedKey);
                if (!string.Equals(Convert.ToBase64String(key), encodedKey,
                        StringComparison.Ordinal) ||
                    ClientDeploymentKeyFingerprint.Compute(key) != fingerprint)
                    throw new InvalidDataException("A server trust fingerprint does not match its key.");
                records.Add((realm, endpoint, key));
            }
            if (!ClientServerTrustRegistry.TryCreate(records, out ClientServerTrustRegistry registry))
                throw new InvalidDataException("Server trust records conflict or exceed their bound.");
            return new(ClientServerTrustLoadStatus.Loaded, registry, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            DecoderFallbackException or JsonException or FormatException or InvalidDataException or
            InvalidOperationException)
        {
            return new(ClientServerTrustLoadStatus.Invalid,
                new ClientServerTrustRegistry(), exception.Message);
        }
    }

    public void Save(ClientServerTrustRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("The server trust path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = _path + ".tmp";
        try
        {
            using (FileStream stream = File.Create(temporary))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", CurrentVersion);
                writer.WriteStartArray("deployments");
                foreach (ClientTrustedDeployment deployment in registry.Records)
                {
                    byte[] key = new byte[deployment.PublicKeyBytes];
                    if (!deployment.TryCopyPublicKey(key, out int written) || written != key.Length)
                        throw new InvalidOperationException("A deployment key could not be copied.");
                    writer.WriteStartObject();
                    writer.WriteString("realm", deployment.Realm.ToString());
                    writer.WriteString("endpoint", deployment.Endpoint);
                    writer.WriteString("publicKey", Convert.ToBase64String(key));
                    writer.WriteString("fingerprint", deployment.Fingerprint.ToString());
                    writer.WriteEndObject();
                    CryptographicOperations.ZeroMemory(key);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
