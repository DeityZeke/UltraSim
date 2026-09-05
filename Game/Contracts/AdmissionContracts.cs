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

/// <summary>Finite Game-owned body choices admitted by the Round-1 guest/demo policy.</summary>
public enum GameGuestBody : byte
{
    None = 0,
    HumanFemale = 1,
    HumanMale = 2,
}

/// <summary>Untrusted guest-entry intent. Admission validates and canonicalizes this value.</summary>
public readonly record struct GameGuestAdmissionRequest(
    GameGuestBody Body,
    string? DisplayName);

/// <summary>Canonical session-scoped guest identity accepted by authoritative Admission.</summary>
public readonly record struct GameGuestIdentity(
    GameGuestBody Body,
    string? DisplayName)
{
    public bool IsValid =>
        Body is GameGuestBody.HumanFemale or GameGuestBody.HumanMale &&
        !string.IsNullOrWhiteSpace(DisplayName);
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

/// <summary>
/// Optional concrete Game seam for accountless guest bodies. Admission owns validation and the
/// binder owns which compiled playable definition realizes an accepted finite body identity.
/// </summary>
public interface IGuestAdmissionEntityBinder : IAdmissionEntityBinder
{
    bool TryCreateGuestPlayable(
        EcsWorld world,
        ParticipantId participant,
        GameGuestBody body,
        out Entity entity);
}
