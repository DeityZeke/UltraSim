using UltraSim.Client.Core;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Server.Runtime;
using UltraSim.UI;

namespace UltraSim.Game.Admission;

/// <summary>Stable participant identity for the local S0.5 admission fixture.</summary>
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

public enum AdmissionStage : byte
{
    WaitingForCharacter = 0,
    Entered = 1,
    Rejected = 2,
}

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
}

/// <summary>Outcome or retained state of one admission attempt.</summary>
public readonly record struct AdmissionResult(
    ParticipantId Participant,
    AdmissionStage Stage,
    Entity Entity,
    AccountId AccountId,
    CharacterId CharacterId,
    ulong GenerationId,
    UiViewInstanceId CharacterSelectView,
    AdmissionRejection Rejection,
    string? Message)
{
    public bool IsEntered => Stage == AdmissionStage.Entered;

    public bool IsRejected => Stage == AdmissionStage.Rejected;

    public bool IsWaitingForCharacter => Stage == AdmissionStage.WaitingForCharacter;
}

/// <summary>
/// Game policy seam for creating and validating playable ECS entities. The
/// admission service never invents an archetype or duplicates player state.
/// </summary>
/// <remarks>
/// A <see langword="false"/> create result must leave the world unchanged. A
/// successful result transfers one newly created live entity to Admission.
/// Unexpected exceptions cross the ServerGameFacet terminal-fault boundary.
/// </remarks>
public interface IAdmissionEntityBinder
{
    bool TryCreatePlayable(EcsWorld world, ParticipantId participant, out Entity entity);

    bool IsPlayable(EcsWorld world, in CharacterRecord character, Entity resolvedEntity);
}

/// <summary>Semantic UI operations required by account-backed admission.</summary>
public interface IAdmissionUiPort
{
    event Action<UiActionRequest>? ActionSubmitted;

    UiViewInstanceId OpenView(UiOpenView operation);

    UiViewInstanceId OpenHotView(UiHotOpenView operation);

    void UpdateView(UiUpdateView operation);

    bool CloseView(UiCloseView operation);
}

/// <summary>Adapter from the S0.4 local semantic link to Admission's UI port.</summary>
public sealed class LocalAdmissionUiPort : IAdmissionUiPort, IDisposable
{
    private readonly LocalSemanticLink _link;
    private bool _disposed;

    public LocalAdmissionUiPort(LocalSemanticLink link)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _link.ActionSubmitted += ForwardAction;
    }

    public event Action<UiActionRequest>? ActionSubmitted;

    public UiViewInstanceId OpenView(UiOpenView operation)
    {
        EnsureOperational();
        return _link.OpenView(operation);
    }

    public UiViewInstanceId OpenHotView(UiHotOpenView operation)
    {
        EnsureOperational();
        return _link.OpenHotView(operation);
    }

    public void UpdateView(UiUpdateView operation)
    {
        EnsureOperational();
        _link.UpdateView(operation);
    }

    public bool CloseView(UiCloseView operation)
    {
        EnsureOperational();
        return _link.CloseView(operation);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _link.ActionSubmitted -= ForwardAction;
        ActionSubmitted = null;
    }

    private void ForwardAction(UiActionRequest request)
    {
        if (!_disposed)
            ActionSubmitted?.Invoke(request);
    }

    private void EnsureOperational()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LocalAdmissionUiPort));
    }
}

/// <summary>Stable identity used for exact CharacterSelect definition caching.</summary>
public sealed class AdmissionOptions
{
    public AdmissionOptions(
        string serverIdentity = "local-server",
        string compositionIdentity = "game-composition",
        ushort protocolVersion = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(compositionIdentity);
        if (protocolVersion == 0)
            throw new ArgumentOutOfRangeException(nameof(protocolVersion));
        ServerIdentity = serverIdentity.Trim();
        CompositionIdentity = compositionIdentity.Trim();
        ProtocolVersion = protocolVersion;
    }

    public string ServerIdentity { get; }

    public string CompositionIdentity { get; }

    public ushort ProtocolVersion { get; }
}

