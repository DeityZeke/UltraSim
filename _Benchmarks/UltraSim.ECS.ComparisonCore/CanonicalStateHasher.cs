using System.Globalization;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS.Comparison;

/// <summary>
/// Stable, allocation-free-until-formatting logical-state hash. Adapters feed
/// logical entity ordinals, signatures, values, enable state, lifecycle revision,
/// and event output; physical rows, chunk IDs, archetype IDs, and world IDs are
/// intentionally normalized away.
/// </summary>
public struct CanonicalStateHasher
{
    private const ulong Offset1 = 14695981039346656037UL;
    private const ulong Prime1 = 1099511628211UL;
    private const ulong Offset2 = 7809847782465536322UL;
    private const ulong Prime2 = 14029467366897019727UL;

    private ulong _first;
    private ulong _second;
    private ulong _length;

    public CanonicalStateHasher()
    {
        _first = Offset1;
        _second = Offset2;
        _length = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddByte(byte value)
    {
        unchecked
        {
            _first = (_first ^ value) * Prime1;
            _second = (_second + value + 0x9E3779B97F4A7C15UL) * Prime2;
            _second ^= _second >> 29;
            _length++;
        }
    }

    public void AddBoolean(bool value) => AddByte(value ? (byte)1 : (byte)0);

    public void AddInt32(int value) => AddUInt32(unchecked((uint)value));

    public void AddUInt32(uint value)
    {
        AddByte(unchecked((byte)value));
        AddByte(unchecked((byte)(value >> 8)));
        AddByte(unchecked((byte)(value >> 16)));
        AddByte(unchecked((byte)(value >> 24)));
    }

    public void AddInt64(long value) => AddUInt64(unchecked((ulong)value));

    public void AddUInt64(ulong value)
    {
        AddUInt32(unchecked((uint)value));
        AddUInt32(unchecked((uint)(value >> 32)));
    }

    public void AddString(ReadOnlySpan<char> value)
    {
        AddInt32(value.Length);
        for (int index = 0; index < value.Length; index++)
            AddUInt32(value[index]);
    }

    public readonly string Finish()
    {
        ulong first;
        ulong second;
        unchecked
        {
            first = Avalanche(_first ^ (_length * 0x9E3779B97F4A7C15UL));
            second = Avalanche(_second ^ (_length * 0xC2B2AE3D27D4EB4FUL));
        }
        return first.ToString("X16", CultureInfo.InvariantCulture) +
            second.ToString("X16", CultureInfo.InvariantCulture);
    }

    private static ulong Avalanche(ulong value)
    {
        unchecked
        {
            value ^= value >> 33;
            value *= 0xff51afd7ed558ccdUL;
            value ^= value >> 33;
            value *= 0xc4ceb9fe1a85ec53UL;
            value ^= value >> 33;
            return value;
        }
    }
}
