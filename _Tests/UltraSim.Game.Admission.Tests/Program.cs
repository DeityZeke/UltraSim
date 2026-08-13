using UltraSim.Client.Core;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Server.Runtime;
using UltraSim.UI;

var tests = new (string Name, Action Body)[]
{
    ("AccountlessEntryDoesNotCreateAccountingRecords", AccountlessEntryDoesNotCreateAccountingRecords),
    ("AccountBackedCharacterSelectUsesRealUiAndBindsPersistentEntity", AccountBackedCharacterSelectUsesRealUiAndBindsPersistentEntity),
    ("EmptyCharacterSelectDisablesPlayDynamically", EmptyCharacterSelectDisablesPlayDynamically),
    ("ForeignAndMissingDurableReferencesRejectEntry", ForeignAndMissingDurableReferencesRejectEntry),
    ("AccountingOrdersCharactersAndKeepsOnlyDomainFacts", AccountingOrdersCharactersAndKeepsOnlyDomainFacts),
    ("AdmissionUiOpenFailureIsTransactional", AdmissionUiOpenFailureIsTransactional),
    ("DuplicateViewOwnershipRejectsWithoutCorruptingFirstSession", DuplicateViewOwnershipRejectsWithoutCorruptingFirstSession),
    ("HotReopenFailureLeavesNoStaleViewMapping", HotReopenFailureLeavesNoStaleViewMapping),
    ("PlayableEntityCannotBindTwoParticipants", PlayableEntityCannotBindTwoParticipants),
    ("AccountingRejectsAmbiguousSlotsLinksAndForeignThreads", AccountingRejectsAmbiguousSlotsLinksAndForeignThreads),
    ("LocalAdmissionUiPortDetachesOnDispose", LocalAdmissionUiPortDetachesOnDispose),
    ("AdmissionDisposeAttemptsEveryOpenView", AdmissionDisposeAttemptsEveryOpenView),
    ("AdmissionConfigurationRejectionsAreExplicit", AdmissionConfigurationRejectionsAreExplicit),
    ("UnownedCharacterRowRejectsAndClosesSelector", UnownedCharacterRowRejectsAndClosesSelector),
    ("AdmissionIsOwnerThreadBound", AdmissionIsOwnerThreadBound),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"All {tests.Length} S0.5 Admission tests passed.");

static void AccountlessEntryDoesNotCreateAccountingRecords()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();

    using var service = new AdmissionService(runtime.Game, composition);
    AdmissionResult result = service.JoinAccountless(new ParticipantId(1));

    Assert.True(result.IsEntered);
    Assert.True(result.Entity.IsValid);
    Assert.Equal(default, result.AccountId);
    Assert.Equal(default, result.CharacterId);
}

static void AccountBackedCharacterSelectUsesRealUiAndBindsPersistentEntity()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();

    var accounting = new AccountingStore();
    var account = new AccountId(10);
    var identity = new TrustedLocalIdentity("local-user-10");
    accounting.AddAccount(new AccountRecord(account));
    accounting.BindTrustedIdentity(identity, account);

    var renderer = new NoopRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    using var ui = new LocalAdmissionUiPort(link);
    using var service = new AdmissionService(runtime.Game, composition, accounting, ui);

    Entity persistentEntity = CreatePersistentPlayable(runtime, composition, new ParticipantId(1));
    CharacterRecord character = composition.CreateCharacterRecord(account, 1, "Alice");
    accounting.AddCharacter(character);

    AdmissionResult accountless = service.JoinAccountless(new ParticipantId(1));
    Assert.True(accountless.IsEntered);
    Assert.False(accountless.Entity == persistentEntity);
    AdmissionResult waiting = service.JoinAccountBacked(new ParticipantId(2), identity);
    Assert.True(waiting.IsWaitingForCharacter);
    Assert.True(waiting.CharacterSelectView.IsValid);
    Assert.Equal(1, client.CachedDefinitionCount);
    Assert.Equal(1, client.OpenViewCount);
    ReadOnlySpan<UiListRow> rows = client.GetRows(
        waiting.CharacterSelectView,
        CharacterSelectUi.Characters);
    Assert.Equal(1, rows.Length);
    Assert.Equal(new UiRowId(character.CharacterId.Value), rows[0].Id);
    Assert.True(client.GetActionState(waiting.CharacterSelectView, CharacterSelectUi.Play).Enabled);

    AdmissionResult hot = service.ReopenCharacterSelect(new ParticipantId(2));
    Assert.True(hot.IsWaitingForCharacter);
    Assert.False(hot.CharacterSelectView == waiting.CharacterSelectView);
    Assert.Equal(1, client.CachedDefinitionCount);
    Assert.Equal(1, client.OpenViewCount);
    Assert.Equal(2, renderer.CreateCount);

    client.SubmitAction(hot.CharacterSelectView, CharacterSelectUi.Play, rows[0].Id);

    Assert.True(service.TryGetSession(new ParticipantId(2), out AdmissionResult entered));
    Assert.True(entered.IsEntered);
    Assert.Equal(character.CharacterId, entered.CharacterId);
    Assert.Equal(persistentEntity, entered.Entity);
    Assert.False(entered.CharacterSelectView.IsValid);
    Assert.Equal(0, client.OpenViewCount);

    AdmissionResult invalidReopen = service.ReopenCharacterSelect(new ParticipantId(2));
    Assert.True(invalidReopen.IsRejected);
    Assert.Equal(AdmissionRejection.InvalidSessionStage, invalidReopen.Rejection);
    Assert.True(service.TryGetSession(new ParticipantId(2), out AdmissionResult stillEntered));
    Assert.True(stillEntered.IsEntered);
    Assert.Equal(entered.Entity, stillEntered.Entity);
    Assert.Equal(entered.CharacterId, stillEntered.CharacterId);
}