/// <summary>
/// S0.5 admission service. It supports accountless instant entry and an
/// account-backed CharacterSelect flow over the same active ServerRuntime.
/// </summary>
public sealed class AdmissionService : IDisposable
{
    private readonly ServerGameFacet _runtime;
    private readonly IAdmissionEntityBinder _binder;
    private readonly AccountingStore? _accounting;
    private readonly IAdmissionUiPort? _ui;
    private readonly UiDefinitionCacheKey _characterSelectCacheKey;
    private readonly Dictionary<ParticipantId, Session> _sessions = [];
    private readonly Dictionary<UiViewInstanceId, ParticipantId> _characterSelectViews = [];
    private readonly int _ownerThreadId;
    private bool _disposed;

    public AdmissionService(
        ServerGameFacet runtime,
        IAdmissionEntityBinder binder,
        AccountingStore? accounting = null,
        IAdmissionUiPort? ui = null,
        AdmissionOptions? options = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        _accounting = accounting;
        _ui = ui;
        options ??= new AdmissionOptions();
        _characterSelectCacheKey = new UiDefinitionCacheKey(
            options.ServerIdentity,
            options.CompositionIdentity,
            options.ProtocolVersion,
            CharacterSelectUi.Definition.Key,
            CharacterSelectUi.Definition.Version,
            CharacterSelectUi.Definition.Hash);
        _ownerThreadId = Environment.CurrentManagedThreadId;
        if (_ui is not null)
            _ui.ActionSubmitted += HandleAction;
    }

    public event Action<AdmissionResult>? Entered;

    public event Action<AdmissionResult>? Rejected;

    public int SessionCount
    {
        get
        {
            EnsureOperational();
            return _sessions.Count;
        }
    }

    public AdmissionResult JoinAccountless(ParticipantId participant)
    {
        EnsureOperational();
        if (!participant.IsValid)
            return RejectNew(participant, AdmissionRejection.InvalidParticipant, "The participant ID is invalid.");
        if (_sessions.ContainsKey(participant))
        {
            return RejectNew(participant, AdmissionRejection.DuplicateParticipant,
                "The participant already has an admission session.");
        }
        if (!_runtime.TryUseActiveGeneration(
            (world, generationId) =>
            {
                bool created = _binder.TryCreatePlayable(world, participant, out Entity entity) &&
                    entity.IsValid &&
                    entity.World == world.Id &&
                    world.IsAlive(entity);
                return new WorldBindingResult(created, entity, generationId);
            },
            out WorldBindingResult binding))
        {
            return RejectNew(participant, AdmissionRejection.RuntimeNotRunning,
                "Admission requires a running ServerRuntime.");
        }
        if (!binding.Success)
        {
            return RejectNew(participant, AdmissionRejection.EntityCreationFailed,
                "The game policy could not create a live playable ECS entity.");
        }
        if (IsPlayableAlreadyBound(binding.Entity))
        {
            return RejectNew(participant, AdmissionRejection.PlayableAlreadyBound,
                "The game policy returned an entity that is already controlled by another participant.");
        }

        var session = new Session(participant)
        {
            Stage = AdmissionStage.Entered,
            Entity = binding.Entity,
            GenerationId = binding.GenerationId,
        };
        _sessions.Add(participant, session);
        AdmissionResult result = session.Snapshot();
        Entered?.Invoke(result);
        return result;
    }

