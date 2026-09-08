using System.Diagnostics;
using System.Text;
using UltraSim.Client.Core;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Chat;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.Inventory;
using UltraSim.Game.Persistence;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;
using UltraSim.UI;

namespace UltraSim.Client.Local;

/// <summary>
/// Permanent packet-free local application composition for the playable Cabin/Concept Demo
/// slices. All mutable authority is created, used, and reclaimed by one private
/// owner thread. Client callers see only bounded command ingress, concrete
/// semantic consumers, and immutable/coalesced presentation facts.
/// </summary>
public sealed class LocalPlayableSession : IDisposable
{
    private const int MaximumPublicationRecords = 64;
    private const int MaximumSubscriptions = 1_024;
    private const int MaximumTopologyChanges = MaximumSubscriptions * 2;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object _viewGate = new();
    private readonly WorldPack _worldPack;
    private readonly BoundWorldPack _boundWorld;
    private readonly bool _conceptDemo;
    private readonly GameGuestAdmissionRequest _guestAdmission;
    private readonly ILocalPlayableServerExtensionFactory? _serverExtensionFactory;
    private readonly string _saveRoot;
    private readonly LocalPlayableSessionLimits _limits;
    private readonly BoundedSpscQueue<LocalPlayableCommand> _commands;
    private readonly BoundedSpscQueue<LocalAdministrativeRequest> _administrative;
    private readonly BoundedSpscQueue<LocalChatSubmission> _chatSubmissions;
    private readonly LocalWorldSemanticChannel _worldChannel;
    private readonly LocalChatSemanticChannel _chatChannel;
    private readonly LocalQueuedUiChannel _uiChannel;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly Thread _authorityThread;
    private readonly GameControlSessionEpoch _controlSession;
    private readonly ulong _applicationSessionEpoch;
    private LocalPlayableAuthorityView _latestView;
    private readonly ClientPublicPresentationMetadata[] _latestPublicPresentation =
        new ClientPublicPresentationMetadata[64];
    private int _latestPublicPresentationCount;
    private Exception? _fault;
    private volatile bool _stopRequested;
    private volatile bool _disposed;
    private volatile bool _running;
    private int _uiAttached;
    private int _inventoryHotOpen;
    private long _authorityTicks;

    public static LocalPlayableSession CreateCabin(
        string saveRoot,
        LocalPlayableSessionLimits limits = default)
        => CreateCabin(
            saveRoot,
            new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Local Guest"),
            limits);

    public static LocalPlayableSession CreateCabin(
        string saveRoot,
        in GameGuestAdmissionRequest guestAdmission,
        LocalPlayableSessionLimits limits = default)
    {
        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        return Create(pack, saveRoot, guestAdmission, limits, conceptDemo: false);
    }

    /// <summary>Creates the permanent local application path over the sparse Concept Demo world.</summary>
    public static LocalPlayableSession CreateConceptDemo(
        string saveRoot,
        LocalPlayableSessionLimits limits = default)
        => CreateConceptDemo(
            saveRoot,
            new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Local Guest"),
            limits);

    public static LocalPlayableSession CreateConceptDemo(
        string saveRoot,
        in GameGuestAdmissionRequest guestAdmission,
        LocalPlayableSessionLimits limits = default,
        ILocalPlayableServerExtensionFactory? serverExtensionFactory = null)
    {
        WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
        return Create(
            pack, saveRoot, guestAdmission, limits, conceptDemo: true, serverExtensionFactory);
    }

    private static LocalPlayableSession Create(
        WorldPack pack,
        string saveRoot,
        in GameGuestAdmissionRequest guestAdmission,
        LocalPlayableSessionLimits limits,
        bool conceptDemo,
        ILocalPlayableServerExtensionFactory? serverExtensionFactory = null)
    {
        var binding = new WorldContentBinding(
            GameContent.WorldFingerprint,
            [
                GameTerrainMaterialIds.Water,
                GameTerrainMaterialIds.Sand,
                GameTerrainMaterialIds.Grass,
                GameTerrainMaterialIds.Rock,
                GameTerrainMaterialIds.Snow,
            ],
            [
                GameWorldStaticDefinitionIds.Tree,
                GameWorldStaticDefinitionIds.Rock,
                GameWorldStaticDefinitionIds.CabinWall,
                GameWorldStaticDefinitionIds.CabinRoof,
                GameWorldStaticDefinitionIds.Shelf,
                GameWorldStaticDefinitionIds.Book,
            ]);
        if (!pack.TryBind(binding, out BoundWorldPack? bound, out WorldPackBindingError error))
            throw new InvalidOperationException($"The playable WorldPack did not bind: {error}.");
        return new LocalPlayableSession(
            pack, bound!, saveRoot, guestAdmission, limits, conceptDemo, serverExtensionFactory);
    }

