using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Copyable independent position in one world's retained lifecycle timeline.</summary>
/// <remarks>
/// A cursor is world-bound and owner-thread consumed. Copying it creates another
/// independent consumer at the same position. It does not register a subscriber
/// with the world and owns no allocation.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public struct LifecycleCursor : IEquatable<LifecycleCursor>
{
    private ulong _nextSequence;
    private readonly WorldId _worldId;

    internal LifecycleCursor(WorldId worldId, ulong nextSequence)
    {
        _nextSequence = nextSequence;
        _worldId = worldId;
    }

    /// <summary>Gets the world that issued this cursor.</summary>
    public readonly WorldId World => _worldId;

    /// <summary>Gets the sequence the next successful read will attempt to deliver.</summary>
    public readonly ulong NextSequence => _nextSequence;

    /// <summary>Gets whether the cursor was issued by a world.</summary>
    public readonly bool IsValid => _worldId.IsValid;

    internal void Advance(int count) =>
        _nextSequence = unchecked(_nextSequence + unchecked((ulong)count));

    public readonly bool Equals(LifecycleCursor other) =>
        _nextSequence == other._nextSequence && _worldId == other._worldId;

    public override readonly bool Equals(object? obj) => obj is LifecycleCursor other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(_nextSequence, _worldId);

    public static bool operator ==(LifecycleCursor left, LifecycleCursor right) => left.Equals(right);

    public static bool operator !=(LifecycleCursor left, LifecycleCursor right) => !left.Equals(right);
}
