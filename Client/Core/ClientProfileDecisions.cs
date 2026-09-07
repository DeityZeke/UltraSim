using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UltraSim.Game.Application;

namespace UltraSim.Client.Core;

/// <summary>Exact canonical identity of one declarative server client profile.</summary>
public readonly record struct ClientProfileHash(ulong Part0, ulong Part1, ulong Part2, ulong Part3)
{
    public const int Bytes = 32;

    public bool IsValid => Part0 != 0 || Part1 != 0 || Part2 != 0 || Part3 != 0;

    public static ClientProfileHash FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Bytes) throw new ArgumentOutOfRangeException(nameof(bytes));
        return new ClientProfileHash(
            BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]));
    }

    public static bool TryParse(string? value, out ClientProfileHash hash)
    {
        hash = default;
        if (value?.Length != Bytes * 2) return false;
        byte[] bytes;
        try { bytes = Convert.FromHexString(value); }
        catch (FormatException) { return false; }
        if (bytes.Length != Bytes) return false;
        hash = FromBytes(bytes);
        return hash.IsValid;
    }

    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[Bytes];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, Part0);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], Part1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], Part2);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], Part3);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public readonly record struct ClientProfileDecision(
    ApplicationRealmId Realm,
    ulong ProfileRevision,
    ClientProfileHash ProfileHash,
    ClientProfileApplicationMode Mode)
{
    public bool IsValid => Realm.IsValid && ProfileRevision != 0 && ProfileHash.IsValid &&
        Mode is ClientProfileApplicationMode.UseServerDefaults or
            ClientProfileApplicationMode.KeepMyControls or ClientProfileApplicationMode.Customize;
}

/// <summary>
/// Durable local consent decisions only. Advertised recommendations remain transient and user
/// settings remain in the independent readable settings document.
/// </summary>
public sealed class ClientProfileDecisionDocument
{
    public const int MaximumRealms = 128;
    private readonly ReadOnlyCollection<ClientProfileDecision> _decisions;

    public ClientProfileDecisionDocument(IEnumerable<ClientProfileDecision>? decisions = null)
    {
        ClientProfileDecision[] copy = (decisions ?? [])
            .OrderBy(static value => value.Realm.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (copy.Length > MaximumRealms)
            throw new ArgumentOutOfRangeException(nameof(decisions));
        for (int index = 0; index < copy.Length; index++)
        {
            if (!copy[index].IsValid || index != 0 && copy[index - 1].Realm == copy[index].Realm)
                throw new ArgumentException(
                    "Profile decisions must be valid and unique by verified realm.", nameof(decisions));
        }
        _decisions = Array.AsReadOnly(copy);
    }

    public static ClientProfileDecisionDocument Empty { get; } = new();
    public ReadOnlyCollection<ClientProfileDecision> Decisions => _decisions;

    public bool TryFindExact(
        ApplicationRealmId realm,
        ulong revision,
        ClientProfileHash hash,
        out ClientProfileDecision decision)
    {
        foreach (ClientProfileDecision value in _decisions)
        {
            if (value.Realm != realm) continue;
            if (value.ProfileRevision == revision && value.ProfileHash == hash)
            {
                decision = value;
                return true;
            }
            break;
        }
        decision = default;
        return false;
    }

    public ClientProfileDecisionDocument WithDecision(in ClientProfileDecision decision)
    {
        if (!decision.IsValid) throw new ArgumentException("A valid decision is required.", nameof(decision));
        var values = new List<ClientProfileDecision>(_decisions.Count + 1);
        bool replaced = false;
        foreach (ClientProfileDecision value in _decisions)
        {
            if (value.Realm == decision.Realm)
            {
                values.Add(decision);
                replaced = true;
            }
            else values.Add(value);
        }
        if (!replaced) values.Add(decision);
        return new ClientProfileDecisionDocument(values);
    }
}

public enum ClientProfileDecisionLoadStatus : byte
{
    Missing = 1,
    Loaded = 2,
    InvalidFallback = 3,
}

public readonly record struct ClientProfileDecisionLoadResult(
    ClientProfileDecisionLoadStatus Status,
    ClientProfileDecisionDocument Document,
    string? Error);

/// <summary>Atomic, bounded persistence for realm/profile consent decisions.</summary>
public sealed class ClientProfileDecisionFileStore
{
    private const int CurrentVersion = 1;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private readonly string _path;

    public ClientProfileDecisionFileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = System.IO.Path.GetFullPath(path);
    }

    public string Path => _path;

    public ClientProfileDecisionLoadResult Load()
    {
        if (!File.Exists(_path))
            return new(ClientProfileDecisionLoadStatus.Missing, ClientProfileDecisionDocument.Empty, null);
        try
        {
            string json = File.ReadAllText(_path, Utf8);
            DocumentDto dto = JsonSerializer.Deserialize<DocumentDto>(json, Options) ??
                throw new JsonException("The profile-decision document was null.");
            if (dto.Version != CurrentVersion)
                throw new JsonException($"Unsupported profile-decision version {dto.Version}.");
            var decisions = new List<ClientProfileDecision>();
            foreach (DecisionDto value in dto.Decisions ?? [])
            {
                if (!ApplicationRealmId.TryParse(value.Realm, out ApplicationRealmId realm) ||
                    !string.Equals(value.Realm, realm.ToString(), StringComparison.Ordinal) ||
                    !ClientProfileHash.TryParse(value.ProfileHash, out ClientProfileHash hash) ||
                    !string.Equals(value.ProfileHash, hash.ToString(), StringComparison.Ordinal))
                    throw new JsonException("A profile-decision identity is noncanonical.");
                var decision = new ClientProfileDecision(
                    realm, value.ProfileRevision, hash, value.Mode);
                if (!decision.IsValid) throw new JsonException("A profile decision is invalid.");
                decisions.Add(decision);
            }
            return new(ClientProfileDecisionLoadStatus.Loaded,
                new ClientProfileDecisionDocument(decisions), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            DecoderFallbackException or JsonException or ArgumentException or OverflowException)
        {
            return new(ClientProfileDecisionLoadStatus.InvalidFallback,
                ClientProfileDecisionDocument.Empty, exception.Message);
        }
    }

    public void Save(ClientProfileDecisionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("The profile-decision path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = _path + ".tmp";
        try
        {
            var dto = new DocumentDto
            {
                Version = CurrentVersion,
                Decisions = document.Decisions.Select(static value => new DecisionDto
                {
                    Realm = value.Realm.ToString(),
                    ProfileRevision = value.ProfileRevision,
                    ProfileHash = value.ProfileHash.ToString(),
                    Mode = value.Mode,
                }).ToList(),
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(dto, Options) + Environment.NewLine, Utf8);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var result = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        result.Converters.Add(new JsonStringEnumConverter());
        return result;
    }

    private sealed class DocumentDto
    {
        public int Version { get; set; }
        public List<DecisionDto>? Decisions { get; set; }
    }

    private sealed class DecisionDto
    {
        public string? Realm { get; set; }
        public ulong ProfileRevision { get; set; }
        public string? ProfileHash { get; set; }
        public ClientProfileApplicationMode Mode { get; set; }
    }
}
