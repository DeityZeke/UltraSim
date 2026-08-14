using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Stable durable world identity; unlike <see cref="WorldId"/>, it survives process/runtime replacement.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 8)]
public readonly struct PersistentWorldId : IEquatable<PersistentWorldId>, IComparable<PersistentWorldId>
{
    public PersistentWorldId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(PersistentWorldId other) => Value.CompareTo(other.Value);
    public bool Equals(PersistentWorldId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is PersistentWorldId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"PersistentWorld({Value})" : "PersistentWorld(Invalid)";
    public static bool operator ==(PersistentWorldId left, PersistentWorldId right) => left.Equals(right);
    public static bool operator !=(PersistentWorldId left, PersistentWorldId right) => !left.Equals(right);
}

/// <summary>Stable durable entity identity unique only within one <see cref="PersistentWorldId"/>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 8)]
public readonly struct PersistentEntityId : IEquatable<PersistentEntityId>, IComparable<PersistentEntityId>
{
    public PersistentEntityId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(PersistentEntityId other) => Value.CompareTo(other.Value);
    public bool Equals(PersistentEntityId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is PersistentEntityId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"PersistentEntity({Value})" : "PersistentEntity(Invalid)";
    public static bool operator ==(PersistentEntityId left, PersistentEntityId right) => left.Equals(right);
    public static bool operator !=(PersistentEntityId left, PersistentEntityId right) => !left.Equals(right);
}

/// <summary>Complete cross-world durable entity key combining world and entity identity.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public readonly struct PersistentEntityKey : IEquatable<PersistentEntityKey>
{
    public PersistentEntityKey(PersistentWorldId world, PersistentEntityId entity)
    {
        if (!world.IsValid || !entity.IsValid) throw new ArgumentException("Both persistent identities must be valid.");
        World = world;
        Entity = entity;
    }
    public PersistentWorldId World { get; }
    public PersistentEntityId Entity { get; }
    public bool IsValid => World.IsValid && Entity.IsValid;
    public bool Equals(PersistentEntityKey other) => World == other.World && Entity == other.Entity;
    public override bool Equals(object? obj) => obj is PersistentEntityKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(World, Entity);
}

/// <summary>Dense snapshot-local entity-reference ordinal; the maximum value represents null.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
public readonly struct SaveEntityOrdinal : IEquatable<SaveEntityOrdinal>
{
    public const uint NullValue = uint.MaxValue;
    public SaveEntityOrdinal(uint value) => Value = value;
    public uint Value { get; }
    public bool IsNull => Value == NullValue;
    public bool Equals(SaveEntityOrdinal other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is SaveEntityOrdinal other && Equals(other);
    public override int GetHashCode() => unchecked((int)Value);
}

/// <summary>Cold world persistence identity and durable allocator high-water state.</summary>
public readonly struct PersistenceIdentityOptions : IEquatable<PersistenceIdentityOptions>
{
    public PersistenceIdentityOptions(PersistentWorldId worldId, ulong allocatorHighWater = 0)
    {
        if (!worldId.IsValid) throw new ArgumentException("A valid persistent world ID is required.", nameof(worldId));
        WorldId = worldId;
        AllocatorHighWater = allocatorHighWater;
    }
    public PersistentWorldId WorldId { get; }
    public ulong AllocatorHighWater { get; }
    public bool IsEnabled => WorldId.IsValid;
    public bool Equals(PersistenceIdentityOptions other) => WorldId == other.WorldId && AllocatorHighWater == other.AllocatorHighWater;
    public override bool Equals(object? obj) => obj is PersistenceIdentityOptions other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(WorldId, AllocatorHighWater);
    public static bool operator ==(PersistenceIdentityOptions left, PersistenceIdentityOptions right) => left.Equals(right);
    public static bool operator !=(PersistenceIdentityOptions left, PersistenceIdentityOptions right) => !left.Equals(right);
}
