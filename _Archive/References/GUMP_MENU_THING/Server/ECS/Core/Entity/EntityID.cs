
using System;

using Godot;

public enum EntityType : byte
{
    None = 0,
    Item = 1,
    Mobile = 2,
    Guild = 3,
    // Add more as needed
}

public readonly struct EntityID : IEquatable<EntityID>
{
    public EntityType Type { get; }
    public int Value { get; }

    public EntityID(EntityType type, int value)
    {
        Type = type;
        Value = value;
    }

    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public override bool Equals(object obj) => obj is EntityID other && Equals(other);
    public bool Equals(EntityID other) => Type == other.Type && Value == other.Value;

    public static bool operator ==(EntityID left, EntityID right) => left.Equals(right);
    public static bool operator !=(EntityID left, EntityID right) => !left.Equals(right);

    public override string ToString() => $"{Type}:{Value}";
}