static void ForeignAndMissingDurableReferencesRejectEntry()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();

    var accounting = new AccountingStore();
    var account = new AccountId(20);
    var identity = new TrustedLocalIdentity("local-user-20");
    accounting.AddAccount(new AccountRecord(account));
    accounting.BindTrustedIdentity(identity, account);
    accounting.AddCharacter(new CharacterRecord(
        new CharacterId(2),
        account,
        slot: 0,
        "Stale",
        new PrefabKey("characters/stale"),
        new PersistentEntityKey(
            new PersistentWorldId(0x5150),
            new PersistentEntityId(1))));

    var client = new UiClientRuntime(new NoopRenderer());
    var link = new LocalSemanticLink(client);
    using var ui = new LocalAdmissionUiPort(link);
    using var service = new AdmissionService(runtime.Game, composition, accounting, ui);
    AdmissionResult waiting = service.JoinAccountBacked(new ParticipantId(3), identity);
    ReadOnlySpan<UiListRow> rows = client.GetRows(waiting.CharacterSelectView, CharacterSelectUi.Characters);
    client.SubmitAction(waiting.CharacterSelectView, CharacterSelectUi.Play, rows[0].Id);

    Assert.True(service.TryGetSession(new ParticipantId(3), out AdmissionResult rejected));
    Assert.True(rejected.IsRejected);
    Assert.Equal(AdmissionRejection.DurableWorldMismatch, rejected.Rejection);
    Assert.Equal(0, client.OpenViewCount);

    var missingAccounting = new AccountingStore();
    var missingAccount = new AccountId(21);
    var missingIdentity = new TrustedLocalIdentity("local-user-21");
    missingAccounting.AddAccount(new AccountRecord(missingAccount));
    missingAccounting.BindTrustedIdentity(missingIdentity, missingAccount);
    missingAccounting.AddCharacter(new CharacterRecord(
        new CharacterId(3),
        missingAccount,
        slot: 0,
        "Missing",
        new PrefabKey("characters/missing"),
        new PersistentEntityKey(
            new PersistentWorldId(0x5050),
            new PersistentEntityId(999))));
    using var missingService = new AdmissionService(runtime.Game, composition, missingAccounting, ui);
    AdmissionResult missing = missingService.JoinAccountBacked(new ParticipantId(5), missingIdentity);
    client.SubmitAction(missing.CharacterSelectView, CharacterSelectUi.Play, new UiRowId(3));
    Assert.True(missingService.TryGetSession(new ParticipantId(5), out AdmissionResult missingResult));
    Assert.Equal(AdmissionRejection.DurableEntityMissing, missingResult.Rejection);
}

