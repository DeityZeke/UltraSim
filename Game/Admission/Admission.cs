using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Server.Runtime;
using UltraSim.UI;

namespace UltraSim.Game.Admission;

/// <summary>Current presentation/authority stage of one participant admission session.</summary>
public enum AdmissionStage : byte
{
    WaitingForCharacter = 0,
    Entered = 1,
    Rejected = 2,
}

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
/// Authoritative admission service supporting accountless instant entry and an
/// account-backed CharacterSelect flow over the same active ServerRuntime.
/// </summary>
/// <remarks>
/// Account-backed operations resolve <see cref="AccountingStore"/> from the
/// active generation. The service never retains a second Accounting authority
/// across generation replacement.
/// </remarks>
public sealed class AdmissionService : IDisposable
{
    private readonly ServerGameFacet _runtime;
    private readonly IAdmissionEntityBinder _binder;
    private readonly IUiSemanticPort? _ui;
    private readonly UiDefinitionCacheKey _characterSelectCacheKey;
    private readonly Dictionary<ParticipantId, Session> _sessions = [];
    private readonly Dictionary<UiViewInstanceId, ParticipantId> _characterSelectViews = [];
    private readonly int _ownerThreadId;
    private bool _disposed;

    public AdmissionService(
        ServerGameFacet runtime,
        IAdmissionEntityBinder binder,
        IUiSemanticPort? ui = null,
        AdmissionOptions? options = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
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
        _runtime.GenerationPublished += HandleGenerationPublished;
    }

    public event Action<AdmissionResult>? Entered;

    public event Action<AdmissionResult>? Rejected;

    /// <summary>Reports isolated failures from non-authoritative observers.</summary>
    public event Action<Exception>? ObserverFaulted;

    public int SessionCount
    {
        get
        {
            EnsureOperational();
            return _sessions.Count;
        }
    }

