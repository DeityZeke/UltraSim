namespace UltraSim.Server;

public sealed class Xoroshiro256PlusPlusRandom : Random
{
    private Xoroshiro256PlusPlus _generator;

    public Xoroshiro256PlusPlusRandom()
        : this((ulong)Environment.TickCount64)
    {
    }

    public Xoroshiro256PlusPlusRandom(int seed)
        : this(unchecked((ulong)seed))
    {
    }

    public Xoroshiro256PlusPlusRandom(ulong seed)
    {
        _generator = new Xoroshiro256PlusPlus(seed);
    }

    public Xoroshiro256PlusPlusRandom(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        _generator = new Xoroshiro256PlusPlus(s0, s1, s2, s3);
    }

    public override int Next()
    {
        return _generator.NextInt32();
    }

    public override int Next(int maxValue)
    {
        return _generator.NextInt32(maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        return _generator.NextInt32(minValue, maxValue);
    }

    public override long NextInt64()
    {
        return _generator.NextInt64();
    }

    public override long NextInt64(long maxValue)
    {
        return _generator.NextInt64(maxValue);
    }

    public override long NextInt64(long minValue, long maxValue)
    {
        return _generator.NextInt64(minValue, maxValue);
    }

    public override double NextDouble()
    {
        return _generator.NextDouble();
    }

    public override void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _generator.Fill(buffer);
    }

    public override void NextBytes(Span<byte> buffer)
    {
        _generator.Fill(buffer);
    }

    protected override double Sample()
    {
        return NextDouble();
    }
}
