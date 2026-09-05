using System.Runtime.CompilerServices;
using System.Text;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;

namespace UltraSim.Game.Chat;

/// <summary>
/// Packet-free authoritative Chat/Commands oracle for entered participants.
/// </summary>
/// <remarks>
/// The service owns interpretation, exact spatial hearing, and retained speech
/// occurrences. It owns no Network message, client state, presentation, channel,
/// or persistence policy.
/// </remarks>
public sealed class GameChatService : IDisposable
{
    private const int MaximumDisplayLabelUtf8Bytes = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ServerGameFacet _runtime;
    private readonly GameChatLimits _limits;
    private readonly GameSpeechDistanceProfile _speechProfile;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly GameChatCommandRegistry _commands;
    private readonly AdmissionResult[] _admissionScratch;
    private readonly AdmissionBinding[] _admissions;
    private readonly Dictionary<ParticipantId, AdmissionResult> _entered;
    private readonly Dictionary<Entity, ParticipantId> _participantsByEntity;
    private readonly Dictionary<ParticipantId, string> _fallbackDisplayLabels;
    private readonly SpatialChunkKey[] _chunkScratch;
    private readonly Entity[] _candidateScratch;
    private readonly ParticipantId[] _recipientScratch;
    private readonly GameSpeechFact[] _speechFacts;
    private readonly int[] _speechRecipientCounts;
    private readonly ParticipantId[] _speechRecipients;
    private readonly ServerGenerationStateOperation<IGameGenerationStateProvider, GameChatSubmissionResult>
        _submitOperation;

    private AdmissionResult _pendingSpeaker;
    private GameSpeechMode _pendingMode;
    private string? _pendingText;
    private int _speechHead;
    private int _speechCount;
    private int _pendingRecipientDeliveries;
    private int _speechHighWater;
    private ulong _nextSpeechSequence = 1;
    private bool _recipientRegistryComplete;
    private Exception? _fault;
    private bool _disposed;
    private long _submissions;
    private long _acceptedSpeech;
    private long _successfulCommands;
    private long _rejectedSubmissions;
    private long _candidateChunksExamined;
    private long _candidateEntitiesCopied;
    private long _exactDistanceTests;
    private long _recipientDeliveries;
    private long _participantCensuses;
    private long _participantCapacityFailures;