static void EmptyCharacterSelectDisablesPlayDynamically()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();

    var accounting = new AccountingStore();
    var account = new AccountId(15);
    var identity = new TrustedLocalIdentity("local-user-15");
    accounting.AddAccount(new AccountRecord(account));
    accounting.BindTrustedIdentity(identity, account);
    var client = new UiClientRuntime(new NoopRenderer());
    var link = new LocalSemanticLink(client);
    using var ui = new LocalAdmissionUiPort(link);
    using var service = new AdmissionService(runtime.Game, composition, accounting, ui);

    AdmissionResult waiting = service.JoinAccountBacked(new ParticipantId(4), identity);

    Assert.True(waiting.IsWaitingForCharacter);
    UiActionState state = client.GetActionState(waiting.CharacterSelectView, CharacterSelectUi.Play);
    Assert.False(state.Enabled);
    Assert.Equal("No characters are available.", state.UnavailableReason);
    Assert.Throws<InvalidOperationException>(() => client.SubmitAction(
        waiting.CharacterSelectView,
        CharacterSelectUi.Play,
        new UiRowId(1)));
}

static void AccountingOrdersCharactersAndKeepsOnlyDomainFacts()
{
    var accounting = new AccountingStore();
    var account = new AccountId(30);
    accounting.AddAccount(new AccountRecord(account));
    Assert.Throws<InvalidOperationException>(() => accounting.AddCharacter(new CharacterRecord(
        new CharacterId(99),
        new AccountId(31),
        0,
        "Unowned",
        new PrefabKey("characters/unowned"),
        new PersistentEntityKey(new PersistentWorldId(1), new PersistentEntityId(1)))));

    accounting.AddCharacter(new CharacterRecord(
        new CharacterId(3), account, 2, "Second",
        new PrefabKey("characters/second"),
        new PersistentEntityKey(new PersistentWorldId(1), new PersistentEntityId(2))));
    accounting.AddCharacter(new CharacterRecord(
        new CharacterId(2), account, 1, "First",
        new PrefabKey("characters/first"),
        new PersistentEntityKey(new PersistentWorldId(1), new PersistentEntityId(1))));
    IReadOnlyList<CharacterRecord> characters = accounting.GetCharacters(account);
    Assert.Equal(2, characters.Count);
    Assert.Equal(new CharacterId(2), characters[0].CharacterId);
    Assert.Equal(new CharacterId(3), characters[1].CharacterId);
}

static void AdmissionUiOpenFailureIsTransactional()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    (AccountingStore accounting, TrustedLocalIdentity identity) = CreateAccount(40);
    var ui = new FaultingUiPort { ThrowOnOpen = true };
    using var service = new AdmissionService(runtime.Game, composition, accounting, ui);

    AdmissionResult result = service.JoinAccountBacked(new ParticipantId(40), identity);

    Assert.True(result.IsRejected);
    Assert.Equal(AdmissionRejection.UiUnavailable, result.Rejection);
    Assert.Equal(0, service.SessionCount);
}

static void DuplicateViewOwnershipRejectsWithoutCorruptingFirstSession()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    (AccountingStore accounting, TrustedLocalIdentity identity) = CreateAccount(41);
    var ui = new FaultingUiPort { FixedView = new UiViewInstanceId(77) };
    using var service = new AdmissionService(runtime.Game, composition, accounting, ui);

    AdmissionResult first = service.JoinAccountBacked(new ParticipantId(41), identity);
    AdmissionResult second = service.JoinAccountBacked(new ParticipantId(42), identity);

    Assert.True(first.IsWaitingForCharacter);
    Assert.True(second.IsRejected);
    Assert.Equal(AdmissionRejection.UiUnavailable, second.Rejection);
    Assert.Equal(1, service.SessionCount);
    Assert.True(service.TryGetSession(new ParticipantId(41), out AdmissionResult retained));
    Assert.Equal(first.CharacterSelectView, retained.CharacterSelectView);
}

static void HotReopenFailureLeavesNoStaleViewMapping()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    (AccountingStore accounting, TrustedLocalIdentity identity) = CreateAccount(43);
    var ui = new FaultingUiPort { FixedView = new UiViewInstanceId(78), ThrowOnHotOpen = true };
    using var service = new AdmissionService(runtime.Game, composition, accounting, ui);
    AdmissionResult waiting = service.JoinAccountBacked(new ParticipantId(43), identity);

    AdmissionResult failed = service.ReopenCharacterSelect(waiting.Participant);

    Assert.True(failed.IsRejected);
    Assert.Equal(AdmissionRejection.UiUnavailable, failed.Rejection);
    Assert.False(failed.CharacterSelectView.IsValid);
    Assert.Equal(1, ui.CloseCount);
}

