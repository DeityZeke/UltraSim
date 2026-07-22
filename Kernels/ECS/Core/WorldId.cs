using System.Threading;

namespace UltraSim.ECS;

/// <summary>
/// Process-lifetime identity of one runtime ECS world.
/// </summary>
/// <remarks>
/// This identity is intentionally transient. It must not be serialized as a
/// durable entity or world identifier.
/// </remarks>
public readonly struct WorldId : IEquatable<WorldId>
{
    public static WorldId Invalid => default;

    private readonly uint _value;

    internal WorldId(uint value)
    {
        _value = value;
    }

    public uint Value => _value;

    public bool IsValid => _value != 0;

    /// <summary>
    /// Allocates a process-unique runtime world identity.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The 32-bit process-lifetime identity space has been exhausted.
    /// </exception>
    public static WorldId Create() => WorldIdSource.Allocate();

    public bool Equals(WorldId other) => _value == other._value;

    public override bool Equals(object? obj) => obj is WorldId other && Equals(other);

    public override int GetHashCode() => (int)_value;

    public static bool operator ==(WorldId left, WorldId right) => left.Equals(right);

    public static bool operator !=(WorldId left, WorldId right) => !left.Equals(right);

    public override string ToString() => IsValid ? $"World({_value})" : "World(Invalid)";

    private static class WorldIdSource
    {
        private static long _lastIssued;

        internal static WorldId Allocate()
        {
            long value = Interlocked.Increment(ref _lastIssued);
            if ((ulong)value > uint.MaxValue)
                ThrowExhausted();

            return new WorldId((uint)value);
        }

        private static void ThrowExhausted()
            => throw new InvalidOperationException(
                "The process-lifetime ECS world identity space is exhausted; world IDs never wrap or reuse.");
    }
}
