using UltraSim.ECS;

namespace UltraSim.Server.Network;

public static class ServerEcsNetworkItemKeys
{
    public const ulong World = 101;

    public static ulong GetSystemRuntimeItemKey(in SystemRuntimeSnapshot snapshot)
    {
        return ComputeStableKey("ecs-system", snapshot.Name, snapshot.Stage.ToString());
    }

    public static ulong GetComponentItemKey<T>(Entity entity)
        where T : unmanaged, IComponent<T>
    {
        ulong hash = ComponentTypeHash<T>.Value;
        MixUInt64(ref hash, entity.Packed);
        return hash == 0 ? 1UL : hash;
    }

    private static ulong ComputeStableKey(string category, string a, string b)
    {
        const ulong offset = 14695981039346656037UL;
        ulong hash = offset;
        MixString(ref hash, category);
        MixString(ref hash, a);
        MixString(ref hash, b);
        return hash == 0 ? 1UL : hash;
    }

    private static void MixString(ref ulong hash, string value)
    {
        const ulong prime = 1099511628211UL;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= prime;
        }

        hash ^= 0xFF;
        hash *= prime;
    }

    private static void MixUInt64(ref ulong hash, ulong value)
    {
        const ulong prime = 1099511628211UL;
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash ^= (byte)(value >> shift);
            hash *= prime;
        }
    }

    private static class ComponentTypeHash<T>
        where T : unmanaged, IComponent<T>
    {
        public static readonly ulong Value = Compute();

        private static ulong Compute()
        {
            const ulong offset = 14695981039346656037UL;
            ulong hash = offset;
            MixString(ref hash, typeof(T).FullName ?? typeof(T).Name);
            return hash == 0 ? 1UL : hash;
        }
    }
}