    public GameChatService(
        ServerGameFacet runtime,
        AdmissionService admission,
        GameChatLimits limits = default,
        GameSpeechDistanceProfile speechProfile = default)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ArgumentNullException.ThrowIfNull(admission);
        if (limits == default)
            limits = GameChatLimits.Default;
        if (speechProfile == default)
            speechProfile = GameSpeechDistanceProfile.Default;
        limits.Validate(speechProfile);
        _limits = limits;
        _speechProfile = speechProfile;
        _commands = new GameChatCommandRegistry(limits.MaximumCommands);
        _admissionScratch = new AdmissionResult[limits.MaximumParticipants];
        _admissions = new AdmissionBinding[limits.MaximumParticipants];
        _entered = new Dictionary<ParticipantId, AdmissionResult>(limits.MaximumParticipants);
        _participantsByEntity = new Dictionary<Entity, ParticipantId>(limits.MaximumParticipants);
        _fallbackDisplayLabels = new Dictionary<ParticipantId, string>(limits.MaximumParticipants);
        _entered.EnsureCapacity(limits.MaximumParticipants);
        _participantsByEntity.EnsureCapacity(limits.MaximumParticipants);
        _fallbackDisplayLabels.EnsureCapacity(limits.MaximumParticipants);
        _chunkScratch = new SpatialChunkKey[limits.MaximumCandidateChunks];
        _candidateScratch = new Entity[limits.MaximumCandidateEntities];
        _recipientScratch = new ParticipantId[limits.MaximumRecipientsPerSpeech];
        _speechFacts = new GameSpeechFact[limits.MaximumRetainedSpeechFacts];
        _speechRecipientCounts = new int[limits.MaximumRetainedSpeechFacts];
        _speechRecipients = new ParticipantId[checked(
            limits.MaximumRetainedSpeechFacts * limits.MaximumRecipientsPerSpeech)];
        _submitOperation = SubmitInsideGeneration;
        AddAdmission(admission, primary: true);
        _runtime.GenerationPublished += HandleGenerationPublished;
        _ = RefreshEnteredParticipantsCore();
    }

    public GameChatLimits Limits => _limits;
    public GameSpeechDistanceProfile SpeechProfile => _speechProfile;
    public char PlayerCommandPrefix => GameChatCommandRegistry.PlayerPrefix;
    public int CommandCount => _commands.Count;
    public int PendingSpeechCount { get { EnsureOperational(); return _speechCount; } }
    public ulong LatestAcceptedSpeechSequence
    {
        get { EnsureOperational(); return _nextSpeechSequence - 1; }
    }

    /// <summary>
    /// Adds one concrete authoritative Admission owner to the bounded Chat
    /// census. Hosted application sessions use one lease each; disposal removes
    /// the source once no lease remains.
    /// </summary>
    public GameChatAdmissionRegistration AttachAdmission(AdmissionService admission)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(admission);
        int slot = FindAdmission(admission);
        if (slot < 0)
        {
            Exception? priorFault = _fault;
            slot = AddAdmission(admission, primary: false);
            if (!RefreshEnteredParticipantsCore())
            {
                RemoveAdmission(slot);
                _fault = priorFault;
                if (!RefreshEnteredParticipantsCore())
                    throw new InvalidOperationException(
                        "Chat could not restore its prior Admission census after rejected attachment.");
                throw new InvalidOperationException("The Admission source exceeds Chat participant capacity.");
            }
        }
        _admissions[slot].Leases = checked(_admissions[slot].Leases + 1);
        return new GameChatAdmissionRegistration(this, admission);
    }

    /// <summary>Reads one current trusted entered binding without exposing mutable registry state.</summary>
    public bool TryGetEnteredParticipant(ParticipantId participant, out AdmissionResult session)
    {
        EnsureOperational();
        if (_entered.TryGetValue(participant, out session) && session.IsEntered)
            return true;
        session = default;
        return false;
    }

    /// <summary>
    /// Refreshes the trusted entered-participant registry from Admission. This is
    /// useful when Chat is created after participants have already entered.
    /// </summary>
    public bool RefreshEnteredParticipants()
    {
        EnsureOperational();
        return RefreshEnteredParticipantsCore();
    }

    /// <summary>
    /// Interprets trusted participant text. No caller-supplied sender name,
    /// position, radius, or recipient list exists on this authority surface.
    /// </summary>
    public GameChatSubmissionResult Submit(
        ParticipantId participant,
        string? text,
        GameSpeechMode mode = GameSpeechMode.Say)
    {
        EnsureOperational();
        _submissions++;
        if (_fault is not null)
            return Reject(GameChatSubmissionStatus.Faulted, _fault.Message);
        if (!participant.IsValid)
            return Reject(GameChatSubmissionStatus.InvalidParticipant, "The participant identity is invalid.");
        if (mode is < GameSpeechMode.Whisper or > GameSpeechMode.Yell)
            return Reject(GameChatSubmissionStatus.InvalidSpeechMode, "The speech mode is invalid.");
        GameChatSubmissionStatus textStatus = ValidateText(text, _limits.MaximumTextUtf8Bytes);
        if (textStatus != GameChatSubmissionStatus.None)
            return Reject(textStatus, TextFailure(textStatus));
        if (!_recipientRegistryComplete && !RefreshEnteredParticipantsCore())
        {
            return Reject(GameChatSubmissionStatus.RecipientRegistryIncomplete,
                "The entered-recipient registry exceeds Chat capacity.");
        }
        if (!_entered.TryGetValue(participant, out AdmissionResult session) || !session.IsEntered)
        {
            return Reject(GameChatSubmissionStatus.ParticipantNotEntered,
                "The participant is not entered with a trusted actor.");
        }

        _pendingSpeaker = session;
        _pendingMode = mode;
        _pendingText = text;
        try
        {
            if (!_runtime.TryUseActiveGeneration(_submitOperation, out GameChatSubmissionResult result))
            {
                return Reject(GameChatSubmissionStatus.RuntimeUnavailable,
                    "Chat requires a running authoritative Game generation.");
            }
            return result;
        }
        finally
        {
            _pendingSpeaker = default;
            _pendingMode = default;
            _pendingText = null;
        }
    }

    /// <summary>Copies the exact retained audience of one pending speech fact.</summary>
    public bool TryCopyRecipients(
        GameSpeechFactId fact,
        Span<ParticipantId> destination,
        out int count)
    {
        EnsureOperational();
        if (!TryFindSpeechSlot(fact, out int slot))
        {
            count = 0;
            return false;
        }
        count = _speechRecipientCounts[slot];
        if (destination.Length < count)
            return false;
        RecipientBlock(slot, count).CopyTo(destination);
        return true;
    }

    /// <summary>Peeks the oldest retained occurrence for transactional remote handoff.</summary>
    public bool TryPeekNextSpeech(out GameSpeechFact fact, out int recipientCount)
    {
        EnsureOperational();
        if (_speechCount == 0)
        {
            fact = default;
            recipientCount = 0;
            return false;
        }
        fact = _speechFacts[_speechHead];
        recipientCount = _speechRecipientCounts[_speechHead];
        return fact.IsValid && recipientCount > 0;
    }

    /// <summary>
    /// Consumes the oldest retained packet-free occurrence after atomically
    /// copying its complete recipient set.
    /// </summary>
    public bool TryConsumeNextSpeech(
        Span<ParticipantId> destination,
        out GameSpeechFact fact,
        out int recipientCount)
    {
        EnsureOperational();
        if (_speechCount == 0)
        {
            fact = default;
            recipientCount = 0;
            return false;
        }
        int slot = _speechHead;
        recipientCount = _speechRecipientCounts[slot];
        if (destination.Length < recipientCount)
        {
            fact = default;
            return false;
        }
        RecipientBlock(slot, recipientCount).CopyTo(destination);
        fact = _speechFacts[slot];
        _speechFacts[slot] = default;
        _speechRecipientCounts[slot] = 0;
        RecipientBlock(slot, recipientCount).Clear();
        _speechHead = (_speechHead + 1) % _speechFacts.Length;
        _speechCount--;
        _pendingRecipientDeliveries -= recipientCount;
        return true;
    }

    public GameChatSnapshot GetSnapshot()
    {
        EnsureOperational();
        long reserved = 0;
        reserved = checked(reserved + (long)_admissionScratch.Length * Unsafe.SizeOf<AdmissionResult>());
        reserved = checked(reserved + (long)_admissions.Length * Unsafe.SizeOf<AdmissionBinding>());
        reserved = checked(reserved + (long)_chunkScratch.Length * Unsafe.SizeOf<SpatialChunkKey>());
        reserved = checked(reserved + (long)_candidateScratch.Length * Unsafe.SizeOf<Entity>());
        reserved = checked(reserved + (long)_recipientScratch.Length * Unsafe.SizeOf<ParticipantId>());
        reserved = checked(reserved + (long)_speechFacts.Length * Unsafe.SizeOf<GameSpeechFact>());
        reserved = checked(reserved + (long)_speechRecipientCounts.Length * sizeof(int));
        reserved = checked(reserved + (long)_speechRecipients.Length * Unsafe.SizeOf<ParticipantId>());
        return new GameChatSnapshot(
            _entered.Count,
            _recipientRegistryComplete,
            _speechCount,
            _pendingRecipientDeliveries,
            _speechHighWater,
            _submissions,
            _acceptedSpeech,
            _successfulCommands,
            _rejectedSubmissions,
            _candidateChunksExamined,
            _candidateEntitiesCopied,
            _exactDistanceTests,
            _recipientDeliveries,
            _participantCensuses,
            _participantCapacityFailures,
            reserved);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;
        _disposed = true;
        for (int index = 0; index < _admissions.Length; index++)
        {
            AdmissionService? admission = _admissions[index].Service;
            if (admission is null) continue;
            admission.Entered -= HandleAdmissionEntered;
            admission.Rejected -= HandleAdmissionRejected;
        }
        _runtime.GenerationPublished -= HandleGenerationPublished;
        _entered.Clear();
        _participantsByEntity.Clear();
        _fallbackDisplayLabels.Clear();
        _speechFacts.AsSpan().Clear();
        _speechRecipients.AsSpan().Clear();
        _speechRecipientCounts.AsSpan().Clear();
        _pendingText = null;
        _fault = null;
    }

    private GameChatSubmissionResult SubmitInsideGeneration(
        EcsWorld world,
        ulong generationId,
        IGameGenerationStateProvider game)
    {
        AdmissionResult speaker = _pendingSpeaker;
        string text = _pendingText ?? throw new InvalidOperationException("Chat lost its pending text.");
        if (speaker.GenerationId != generationId || speaker.Entity.World != world.Id)
        {
            return Reject(GameChatSubmissionStatus.GenerationMismatch,
                "The trusted speaker belongs to another Game generation.");
        }
        if (!world.IsAlive(speaker.Entity))
        {
            return Reject(GameChatSubmissionStatus.MissingSpeaker,
                "The trusted speaker entity is stale or missing.");
        }

        if (text[0] == GameChatCommandRegistry.PlayerPrefix)
            return ExecuteCommand(world, game, speaker, text);
        if (!game.WorldState.TryReadPosition(world, speaker.Entity, out WorldPosition position))
        {
            return Reject(GameChatSubmissionStatus.MissingSpeakerPosition,
                "The trusted speaker has no authoritative world position.");
        }
        return RouteSpeech(world, generationId, game, speaker, position, text, _pendingMode);
    }

    private GameChatSubmissionResult ExecuteCommand(
        EcsWorld world,
        IGameGenerationStateProvider game,
        in AdmissionResult speaker,
        string text)
    {
        ReadOnlySpan<char> input = text.AsSpan(1);
        if (input.IsEmpty || char.IsWhiteSpace(input[0]))
        {
            return Reject(GameChatSubmissionStatus.InvalidCommandSyntax,
                "Command syntax is invalid.");
        }
        int nameLength = 0;
        while (nameLength < input.Length && !char.IsWhiteSpace(input[nameLength]))
            nameLength++;
        ReadOnlySpan<char> name = input[..nameLength];
        int remainder = nameLength;
        while (remainder < input.Length && char.IsWhiteSpace(input[remainder]))
            remainder++;
        if (!_commands.TryResolve(name, out GameChatCommandDefinition command))
        {
            return Reject(GameChatSubmissionStatus.UnknownCommand,
                "Unknown command. Use /help for available commands.");
        }
        if (remainder != input.Length)
        {
            return Reject(GameChatSubmissionStatus.InvalidCommandSyntax,
                $"/{command.Name} does not accept arguments.");
        }

        string reply;
        switch (command.Kind)
        {
            case GameChatCommandKind.Help:
                reply = _commands.HelpReply;
                break;
            case GameChatCommandKind.Where:
                if (!game.WorldState.TryReadPosition(world, speaker.Entity, out WorldPosition position))
                {
                    return Reject(GameChatSubmissionStatus.MissingSpeakerPosition,
                        "The trusted speaker has no authoritative world position.");
                }
                reply = $"Where: {position}";
                break;
            default:
                throw new InvalidOperationException("The finite command registry contains an unknown command kind.");
        }
        _successfulCommands++;
        return new GameChatSubmissionResult(
            GameChatSubmissionStatus.CommandSucceeded,
            default,
            0,
            reply);
    }

    private GameChatSubmissionResult RouteSpeech(
        EcsWorld world,
        ulong generationId,
        IGameGenerationStateProvider game,
        in AdmissionResult speaker,
        in WorldPosition speakerPosition,
        string text,
        GameSpeechMode mode)
    {
        int radius = _speechProfile.GetRadius(mode);
        SpatialChunkKey expectedHome = speakerPosition.ToChunkKey(GameWorldState.DefaultSpace);
        if (!game.WorldState.Index.TryGetHome(speaker.Entity, out SpatialChunkKey actualHome) ||
            actualHome != expectedHome)
        {
            return Reject(GameChatSubmissionStatus.SpatialIndexUnavailable,
                "The trusted speaker is not in the committed spatial index.");
        }
        if (!TryGetCandidateRange(speakerPosition, radius,
                out int minimumX, out int maximumX, out int minimumZ, out int maximumZ))
        {
            return Reject(GameChatSubmissionStatus.CandidateChunkCapacityExceeded,
                "The speech footprint is outside the addressable world.");
        }
        long width = (long)maximumX - minimumX + 1;
        long height = (long)maximumZ - minimumZ + 1;
        if (height <= 0 || width > _chunkScratch.Length / height)
        {
            return Reject(GameChatSubmissionStatus.CandidateChunkCapacityExceeded,
                "The speech footprint exceeds candidate-chunk capacity.");
        }

        int chunkCount = 0;
        int candidateCount = 0;
        long centerX = speakerPosition.GlobalXMillimeters;
        long centerZ = speakerPosition.GlobalZMillimeters;
        for (int chunkX = minimumX; ; chunkX++)
        {
            for (int chunkZ = minimumZ; ; chunkZ++)
            {
                var coord = new SpatialChunkCoord(chunkX, chunkZ);
                if (GameWorldSpatialProfile.ChunkGeometry.GetBounds(coord)
                    .IntersectsCircle(centerX, centerZ, radius))
                {
                    if (chunkCount == _chunkScratch.Length)
                    {
                        return Reject(GameChatSubmissionStatus.CandidateChunkCapacityExceeded,
                            "The speech footprint exceeds candidate-chunk capacity.");
                    }
                    var key = new SpatialChunkKey(GameWorldState.DefaultSpace, coord);
                    int members = game.WorldState.Index.GetMemberCount(key);
                    if (members > _candidateScratch.Length - candidateCount)
                    {
                        return Reject(GameChatSubmissionStatus.CandidateEntityCapacityExceeded,
                            "The speech footprint exceeds candidate-entity capacity.");
                    }
                    _chunkScratch[chunkCount++] = key;
                    candidateCount += members;
                }
                if (chunkZ == maximumZ)
                    break;
            }
            if (chunkX == maximumX)
                break;
        }

        int copied = 0;
        for (int index = 0; index < chunkCount; index++)
        {
            int count = game.WorldState.Index.CopyMembers(
                _chunkScratch[index], _candidateScratch.AsSpan(copied));
            copied += count;
        }
        if (copied != candidateCount)
            throw new InvalidOperationException("Spatial membership changed during one owner-thread Chat query.");

        int recipientCount = 0;
        bool foundSpeaker = false;
        Int128 radiusSquared = (Int128)radius * radius;
        for (int index = 0; index < candidateCount; index++)
        {
            Entity candidate = _candidateScratch[index];
            if (!_participantsByEntity.TryGetValue(candidate, out ParticipantId recipient))
                continue;
            if (!game.WorldState.TryReadPosition(world, candidate, out WorldPosition recipientPosition))
                throw new InvalidOperationException("An indexed entered actor lost authoritative position truth.");
            _exactDistanceTests++;
            if (SquaredDistance(speakerPosition, recipientPosition) > radiusSquared)
                continue;
            if (recipientCount == _recipientScratch.Length)
            {
                return Reject(GameChatSubmissionStatus.RecipientCapacityExceeded,
                    "The complete speech audience exceeds recipient capacity.");
            }
            _recipientScratch[recipientCount++] = recipient;
            foundSpeaker |= recipient == speaker.Participant;
        }
        _candidateChunksExamined = checked(_candidateChunksExamined + chunkCount);
        _candidateEntitiesCopied = checked(_candidateEntitiesCopied + candidateCount);
        if (!foundSpeaker)
        {
            return Reject(GameChatSubmissionStatus.SpatialIndexUnavailable,
                "The trusted speaker was absent from the committed entered-actor index.");
        }
        InsertionSortRecipients(_recipientScratch.AsSpan(0, recipientCount));

        if (_speechCount == _speechFacts.Length)
        {
            return Reject(GameChatSubmissionStatus.SpeechRetentionCapacityExceeded,
                "The retained speech occurrence queue is full.");
        }
        if (_nextSpeechSequence == ulong.MaxValue)
        {
            return Reject(GameChatSubmissionStatus.SpeechSequenceExhausted,
                "The speech occurrence sequence is exhausted.");
        }

        string displayLabel = ResolveDisplayLabel(game, speaker);
        _ = game.WorldState.Publications.TryGetPublicEntityId(
            speaker.Entity, out WorldPublicEntityId publicAnchor);
        var fact = new GameSpeechFact(
            new GameSpeechFactId(_nextSpeechSequence),
            generationId,
            world.TickCount,
            speaker.Participant,
            speaker.Entity,
            mode,
            text,
            displayLabel,
            publicAnchor);
        int slot = (_speechHead + _speechCount) % _speechFacts.Length;
        _recipientScratch.AsSpan(0, recipientCount).CopyTo(RecipientBlock(slot, recipientCount));
        _speechFacts[slot] = fact;
        _speechRecipientCounts[slot] = recipientCount;
        _speechCount++;
        _pendingRecipientDeliveries += recipientCount;
        _speechHighWater = Math.Max(_speechHighWater, _speechCount);
        _nextSpeechSequence++;
        _acceptedSpeech++;
        _recipientDeliveries = checked(_recipientDeliveries + recipientCount);
        return new GameChatSubmissionResult(
            GameChatSubmissionStatus.SpeechAccepted,
            fact,
            recipientCount,
            null);
    }

    private bool RefreshEnteredParticipantsCore()
    {
        _participantCensuses++;
        _entered.Clear();
        _participantsByEntity.Clear();
        _fallbackDisplayLabels.Clear();
        int total = 0;
        for (int source = 0; source < _admissions.Length; source++)
        {
            AdmissionService? admission = _admissions[source].Service;
            if (admission is null) continue;
            if (!admission.TryCopyEntered(_admissionScratch.AsSpan(total), out int count) ||
                count > _admissionScratch.Length - total)
            {
                _recipientRegistryComplete = false;
                _participantCapacityFailures++;
                return false;
            }
            total += count;
        }
        for (int index = 0; index < total; index++)
        {
            AdmissionResult session = _admissionScratch[index];
            if (!session.IsEntered || !session.Participant.IsValid || !session.Entity.IsValid ||
                _entered.ContainsKey(session.Participant) || _participantsByEntity.ContainsKey(session.Entity))
            {
                _fault ??= new InvalidOperationException(
                    "Admission sources published contradictory entered-participant bindings.");
                _recipientRegistryComplete = false;
                return false;
            }
            _entered.Add(session.Participant, session);
            _participantsByEntity.Add(session.Entity, session.Participant);
            _fallbackDisplayLabels.Add(
                session.Participant, ResolveAdmissionDisplayLabel(session));
        }
        _admissionScratch.AsSpan(0, total).Clear();
        _recipientRegistryComplete = true;
        return true;
    }

    private bool UpdateParticipant(in AdmissionResult session)
    {
        if (!session.IsEntered || !session.Participant.IsValid || !session.Entity.IsValid)
            return false;
        if (_entered.TryGetValue(session.Participant, out AdmissionResult previous))
        {
            if (previous == session)
                return true;
            _participantsByEntity.Remove(previous.Entity);
        }
        else if (_entered.Count == _limits.MaximumParticipants)
        {
            _recipientRegistryComplete = false;
            _participantCapacityFailures++;
            return false;
        }
        if (_participantsByEntity.TryGetValue(session.Entity, out ParticipantId owner) &&
            owner != session.Participant)
        {
            _fault ??= new InvalidOperationException(
                "Two entered participants claim the same trusted actor.");
            _recipientRegistryComplete = false;
            return false;
        }
        _entered[session.Participant] = session;
        _participantsByEntity[session.Entity] = session.Participant;
        _fallbackDisplayLabels[session.Participant] = ResolveAdmissionDisplayLabel(session);
        return true;
    }

    private void HandleAdmissionEntered(AdmissionResult result)
    {
        if (_disposed)
            return;
        try
        {
            _ = result;
            _ = RefreshEnteredParticipantsCore();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { _fault ??= exception; }
    }

    private void HandleAdmissionRejected(AdmissionResult result)
    {
        if (_disposed)
            return;
        try
        {
            // Rejected is an operation outcome, not a lifecycle transition.
            // Reconcile the complete bounded authoritative census because the
            // affected participant may belong to any attached Admission owner.
            _ = result;
            _ = RefreshEnteredParticipantsCore();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _fault ??= exception;
        }
    }

    private void HandleGenerationPublished(ulong generationId)
    {
        if (_disposed)
            return;
        try
        {
            // Accepted speech is Event history. Generation-local public identity
            // is retained only as presentation metadata and may fail to realize
            // an overhead bubble after replacement; it does not erase the fact.
            _ = RefreshEnteredParticipantsCore();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { _fault ??= exception; }
    }

    private string ResolveDisplayLabel(
        IGameGenerationStateProvider game,
        in AdmissionResult speaker)
    {
        if (speaker.CharacterId.IsValid &&
            game.Accounting.TryGetCharacter(speaker.CharacterId, out var character) &&
            IsCanonicalBoundedUtf8(character.DisplayName, MaximumDisplayLabelUtf8Bytes))
            return character.DisplayName;
        if (_fallbackDisplayLabels.TryGetValue(speaker.Participant, out string? fallback))
            return fallback;
        throw new InvalidOperationException("An entered Chat speaker lost its bounded display label.");
    }

    private static string ResolveAdmissionDisplayLabel(in AdmissionResult session) =>
        session.GuestIdentity.IsValid
            ? session.GuestIdentity.DisplayName!
            : $"Player {session.Participant.Value}";

    private static bool IsCanonicalBoundedUtf8(string value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return StrictUtf8.GetByteCount(value) <= maximumBytes; }
        catch (EncoderFallbackException) { return false; }
    }

    private bool TryFindSpeechSlot(GameSpeechFactId fact, out int slot)
    {
        if (!fact.IsValid)
        {
            slot = -1;
            return false;
        }
        for (int offset = 0; offset < _speechCount; offset++)
        {
            int candidate = (_speechHead + offset) % _speechFacts.Length;
            if (_speechFacts[candidate].Id == fact)
            {
                slot = candidate;
                return true;
            }
        }
        slot = -1;
        return false;
    }

    internal void ReleaseAdmission(AdmissionService admission)
    {
        EnsureOwnerThread();
        if (_disposed) return;
        int slot = FindAdmission(admission);
        if (slot < 0 || _admissions[slot].Leases == 0)
            return;
        _admissions[slot].Leases--;
        if (_admissions[slot].Leases == 0 && !_admissions[slot].Primary)
        {
            RemoveAdmission(slot);
            _ = RefreshEnteredParticipantsCore();
        }
    }

    private int AddAdmission(AdmissionService admission, bool primary)
    {
        int existing = FindAdmission(admission);
        if (existing >= 0) return existing;
        for (int index = 0; index < _admissions.Length; index++)
        {
            if (_admissions[index].Service is not null) continue;
            _admissions[index] = new AdmissionBinding(admission, primary, 0);
            admission.Entered += HandleAdmissionEntered;
            admission.Rejected += HandleAdmissionRejected;
            return index;
        }
        throw new InvalidOperationException("The bounded Chat Admission-source table is full.");
    }

    private int FindAdmission(AdmissionService admission)
    {
        for (int index = 0; index < _admissions.Length; index++)
            if (ReferenceEquals(_admissions[index].Service, admission)) return index;
        return -1;
    }

    private void RemoveAdmission(int slot)
    {
        AdmissionService? admission = _admissions[slot].Service;
        if (admission is null) return;
        admission.Entered -= HandleAdmissionEntered;
        admission.Rejected -= HandleAdmissionRejected;
        _admissions[slot] = default;
    }

    private struct AdmissionBinding
    {
        internal AdmissionBinding(AdmissionService service, bool primary, int leases) =>
            (Service, Primary, Leases) = (service, primary, leases);
        internal AdmissionService? Service;
        internal bool Primary;
        internal int Leases;
    }

    private Span<ParticipantId> RecipientBlock(int slot, int count) =>
        _speechRecipients.AsSpan(checked(slot * _limits.MaximumRecipientsPerSpeech), count);

    private GameChatSubmissionResult Reject(GameChatSubmissionStatus status, string message)
    {
        _rejectedSubmissions++;
        return new GameChatSubmissionResult(status, default, 0, message);
    }

    private static GameChatSubmissionStatus ValidateText(string? text, int maximumUtf8Bytes)
    {
        if (string.IsNullOrEmpty(text))
            return GameChatSubmissionStatus.EmptyText;
        bool hasNonWhitespace = false;
        long utf8Bytes = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char value = text[index];
            hasNonWhitespace |= !char.IsWhiteSpace(value);
            if (char.IsHighSurrogate(value))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                    return GameChatSubmissionStatus.MalformedUnicode;
                utf8Bytes += 4;
                index++;
            }
            else if (char.IsLowSurrogate(value))
            {
                return GameChatSubmissionStatus.MalformedUnicode;
            }
            else
            {
                utf8Bytes += value <= 0x7F ? 1 : value <= 0x7FF ? 2 : 3;
            }
        }
        if (!hasNonWhitespace)
            return GameChatSubmissionStatus.EmptyText;
        return utf8Bytes > maximumUtf8Bytes
            ? GameChatSubmissionStatus.TextTooLong
            : GameChatSubmissionStatus.None;
    }

    private static string TextFailure(GameChatSubmissionStatus status) => status switch
    {
        GameChatSubmissionStatus.EmptyText => "Chat text is empty or whitespace-only.",
        GameChatSubmissionStatus.MalformedUnicode => "Chat text contains malformed Unicode.",
        GameChatSubmissionStatus.TextTooLong => "Chat text exceeds the UTF-8 byte limit.",
        _ => "Chat text is invalid.",
    };

    private static bool TryGetCandidateRange(
        in WorldPosition anchor,
        int radius,
        out int minimumX,
        out int maximumX,
        out int minimumZ,
        out int maximumZ)
    {
        int edge = GameWorldSpatialProfile.WorldChunkEdgeUnits;
        SpatialMath.FloorDivRem(checked(anchor.GlobalXMillimeters - radius), edge,
            out long wideMinimumX, out int minimumRemainderX);
        SpatialMath.FloorDivRem(checked(anchor.GlobalXMillimeters + radius), edge,
            out long wideMaximumX, out _);
        SpatialMath.FloorDivRem(checked(anchor.GlobalZMillimeters - radius), edge,
            out long wideMinimumZ, out int minimumRemainderZ);
        SpatialMath.FloorDivRem(checked(anchor.GlobalZMillimeters + radius), edge,
            out long wideMaximumZ, out _);
        if (minimumRemainderX == 0)
            wideMinimumX--;
        if (minimumRemainderZ == 0)
            wideMinimumZ--;
        minimumX = checked((int)Math.Max(wideMinimumX, int.MinValue));
        maximumX = checked((int)Math.Min(wideMaximumX, int.MaxValue));
        minimumZ = checked((int)Math.Max(wideMinimumZ, int.MinValue));
        maximumZ = checked((int)Math.Min(wideMaximumZ, int.MaxValue));
        return minimumX <= maximumX && minimumZ <= maximumZ;
    }

    private static Int128 SquaredDistance(in WorldPosition left, in WorldPosition right)
    {
        Int128 deltaX = (Int128)left.GlobalXMillimeters - right.GlobalXMillimeters;
        Int128 deltaZ = (Int128)left.GlobalZMillimeters - right.GlobalZMillimeters;
        return checked(deltaX * deltaX + deltaZ * deltaZ);
    }

    private static void InsertionSortRecipients(Span<ParticipantId> recipients)
    {
        for (int index = 1; index < recipients.Length; index++)
        {
            ParticipantId value = recipients[index];
            int destination = index;
            while (destination > 0 && recipients[destination - 1].CompareTo(value) > 0)
            {
                recipients[destination] = recipients[destination - 1];
                destination--;
            }
            recipients[destination] = value;
        }
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Chat is bound to its creating Game owner thread.");
    }
}
