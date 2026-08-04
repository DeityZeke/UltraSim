using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>Stable runtime hash for one canonical schema-local component signature.</summary>
/// <remarks>
/// The hash accelerates lookup but is never treated as identity; registry hits
/// always compare the complete canonical component sequence.
/// </remarks>
internal static class ArchetypeSignatureHash
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    internal static ulong Compute(ReadOnlySpan<ComponentId> signature)
    {
        unchecked
        {
            ulong hash = OffsetBasis;
            hash = MixUInt32(hash, (uint)signature.Length);
            for (int i = 0; i < signature.Length; i++)
                hash = MixUInt16(hash, signature[i].Value);

            // MurmurHash3 finalization prevents sequential dense component IDs from
            // clustering in the low bits used by a power-of-two lookup table.
            hash ^= hash >> 33;
            hash *= 0xff51afd7ed558ccdUL;
            hash ^= hash >> 33;
            hash *= 0xc4ceb9fe1a85ec53UL;
            hash ^= hash >> 33;
            return hash;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetInitialSlot(ulong hash, int mask) =>
        unchecked((int)(hash ^ (hash >> 32))) & mask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixUInt16(ulong hash, ushort value)
    {
        unchecked
        {
            hash = (hash ^ (byte)value) * Prime;
            return (hash ^ (byte)(value >> 8)) * Prime;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixUInt32(ulong hash, uint value)
    {
        unchecked
        {
            hash = (hash ^ (byte)value) * Prime;
            hash = (hash ^ (byte)(value >> 8)) * Prime;
            hash = (hash ^ (byte)(value >> 16)) * Prime;
            return (hash ^ (byte)(value >> 24)) * Prime;
        }
    }
}
