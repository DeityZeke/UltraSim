namespace UltraSim.Server.Network;

public static class ServerRuntimeNetworkItemKeys
{
    public const ulong Status = 1;
    public const ulong Metrics = 2;

    public static ulong GetSystemRuntimeItemKey(in ServerSystemRuntimeSnapshot snapshot)
    {
        return ComputeStableKey("system", snapshot.Name, snapshot.Stage);
    }

    public static ulong GetPlayerSessionItemKey(in ServerPlayerSessionSnapshot snapshot)
    {
        return ComputeStableKey("player", snapshot.SessionId, snapshot.PlayerName);
    }

    public static ulong GetAccountItemKey(in ServerAccountSummarySnapshot snapshot)
    {
        return ComputeStableKey("account", snapshot.AccountId, snapshot.DisplayName);
    }

    private static ulong ComputeStableKey(string category, string a, string b)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offset;
        Add(category);
        Add(a);
        Add(b);

        return hash == 0 ? 1UL : hash;

        void Add(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= prime;
            }

            hash ^= 0xFF;
            hash *= prime;
        }
    }
}
