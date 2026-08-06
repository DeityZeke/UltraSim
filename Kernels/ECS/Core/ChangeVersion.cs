using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// World-local monotonic change stamp. Zero means that a component column has
/// never been marked. Ordering is well-defined while cursors remain within half
/// of the 64-bit sequence space, including the explicit maximum-to-one wrap.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 8)]
public readonly struct ChangeVersion : IEquatable<ChangeVersion>
{
    private const ulong HalfRange = 1UL << 63;
    private readonly ulong _value;

    internal ChangeVersion(ulong value) => _value = value;

    public static ChangeVersion None => default;

    public bool IsValid => _value != 0;

    public ulong Value => _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChangeVersion Next() => new(_value == ulong.MaxValue ? 1UL : _value + 1UL);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsNewerThan(ChangeVersion baseline)
    {
        if (!IsValid || this == baseline)
            return false;
        if (!baseline.IsValid)
            return true;

        ulong distance = unchecked(_value - baseline._value);
        return distance < HalfRange;
    }

    public bool Equals(ChangeVersion other) => _value == other._value;

    public override bool Equals(object? obj) => obj is ChangeVersion other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();

    public override string ToString() => IsValid ? $"ChangeVersion({_value})" : "ChangeVersion(None)";

    public static bool operator ==(ChangeVersion left, ChangeVersion right) => left.Equals(right);

    public static bool operator !=(ChangeVersion left, ChangeVersion right) => !left.Equals(right);
}
