using System.Runtime.InteropServices;

namespace UltraSim.Game.Application;

/// <summary>
/// Durable application/shard namespace asserted by Game and authenticated by a trusted
/// deployment. Possessing a value does not itself establish that authentication.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct ApplicationRealmId :
    IEquatable<ApplicationRealmId>, IComparable<ApplicationRealmId>
{
    public ApplicationRealmId(ulong high, ulong low)
    {
        if (high == 0 && low == 0) throw new ArgumentOutOfRangeException(nameof(high));
        High = high;
        Low = low;
    }

    public ulong High { get; }
    public ulong Low { get; }
    public bool IsValid => High != 0 || Low != 0;

    public static bool TryParse(string? value, out ApplicationRealmId realm)
    {
        realm = default;
        if (value is null || value.Length != 32) return false;
        if (!ulong.TryParse(value.AsSpan(0, 16),
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out ulong high) ||
            !ulong.TryParse(value.AsSpan(16, 16),
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out ulong low) ||
            (high == 0 && low == 0)) return false;
        realm = new ApplicationRealmId(high, low);
        return true;
    }

    public int CompareTo(ApplicationRealmId other)
    {
        int high = High.CompareTo(other.High);
        return high != 0 ? high : Low.CompareTo(other.Low);
    }

    public bool Equals(ApplicationRealmId other) => High == other.High && Low == other.Low;
    public override bool Equals(object? obj) => obj is ApplicationRealmId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(High, Low);
    public override string ToString() => IsValid ? $"{High:x16}{Low:x16}" : "invalid";
    public static bool operator ==(ApplicationRealmId left, ApplicationRealmId right) => left.Equals(right);
    public static bool operator !=(ApplicationRealmId left, ApplicationRealmId right) => !left.Equals(right);
}
