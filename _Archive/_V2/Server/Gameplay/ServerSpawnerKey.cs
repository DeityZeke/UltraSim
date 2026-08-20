namespace UltraSim.Server.Gameplay;

public static class ServerSpawnerKey
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static string Normalize(string spawnSetKey)
    {
        if (string.IsNullOrWhiteSpace(spawnSetKey))
            throw new ArgumentException("Spawn set key cannot be empty.", nameof(spawnSetKey));

        string normalized = spawnSetKey.Trim();
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            bool valid =
                c is >= 'a' and <= 'z' ||
                c is >= 'A' and <= 'Z' ||
                c is >= '0' and <= '9' ||
                c is '.' or '_' or '-' or ':' or '/';
            if (!valid)
                throw new ArgumentException($"Spawn set key contains invalid character '{c}'.", nameof(spawnSetKey));
        }

        return normalized;
    }

    public static ulong ComputeHash(string spawnSetKey)
    {
        string normalized = Normalize(spawnSetKey);
        ulong hash = FnvOffset;
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            hash ^= (byte)c;
            hash *= FnvPrime;

            byte high = (byte)(c >> 8);
            if (high != 0)
            {
                hash ^= high;
                hash *= FnvPrime;
            }
        }

        return hash == 0 ? 1 : hash;
    }
}
