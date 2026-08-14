using UltraSim.ECS;
using UltraSim.Game.Accounting;

namespace UltraSim.Game.Admission;

/// <summary>Stable participant identity shared by game composition and Admission.</summary>
public readonly struct ParticipantId : IEquatable<ParticipantId>, IComparable<ParticipantId>
{
    public ParticipantId(ulong value)
    {
        if (value == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(ParticipantId other) => Value.CompareTo(other.Value);
    public bool Equals(ParticipantId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ParticipantId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"Participant({Value})" : "Participant(Invalid)";
    public static bool operator ==(ParticipantId left, ParticipantId right) => left.Equals(right);
    public static bool operator !=(ParticipantId left, ParticipantId right) => !left.Equals(right);
}

/// <summary>
/// Narrow game-policy seam for creating and validating playable ECS entities.
/// It is separate from the Admission/UI implementation so authoritative game
/// composition does not acquire a client dependency.
/// </summary>
public interface IAdmissionEntityBinder
{
    bool TryCreatePlayable(EcsWorld world, ParticipantId participant, out Entity entity);

    bool IsPlayable(EcsWorld world, in CharacterRecord character, Entity resolvedEntity);
}
