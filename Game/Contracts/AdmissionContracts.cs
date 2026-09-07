using UltraSim.ECS;
using UltraSim.Game.Accounting;
using System.Text;

namespace UltraSim.Game.Admission;

/// <summary>Expected fail-closed reason that an admission or character action was rejected.</summary>
public enum AdmissionRejection : byte
{
    None = 0,
    RuntimeNotRunning = 1,
    InvalidParticipant = 2,
    DuplicateParticipant = 3,
    AccountingUnavailable = 4,
    TrustedIdentityUnknown = 5,
    AccountDisabled = 6,
    UiUnavailable = 7,
    EntityCreationFailed = 8,
    CharacterNotOwned = 9,
    DurableIdentityUnavailable = 10,
    DurableWorldMismatch = 11,
    DurableEntityMissing = 12,
    PlayableBindingRejected = 13,
    InvalidCharacterSelectAction = 14,
    PlayableAlreadyBound = 15,
    InvalidSessionStage = 16,
    GenerationReplaced = 17,
    InvalidGuestDisplayName = 18,
    UnsupportedGuestBody = 19,
}

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
/// Finite deterministic guest normalization shared by immediate client feedback and authoritative
/// Admission. The authority always validates the untrusted request again.
/// </summary>
public static class GameGuestAdmissionPolicy
{
    public const int MaximumDisplayNameScalars = 24;
    public const int MaximumDisplayNameUtf8Bytes = 96;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryNormalize(
        in GameGuestAdmissionRequest request,
        out GameGuestIdentity identity,
        out AdmissionRejection rejection,
        out string? message)
    {
        identity = default;
        rejection = AdmissionRejection.None;
        message = null;
        if (request.Body is not (GameGuestBody.HumanFemale or GameGuestBody.HumanMale))
        {
            rejection = AdmissionRejection.UnsupportedGuestBody;
            message = "Select one of the supported demo bodies.";
            return false;
        }
        string? supplied = request.DisplayName;
        if (supplied is null)
        {
            rejection = AdmissionRejection.InvalidGuestDisplayName;
            message = "Enter a display name.";
            return false;
        }
        foreach (char value in supplied)
        {
            if (!char.IsControl(value)) continue;
            rejection = AdmissionRejection.InvalidGuestDisplayName;
            message = "Display names cannot contain control characters.";
            return false;
        }
        string normalized = supplied.Trim();
        if (normalized.Length == 0)
        {
            rejection = AdmissionRejection.InvalidGuestDisplayName;
            message = "Enter a display name.";
            return false;
        }
        int scalarCount = 0;
        try
        {
            foreach (Rune _ in normalized.EnumerateRunes()) scalarCount++;
            if (scalarCount > MaximumDisplayNameScalars ||
                StrictUtf8.GetByteCount(normalized) > MaximumDisplayNameUtf8Bytes)
            {
                rejection = AdmissionRejection.InvalidGuestDisplayName;
                message = $"Display names are limited to {MaximumDisplayNameScalars} characters.";
                return false;
            }
        }
        catch (EncoderFallbackException)
        {
            rejection = AdmissionRejection.InvalidGuestDisplayName;
            message = "The display name contains malformed Unicode.";
            return false;
        }
        identity = new GameGuestIdentity(request.Body, normalized);
        return true;
    }
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
