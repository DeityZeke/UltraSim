using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.World;

namespace UltraSim.Game.Chat;

/// <summary>Current Game-owned spatial speech modes.</summary>
public enum GameSpeechMode : byte
{
    Whisper = 1,
    Say = 2,
    Yell = 3,
}

/// <summary>Exact current-game X/Z hearing radii in authoritative millimeters.</summary>
public readonly record struct GameSpeechDistanceProfile(
    int WhisperMillimeters,
    int SayMillimeters,
    int YellMillimeters)
{
    public static GameSpeechDistanceProfile Default => new(2_000, 15_000, 30_000);

    public bool IsValid => WhisperMillimeters > 0 &&
        WhisperMillimeters < SayMillimeters && SayMillimeters < YellMillimeters;

    public int GetRadius(GameSpeechMode mode) => mode switch
    {
        GameSpeechMode.Whisper => WhisperMillimeters,
        GameSpeechMode.Say => SayMillimeters,
        GameSpeechMode.Yell => YellMillimeters,
        _ => 0,
    };
}

/// <summary>Bounded storage and work limits for one packet-free Chat oracle.</summary>
public readonly record struct GameChatLimits(
    int MaximumParticipants,
    int MaximumCandidateChunks,
    int MaximumCandidateEntities,
    int MaximumRecipientsPerSpeech,
    int MaximumRetainedSpeechFacts,
    int MaximumTextUtf8Bytes,
    int MaximumCommands)
{
    public static GameChatLimits Default => new(
        MaximumParticipants: 256,
        MaximumCandidateChunks: 25,
        MaximumCandidateEntities: 4_096,
        MaximumRecipientsPerSpeech: 256,
        MaximumRetainedSpeechFacts: 64,
        MaximumTextUtf8Bytes: 256,
        MaximumCommands: 8);

    public void Validate(GameSpeechDistanceProfile profile)
    {
        if (!profile.IsValid)
            throw new ArgumentException("The speech-distance profile is invalid.", nameof(profile));
        if (MaximumParticipants <= 0 || MaximumCandidateChunks <= 0 ||
            MaximumCandidateEntities <= 0 || MaximumRecipientsPerSpeech <= 0 ||
            MaximumRetainedSpeechFacts <= 0 || MaximumTextUtf8Bytes <= 0 ||
            MaximumCommands < 2 || MaximumRecipientsPerSpeech > MaximumParticipants ||
            MaximumCandidateEntities < MaximumParticipants)
        {
            throw new ArgumentOutOfRangeException(nameof(GameChatLimits));
        }

        long edge = World.GameWorldSpatialProfile.WorldChunkEdgeUnits;
        long maximumAxisChunks = checked((long)profile.YellMillimeters * 2 / edge + 2);
        if (checked(maximumAxisChunks * maximumAxisChunks) > MaximumCandidateChunks)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCandidateChunks),
                "Candidate-chunk capacity cannot cover the configured Yell radius.");
        }
        _ = checked(MaximumRetainedSpeechFacts * MaximumRecipientsPerSpeech);
    }
}

/// <summary>Monotonic identity of one independently meaningful accepted speech occurrence.</summary>
public readonly struct GameSpeechFactId : IEquatable<GameSpeechFactId>, IComparable<GameSpeechFactId>
{
    public GameSpeechFactId(ulong value)
    {
        if (value == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(GameSpeechFactId other) => Value.CompareTo(other.Value);
    public bool Equals(GameSpeechFactId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is GameSpeechFactId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"Speech({Value})" : "Speech(Invalid)";
    public static bool operator ==(GameSpeechFactId left, GameSpeechFactId right) => left.Equals(right);
    public static bool operator !=(GameSpeechFactId left, GameSpeechFactId right) => !left.Equals(right);
}

/// <summary>One accepted server-created spatial speech occurrence.</summary>
public readonly record struct GameSpeechFact(
    GameSpeechFactId Id,
    ulong GenerationId,
    ulong SimulationTick,
    ParticipantId Speaker,
    Entity SpeakerEntity,
    GameSpeechMode Mode,
    string Text,
    string DisplayLabel,
    WorldPublicEntityId PublicAnchor)
{
    public bool IsValid => Id.IsValid && GenerationId != 0 && Speaker.IsValid &&
        SpeakerEntity.IsValid && Mode is >= GameSpeechMode.Whisper and <= GameSpeechMode.Yell &&
        !string.IsNullOrWhiteSpace(Text) && !string.IsNullOrWhiteSpace(DisplayLabel);
}

/// <summary>Expected semantic outcome of one trusted packet-free text submission.</summary>
public enum GameChatSubmissionStatus : byte
{
    None = 0,
    SpeechAccepted = 1,
    CommandSucceeded = 2,
    EmptyText = 3,
    MalformedUnicode = 4,
    TextTooLong = 5,
    InvalidSpeechMode = 6,
    InvalidParticipant = 7,
    ParticipantNotEntered = 8,
    RuntimeUnavailable = 9,
    GenerationMismatch = 10,
    MissingSpeaker = 11,
    MissingSpeakerPosition = 12,
    SpatialIndexUnavailable = 13,
    RecipientRegistryIncomplete = 14,
    CandidateChunkCapacityExceeded = 15,
    CandidateEntityCapacityExceeded = 16,
    RecipientCapacityExceeded = 17,
    SpeechRetentionCapacityExceeded = 18,
    SpeechSequenceExhausted = 19,
    UnknownCommand = 20,
    InvalidCommandSyntax = 21,
    Faulted = 22,
}

/// <summary>Speech fact or private command response produced by one submission.</summary>
public readonly record struct GameChatSubmissionResult(
    GameChatSubmissionStatus Status,
    GameSpeechFact Speech,
    int RecipientCount,
    string? PrivateReply)
{
    public bool AcceptedSpeech => Status == GameChatSubmissionStatus.SpeechAccepted &&
        Speech.IsValid && RecipientCount > 0 && PrivateReply is null;
    public bool CommandSucceeded => Status == GameChatSubmissionStatus.CommandSucceeded &&
        !Speech.IsValid && RecipientCount == 0 && PrivateReply is not null;
    public bool IsCommandResult => CommandSucceeded ||
        (Status is GameChatSubmissionStatus.UnknownCommand or GameChatSubmissionStatus.InvalidCommandSyntax &&
            !Speech.IsValid && RecipientCount == 0 && PrivateReply is not null);
}

/// <summary>Bounded diagnostics for packet-free Chat semantics and work.</summary>
public readonly record struct GameChatSnapshot(
    int EnteredParticipants,
    bool RecipientRegistryComplete,
    int PendingSpeechFacts,
    int PendingRecipientDeliveries,
    int PendingSpeechHighWater,
    long Submissions,
    long AcceptedSpeech,
    long SuccessfulCommands,
    long RejectedSubmissions,
    long CandidateChunksExamined,
    long CandidateEntitiesCopied,
    long ExactDistanceTests,
    long RecipientDeliveries,
    long ParticipantCensuses,
    long ParticipantCapacityFailures,
    long KnownArrayReservedBytes);
