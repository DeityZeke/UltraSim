using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public readonly struct ComponentMask
    {
        private readonly ulong[] _bits;

        public static readonly ComponentMask Empty = new ComponentMask(Array.Empty<ulong>());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentMask(ulong[] bits) => _bits = bits ?? Array.Empty<ulong>();

        public ReadOnlySpan<ulong> Bits
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _bits;
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _bits.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(in ComponentMask other)
        {
            int min = _bits.Length < other._bits.Length ? _bits.Length : other._bits.Length;
            for (int i = 0; i < min; i++)
            {
                if ((_bits[i] & other._bits[i]) != 0)
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSupersetOf(in ComponentMask other)
        {
            if (other._bits.Length > _bits.Length)
                return false;

            for (int i = 0; i < other._bits.Length; i++)
            {
                if ((other._bits[i] & ~_bits[i]) != 0)
                    return false;
            }
            return true;
        }

        public static ComponentMask FromIds(ReadOnlySpan<ComponentId> componentIds)
        {
            if (componentIds.Length == 0)
                return Empty;

            int maxId = 0;
            for (int i = 0; i < componentIds.Length; i++)
            {
                int id = componentIds[i].Value;
                if (id > maxId)
                    maxId = id;
            }

            int arrayLen = (maxId >> 6) + 1;
            var bits = new ulong[arrayLen];

            for (int i = 0; i < componentIds.Length; i++)
            {
                int id = componentIds[i].Value;
                bits[id >> 6] |= 1UL << (id & 63);
            }

            return new ComponentMask(bits);
        }
    }
}
