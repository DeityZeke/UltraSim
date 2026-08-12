namespace UltraSim.ECS;

/// <summary>Stable durable authoring identity for one concrete content prefab.</summary>
/// <remarks>
/// A prefab key survives catalog rebuilds. It is not a runtime handle, archetype
/// identity, entity identity, or persistence record by itself.
/// </remarks>
public readonly struct PrefabKey : IEquatable<PrefabKey>, IComparable<PrefabKey>
{
    public const int MaximumLength = 256;
    private readonly string? _value;
    private readonly ulong _stableHash;

    public PrefabKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Validate(value);
        _value = value;
        _stableHash = StableHash64.Compute(value);
    }

    public static PrefabKey Invalid => default;

    public string Value => _value ?? string.Empty;

    public ulong StableHash => _stableHash;

    public bool IsValid => _value is not null;

    public int CompareTo(PrefabKey other) =>
        StringComparer.Ordinal.Compare(_value, other._value);

    public bool Equals(PrefabKey other) =>
        StringComparer.Ordinal.Equals(_value, other._value);

    public override bool Equals(object? obj) => obj is PrefabKey other && Equals(other);

    public override int GetHashCode() => _value is null
        ? 0
        : StringComparer.Ordinal.GetHashCode(_value);

    public override string ToString() => _value ?? "PrefabKey(Invalid)";

    public static bool operator ==(PrefabKey left, PrefabKey right) => left.Equals(right);

    public static bool operator !=(PrefabKey left, PrefabKey right) => !left.Equals(right);

    private static void Validate(string value)
    {
        if (value.Length is 0 or > MaximumLength)
            throw new ArgumentException($"Prefab keys must contain 1-{MaximumLength} characters.", nameof(value));
        if (value[0] == '/' || value[^1] == '/' || value.Contains("//", StringComparison.Ordinal))
            throw new ArgumentException("Prefab keys cannot begin/end with '/' or contain empty path segments.", nameof(value));

        bool segmentStart = true;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            bool lower = character is >= 'a' and <= 'z';
            bool digit = character is >= '0' and <= '9';
            bool punctuation = character is '-' or '_' or '.';
            if (!(lower || digit || punctuation || character == '/'))
            {
                throw new ArgumentException(
                    "Prefab keys use lowercase ASCII letters, digits, '/', '-', '_', and '.'.",
                    nameof(value));
            }
            if (segmentStart && !(lower || digit))
                throw new ArgumentException("Every prefab-key path segment must begin with a letter or digit.", nameof(value));
            segmentStart = character == '/';
        }
    }
}