    public AdmissionResult JoinAccountBacked(
        ParticipantId participant,
        TrustedLocalIdentity trustedIdentity)
    {
        EnsureOperational();
        if (!participant.IsValid)
            return RejectNew(participant, AdmissionRejection.InvalidParticipant, "The participant ID is invalid.");
        if (_sessions.ContainsKey(participant))
        {
            return RejectNew(participant, AdmissionRejection.DuplicateParticipant,
                "The participant already has an admission session.");
        }
        if (_accounting is null)
        {
            return RejectNew(participant, AdmissionRejection.AccountingUnavailable,
                "This composition does not enable Accounting.");
        }
        if (!_accounting.TryResolveAccount(trustedIdentity, out AccountRecord account))
        {
            return RejectNew(participant, AdmissionRejection.TrustedIdentityUnknown,
                "The trusted local identity does not resolve to an account.");
        }
        if (!account.IsEnabled)
        {
            return RejectNew(participant, AdmissionRejection.AccountDisabled,
                "The resolved account is disabled.");
        }
        if (_ui is null)
        {
            return RejectNew(participant, AdmissionRejection.UiUnavailable,
                "Account-backed admission requires a CharacterSelect UI port.");
        }
        if (!_runtime.TryUseActiveGeneration(
            static (_, generationId) => generationId,
            out ulong generationId))
        {
            return RejectNew(participant, AdmissionRejection.RuntimeNotRunning,
                "Admission requires a running ServerRuntime.");
        }

        IReadOnlyList<CharacterRecord> characters = _accounting.GetCharacters(account.AccountId);
        if (characters.Count > CharacterSelectUi.Definition.Limits.MaximumRowsPerList)
        {
            return RejectNew(participant, AdmissionRejection.UiUnavailable,
                "The account has more characters than CharacterSelect can represent.");
        }
        UiModel model = CreateCharacterSelectModel(account, characters);
        UiViewInstanceId view;
        try
        {
            view = _ui.OpenView(new UiOpenView(
                _characterSelectCacheKey,
                CharacterSelectUi.Definition,
                model,
                sequence: 1));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return RejectNew(participant, AdmissionRejection.UiUnavailable,
                $"CharacterSelect could not be opened: {exception.Message}");
        }
        if (!view.IsValid || _characterSelectViews.ContainsKey(view))
        {
            return RejectNew(participant, AdmissionRejection.UiUnavailable,
                "CharacterSelect returned an invalid or already-owned view instance.");
        }

        var session = new Session(participant)
        {
            Stage = AdmissionStage.WaitingForCharacter,
            AccountId = account.AccountId,
            GenerationId = generationId,
            CharacterSelectView = view,
        };
        _sessions.Add(participant, session);
        _characterSelectViews.Add(view, participant);
        return session.Snapshot();
    }

