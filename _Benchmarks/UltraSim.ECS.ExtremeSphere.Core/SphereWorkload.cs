using System.Runtime.CompilerServices;

namespace UltraSim.ECS.ExtremeSphere;

public static class SphereWorkload
{
    public const string ProtocolVersion = "ultrasim-extreme-sphere-v1";
    public const float TwoPi = MathF.PI * 2f;
    public const int SinLookupSize = 1_024;
    public const int SplitPipelineBytesPerEntityTick = 96;
    public const int FusedPipelineBytesPerEntityTick = 84;

    private const float SinLookupScale = SinLookupSize / TwoPi;
    private static readonly float[] SinLookup = BuildSinLookup();

    public static void Initialize(
        int ordinal,
        uint seed,
        float radius,
        out float positionX,
        out float positionY,
        out float positionZ,
        out float speed,
        out float frequency,
        out float phase)
    {
        uint state = Hash(unchecked((uint)ordinal) ^ seed);
        uint azimuthBits = Hash(state ^ 0x9E3779B9u);
        uint radiusBits = Hash(azimuthBits ^ 0x85EBCA6Bu);
        uint speedBits = Hash(radiusBits ^ 0xC2B2AE35u);
        uint frequencyBits = Hash(speedBits ^ 0x27D4EB2Du);
        uint phaseBits = Hash(frequencyBits ^ 0x165667B1u);

        float vertical = Random01(state) * 2f - 1f;
        float azimuth = Random01(azimuthBits) * TwoPi;
        float radial = radius * MathF.Pow(MathF.Max(Random01(radiusBits), 1f / 16_777_215f), 1f / 3f);
        float horizontal = MathF.Sqrt(MathF.Max(0f, 1f - vertical * vertical));

        positionX = radial * horizontal * MathF.Cos(azimuth);
        positionY = radial * vertical;
        positionZ = radial * horizontal * MathF.Sin(azimuth);
        speed = 2f + Random01(speedBits) * 6f;
        frequency = 0.5f + Random01(frequencyBits) * 1.5f;
        phase = Random01(phaseBits) * TwoPi;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float AdvancePhase(float phase, float frequency, float deltaSeconds)
    {
        phase += frequency * deltaSeconds;
        if (phase > TwoPi)
            phase -= TwoPi;
        return phase;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FastSin(float angle)
    {
        float wrapped = angle % TwoPi;
        if (wrapped < 0f)
            wrapped += TwoPi;
        int index = (int)(wrapped * SinLookupScale) & (SinLookupSize - 1);
        return SinLookup[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ComputeVelocity(
        float positionX,
        float positionY,
        float positionZ,
        float speed,
        float phase,
        out float velocityX,
        out float velocityY,
        out float velocityZ)
    {
        float distanceSquared = positionX * positionX + positionY * positionY + positionZ * positionZ;
        if (distanceSquared > 0.0001f)
        {
            float scale = FastSin(phase) * speed / MathF.Sqrt(distanceSquared);
            velocityX = positionX * scale;
            velocityY = positionY * scale;
            velocityZ = positionZ * scale;
            return;
        }

        // The legacy scalar path used process-global random numbers here. A fixed
        // fallback keeps cross-generation replays deterministic.
        float fallback = speed * 0.35355338f;
        velocityX = fallback;
        velocityY = -fallback;
        velocityZ = fallback;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Integrate(
        ref float positionX,
        ref float positionY,
        ref float positionZ,
        float velocityX,
        float velocityY,
        float velocityZ,
        float deltaSeconds)
    {
        positionX += velocityX * deltaSeconds;
        positionY += velocityY * deltaSeconds;
        positionZ += velocityZ * deltaSeconds;
    }

    private static uint Hash(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }

    private static float Random01(uint value) =>
        (value & 0x00FFFFFFu) / 16_777_215f;

    private static float[] BuildSinLookup()
    {
        var values = new float[SinLookupSize];
        for (int index = 0; index < values.Length; index++)
            values[index] = MathF.Sin(index / (float)values.Length * TwoPi);
        return values;
    }
}
