namespace UltraSim.ECS;

/// <summary>Stable FNV-1a hash for canonical ASCII schema keys.</summary>
public static class StableHash64
{
    public const ulong OffsetBasis = 14695981039346656037UL;
    public const ulong Prime = 1099511628211UL;

    public static ulong Compute(ReadOnlySpan<char> canonicalAsciiKey)
    {
        if (!TryCompute(canonicalAsciiKey, out ulong hash))
            throw new ArgumentException("Canonical ECS keys must contain ASCII characters only.");

        return hash;
    }

    public static bool TryCompute(ReadOnlySpan<char> canonicalAsciiKey, out ulong hash)
    {
        hash = OffsetBasis;
        for (int i = 0; i < canonicalAsciiKey.Length; i++)
        {
            char value = canonicalAsciiKey[i];
            if (value > 0x7F)
            {
                hash = 0;
                return false;
            }

            hash = unchecked((hash ^ value) * Prime);
        }

        return true;
    }
}