    public LocalPlayableSession(
        WorldPack worldPack,
        BoundWorldPack boundWorld,
        string saveRoot,
        GameGuestAdmissionRequest guestAdmission,
        LocalPlayableSessionLimits limits = default,
        bool conceptDemo = false,
        ILocalPlayableServerExtensionFactory? serverExtensionFactory = null)
    {
        _worldPack = worldPack ?? throw new ArgumentNullException(nameof(worldPack));
        _boundWorld = boundWorld ?? throw new ArgumentNullException(nameof(boundWorld));
        _conceptDemo = conceptDemo;
        _guestAdmission = guestAdmission;
        _serverExtensionFactory = serverExtensionFactory;
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        if (worldPack.Fingerprint != boundWorld.Fingerprint)
            throw new ArgumentException("The bound world does not belong to the supplied WorldPack.", nameof(boundWorld));
        if (limits == default) limits = LocalPlayableSessionLimits.Default;
        limits.Validate();
        _saveRoot = saveRoot;
        _limits = limits;
        _commands = new BoundedSpscQueue<LocalPlayableCommand>(limits.CommandCapacity);
        _administrative = new BoundedSpscQueue<LocalAdministrativeRequest>(limits.AdministrativeCapacity);
        _chatSubmissions = new BoundedSpscQueue<LocalChatSubmission>(limits.ChatSubmissionCapacity);
        _controlSession = GameControlSessionEpoch.Create();
        _applicationSessionEpoch = GameControlSessionEpoch.Create().Value;
        _worldChannel = new LocalWorldSemanticChannel(limits, MaximumPublicationRecords);
        _chatChannel = new LocalChatSemanticChannel(limits, _applicationSessionEpoch);
        _uiChannel = new LocalQueuedUiChannel(limits);
        _authorityThread = new Thread(AuthorityMain)
        {
            IsBackground = true,
            Name = "UltraSim Local Authority",
        };
        _authorityThread.Start();
    }

    public BoundWorldPack BoundWorld => _boundWorld;
    public GameControlSessionEpoch ControlSession => _controlSession;
    public ulong ApplicationSessionEpoch => _applicationSessionEpoch;

    /// <summary>Exact stock Inventory identity used by this local application composition.</summary>
    public static UiDefinitionCacheKey InventoryDefinitionCacheKey => new(
        "local-server",
        "game-composition",
        1,
        InventoryUi.Definition.Key,
        InventoryUi.Definition.Version,
        InventoryUi.Definition.Hash);