    /// <summary>Reopens a retained account-backed selector through the hot path.</summary>
    public AdmissionResult ReopenCharacterSelect(ParticipantId participant)
    {
        EnsureOperational();
        if (!_sessions.TryGetValue(participant, out Session? session) || !session.AccountId.IsValid)
        {
            return RejectNew(participant, AdmissionRejection.TrustedIdentityUnknown,
                "The participant has no account-backed admission session.");
        }
        if (session.Stage != AdmissionStage.WaitingForCharacter)
        {
            return RejectNew(participant, AdmissionRejection.InvalidSessionStage,
                "CharacterSelect can only be reopened while the participant is waiting for character selection.");
        }
        if (_accounting is null || _ui is null)
            return RejectExisting(session, AdmissionRejection.AccountingUnavailable,
                "Account-backed admission is not configured.");
        if (!_accounting.TryGetAccount(session.AccountId, out AccountRecord account) ||
            !account.IsEnabled)
        {
            return RejectExisting(session, AdmissionRejection.AccountDisabled,
                "The admission account is missing or disabled.");
        }
        if (!_runtime.TryUseActiveGeneration(
            static (_, generationId) => generationId,
            out ulong generationId))
            return RejectExisting(session, AdmissionRejection.RuntimeNotRunning,
                "Admission requires a running ServerRuntime.");

        if (session.CharacterSelectView.IsValid)
        {
            UiViewInstanceId oldView = session.CharacterSelectView;
            _characterSelectViews.Remove(oldView);
            session.CharacterSelectView = UiViewInstanceId.Invalid;
            try
            {
                if (!_ui.CloseView(new UiCloseView(oldView)))
                {
                    return RejectExisting(session, AdmissionRejection.UiUnavailable,
                        "The previous CharacterSelect view was no longer open.");
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return RejectExisting(session, AdmissionRejection.UiUnavailable,
                    $"The previous CharacterSelect view could not close: {exception.Message}");
            }
        }

        IReadOnlyList<CharacterRecord> characters = _accounting.GetCharacters(session.AccountId);
        if (characters.Count > CharacterSelectUi.Definition.Limits.MaximumRowsPerList)
        {
            return RejectExisting(session, AdmissionRejection.UiUnavailable,
                "The account has more characters than CharacterSelect can represent.");
        }
        UiModel model = CreateCharacterSelectModel(account, characters);
        UiViewInstanceId view;
        try
        {
            view = _ui.OpenHotView(new UiHotOpenView(
                _characterSelectCacheKey,
                model,
                sequence: 1));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return RejectExisting(session, AdmissionRejection.UiUnavailable,
                $"CharacterSelect could not be reopened from cache: {exception.Message}");
        }
        if (!view.IsValid || _characterSelectViews.ContainsKey(view))
        {
            return RejectExisting(session, AdmissionRejection.UiUnavailable,
                "CharacterSelect returned an invalid or already-owned view instance.");
        }
        session.Stage = AdmissionStage.WaitingForCharacter;
        session.GenerationId = generationId;
        session.CharacterSelectView = view;
        session.Rejection = AdmissionRejection.None;
        session.Message = null;
        _characterSelectViews.Add(view, participant);
        return session.Snapshot();
    }

    public bool TryGetSession(ParticipantId participant, out AdmissionResult result)
    {
        EnsureOperational();
        if (_sessions.TryGetValue(participant, out Session? session))
        {
            result = session.Snapshot();
            return true;
        }

        result = default;
        return false;
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;
        _disposed = true;
        Exception? firstFailure = null;
        if (_ui is not null)
        {
            _ui.ActionSubmitted -= HandleAction;
            UiViewInstanceId[] openViews = _characterSelectViews.Keys.ToArray();
            _characterSelectViews.Clear();
            _sessions.Clear();
            foreach (UiViewInstanceId view in openViews)
            {
                try
                {
                    _ui.CloseView(new UiCloseView(view));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    firstFailure ??= exception;
                }
            }
        }
        else
        {
            _characterSelectViews.Clear();
            _sessions.Clear();
        }

        if (firstFailure is not null)
            throw new InvalidOperationException("Admission disposal could not close every UI view.", firstFailure);
    }

    private void HandleAction(UiActionRequest request)
    {
        EnsureOwnerThread();
        if (_disposed || !_characterSelectViews.TryGetValue(request.Instance, out ParticipantId participant))
            return;
        if (!_sessions.TryGetValue(participant, out Session? session))
            return;
        if (request.Definition != CharacterSelectUi.Definition.Handle ||
            request.Action != CharacterSelectUi.Play ||
            !request.Row.IsValid)
        {
            RejectExisting(session, AdmissionRejection.InvalidCharacterSelectAction,
                "The CharacterSelect action was not a valid Play row action.");
            return;
        }
        if (_accounting is null)
        {
            RejectExisting(session, AdmissionRejection.AccountingUnavailable,
                "Account-backed admission is not configured.");
            return;
        }

        CharacterId characterId;
        try
        {
            characterId = new CharacterId(request.Row.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            RejectExisting(session, AdmissionRejection.CharacterNotOwned,
                "The selected character row is invalid.");
            return;
        }

        if (!_accounting.TryGetCharacter(characterId, out CharacterRecord character) ||
            character.AccountId != session.AccountId)
        {
            RejectExisting(session, AdmissionRejection.CharacterNotOwned,
                "The selected character is not owned by the resolved account.");
            return;
        }
        if (!_runtime.TryUseActiveGeneration(
            (world, generationId) => ResolveCharacter(world, generationId, character),
            out CharacterResolution resolution))
        {
            RejectExisting(session, AdmissionRejection.RuntimeNotRunning,
                "Admission requires a running ServerRuntime.");
            return;
        }
        if (resolution.Rejection != AdmissionRejection.None)
        {
            RejectExisting(session, resolution.Rejection, resolution.Message!);
            return;
        }
        if (IsPlayableAlreadyBound(resolution.Entity) ||
            IsCharacterAlreadyBound(character.CharacterId))
        {
            RejectExisting(session, AdmissionRejection.PlayableAlreadyBound,
                "The selected character is already controlled by another participant.");
            return;
        }

        session.Stage = AdmissionStage.Entered;
        session.Entity = resolution.Entity;
        session.CharacterId = character.CharacterId;
        session.GenerationId = resolution.GenerationId;
        session.Rejection = AdmissionRejection.None;
        session.Message = null;
        UiViewInstanceId completedView = session.CharacterSelectView;
        _characterSelectViews.Remove(completedView);
        session.CharacterSelectView = UiViewInstanceId.Invalid;
        _ui!.CloseView(new UiCloseView(completedView));
        Entered?.Invoke(session.Snapshot());
    }

    private UiModel CreateCharacterSelectModel(
        AccountRecord account,
        IReadOnlyList<CharacterRecord> characters)
    {
        var model = new UiModel(CharacterSelectUi.Definition);
        model.SetText(CharacterSelectUi.Title, $"Select a character for {account.AccountId.Value}");
        var rows = new UiListRow[characters.Count];
        for (int index = 0; index < characters.Count; index++)
        {
            CharacterRecord character = characters[index];
            rows[index] = new UiListRow(
                new UiRowId(character.CharacterId.Value),
                new[]
                {
                    UiValue.FromNumber(character.Slot),
                    UiValue.FromText(character.DisplayName),
                    UiValue.FromText(character.PrefabKey.Value),
                });
        }
        model.ReplaceRows(CharacterSelectUi.Characters, rows);
        model.SetActionState(
            CharacterSelectUi.Play,
            characters.Count == 0
                ? new UiActionState(false, "No characters are available.")
                : UiActionState.Default);
        return model;
    }

    private CharacterResolution ResolveCharacter(
        EcsWorld world,
        ulong generationId,
        CharacterRecord character)
    {
        PersistentEntityKey key = character.PersistentEntityKey;
        PersistentWorldId worldId;
        try
        {
            worldId = world.PersistentWorldId;
        }
        catch (InvalidOperationException)
        {
            return CharacterResolution.Reject(
                AdmissionRejection.DurableIdentityUnavailable,
                "The active ECS generation has no durable identity map.");
        }
        if (key.World != worldId)
        {
            return CharacterResolution.Reject(
                AdmissionRejection.DurableWorldMismatch,
                "The character belongs to a different persistent world generation.");
        }
        if (!world.TryResolvePersistentEntity(key.Entity, out Entity entity) ||
            !entity.IsValid ||
            !world.IsAlive(entity))
        {
            return CharacterResolution.Reject(
                AdmissionRejection.DurableEntityMissing,
                "The character's persistent entity is missing or stale in the active generation.");
        }
        if (!_binder.IsPlayable(world, character, entity))
        {
            return CharacterResolution.Reject(
                AdmissionRejection.PlayableBindingRejected,
                "The resolved entity is not accepted by the game playable-entity policy.");
        }
        return new CharacterResolution(entity, generationId, AdmissionRejection.None, null);
    }

    private bool IsPlayableAlreadyBound(Entity entity) =>
        _sessions.Values.Any(session => session.Stage == AdmissionStage.Entered && session.Entity == entity);

    private bool IsCharacterAlreadyBound(CharacterId characterId) =>
        _sessions.Values.Any(session =>
            session.Stage == AdmissionStage.Entered && session.CharacterId == characterId);

    private AdmissionResult RejectNew(
        ParticipantId participant,
        AdmissionRejection rejection,
        string message)
    {
        var result = new AdmissionResult(
            participant,
            AdmissionStage.Rejected,
            Entity.Null,
            default,
            default,
            0,
            UiViewInstanceId.Invalid,
            rejection,
            message);
        Rejected?.Invoke(result);
        return result;
    }

    private AdmissionResult RejectExisting(
        Session session,
        AdmissionRejection rejection,
        string message)
    {
        session.Stage = AdmissionStage.Rejected;
        session.Entity = Entity.Null;
        session.Rejection = rejection;
        session.Message = message;
        if (session.CharacterSelectView.IsValid)
        {
            UiViewInstanceId rejectedView = session.CharacterSelectView;
            _characterSelectViews.Remove(rejectedView);
            session.CharacterSelectView = UiViewInstanceId.Invalid;
            _ui?.CloseView(new UiCloseView(rejectedView));
        }
        AdmissionResult result = session.Snapshot();
        Rejected?.Invoke(result);
        return result;
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        if (_disposed)
            throw new ObjectDisposedException(nameof(AdmissionService));
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Admission is bound to its creating owner thread.");
    }

    private readonly record struct WorldBindingResult(bool Success, Entity Entity, ulong GenerationId);

    private readonly record struct CharacterResolution(
        Entity Entity,
        ulong GenerationId,
        AdmissionRejection Rejection,
        string? Message)
    {
        internal static CharacterResolution Reject(AdmissionRejection rejection, string message) =>
            new(Entity.Null, 0, rejection, message);
    }

    private sealed class Session
    {
        public Session(ParticipantId participant) => Participant = participant;

        public ParticipantId Participant { get; }

        public AdmissionStage Stage { get; set; }

        public Entity Entity { get; set; }

        public AccountId AccountId { get; set; }

        public CharacterId CharacterId { get; set; }

        public ulong GenerationId { get; set; }

        public UiViewInstanceId CharacterSelectView { get; set; }

        public AdmissionRejection Rejection { get; set; }

        public string? Message { get; set; }

        public AdmissionResult Snapshot() => new(
            Participant,
            Stage,
            Entity,
            AccountId,
            CharacterId,
            GenerationId,
            CharacterSelectView,
            Rejection,
            Message);
    }
}