static void PlayableEntityCannotBindTwoParticipants()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    (AccountingStore accounting, TrustedLocalIdentity identity) = CreateAccount(44);
    _ = CreatePersistentPlayable(runtime, composition, new ParticipantId(99));
    accounting.AddCharacter(composition.CreateCharacterRecord(new AccountId(44), 44, "Shared"));
    var client = new UiClientRuntime(new NoopRenderer());
    var link = new LocalSemanticLink(client);
    using var ui = new LocalAdmissionUiPort(link);
    using var service = new AdmissionService(runtime.Game, composition, accounting, ui);
    AdmissionResult first = service.JoinAccountBacked(new ParticipantId(44), identity);
    AdmissionResult second = service.JoinAccountBacked(new ParticipantId(45), identity);

    client.SubmitAction(first.CharacterSelectView, CharacterSelectUi.Play, new UiRowId(44));
    client.SubmitAction(second.CharacterSelectView, CharacterSelectUi.Play, new UiRowId(44));

    Assert.True(service.TryGetSession(first.Participant, out AdmissionResult entered));
    Assert.True(entered.IsEntered);
    Assert.True(service.TryGetSession(second.Participant, out AdmissionResult rejected));
    Assert.True(rejected.IsRejected);
    Assert.Equal(AdmissionRejection.PlayableAlreadyBound, rejected.Rejection);
}

static void AccountingRejectsAmbiguousSlotsLinksAndForeignThreads()
{
    var accounting = new AccountingStore();
    var account = new AccountId(50);
    accounting.AddAccount(new AccountRecord(account));
    var key1 = new PersistentEntityKey(new PersistentWorldId(1), new PersistentEntityId(1));
    var key2 = new PersistentEntityKey(new PersistentWorldId(1), new PersistentEntityId(2));
    var key3 = new PersistentEntityKey(new PersistentWorldId(1), new PersistentEntityId(3));
    accounting.AddCharacter(Character(1, account, 0, key1));
    Assert.Throws<InvalidOperationException>(() => accounting.AddCharacter(Character(2, account, 0, key2)));
    accounting.AddCharacter(Character(2, account, 1, key2));
    Assert.Throws<InvalidOperationException>(() => accounting.AddCharacter(Character(3, account, 2, key1)));
    accounting.AddCharacter(Character(3, account, 2, key3));
    Assert.Throws<ArgumentOutOfRangeException>(() => accounting.AddAccount(
        new AccountRecord(new AccountId(51), (AccountStatus)99)));
    Assert.Equal(3, accounting.CharacterCount);

    Exception? observed = null;
    var thread = new Thread(() =>
    {
        try { _ = accounting.AccountCount; }
        catch (Exception exception) { observed = exception; }
    });
    thread.Start();
    thread.Join();
    Assert.True(observed is InvalidOperationException);
}

static void LocalAdmissionUiPortDetachesOnDispose()
{
    CompiledUiDefinition definition = CharacterSelectUi.Definition;
    var client = new UiClientRuntime(new NoopRenderer());
    var link = new LocalSemanticLink(client);
    var port = new LocalAdmissionUiPort(link);
    int forwarded = 0;
    port.ActionSubmitted += _ => forwarded++;
    var model = new UiModel(definition);
    model.SetText(CharacterSelectUi.Title, "Detached");
    model.ReplaceRows(CharacterSelectUi.Characters, new[]
    {
        new UiListRow(new UiRowId(1), new[] { UiValue.FromText("One") }),
    });
    UiViewInstanceId view = port.OpenView(new UiOpenView(
        new UiDefinitionCacheKey("local", "detach", 1, definition.Key, definition.Version, definition.Hash),
        definition,
        model));
    port.Dispose();

    Assert.Throws<InvalidOperationException>(() => client.SubmitAction(
        view, CharacterSelectUi.Play, new UiRowId(1)));
    Assert.Equal(0, forwarded);
    Assert.Throws<ObjectDisposedException>(() => port.CloseView(new UiCloseView(view)));
}

static void AdmissionDisposeAttemptsEveryOpenView()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    (AccountingStore accounting, TrustedLocalIdentity identity) = CreateAccount(60);
    var ui = new FaultingUiPort { UseSequentialViews = true, ThrowOnClose = true };
    var service = new AdmissionService(runtime.Game, composition, accounting, ui);
    Assert.True(service.JoinAccountBacked(new ParticipantId(60), identity).IsWaitingForCharacter);
    Assert.True(service.JoinAccountBacked(new ParticipantId(61), identity).IsWaitingForCharacter);

    Assert.Throws<InvalidOperationException>(() => service.Dispose());

    Assert.Equal(2, ui.CloseCount);
    Assert.Throws<ObjectDisposedException>(() => _ = service.SessionCount);
}

