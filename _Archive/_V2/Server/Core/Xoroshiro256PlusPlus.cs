namespace UltraSim.Server;

public struct Xoroshiro256PlusPlus
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public Xoroshiro256PlusPlus(ulong seed)
    {
        var seeder = new SplitMix64(seed);
        _s0 = seeder.NextUInt64();
        _s1 = seeder.NextUInt64();
        _s2 = seeder.NextUInt64();
        _s3 = seeder.NextUInt64();
    }

    public Xoroshiro256PlusPlus(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        if ((s0 | s1 | s2 | s3) == 0UL)
            throw new ArgumentException("Xoroshiro256++ state cannot be all zero.");

        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }

    public ulong NextUInt64()
    {
        ulong result = RotateLeft(_s0 + _s3, 23) + _s0;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;

        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);

        return result;
    }

    public uint NextUInt32()
    {
        return (uint)(NextUInt64() >> 32);
    }

    public int NextInt32()
    {
        return (int)(NextUInt64() >> 33);
    }

    public int NextInt32(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Maximum must be positive.");

        return (int)NextUInt32Bounded((uint)maxExclusive);
    }

    public int NextInt32(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Maximum must be greater than minimum.");

        uint range = checked((uint)(maxExclusive - minInclusive));
        return minInclusive + (int)NextUInt32Bounded(range);
    }

    public long NextInt64()
    {
        return (long)(NextUInt64() >> 1);
    }

    public long NextInt64(long maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Maximum must be positive.");

        return (long)NextUInt64Bounded((ulong)maxExclusive);
    }

    public long NextInt64(long minInclusive, long maxExclusive)
    {
        if (minInclusive >= maxExclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Maximum must be greater than minimum.");

        ulong range = checked((ulong)(maxExclusive - minInclusive));
        return minInclusive + (long)NextUInt64Bounded(range);
    }

    public double NextDouble()
    {
        return (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
    }

    public void Fill(Span<byte> buffer)
    {
        int index = 0;
        while (buffer.Length - index >= sizeof(ulong))
        {
            ulong value = NextUInt64();
            BitConverter.TryWriteBytes(buffer[index..], value);
            index += sizeof(ulong);
        }

        if (index >= buffer.Length)
            return;

        ulong tail = NextUInt64();
        for (int shift = 0; index < buffer.Length; shift += 8, index++)
            buffer[index] = (byte)(tail >> shift);
    }

    private uint NextUInt32Bounded(uint bound)
    {
        uint threshold = (0U - bound) % bound;
        while (true)
        {
            uint value = NextUInt32();
            ulong product = (ulong)value * bound;
            uint low = (uint)product;
            if (low >= threshold)
                return (uint)(product >> 32);
        }
    }

    private ulong NextUInt64Bounded(ulong bound)
    {
        ulong threshold = (0UL - bound) % bound;
        while (true)
        {
            ulong value = NextUInt64();
            if (value >= threshold)
                return value % bound;
        }
    }

    private static ulong RotateLeft(ulong value, int offset)
    {
        return (value << offset) | (value >> (64 - offset));
    }

    private struct SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed)
        {
            _state = seed;
        }

        public ulong NextUInt64()
        {
            ulong z = unchecked(_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
