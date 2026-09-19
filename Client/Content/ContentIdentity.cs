using System.Buffers.Binary;
using System.Security.Cryptography;
using UltraSim.Game.Application;

namespace UltraSim.Client.Content;

/// <summary>The finite authorization owner kinds understood by the passive-content store.</summary>
public enum ContentScopeKind : byte
{
    Invalid = 0,
    Local = 1,
    Realm = 2,
}

/// <summary>
/// Canonical authorization owner for persistent content metadata. Local is one reserved scope;
/// Realm preserves the exact authenticated application-realm identity. Filesystem text is never
/// accepted as a scope identity.
/// </summary>
public readonly struct ContentScopeKey :
    IEquatable<ContentScopeKey>, IComparable<ContentScopeKey>
{
    private ContentScopeKey(ContentScopeKind kind, ApplicationRealmId realm)
    {
        Kind = kind;
        Realm = realm;
    }

    public static ContentScopeKey Local { get; } = new(ContentScopeKind.Local, default);

    public ContentScopeKind Kind { get; }
    public ApplicationRealmId Realm { get; }
    public bool IsValid => Kind == ContentScopeKind.Local && !Realm.IsValid ||
        Kind == ContentScopeKind.Realm && Realm.IsValid;
    public bool IsLocal => Kind == ContentScopeKind.Local && IsValid;
    public bool IsRealm => Kind == ContentScopeKind.Realm && IsValid;

    public static ContentScopeKey ForRealm(ApplicationRealmId realm)
    {
        if (!realm.IsValid) throw new ArgumentException("An authenticated realm is required.", nameof(realm));
        return new ContentScopeKey(ContentScopeKind.Realm, realm);
    }

    public bool TryGetRealm(out ApplicationRealmId realm)
    {
        realm = IsRealm ? Realm : default;
        return realm.IsValid;
    }

    public int CompareTo(ContentScopeKey other)
    {
        int kind = Kind.CompareTo(other.Kind);
        if (kind != 0) return kind;
        int high = Realm.High.CompareTo(other.Realm.High);
        return high != 0 ? high : Realm.Low.CompareTo(other.Realm.Low);
    }

    public bool Equals(ContentScopeKey other) => Kind == other.Kind && Realm == other.Realm;
    public override bool Equals(object? obj) => obj is ContentScopeKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((byte)Kind, Realm);
    public override string ToString() => IsLocal ? "world.local" : IsRealm ? $"realm:{Realm}" : "invalid";
    public static bool operator ==(ContentScopeKey left, ContentScopeKey right) => left.Equals(right);
    public static bool operator !=(ContentScopeKey left, ContentScopeKey right) => !left.Equals(right);
}

/// <summary>Exact SHA-256 value used by content descriptors and canonical identities.</summary>
public readonly struct ContentHash : IEquatable<ContentHash>, IComparable<ContentHash>
{
    public const int ByteLength = 32;
    public const int HexLength = 64;

    private ContentHash(ulong part0, ulong part1, ulong part2, ulong part3)
    {
        Part0 = part0;
        Part1 = part1;
        Part2 = part2;
        Part3 = part3;
    }

    public ulong Part0 { get; }
    public ulong Part1 { get; }
    public ulong Part2 { get; }
    public ulong Part3 { get; }
    public bool IsValid => Part0 != 0 || Part1 != 0 || Part2 != 0 || Part3 != 0;

    public static ContentHash Compute(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[ByteLength];
        SHA256.HashData(bytes, digest);
        return FromDigest(digest);
    }

    public static ContentHash FromDigest(ReadOnlySpan<byte> digest)
    {
        if (digest.Length != ByteLength) throw new ArgumentException("A SHA-256 digest is exactly 32 bytes.", nameof(digest));
        var result = new ContentHash(
            BinaryPrimitives.ReadUInt64BigEndian(digest),
            BinaryPrimitives.ReadUInt64BigEndian(digest[8..]),
            BinaryPrimitives.ReadUInt64BigEndian(digest[16..]),
            BinaryPrimitives.ReadUInt64BigEndian(digest[24..]));
        if (!result.IsValid) throw new ArgumentException("The all-zero content hash is reserved as invalid.", nameof(digest));
        return result;
    }

    public static bool TryParse(string? value, out ContentHash hash)
    {
        hash = default;
        if (value is null || value.Length != HexLength) return false;
        foreach (char character in value)
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        byte[] digest;
        try { digest = Convert.FromHexString(value); }
        catch (FormatException) { return false; }
        try { hash = FromDigest(digest); }
        catch (ArgumentException) { return false; }
        return string.Equals(value, hash.ToString(), StringComparison.Ordinal);
    }

    public void WriteDigest(Span<byte> destination)
    {
        if (!IsValid) throw new InvalidOperationException("A default content hash is invalid.");
        if (destination.Length < ByteLength) throw new ArgumentException("The destination is too small.", nameof(destination));
        BinaryPrimitives.WriteUInt64BigEndian(destination, Part0);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..], Part1);
        BinaryPrimitives.WriteUInt64BigEndian(destination[16..], Part2);
        BinaryPrimitives.WriteUInt64BigEndian(destination[24..], Part3);
    }

    public int CompareTo(ContentHash other)
    {
        int result = Part0.CompareTo(other.Part0);
        if (result != 0) return result;
        result = Part1.CompareTo(other.Part1);
        if (result != 0) return result;
        result = Part2.CompareTo(other.Part2);
        return result != 0 ? result : Part3.CompareTo(other.Part3);
    }

    public bool Equals(ContentHash other) => Part0 == other.Part0 && Part1 == other.Part1 &&
        Part2 == other.Part2 && Part3 == other.Part3;
    public override bool Equals(object? obj) => obj is ContentHash other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Part0, Part1, Part2, Part3);
    public override string ToString() => IsValid
        ? $"{Part0:x16}{Part1:x16}{Part2:x16}{Part3:x16}"
        : "invalid";
    public static bool operator ==(ContentHash left, ContentHash right) => left.Equals(right);
    public static bool operator !=(ContentHash left, ContentHash right) => !left.Equals(right);
}