static void AdmissionConfigurationRejectionsAreExplicit()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    using var accountlessOnly = new AdmissionService(runtime.Game, composition);
    Assert.Equal(AdmissionRejection.InvalidParticipant,
        accountlessOnly.JoinAccountless(default).Rejection);
    Assert.Equal(AdmissionRejection.AccountingUnavailable,
        accountlessOnly.JoinAccountBacked(new ParticipantId(70), new TrustedLocalIdentity("unknown")).Rejection);

    var disabled = new AccountingStore();
    var disabledAccount = new AccountId(70);
    var disabledIdentity = new TrustedLocalIdentity("disabled");
    disabled.AddAccount(new AccountRecord(disabledAccount, AccountStatus.Disabled));
    disabled.BindTrustedIdentity(disabledIdentity, disabledAccount);
    using var disabledService = new AdmissionService(runtime.Game, composition, disabled);
    Assert.Equal(AdmissionRejection.TrustedIdentityUnknown,
        disabledService.JoinAccountBacked(new ParticipantId(71), new TrustedLocalIdentity("missing")).Rejection);
    Assert.Equal(AdmissionRejection.AccountDisabled,
        disabledService.JoinAccountBacked(new ParticipantId(72), disabledIdentity).Rejection);

    (AccountingStore enabled, TrustedLocalIdentity enabledIdentity) = CreateAccount(71);
    using var missingUi = new AdmissionService(runtime.Game, composition, enabled);
    Assert.Equal(AdmissionRejection.UiUnavailable,
        missingUi.JoinAccountBacked(new ParticipantId(73), enabledIdentity).Rejection);
    Assert.Equal(0, accountlessOnly.SessionCount);
    Assert.Equal(0, disabledService.SessionCount);
    Assert.Equal(0, missingUi.SessionCount);
}

static void UnownedCharacterRowRejectsAndClosesSelector()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    var accounting = new AccountingStore();
    var firstAccount = new AccountId(80);
    var secondAccount = new AccountId(81);
    var identity = new TrustedLocalIdentity("first-account");
    accounting.AddAccount(new AccountRecord(firstAccount));
    accounting.AddAccount(new AccountRecord(secondAccount));
    accounting.BindTrustedIdentity(identity, firstAccount);
    accounting.AddCharacter(Character(
        80,
        firstAccount,
        0,
        new PersistentEntityKey(new PersistentWorldId(0x5050), new PersistentEntityId(80))));
    accounting.AddCharacter(Character(
        81,
        secondAccount,
        0,
        new PersistentEntityKey(new PersistentWorldId(0x5050), new PersistentEntityId(81))));
    var client = new UiClientRuntime(new NoopRenderer());
    var link = new LocalSemanticLink(client);
    using var ui = new LocalAdmissionUiPort(link);
    using var service = new AdmissionService(runtime.Game, composition, accounting, ui);
    AdmissionResult waiting = service.JoinAccountBacked(new ParticipantId(80), identity);

    client.SubmitAction(waiting.CharacterSelectView, CharacterSelectUi.Play, new UiRowId(81));

    Assert.True(service.TryGetSession(waiting.Participant, out AdmissionResult result));
    Assert.Equal(AdmissionRejection.CharacterNotOwned, result.Rejection);
    Assert.Equal(0, client.OpenViewCount);
}

static void AdmissionIsOwnerThreadBound()
{
    var composition = new AdmissionComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    using var service = new AdmissionService(runtime.Game, composition);
    Exception? observed = null;
    var thread = new Thread(() =>
    {
        try { _ = service.SessionCount; }
        catch (Exception exception) { observed = exception; }
    });
    thread.Start();
    thread.Join();
    Assert.True(observed is InvalidOperationException);
}

static (AccountingStore Store, TrustedLocalIdentity Identity) CreateAccount(ulong id)
{
    var accounting = new AccountingStore();
    var account = new AccountId(id);
    var identity = new TrustedLocalIdentity($"local-user-{id}");
    accounting.AddAccount(new AccountRecord(account));
    accounting.BindTrustedIdentity(identity, account);
    return (accounting, identity);
}

static CharacterRecord Character(
    ulong id,
    AccountId account,
    int slot,
    PersistentEntityKey key) => new(
        new CharacterId(id),
        account,
        slot,
        $"Character {id}",
        new PrefabKey($"characters/{id}"),
        key);