    public void WaitUntilReady(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!_ready.Wait(timeout))
            throw new TimeoutException("The local authoritative application did not become ready.");
        ThrowIfFaulted();
    }

    public bool TryGetLatestView(out LocalPlayableAuthorityView view)
    {
        lock (_viewGate) view = _latestView;
        return view.IsReady;
    }

    public bool TrySubmit(in LocalPlayableCommand command)
    {
        EnsureOperational();
        if (!command.IsValid || command.Command.ControlSession != _controlSession) return false;
        bool queued = _commands.TryEnqueue(command);
        if (queued) _wake.Set();
        return queued;
    }

    public bool TrySubmitChat(ulong sequence, ClientChatSpeechMode mode, string text)
    {
        EnsureOperational();
        if (sequence == 0 || mode is < ClientChatSpeechMode.Whisper or > ClientChatSpeechMode.Yell ||
            !IsBoundedChatText(text))
            return false;
        bool queued = _chatSubmissions.TryEnqueue(new LocalChatSubmission(sequence, mode, text));
        if (queued) _wake.Set();
        return queued;
    }

    public bool TryRequestSave(ulong sequence) =>
        TryRequestAdministrative(new LocalAdministrativeRequest(
            sequence, LocalAdministrativeOperationKind.Save, default));

    public bool TryRequestLoad(ulong sequence) =>
        TryRequestAdministrative(new LocalAdministrativeRequest(
            sequence, LocalAdministrativeOperationKind.Load, default));

    public bool TryRequestRelocation(ulong sequence, in WorldPosition position) =>
        TryRequestAdministrative(new LocalAdministrativeRequest(
            sequence, LocalAdministrativeOperationKind.Relocate, position));

    public bool TryRequestOpenInventory(ulong sequence) =>
        TryRequestAdministrative(new LocalAdministrativeRequest(
            sequence, LocalAdministrativeOperationKind.OpenInventory, default));

    public bool TryRequestHosting(ulong sequence, bool enabled) =>
        TryRequestAdministrative(new LocalAdministrativeRequest(
            sequence,
            enabled
                ? LocalAdministrativeOperationKind.EnableHosting
                : LocalAdministrativeOperationKind.DisableHosting,
            default));

    public int PumpWorld(ClientWorldSemanticCache cache, int maximumDeliveries = 64)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDeliveries);
        int count = 0;
        while (count < maximumDeliveries && _worldChannel.TryApplyNext(cache, out _)) count++;
        if (count != 0) _wake.Set();
        return count;
    }

    public int CopyPublicPresentationMetadata(
        Span<ClientPublicPresentationMetadata> destination)
    {
        EnsureOperational();
        lock (_viewGate)
        {
            if (destination.Length < _latestPublicPresentationCount) return -1;
            _latestPublicPresentation.AsSpan(0, _latestPublicPresentationCount).CopyTo(destination);
            return _latestPublicPresentationCount;
        }
    }

    public int PumpChat(ClientChatState chat, int maximumDeliveries = 32)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDeliveries);
        int count = 0;
        while (count < maximumDeliveries && _chatChannel.TryApplyNext(chat)) count++;
        if (count != 0) _wake.Set();
        return count;
    }

    public LocalQueuedUiClient AttachUiClient(
        UiClientRuntime client,
        Func<UiDefinitionCacheKey, CompiledUiDefinition?>? standardDefinitionResolver = null)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(client);
        if (Interlocked.CompareExchange(ref _uiAttached, 1, 0) != 0)
            throw new InvalidOperationException("The local session already has a UI client attached.");
        try
        {
            if (standardDefinitionResolver?.Invoke(InventoryDefinitionCacheKey) is
                CompiledUiDefinition inventory)
            {
                _ = client.CacheDefinition(InventoryDefinitionCacheKey, inventory);
                Volatile.Write(ref _inventoryHotOpen, 1);
            }
            return new LocalQueuedUiClient(_uiChannel, client, ReleaseUiClient);
        }
        catch
        {
            ReleaseUiClient();
            throw;
        }
    }

    private void ReleaseUiClient()
    {
        Volatile.Write(ref _inventoryHotOpen, 0);
        Volatile.Write(ref _uiAttached, 0);
    }

    public LocalPlayableSessionSnapshot GetSnapshot()
    {
        string? fault;
        lock (_viewGate) fault = _fault?.ToString();
        long reserved = checked(
            (long)_limits.CommandCapacity * 96L +
            (long)_limits.AdministrativeCapacity * 64L +
            (long)_limits.ChatSubmissionCapacity * 32L +
            _worldChannel.ReservedBytes + _chatChannel.ReservedBytes + _uiChannel.ReservedBytes);
        return new LocalPlayableSessionSnapshot(
            _ready.IsSet && fault is null,
            _running,
            checked((ulong)Interlocked.Read(ref _authorityTicks)),
            _commands.Count,
            _worldChannel.Pending,
            _chatChannel.Pending,
            _commands.HighWater,
            _worldChannel.HighWater,
            _chatChannel.HighWater,
            _worldChannel.Applied,
            _worldChannel.Rejected,
            _worldChannel.Invalidated,
            _chatChannel.Applied,
            _chatChannel.Rejected,
            _uiChannel.Applied,
            _uiChannel.Rejected,
            _uiChannel.ColdOpensApplied,
            _uiChannel.HotOpensApplied,
            reserved,
            fault);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopRequested = true;
        _wake.Set();
        if (!_authorityThread.Join(TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("The local authority owner thread did not stop.");
        _ready.Dispose();
        _wake.Dispose();
    }

    private bool TryRequestAdministrative(in LocalAdministrativeRequest request)
    {
        EnsureOperational();
        if (request.Sequence == 0 || request.Kind == LocalAdministrativeOperationKind.None) return false;
        bool queued = _administrative.TryEnqueue(request);
        if (queued) _wake.Set();
        return queued;
    }

    private static bool IsBoundedChatText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            return StrictUtf8.GetByteCount(text) <= GameChatLimits.Default.MaximumTextUtf8Bytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private void AuthorityMain()
    {
        ServerRuntime? runtime = null;
        AdmissionService? admission = null;
        GameChatService? chat = null;
        LocalQueuedUiSemanticPort? ui = null;
        InventoryUiController? inventoryUi = null;
        InformationalSignUiController? signUi = null;
        ILocalPlayableServerExtension? serverExtension = null;
        try
        {
            GameRuntimeProfile runtimeProfile = _serverExtensionFactory is null
                ? GameRuntimeProfile.Local
                : GameRuntimeProfile.Networked;
            var composition = new UltraSimGameComposition(
                new InventoryLimits(32, 128, 32),
                runtimeProfile: runtimeProfile,
                baseWorldPack: _worldPack);
            runtime = new ServerRuntime(
                composition,
                new ServerRuntimeOptions(
                    UltraSimGameComposition.CreateWorldOptions(
                        128,
                        new PersistenceIdentityOptions(new PersistentWorldId(
                            _conceptDemo ? 0x80_D000UL : 0x80_4010UL)),
                        runtimeProfile),
                    WorldClockOptions.Default));
            runtime.Initialize();
            runtime.Start();
            admission = new AdmissionService(runtime.Game, composition);
            AdmissionResult entered = admission.JoinGuest(
                new ParticipantId(4010), _guestAdmission);
            if (!entered.IsEntered)
                throw new InvalidOperationException(
                    entered.Message ?? "The local playable guest could not enter.");
            if (!entered.GuestIdentity.IsValid)
                throw new InvalidOperationException(
                    "Authoritative Admission did not retain the accepted guest identity.");
            bool conceptDemo = _conceptDemo;
            if (!runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, LocalPopulation>(
                    (world, _, game) =>
                    {
                        if (!conceptDemo)
                            return new LocalPopulation(
                                default, default,
                                GamePlayableWorld.Populate(world, game, entered.Entity));
                        GamePlayableWorldScene scene =
                            GamePlayableWorld.PopulateSharedConceptDemoScene(world, game);
                        GamePlayableParticipantEntities participant =
                            GamePlayableWorld.PopulateSharedParticipant(
                                world, game, entered.Entity, GamePlayableWorld.PlayerStart);
                        return new LocalPopulation(
                            scene, participant, ComposePlayableEntities(scene, participant));
                    },
                    out LocalPopulation population))
                throw new InvalidOperationException("The local playable world could not populate.");
            GamePlayableWorldEntities entities = population.Entities;
            runtime.Tick();

            GameChatLimits chatLimits = GameChatLimits.Default with
            {
                MaximumParticipants = 8,
                MaximumRecipientsPerSpeech = 8,
            };
            chat = new GameChatService(runtime.Game, admission, chatLimits);
            ui = new LocalQueuedUiSemanticPort(_uiChannel, _limits.UiOperationCapacity);
            inventoryUi = new InventoryUiController(runtime.Game, ui);
            signUi = new InformationalSignUiController(ui);
            var persistence = new GameRuntimePersistence(
                runtime.Game,
                composition,
                RestoreOptions(),
                new SaveSetCoordinator(_saveRoot));
            OfflineDirectChunkRecipient recipient = CreateRecipient();
            BindControl(runtime, entities);
            if (_serverExtensionFactory is not null)
            {
                WorldPublicEntityId localPublic = default;
                if (!conceptDemo || !population.Scene.IsValid || !population.Participant.IsValid ||
                    !runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
                        WorldPublicEntityId>(
                        (world, _, game) =>
                        {
                            Entity player = Resolve(world, population.Participant.PlayerId);
                            return game.WorldState.Publications.TryGetPublicEntityId(
                                player, out WorldPublicEntityId publicId)
                                    ? publicId
                                    : default;
                        }, out localPublic) || !localPublic.IsValid)
                    throw new InvalidOperationException(
                        "The hosted local participant could not bind its shared public identity.");
                serverExtension = _serverExtensionFactory.Create(new LocalPlayableServerContext(
                    Path.Combine(_saveRoot, "hosting"),
                    _worldPack,
                    composition,
                    runtime,
                    admission,
                    chat,
                    population.Scene,
                    entered.Participant,
                    entered.GuestIdentity,
                    _controlSession,
                    population.Participant,
                    localPublic,
                    _chatChannel));
            }

            var worldPump = new WorldPumpState();
            var recipients = new ParticipantId[chatLimits.MaximumRecipientsPerSpeech];
            var snapshotScratch = new WorldPublicRecord[MaximumPublicationRecords];
            var deltaScratch = new WorldPublicDeltaRecord[MaximumPublicationRecords];
            var publicPresentationScratch = new ClientPublicPresentationMetadata[64];
            ulong observedGeneration = runtime.ActiveGenerationId;
            ulong lastChatSubmission = 0;
            ulong lastAdministrative = 0;
            ulong saveSequence = 1;
            GamePlayerCommandResult lastCommand = default;
            ulong lastCommandSequence = 0;
            LocalPlayableSelection lastSelection = LocalPlayableSelection.None;
            GameWorldActionStatus lastChest = GameWorldActionStatus.NotRequested;
            LocalAdministrativeRequest lastAdmin = default;
            bool lastAdminSucceeded = false;
            string? lastAdminMessage = null;

            CaptureView(runtime, entities, entered.GuestIdentity,
                lastCommandSequence, lastSelection, lastCommand, lastChest,
                lastAdmin, lastAdminSucceeded, lastAdminMessage,
                serverExtension?.GetSnapshot() ?? default,
                serverExtension,
                publicPresentationScratch);
            _running = true;
            _ready.Set();

            long interval = Math.Max(1, Stopwatch.Frequency / runtime.SimulationFrequencyHz);
            long nextTick = Stopwatch.GetTimestamp();
            while (!_stopRequested)
            {
                long now = Stopwatch.GetTimestamp();
                if (now < nextTick)
                {
                    int waitMilliseconds = Math.Clamp(
                        (int)((nextTick - now) * 1000L / Stopwatch.Frequency), 0, 16);
                    _wake.Wait(waitMilliseconds);
                    _wake.Reset();
                    continue;
                }
                nextTick = checked(nextTick + interval);
                if (now - nextTick > interval * 4) nextTick = now + interval;

                ui.Pump();
                serverExtension?.Pump();
                ProcessWorldProofs(ref recipient);
                ProcessChatProofs();
                ProcessCommands(
                    runtime,
                    signUi,
                    entities,
                    ref lastCommandSequence,
                    ref lastSelection,
                    ref lastCommand,
                    ref lastChest);
                ProcessAdministrative(
                    runtime, persistence, inventoryUi, entities, serverExtension,
                    ref lastAdministrative, ref lastAdminSucceeded, ref lastAdminMessage,
                    ref lastAdmin, ref saveSequence);
                ProcessChatSubmissions(runtime, chat, entered.Participant, ref lastChatSubmission);

                runtime.Tick();
                Interlocked.Increment(ref _authorityTicks);
                if (runtime.ActiveGenerationId != observedGeneration)
                {
                    observedGeneration = runtime.ActiveGenerationId;
                    recipient = CreateRecipient();
                    _worldChannel.ResetOutstanding();
                    worldPump.Reset();
                    BindControl(runtime, entities);
                }

                if (serverExtension?.OwnsSharedChatHandoff != true)
                    DriveChat(chat, entered.Participant, recipients);
                serverExtension?.Pump();
                DriveWorld(
                    runtime, entities, recipient, worldPump, snapshotScratch, deltaScratch);
                CaptureView(runtime, entities, entered.GuestIdentity,
                    lastCommandSequence, lastSelection, lastCommand, lastChest,
                    lastAdmin, lastAdminSucceeded, lastAdminMessage,
                    serverExtension?.GetSnapshot() ?? default,
                    serverExtension,
                    publicPresentationScratch);
            }

            // Remote applications borrow this running authority. Retire the optional hosted
            // adapter before stopping the internal server so terminal per-participant cleanup can
            // end control and destroy its Game-owned entities on the still-valid generation.
            serverExtension?.Dispose();
            serverExtension = null;
            runtime.Stop();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (_viewGate) _fault = exception;
            _ready.Set();
        }
        finally
        {
            _running = false;
            serverExtension?.Dispose();
            signUi?.Dispose();
            inventoryUi?.Dispose();
            ui?.Dispose();
            chat?.Dispose();
            admission?.Dispose();
            runtime?.Dispose();
        }
    }

    private void ProcessCommands(
        ServerRuntime runtime,
        InformationalSignUiController signUi,
        GamePlayableWorldEntities entities,
        ref ulong lastSequence,
        ref LocalPlayableSelection lastSelection,
        ref GamePlayerCommandResult lastResult,
        ref GameWorldActionStatus lastChest)
    {
        while (_commands.TryDequeue(out LocalPlayableCommand request))
        {
            GamePlayerCommand admitted = request.Command;
            bool interactionPressed = (admitted.PressedActions & GamePlayerActions.Interact) != 0;
            bool chestInteraction = request.Selection == LocalPlayableSelection.CabinChest && interactionPressed;
            bool unsupported = request.Selection == LocalPlayableSelection.Unsupported && interactionPressed;
            if (chestInteraction || unsupported)
            {
                admitted = admitted with
                {
                    HeldActions = admitted.HeldActions & ~GamePlayerActions.Interact,
                    PressedActions = admitted.PressedActions & ~GamePlayerActions.Interact,
                };
            }
            LocalPlayableCommand captured = request;
            if (!runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, CommandOutcome>(
                    (world, generationId, game) =>
                    {
                        Entity actor = Resolve(world, entities.PlayerId);
                        Entity target = captured.Selection switch
                        {
                            LocalPlayableSelection.CabinDoor => Resolve(world, entities.CabinDoorId),
                            LocalPlayableSelection.SkyTelepad when entities.SkyTelepadId.IsValid =>
                                Resolve(world, entities.SkyTelepadId),
                            LocalPlayableSelection.FarGate when
                                entities.CentralFarGateId.IsValid && entities.FarReturnGateId.IsValid =>
                                ResolveNearestFarGate(world, game.WorldState, actor, entities),
                            LocalPlayableSelection.InformationalSign when entities.WelcomeSignId.IsValid =>
                                Resolve(world, entities.WelcomeSignId),
                            LocalPlayableSelection.FallbackLabSign when
                                entities.FallbackLabSignId.IsValid =>
                                Resolve(world, entities.FallbackLabSignId),
                            _ => Entity.Null,
                        };
                        game.WorldActions.ApplyPlayerCommand(
                            world, game.WorldState, actor, admitted, target,
                            out GamePlayerCommandResult result);
                        if (result.WasAdmitted && captured.FaceRequested &&
                            (!captured.FacingRequiresAcceptedLocomotion ||
                             result.Locomotion == GameWorldActionStatus.Accepted))
                        {
                            GameWorldActionStatus facing = game.WorldActions.FaceActorToward(
                                world, game.WorldState, actor,
                                captured.FacePoint.XMillimeters,
                                captured.FacePoint.ZMillimeters, out WorldYaw _);
                            if (facing != GameWorldActionStatus.Accepted)
                                throw new InvalidOperationException($"Local profile facing failed: {facing}.");
                        }
                        GameWorldActionStatus chest = GameWorldActionStatus.NotRequested;
                        if (chestInteraction)
                            chest = ToggleBreadContainer(world, game, entities);
                        else if (unsupported)
                            chest = GameWorldActionStatus.MissingTarget;
                        if ((captured.Selection is LocalPlayableSelection.InformationalSign or
                                LocalPlayableSelection.FallbackLabSign) &&
                            result.Interaction == GameWorldActionStatus.Accepted)
                        {
                            InformationalSignUiOpenResult opened = signUi.Open(
                                captured.Selection == LocalPlayableSelection.FallbackLabSign
                                    ? GameInformationalSigns.FallbackLab
                                    : GameInformationalSigns.ConceptDemoWelcome);
                            if (!opened.Succeeded)
                                result = result with { Interaction = GameWorldActionStatus.InvariantViolation };
                        }
                        return new CommandOutcome(result, chest);
                    }, out CommandOutcome outcome))
                throw new InvalidOperationException("The local command had no authoritative generation.");
            lastSequence = request.Command.Sequence;
            lastSelection = request.Selection;
            lastResult = outcome.Result;
            lastChest = outcome.Chest;
        }
    }

    private void ProcessAdministrative(
        ServerRuntime runtime,
        GameRuntimePersistence persistence,
        InventoryUiController inventoryUi,
        GamePlayableWorldEntities entities,
        ILocalPlayableServerExtension? serverExtension,
        ref ulong lastAcceptedSequence,
        ref bool lastSucceeded,
        ref string? lastMessage,
        ref LocalAdministrativeRequest lastRequest,
        ref ulong saveSequence)
    {
        while (_administrative.TryDequeue(out LocalAdministrativeRequest request))
        {
            if (request.Sequence <= lastAcceptedSequence) continue;
            lastAcceptedSequence = request.Sequence;
            lastRequest = request;
            switch (request.Kind)
            {
                case LocalAdministrativeOperationKind.Save:
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    ulong sequence = saveSequence++;
                    GameSaveResult result = persistence.TrySave(
                        new SaveSetContext(sequence, now), static capture => capture());
                    lastSucceeded = result.Succeeded;
                    lastMessage = result.Succeeded
                        ? $"Saved generation {sequence}"
                        : $"Save failed: {result.Failure?.Message}";
                    break;
                }
                case LocalAdministrativeOperationKind.Load:
                {
                    GameLoadResult result = persistence.TryLoad();
                    lastSucceeded = result.Succeeded;
                    lastMessage = result.Succeeded
                        ? $"Restored generation {result.GenerationId}; control intent reset"
                        : $"Load failed: {result.Failure?.Message}";
                    break;
                }
                case LocalAdministrativeOperationKind.Relocate:
                {
                    WorldPosition target = request.Position;
                    bool ran = runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
                        (world, generationId, game) => game.WorldActions.RelocateActor(
                            world, game.WorldState, Resolve(world, entities.PlayerId),
                            target, out WorldPosition _), out GameWorldActionStatus status);
                    lastSucceeded = ran && status == GameWorldActionStatus.Accepted;
                    lastMessage = lastSucceeded ? "Relocated" : $"Relocation failed: {status}";
                    break;
                }
                case LocalAdministrativeOperationKind.OpenInventory:
                {
                    bool ran = runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, InventoryOpenEntities>(
                        (world, _, _) => new InventoryOpenEntities(
                            Resolve(world, entities.BackpackId),
                            Resolve(world, entities.PlayerId),
                            Resolve(world, entities.CabinChestId)),
                        out InventoryOpenEntities openedEntities);
                    InventoryUiOpenResult opened = ran
                        ? Volatile.Read(ref _inventoryHotOpen) != 0
                            ? inventoryUi.OpenOwnedHot(
                                openedEntities.Backpack, openedEntities.Player)
                            : inventoryUi.OpenOwned(
                                openedEntities.Backpack, openedEntities.Player)
                        : default;
                    lastSucceeded = ran && opened.Succeeded;
                    lastMessage = opened.Message;
                    break;
                }
                case LocalAdministrativeOperationKind.EnableHosting:
                case LocalAdministrativeOperationKind.DisableHosting:
                {
                    bool enable = request.Kind == LocalAdministrativeOperationKind.EnableHosting;
                    lastSucceeded = serverExtension?.TrySetHostingEnabled(
                        enable, out lastMessage) == true;
                    lastMessage ??= serverExtension is null
                        ? "This client composition has no internal-server hosting adapter."
                        : enable ? "Hosting could not start." : "Hosting could not stop.";
                    break;
                }
            }
        }
    }

    private void ProcessChatSubmissions(
        ServerRuntime runtime,
        GameChatService chat,
        ParticipantId participant,
        ref ulong lastSequence)
    {
        while (_chatChannel.CanPublish && _chatSubmissions.TryDequeue(out LocalChatSubmission submission))
        {
            if (submission.Sequence <= lastSequence) continue;
            lastSequence = submission.Sequence;
            GameChatSubmissionResult result = chat.Submit(
                participant, submission.Text, (GameSpeechMode)submission.Mode);
            if (result.AcceptedSpeech) continue;
            if (!runtime.Game.TryCaptureClockSnapshot(out WorldClockSnapshot clock))
                throw new InvalidOperationException("Chat feedback could not capture authoritative time.");
            string text = result.PrivateReply ?? $"Chat rejected: {result.Status}.";
            bool error = !result.CommandSucceeded;
            if (!_chatChannel.TryPublishPrivate(
                    runtime.ActiveGenerationId, clock.SimulationTick,
                    error ? "Error" : "System", text, error))
                throw new InvalidOperationException("Preflighted local Chat feedback could not be retained.");
        }
    }

    private void DriveChat(GameChatService chat, ParticipantId participant, ParticipantId[] recipients)
    {
        if (!_chatChannel.CanPublish ||
            !chat.TryPeekNextSpeech(out GameSpeechFact fact, out int recipientCount))
            return;
        if (!chat.TryCopyRecipients(fact.Id, recipients, out int copied) || copied != recipientCount)
            throw new InvalidOperationException("The local Chat occurrence lost its retained audience.");
        bool localRecipient = recipients.AsSpan(0, copied).Contains(participant);
        if (!localRecipient)
            throw new InvalidOperationException("The single-player speech audience excluded its speaker.");
        if (!chat.TryConsumeNextSpeech(recipients, out GameSpeechFact consumed, out int consumedCount) ||
            consumed.Id != fact.Id || consumedCount != copied)
            throw new InvalidOperationException("The local Chat occurrence changed during handoff.");
        if (!_chatChannel.TryPublishSpatial(consumed))
            throw new InvalidOperationException("Preflighted local Chat occurrence could not be retained.");
    }

    private void ProcessChatProofs()
    {
        while (_chatChannel.TryTakeProof(out LocalDeliveryProof proof))
            _chatChannel.ResolveProof(proof);
    }

    private void ProcessWorldProofs(ref OfflineDirectChunkRecipient recipient)
    {
        while (_worldChannel.TryTakeProof(out LocalDeliveryProof proof))
        {
            if (!_worldChannel.TryResolveProof(proof, out OfflineDirectChunkDeliveryTicket ticket, out bool applied))
                continue;
            if (!applied || !ticket.IsValid) continue;
            OfflineDirectChunkAppliedProofStatus status = recipient.CommitAppliedProof(ticket);
            if (status is not (OfflineDirectChunkAppliedProofStatus.Applied or
                OfflineDirectChunkAppliedProofStatus.Duplicate or
                OfflineDirectChunkAppliedProofStatus.Stale))
                throw new InvalidOperationException($"Local public application proof was invalid: {status}.");
        }
    }

    private void DriveWorld(
        ServerRuntime runtime,
        GamePlayableWorldEntities entities,
        OfflineDirectChunkRecipient recipient,
        WorldPumpState state,
        WorldPublicRecord[] snapshotScratch,
        WorldPublicDeltaRecord[] deltaScratch)
    {
        if (!runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (world, generationId, game) =>
                {
                    Entity player = Resolve(world, entities.PlayerId);
                    if (!game.WorldState.TryReadPosition(world, player, out WorldPosition position))
                        throw new InvalidOperationException("The local public recipient lost its anchor.");
                    DriveWorldCore(
                        game.WorldState.Publications,
                        position,
                        world.TickCount,
                        recipient,
                        state,
                        snapshotScratch,
                        deltaScratch);
                    return true;
                }, out _))
            throw new InvalidOperationException("The local public authority was unavailable.");
    }

    private void DriveWorldCore(
        ChunkPublicationStore publications,
        in WorldPosition anchor,
        ulong simulationTick,
        OfflineDirectChunkRecipient recipient,
        WorldPumpState state,
        WorldPublicRecord[] snapshotScratch,
        WorldPublicDeltaRecord[] deltaScratch)
    {
        if (state.PendingCount == 0)
        {
            OfflineDirectChunkReconcileStatus status = recipient.Reconcile(
                publications,
                GameWorldState.DefaultSpace,
                anchor,
                state.Changes,
                out OfflineDirectChunkReconcileResult result);
            if (status != OfflineDirectChunkReconcileStatus.Reconciled)
                throw new InvalidOperationException($"Local public interest failed: {status}.");
            for (int index = 0; index < result.ChangeCount; index++)
                if (state.Changes[index].Kind == OfflineDirectChunkSubscriptionChangeKind.Left)
                    _worldChannel.Invalidate(state.Changes[index].Key);
            if (result.ChangeCount != 0)
            {
                state.Changes.AsSpan(0, result.ChangeCount).CopyTo(state.Pending);
                state.PendingHead = 0;
                state.PendingCount = result.ChangeCount;
            }
            state.SourceResetPending |= result.SourceReset;
        }

        if (state.SourceResetPending)
        {
            if (!_worldChannel.TryPublishControl(
                    LocalWorldDeliveryKind.PublicationEpochReset, default, publications.Epoch))
                return;
            state.SourceResetPending = false;
        }

        while (state.PendingCount != 0)
        {
            OfflineDirectChunkSubscriptionChange change = state.Pending[state.PendingHead];
            if (change.Kind == OfflineDirectChunkSubscriptionChangeKind.Left &&
                !_worldChannel.TryPublishControl(
                    LocalWorldDeliveryKind.PublicInterestRetired, change.Key, publications.Epoch))
                return;
            state.PendingHead++;
            state.PendingCount--;
        }

        OfflineDirectChunkSubscriptionView subscriptions = recipient.Subscriptions;
        for (int index = 0; index < subscriptions.Length && _worldChannel.CanPublish; index++)
        {
            OfflineDirectChunkSubscription subscription = subscriptions[index];
            SpatialChunkKey key = subscription.Interest.Key;
            if (_worldChannel.HasOutstanding(key)) continue;
            if (subscription.HasKnowledge)
            {
                bool exists = publications.TryGetCommittedView(key, out ChunkPublicationView committed);
                if (!exists && !subscription.Cursor.IsValid ||
                    exists && subscription.Cursor.IsValid &&
                    subscription.Cursor.Revision == committed.Revision)
                    continue;
            }
            OfflineDirectChunkReadStatus status = recipient.TryBeginRead(
                publications, index, simulationTick, out OfflineDirectChunkRead read);
            if (status == OfflineDirectChunkReadStatus.NotDue) continue;
            if (status == OfflineDirectChunkReadStatus.NoChanges)
            {
                recipient.CommitRead();
                continue;
            }
            if (status is not (OfflineDirectChunkReadStatus.Snapshot or
                OfflineDirectChunkReadStatus.Delta or OfflineDirectChunkReadStatus.ChunkAbsent))
                throw new InvalidOperationException($"Local publication read failed: {status}.");
            int count = read.Batch.Count;
            ChunkPublicationBatch batch = read.Batch;
            read.Snapshot.CopyTo(snapshotScratch);
            read.Deltas.CopyTo(deltaScratch);
            ulong deliveryId = _worldChannel.NextDeliveryId;
            OfflineDirectChunkDeliveryTicket ticket = recipient.ReleaseReadForRemote(deliveryId);
            if (!_worldChannel.TryPublishRead(
                    status,
                    batch,
                    snapshotScratch.AsSpan(0, status == OfflineDirectChunkReadStatus.Snapshot ? count : 0),
                    deltaScratch.AsSpan(0, status == OfflineDirectChunkReadStatus.Delta ? count : 0),
                    ticket))
                throw new InvalidOperationException("Preflighted local publication debt could not be retained.");
        }
    }

    private void CaptureView(
        ServerRuntime runtime,
        GamePlayableWorldEntities entities,
        GameGuestIdentity guestIdentity,
        ulong lastCommandSequence,
        LocalPlayableSelection lastSelection,
        GamePlayerCommandResult lastCommand,
        GameWorldActionStatus lastChest,
        LocalAdministrativeRequest lastAdministrative,
        bool lastAdministrativeSucceeded,
        string? lastAdministrativeMessage,
        LocalPlayableServerExtensionSnapshot serverExtension,
        ILocalPlayableServerExtension? extension,
        ClientPublicPresentationMetadata[] publicPresentationScratch)
    {
        if (!runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, LocalPlayableAuthorityView>(
                (world, generationId, game) =>
                {
                    Entity player = Resolve(world, entities.PlayerId);
                    if (!game.WorldState.TryReadPosition(world, player, out WorldPosition position) ||
                        !game.WorldState.TryReadPublic(world, player, out WorldPublicDescriptor descriptor) ||
                        !game.WorldState.Publications.TryGetPublicEntityId(
                            player, out WorldPublicEntityId publicActor) ||
                        !game.WorldActions.TryReadCabinDoor(
                            world, Resolve(world, entities.CabinDoorId), out bool doorOpen) ||
                        !game.Inventory.TryReadItem(
                            world, Resolve(world, entities.BreadId), out InventoryItem bread))
                        throw new InvalidOperationException("The local owner/private view was incomplete.");
                    bool inBackpack = bread.Parent == Resolve(world, entities.BackpackId);
                    WorldPreparedView prepared = game.WorldState.PreparedWorld ??
                        throw new InvalidOperationException("The local prepared world was unavailable.");
                    return new LocalPlayableAuthorityView(
                        generationId,
                        world.TickCount,
                        guestIdentity,
                        publicActor,
                        position,
                        descriptor.Orientation,
                        doorOpen,
                        inBackpack,
                        lastCommandSequence,
                        lastSelection,
                        lastCommand,
                        lastChest,
                        lastAdministrative.Sequence,
                        lastAdministrative.Kind,
                        lastAdministrativeSucceeded,
                        lastAdministrativeMessage,
                        serverExtension,
                        prepared);
                }, out LocalPlayableAuthorityView view))
            throw new InvalidOperationException("The local owner/private view could not be captured.");
        int publicPresentationCount = extension?.CopyPublicPresentationMetadata(
            publicPresentationScratch) ?? 0;
        if (publicPresentationCount < 0 ||
            publicPresentationCount > _latestPublicPresentation.Length)
            throw new InvalidOperationException(
                "The local shared public-presentation snapshot exceeded capacity.");
        lock (_viewGate)
        {
            _latestView = view;
            publicPresentationScratch.AsSpan(0, publicPresentationCount)
                .CopyTo(_latestPublicPresentation);
            if (publicPresentationCount < _latestPublicPresentationCount)
                Array.Clear(
                    _latestPublicPresentation,
                    publicPresentationCount,
                    _latestPublicPresentationCount - publicPresentationCount);
            _latestPublicPresentationCount = publicPresentationCount;
        }
    }

    private void BindControl(ServerRuntime runtime, GamePlayableWorldEntities entities)
    {
        if (!runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, generationId, game) => game.WorldActions.BeginControlSession(
                    world, Resolve(world, entities.PlayerId), _controlSession),
                out GameWorldActionStatus status) || status != GameWorldActionStatus.Accepted)
            throw new InvalidOperationException($"The local control session could not bind: {status}.");
    }

    private static GameWorldActionStatus ToggleBreadContainer(
        EcsWorld world,
        IGameGenerationStateProvider game,
        GamePlayableWorldEntities entities)
    {
        Entity player = Resolve(world, entities.PlayerId);
        Entity bread = Resolve(world, entities.BreadId);
        Entity backpack = Resolve(world, entities.BackpackId);
        Entity chest = Resolve(world, entities.CabinChestId);
        if (!game.Inventory.TryReadItem(world, bread, out InventoryItem item))
            return GameWorldActionStatus.MissingTarget;
        Entity source = item.Parent;
        Entity destination = source == backpack ? chest : backpack;
        return game.WorldActions.MoveItemToNearbyContainer(
            world, game.WorldState, game.Inventory,
            player, bread, source, destination, out _);
    }

    private static OfflineDirectChunkRecipient CreateRecipient() => new(
        new OfflineDirectChunkRecipientLimits(
            MaximumSubscriptions, MaximumPublicationRecords, MaximumPublicationRecords),
        GameChunkInterestProfile.Default,
        OfflineDirectChunkCadence.EvidenceProfile);

    private static Entity Resolve(EcsWorld world, PersistentEntityId id) =>
        world.TryResolvePersistentEntity(id, out Entity entity)
            ? entity
            : throw new InvalidOperationException($"Persistent entity {id} was unavailable.");

    private static GamePlayableWorldEntities ComposePlayableEntities(
        in GamePlayableWorldScene scene,
        in GamePlayableParticipantEntities participant) => new(
        participant.Player,
        participant.Backpack,
        scene.CabinDoor,
        scene.CabinChest,
        scene.WoodlandAnimal,
        participant.Bread,
        participant.PlayerId,
        participant.BackpackId,
        scene.CabinDoorId,
        scene.CabinChestId,
        scene.WoodlandAnimalId,
        participant.BreadId,
        scene.SkyTelepad,
        scene.SkyTelepadId,
        scene.CentralFarGate,
        scene.CentralFarGateId,
        scene.FarReturnGate,
        scene.FarReturnGateId,
        scene.WelcomeSign,
        scene.WelcomeSignId,
        scene.FallbackLabSign,
        scene.FallbackLabSignId);

    private static Entity ResolveNearestFarGate(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        in GamePlayableWorldEntities entities)
    {
        Entity central = Resolve(world, entities.CentralFarGateId);
        Entity far = Resolve(world, entities.FarReturnGateId);
        if (!worldState.TryReadPosition(world, actor, out WorldPosition actorPosition) ||
            !worldState.TryReadPosition(world, central, out WorldPosition centralPosition) ||
            !worldState.TryReadPosition(world, far, out WorldPosition farPosition))
            return Entity.Null;
        return DistanceSquared(actorPosition, centralPosition) <= DistanceSquared(actorPosition, farPosition)
            ? central
            : far;

        static Int128 DistanceSquared(in WorldPosition left, in WorldPosition right)
        {
            long x = checked(left.GlobalXMillimeters - right.GlobalXMillimeters);
            long z = checked(left.GlobalZMillimeters - right.GlobalZMillimeters);
            return (Int128)x * x + (Int128)z * z;
        }
    }

    private static PersistenceRestoreOptions RestoreOptions() => new(
        new PersistenceLimits(
            maximumArtifactBytes: 16 * 1024 * 1024,
            maximumEntities: 100_000,
            maximumShapes: 64,
            maximumComponents: 64,
            maximumSections: 256,
            maximumComponentPayloadBytes: 8 * 1024 * 1024,
            maximumTemporaryBytes: 64 * 1024 * 1024),
        ChunkLayoutOptions.KiB16);

    private void ThrowIfFaulted()
    {
        Exception? fault;
        lock (_viewGate) fault = _fault;
        if (fault is not null)
            throw new InvalidOperationException("The local authoritative application faulted.", fault);
    }

    private void EnsureOperational()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFaulted();
    }

    private readonly record struct CommandOutcome(
        GamePlayerCommandResult Result,
        GameWorldActionStatus Chest);

    private readonly record struct LocalPopulation(
        GamePlayableWorldScene Scene,
        GamePlayableParticipantEntities Participant,
        GamePlayableWorldEntities Entities);

    private readonly record struct InventoryOpenEntities(Entity Backpack, Entity Player, Entity Chest);

    private sealed class WorldPumpState
    {
        internal readonly OfflineDirectChunkSubscriptionChange[] Changes =
            new OfflineDirectChunkSubscriptionChange[MaximumTopologyChanges];
        internal readonly OfflineDirectChunkSubscriptionChange[] Pending =
            new OfflineDirectChunkSubscriptionChange[MaximumTopologyChanges];
        internal int PendingHead;
        internal int PendingCount;
        internal bool SourceResetPending = true;

        internal void Reset()
        {
            PendingHead = 0;
            PendingCount = 0;
            SourceResetPending = true;
        }
    }
}