public readonly record struct ContentNamespaceId : IComparable<ContentNamespaceId>
{
    public const int MaximumCharacters = 48;
    private ContentNamespaceId(string value) => Value = value;
    public string? Value { get; }
    public bool IsValid => ContentText.IsIdentifier(Value, MaximumCharacters, allowDot: false);
    public static bool TryCreate(string? value, out ContentNamespaceId id)
    {
        id = new ContentNamespaceId(value ?? string.Empty);
        if (id.IsValid) return true;
        id = default;
        return false;
    }
    public int CompareTo(ContentNamespaceId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value ?? "invalid";
}

public readonly record struct ContentPackageId : IComparable<ContentPackageId>
{
    public const int MaximumCharacters = 96;
    private ContentPackageId(string value) => Value = value;
    public string? Value { get; }
    public bool IsValid => ContentText.IsIdentifier(Value, MaximumCharacters, allowDot: true);
    public static bool TryCreate(string? value, out ContentPackageId id)
    {
        id = new ContentPackageId(value ?? string.Empty);
        if (id.IsValid) return true;
        id = default;
        return false;
    }
    public int CompareTo(ContentPackageId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value ?? "invalid";
}

public readonly record struct LogicalContentId : IComparable<LogicalContentId>
{
    public const int MaximumCharacters = 160;
    private LogicalContentId(string value) => Value = value;
    public string? Value { get; }
    public bool IsValid => ContentText.IsLogicalId(Value);

    public ContentNamespaceId Namespace
    {
        get
        {
            if (!IsValid) return default;
            string value = Value!;
            _ = ContentNamespaceId.TryCreate(value[..value.IndexOf(':')], out ContentNamespaceId result);
            return result;
        }
    }

    public static bool TryCreate(string? value, out LogicalContentId id)
    {
        id = new LogicalContentId(value ?? string.Empty);
        if (id.IsValid) return true;
        id = default;
        return false;
    }

    public int CompareTo(LogicalContentId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value ?? "invalid";
}

public readonly record struct ContentFilePath : IComparable<ContentFilePath>
{
    public const int MaximumCharacters = 192;
    private ContentFilePath(string value) => Value = value;
    public string? Value { get; }
    public bool IsValid => ContentText.IsFilePath(Value);
    public static bool TryCreate(string? value, out ContentFilePath path)
    {
        path = new ContentFilePath(value ?? string.Empty);
        if (path.IsValid) return true;
        path = default;
        return false;
    }
    public int CompareTo(ContentFilePath other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value ?? "invalid";
}

internal static class ContentText
{
    internal static bool IsIdentifier(string? value, int maximum, bool allowDot)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum || value[0] is '.' or '-' or '_' ||
            value[^1] is '.' or '-' or '_') return false;
        char previous = '\0';
        foreach (char character in value)
        {
            bool valid = character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' ||
                allowDot && character == '.';
            if (!valid || character == '.' && previous == '.') return false;
            previous = character;
        }
        return true;
    }

    internal static bool IsLogicalId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > LogicalContentId.MaximumCharacters) return false;
        int colon = value.IndexOf(':');
        if (colon <= 0 || colon != value.LastIndexOf(':') || colon == value.Length - 1 ||
            !IsIdentifier(value[..colon], ContentNamespaceId.MaximumCharacters, allowDot: false)) return false;
        string path = value[(colon + 1)..];
        if (path[0] == '/' || path[^1] == '/') return false;
        foreach (string segment in path.Split('/'))
            if (!IsIdentifier(segment, 64, allowDot: false)) return false;
        return true;
    }

    internal static bool IsFilePath(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > ContentFilePath.MaximumCharacters ||
            value[0] == '/' || value[^1] == '/' || value.Contains('\\') || value.Contains(':')) return false;
        foreach (string segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.Length > 96 ||
                segment[0] == '.' || segment[^1] == '.') return false;
            foreach (char character in segment)
                if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')) return false;
        }
        return true;
    }

}