static Entity CreatePersistentPlayable(
    ServerRuntime runtime,
    AdmissionComposition composition,
    ParticipantId participant)
{
    Assert.True(runtime.Game.TryUseActiveGeneration(
        (world, _) =>
        {
            Assert.True(composition.TryCreatePlayable(world, participant, out Entity entity));
            return entity;
        },
        out Entity created));
    return created;
}

static ServerRuntime CreateRuntime(AdmissionComposition composition) =>
    new(composition, new ServerRuntimeOptions(
        new EcsWorldOptions(
            new EntityCapacity(8),
            maximumArchetypes: 1,
            maximumPreparedPlans: 0,
            scheduler: new EcsSchedulerOptions(
                maximumSystems: 1,
                maximumDependencies: 1,
                maximumWorkItems: 1,
                maximumStructuralSegments: 1,
                backgroundWorkerCount: 0),
            persistence: new PersistenceIdentityOptions(new PersistentWorldId(0x5050))),
        WorldClockOptions.Default));

sealed class AdmissionComposition : IServerComposition, IAdmissionEntityBinder
{
    private readonly Dictionary<WorldId, ArchetypeHandle> _archetypes = [];

    public ComponentSchema Schema { get; } = ComponentSchema.Create(
        new SchemaId(0x50, 0x50),
        ReadOnlySpan<ComponentRegistration>.Empty);

    public Entity LastCreated { get; private set; }

    public PersistentEntityKey LastPersistentKey { get; private set; }

    public void Configure(ServerGenerationBuilder generation)
    {
        ArchetypeHandle archetype = generation.World.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty,
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        _archetypes.Add(generation.World.Id, archetype);
    }

    public bool TryCreatePlayable(EcsWorld world, ParticipantId participant, out Entity entity)
    {
        if (!_archetypes.TryGetValue(world.Id, out ArchetypeHandle archetype) ||
            !world.TryCreate(archetype, out entity))
        {
            entity = Entity.Null;
            return false;
        }

        LastCreated = entity;
        LastPersistentKey = new PersistentEntityKey(
            world.PersistentWorldId,
            world.GetOrAssignPersistentId(entity));
        return true;
    }

    public bool IsPlayable(EcsWorld world, in CharacterRecord character, Entity resolvedEntity) =>
        world.IsAlive(resolvedEntity);

    public CharacterRecord CreateCharacterRecord(AccountId account, ulong id, string name)
    {
        return new CharacterRecord(
            new CharacterId(id),
            account,
            slot: 0,
            name,
            new PrefabKey("characters/" + name.ToLowerInvariant()),
            LastPersistentKey);
    }
}

sealed class NoopRenderer : IUiRenderer
{
    public int CreateCount { get; private set; }

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        CreateCount++;
        return new Handle();
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction)
    { }

    public void Close(IUiRenderHandle handle) { }

    private sealed class Handle : IUiRenderHandle { }
}

sealed class FaultingUiPort : IAdmissionUiPort
{
    private ulong _nextView = 1;

    public event Action<UiActionRequest>? ActionSubmitted;

    public UiViewInstanceId FixedView { get; set; } = new(1);

    public bool ThrowOnOpen { get; set; }

    public bool ThrowOnHotOpen { get; set; }

    public bool ThrowOnClose { get; set; }

    public bool UseSequentialViews { get; set; }

    public int CloseCount { get; private set; }

    public UiViewInstanceId OpenView(UiOpenView operation)
    {
        if (ThrowOnOpen)
            throw new InvalidOperationException("Injected cold-open failure.");
        return UseSequentialViews ? new UiViewInstanceId(_nextView++) : FixedView;
    }

    public UiViewInstanceId OpenHotView(UiHotOpenView operation)
    {
        if (ThrowOnHotOpen)
            throw new InvalidOperationException("Injected hot-open failure.");
        return FixedView;
    }

    public void UpdateView(UiUpdateView operation) { }

    public bool CloseView(UiCloseView operation)
    {
        CloseCount++;
        if (ThrowOnClose)
            throw new InvalidOperationException("Injected close failure.");
        return true;
    }

    public void Submit(UiActionRequest request) => ActionSubmitted?.Invoke(request);
}

static class Assert
{
    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    public static void True(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Expected true.");
    }

    public static void False(bool value)
    {
        if (value)
            throw new InvalidOperationException("Expected false.");
    }
}