    /// <summary>
    /// Transactionally copies every currently entered participant. This is a
    /// trusted Game-policy census; it does not expose a client-controlled
    /// recipient list or transfer Admission ownership to the consumer.
    /// </summary>
    public bool TryCopyEntered(Span<AdmissionResult> destination, out int count)
    {
        EnsureOperational();
        count = 0;
        foreach (Session session in _sessions.Values)
        {
            ReconcileSession(session);
            if (session.Stage == AdmissionStage.Entered)
                count++;
        }
        if (destination.Length < count)
            return false;

        int written = 0;
        foreach (Session session in _sessions.Values)
        {
            if (session.Stage == AdmissionStage.Entered)
                destination[written++] = session.Snapshot();
        }
        InsertionSortByParticipant(destination[..written]);
        count = written;
        return true;
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
                PersistentEntityKey persistentKey = default;
                if (created)
                {
                    persistentKey = new PersistentEntityKey(
                        world.PersistentWorldId,
                        world.GetOrAssignPersistentId(entity));
                }
                return new WorldBindingResult(created, entity, persistentKey, generationId);
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
            ControlledEntity = binding.PersistentKey,
            GenerationId = binding.GenerationId,
        };
        _sessions.Add(participant, session);
        AdmissionResult result = session.Snapshot();
        NotifyObservers(Entered, result);
        return result;
    }

    public AdmissionResult JoinAccountBacked(
        ParticipantId participant,
        TrustedLocalIdentity trustedIdentity) =>
        JoinAccountBacked(participant, trustedIdentity.ToTrustedIdentity());

    /// <summary>Begins account-backed admission from an authenticated provider subject.</summary>
    public AdmissionResult JoinAccountBacked(
        ParticipantId participant,
        TrustedIdentity trustedIdentity)
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
            static (_, generationId) => generationId,
            out _))
        {
            return RejectNew(participant, AdmissionRejection.RuntimeNotRunning,
                "Admission requires a running ServerRuntime.");
        }
        if (!_runtime.TryUseActiveGeneration<IAccountingGenerationStateProvider, AccountSelection>(
            (_, generationId, provider) =>
            {
                AccountingStore accounting = provider.Accounting;
                if (!accounting.TryResolveAccount(trustedIdentity, out AccountRecord account))
                    return AccountSelection.Missing(generationId);
                return new AccountSelection(
                    account,
                    accounting.GetCharacters(account.AccountId),
                    generationId,
                    true);
            },
            out AccountSelection selection))
        {
            return RejectNew(participant, AdmissionRejection.AccountingUnavailable,
                "This active game generation does not enable Accounting.");
        }
        if (!selection.Found)
        {
            return RejectNew(participant, AdmissionRejection.TrustedIdentityUnknown,
                "The trusted identity does not resolve to an account.");
        }
        AccountRecord account = selection.Account;
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
        IReadOnlyList<CharacterRecord> characters = selection.Characters;
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
            TryCloseUnownedView(view);
            return RejectNew(participant, AdmissionRejection.UiUnavailable,
                "CharacterSelect returned an invalid or already-owned view instance.");
        }
        try { _ui.BindActions(view, HandleAction); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            TryCloseUnownedView(view);
            return RejectNew(participant, AdmissionRejection.UiUnavailable,
                $"CharacterSelect action routing could not be bound: {exception.Message}");
        }

        var session = new Session(participant)
        {
            Stage = AdmissionStage.WaitingForCharacter,
            AccountId = account.AccountId,
            TrustedIdentity = trustedIdentity,
            GenerationId = selection.GenerationId,
            CharacterSelectView = view,
            CharacterSelectModel = model,
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
        if (_ui is null)
            return RejectExisting(session, AdmissionRejection.AccountingUnavailable,
                "Account-backed admission is not configured.");
        if (!_runtime.TryUseActiveGeneration(
            static (_, generationId) => generationId,
            out _))
        {
            return RejectExisting(session, AdmissionRejection.RuntimeNotRunning,
                "Admission requires a running ServerRuntime.");
        }
        if (!_runtime.TryUseActiveGeneration<IAccountingGenerationStateProvider, AccountSelection>(
            (_, generationId, provider) =>
            {
                AccountingStore accounting = provider.Accounting;
                if (!accounting.TryResolveAccount(session.TrustedIdentity, out AccountRecord account) ||
                    account.AccountId != session.AccountId)
                    return AccountSelection.Missing(generationId);
                return new AccountSelection(
                    account,
                    accounting.GetCharacters(account.AccountId),
                    generationId,
                    true);
            },
            out AccountSelection selection))
        {
            return RejectExisting(session, AdmissionRejection.AccountingUnavailable,
                "The active game generation does not enable Accounting.");
        }
        if (!selection.Found || !selection.Account.IsEnabled)
        {
            return RejectExisting(session, AdmissionRejection.AccountDisabled,
                "The admission account is missing or disabled.");
        }
        AccountRecord account = selection.Account;

        if (session.CharacterSelectView.IsValid)
        {
            UiViewInstanceId oldView = session.CharacterSelectView;
            _characterSelectViews.Remove(oldView);
            session.CharacterSelectView = UiViewInstanceId.Invalid;
            session.CharacterSelectModel = null;
            if (!TryReleaseView(oldView, out Exception? releaseFailure))
            {
                return RejectExisting(session, AdmissionRejection.UiUnavailable,
                    $"The previous CharacterSelect view could not detach: {releaseFailure!.Message}");
            }
        }

        IReadOnlyList<CharacterRecord> characters = selection.Characters;
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
            TryCloseUnownedView(view);
            return RejectExisting(session, AdmissionRejection.UiUnavailable,
                "CharacterSelect returned an invalid or already-owned view instance.");
        }
        try { _ui.BindActions(view, HandleAction); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            TryCloseUnownedView(view);
            return RejectExisting(session, AdmissionRejection.UiUnavailable,
                $"CharacterSelect action routing could not be bound: {exception.Message}");
        }
        session.Stage = AdmissionStage.WaitingForCharacter;
        session.GenerationId = selection.GenerationId;
        session.CharacterSelectView = view;
        session.CharacterSelectModel = model;
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
            ReconcileSession(session);
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
        _runtime.GenerationPublished -= HandleGenerationPublished;
        Exception? firstFailure = null;
        if (_ui is not null)
        {
            UiViewInstanceId[] openViews = _characterSelectViews.Keys.ToArray();
            _characterSelectViews.Clear();
            _sessions.Clear();
            foreach (UiViewInstanceId view in openViews)
            {
                if (!TryReleaseView(view, out Exception? releaseFailure))
                    firstFailure ??= releaseFailure;
            }
        }
        else
        {
            _characterSelectViews.Clear();
            _sessions.Clear();
        }

        Entered = null;
        Rejected = null;
        PresentationFaulted = null;
        ObserverFaulted = null;

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
        if (session.CharacterSelectModel is null ||
            !session.CharacterSelectModel.TryValidateAction(request))
        {
            // Structurally valid remote input is still untrusted semantic intent.
            // Reject it before Accounting/ECS authority while keeping the retained
            // selector usable for a later well-formed action.
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

        CharacterRecord character = default;
        if (!_runtime.TryUseActiveGeneration(
            static (_, generationId) => generationId,
            out _))
        {
            RejectExisting(session, AdmissionRejection.RuntimeNotRunning,
                "Admission requires a running ServerRuntime.");
            return;
        }
        if (!_runtime.TryUseActiveGeneration<IAccountingGenerationStateProvider, CharacterResolution>(
            (world, generationId, provider) =>
            {
                AccountingStore accounting = provider.Accounting;
                if (!accounting.TryResolveAccount(session.TrustedIdentity, out AccountRecord currentAccount) ||
                    currentAccount.AccountId != session.AccountId)
                {
                    return CharacterResolution.Reject(
                        AdmissionRejection.TrustedIdentityUnknown,
                        "The trusted identity no longer resolves to this admission account.");
                }
                if (!currentAccount.IsEnabled)
                {
                    return CharacterResolution.Reject(
                        AdmissionRejection.AccountDisabled,
                        "The admission account is now disabled.");
                }
                if (!accounting.TryGetCharacter(characterId, out character) ||
                    character.AccountId != session.AccountId)
                {
                    return CharacterResolution.Reject(
                        AdmissionRejection.CharacterNotOwned,
                        "The selected character is not owned by the resolved account.");
                }
                return ResolveCharacter(world, generationId, character);
            },
            out CharacterResolution resolution))
        {
            RejectExisting(session, AdmissionRejection.AccountingUnavailable,
                "The active game generation does not enable Accounting.");
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
        session.ControlledEntity = character.PersistentEntityKey;
        session.CharacterId = character.CharacterId;
        session.GenerationId = resolution.GenerationId;
        session.Rejection = AdmissionRejection.None;
        session.Message = null;
        UiViewInstanceId completedView = session.CharacterSelectView;
        _characterSelectViews.Remove(completedView);
        session.CharacterSelectView = UiViewInstanceId.Invalid;
        session.CharacterSelectModel = null;
        if (!TryReleaseView(completedView, out Exception? releaseFailure))
            NotifyPresentationFault(releaseFailure!);
        NotifyObservers(Entered, session.Snapshot());
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
        NotifyObservers(Rejected, result);
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
            session.CharacterSelectModel = null;
            if (!TryReleaseView(rejectedView, out Exception? releaseFailure))
                NotifyPresentationFault(releaseFailure!);
        }
        AdmissionResult result = session.Snapshot();
        NotifyObservers(Rejected, result);
        return result;
    }

    private void HandleGenerationPublished(ulong generationId)
    {
        if (_disposed)
            return;
        foreach (Session session in _sessions.Values.ToArray())
            ReconcileSession(session);
    }

    private void ReconcileSession(Session session)
    {
        if (session.Stage == AdmissionStage.Rejected)
            return;

        // Accountless sessions have no durable authorization record. Their ECS
        // identity may exist in a restored world, but authority to control it
        // cannot be reconstructed, so replacement explicitly invalidates them.
        if (!session.AccountId.IsValid)
        {
            if (_runtime.TryUseActiveGeneration(static (_, generationId) => generationId, out ulong active) &&
                active != session.GenerationId)
            {
                RejectExisting(session, AdmissionRejection.GenerationReplaced,
                    "The accountless admission session expired when authority was replaced.");
            }
            return;
        }

        if (!_runtime.TryUseActiveGeneration<IAccountingGenerationStateProvider, CharacterResolution>(
            (world, generationId, provider) =>
            {
                AccountingStore accounting = provider.Accounting;
                if (!accounting.TryResolveAccount(session.TrustedIdentity, out AccountRecord account) ||
                    account.AccountId != session.AccountId)
                {
                    return CharacterResolution.Reject(
                        AdmissionRejection.TrustedIdentityUnknown,
                        "The trusted identity no longer authorizes this session.");
                }
                if (!account.IsEnabled)
                    return CharacterResolution.Reject(AdmissionRejection.AccountDisabled, "The admission account is disabled.");
                if (session.Stage == AdmissionStage.WaitingForCharacter)
                    return new CharacterResolution(Entity.Null, generationId, AdmissionRejection.None, null);
                if (!accounting.TryGetCharacter(session.CharacterId, out CharacterRecord character) ||
                    character.AccountId != account.AccountId)
                {
                    return CharacterResolution.Reject(
                        AdmissionRejection.CharacterNotOwned,
                        "The controlled character is no longer owned by this account.");
                }
                return ResolveCharacter(world, generationId, character);
            },
            out CharacterResolution resolution))
        {
            RejectExisting(session, AdmissionRejection.AccountingUnavailable,
                "The replacement generation does not provide Accounting.");
            return;
        }

        if (resolution.Rejection != AdmissionRejection.None)
        {
            RejectExisting(session, resolution.Rejection, resolution.Message!);
            return;
        }

        session.GenerationId = resolution.GenerationId;
        if (session.Stage == AdmissionStage.Entered)
            session.Entity = resolution.Entity;
    }

    private void TryCloseUnownedView(UiViewInstanceId view)
    {
        if (!view.IsValid || _ui is null)
            return;
        if (!TryReleaseView(view, out Exception? releaseFailure))
            NotifyPresentationFault(releaseFailure!);
    }

    private bool TryReleaseView(UiViewInstanceId view, out Exception? failure)
    {
        failure = null;
        if (_ui is null || !view.IsValid)
            return true;
        try { _ui.UnbindActions(view); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = exception;
        }
        try
        {
            if (!_ui.CloseView(new UiCloseView(view)))
            {
                failure ??= new InvalidOperationException(
                    $"CharacterSelect view {view.Value} was no longer open during cleanup.");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure ??= exception;
        }
        return failure is null;
    }

    public event Action<Exception>? PresentationFaulted;

    private void NotifyPresentationFault(Exception exception) =>
        NotifyObservers(PresentationFaulted, exception);

    private void NotifyObservers<T>(Action<T>? observers, T value)
    {
        if (observers is null)
            return;
        foreach (Action<T> observer in observers.GetInvocationList().Cast<Action<T>>())
        {
            try { observer(value); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                NotifyObserverFault(exception);
            }
        }
    }

    private void NotifyObserverFault(Exception failure)
    {
        Action<Exception>? observers = ObserverFaulted;
        if (observers is null)
            return;
        foreach (Action<Exception> observer in observers.GetInvocationList().Cast<Action<Exception>>())
        {
            try { observer(failure); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
    }

    private static void InsertionSortByParticipant(Span<AdmissionResult> sessions)
    {
        for (int index = 1; index < sessions.Length; index++)
        {
            AdmissionResult value = sessions[index];
            int destination = index;
            while (destination > 0 &&
                sessions[destination - 1].Participant.CompareTo(value.Participant) > 0)
            {
                sessions[destination] = sessions[destination - 1];
                destination--;
            }
            sessions[destination] = value;
        }
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

    private readonly record struct WorldBindingResult(
        bool Success,
        Entity Entity,
        PersistentEntityKey PersistentKey,
        ulong GenerationId);

    private readonly record struct AccountSelection(
        AccountRecord Account,
        IReadOnlyList<CharacterRecord> Characters,
        ulong GenerationId,
        bool Found)
    {
        internal static AccountSelection Missing(ulong generationId) =>
            new(default, Array.Empty<CharacterRecord>(), generationId, false);
    }

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

        public PersistentEntityKey ControlledEntity { get; set; }

        public TrustedIdentity TrustedIdentity { get; set; }

        public AccountId AccountId { get; set; }

        public CharacterId CharacterId { get; set; }

        public ulong GenerationId { get; set; }

        public UiViewInstanceId CharacterSelectView { get; set; }

        public UiModel? CharacterSelectModel { get; set; }

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
