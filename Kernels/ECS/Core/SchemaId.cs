using System.Globalization;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Deterministic 128-bit fingerprint of a generated component schema.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public readonly struct SchemaId : IEquatable<SchemaId>
{
    public static readonly SchemaId Invalid = default;

    public SchemaId(ulong low, ulong high)
    {
        Low = low;
        High = high;
    }

    public ulong Low { get; }

    public ulong High { get; }

    public bool IsValid => (Low | High) != 0;

    public bool Equals(SchemaId other) => Low == other.Low && High == other.High;

    public override bool Equals(object? obj) => obj is SchemaId other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Low, High);

    public override string ToString() => string.Concat(
        High.ToString("x16", CultureInfo.InvariantCulture),
        Low.ToString("x16", CultureInfo.InvariantCulture));

    public static bool operator ==(SchemaId left, SchemaId right) => left.Equals(right);

    public static bool operator !=(SchemaId left, SchemaId right) => !left.Equals(right);
}
