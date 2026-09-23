using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Client.Local;
using UltraSim.Client.StandardUi;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Spatial;
using UltraSim.UI;

namespace UltraSim.Client.Godot;

public enum CabinHumanBody : byte
{
    XBot = 1,
    YBot = 2,
}

public readonly record struct LocalCabinPresentationSnapshot(
    bool IsInitialized,
    CabinContentVariant ContentVariant,
    ContentHash ContentFingerprint,
    ContentCatalogGenerationId ContentGeneration,
    ContentHash WallTextureHash,
    CabinHumanBody HumanBody,
    string? DisplayName,
    int HumanMeshInstances,
    float CameraPivotMeters,
    float NameplateHeightMeters,
    float EffectsHeightMeters,
    bool AddedPresentationRealized,
    ulong ApplicationSessionEpoch,
    ulong ControlSessionEpoch,
    ulong AuthorityTicks,
    WorldPosition PlayerPosition,
    bool DoorIsOpen,
    bool BreadIsInBackpack,
    int OpenUiViews,
    ClientControlGrammar ControlGrammar,
    ClientCameraProfile CameraProfile,
    float CameraDistance,
    float CameraFovDegrees,
    int RenderDistanceChunks,
    int AuthoredChunks,
    int SemanticResidentChunks,
    int PeakSemanticResidentChunks,
    int RenderedChunks,
    int PeakRenderedChunks,
    string? InitializationError,
    ClientWorldSemanticCacheSnapshot SemanticWorld,
    ClientStructuralResidencySnapshot Residency,
    GodotWorldRendererSnapshot Renderer,
    GodotActorPresenterSnapshot Actors,
    LocalCabinUiOwnershipSnapshot Ui,
    OfficialLocalPresentationResult OfficialPresentation,
    ContentCacheSnapshot PresentationCache,
    PresentationGenerationSnapshot PresentationGeneration,
    GodotContentActorVisualResolverSnapshot ActorResources,
    GodotDerivedResourceStoreSnapshot DerivedResources,
    PresentationSourceKind WallTextureSource,
    GodotDerivedResourceKey WallDerivedKey);

/// <summary>
/// Cold, engine-thread-only observation of the F1.7 category hosts owned by one local gameplay
/// lifetime. Semantic view ownership remains independent from its native gump realization.
/// </summary>
public readonly record struct LocalCabinUiOwnershipSnapshot(
    int WorldUiHostChildren,
    int HudHostChildren,
    int InventoryHostChildren,
    int OpenSemanticViews,
    bool ChatFocused,
    bool GameplayInputAllowed,
    bool HudOwnedBySharedHud,
    bool WorldOwnedBySharedWorld,
    bool GumpsOwnedBySharedManager,
    bool BannerOwnedByHud,
    bool TooltipOwnedByContext);

public readonly record struct LocalCabinLifecycleResidueSnapshot(
    int OpenUiViews,
    int ActiveActors,
    int VisibleNameplates,
    int VisibleOverheadLabels,
    int RenderedChunks,
    bool ChatFocused,
    bool GameplayInputAllowed,
    int RendererNodes,
    int RendererResources,
    int PrimitiveFallbackActors,
    int AnimatedActors,
    int EquippedPresentations,
    LocalCabinUiOwnershipSnapshot Ui);

public readonly record struct LocalF16DerivedAcceptanceSnapshot(
    ClientSemanticHash SemanticHash,
    ContentHash LowerPresentationFingerprint,
    ContentHash UserPresentationFingerprint,
    ContentHash RestoredPresentationFingerprint,
    ContentHash LowerTextureHash,
    ContentHash UserTextureHash,
    ContentHash RestoredTextureHash,
    GodotDerivedResourceKey LowerDerivedKey,
    GodotDerivedResourceKey UserDerivedKey,
    GodotDerivedResourceKey RestoredDerivedKey,
    GodotDerivedResourceStoreSnapshot Before,
    GodotDerivedResourceStoreSnapshot AfterUser,
    GodotDerivedResourceStoreSnapshot AfterRestore,
    GodotDerivedResourceStoreSnapshot AfterIneffective);

public readonly record struct LocalF17UiAcceptanceSnapshot(
    int MaximumOpenSemanticViews,
    int MaximumOpenNativeWindows,
    Vector2 InventoryInitialPosition,
    Vector2 MonitorInitialPosition,
    Vector2 DraggedPosition,
    bool ContextSelectInvoked,
    bool ContextInteractInvoked,
    GodotGumpWindowManagerSnapshot RetiredGumps);

/// <summary>
/// Permanent Godot consumer for one Network-free local playable application session.
/// Authority remains behind <see cref="LocalPlayableSession"/>; this node owns only Client Core
/// semantic caches and Godot presentation.
/// </summary>
public partial class LocalCabinClient : Node3D
{
    private enum ControlProfile : byte
    {
        OrbitCommand,
        ThirdPersonDirect,
        IndependentMoveLook,
    }

    private enum SelectionKind : byte
    {
        None,
        Terrain,
        CabinDoor,
        CabinChest,
        SkyTelepad,
        FarGate,
        InformationalSign,
        FallbackLabSign,
        WoodlandAnimal,
        IronSword,
    }

    private const int MaximumChatTextCharacters = 256;
    private const float CameraPivotMeters = 1.6f;
    private const float MinimumZoomCameraDistanceMeters = 3f;
    private const float OrbitDefaultPitchRadians = MathF.PI * 0.25f;
    private const float OrbitDefaultCameraDistanceMeters =
        ClientSettingsResolver.DefaultOrbitCameraDistance;
    private const float NameplateHeightMeters = 2.0f;
    private const float EffectsHeightMeters = 0.9f;

    private CabinContentOracle? _contentOracle;
    private CabinResolvedContent? _resolvedContent;
    private string? _saveRoot;
    private GodotSessionUiLease? _sessionUi;
    private CabinHumanBody _humanBody = CabinHumanBody.XBot;
    private string _displayName = "Local Guest";
    private GameGuestAdmissionRequest _guestAdmission = new(
        GameGuestBody.HumanFemale, "Local Guest");
    // F0.2 acceptance retains the historical Cabin fixture in the maintained application;
    // normal Single Player composition selects the Concept Demo explicitly.
    private bool _useConceptDemoWorld;
    private bool _primitiveOnlyPresentation;
    private string? _presentationCacheRoot;
    private string? _officialContentRoot;
    private ContentCache? _presentationCache;
    private PresentationGeneration? _presentationGeneration;
    private ContentScopeActivationKey _cabinPresentationKey;
    private ulong _presentationSelectionRevision;
    private GodotContentActorVisualResolver? _actorVisuals;
    private GodotDerivedResourceStore? _derivedResources;
    private OfficialLocalPresentationResult _officialPresentation;
    private ResolvedClientSettings? _settings;
    private CabinCatalogAssetResolver? _assetResolver;
    private LocalPlayableSession? _localSession;
    private ILocalPlayableServerExtensionFactory? _serverExtensionFactory;
    private BoundWorldPack? _boundWorld;
    private ClientWorldSemanticCache? _cache;
    private ClientStructuralResidency? _residency;
    private GodotWorldRenderer? _renderer;
    private GodotSlidingRenderWindow? _window;
    private ClientWorldInputSequencer? _sequencer;
    private ClientWorldInputScheduler? _inputScheduler;
    private readonly OrbitCommandProfile _orbit = new();
    private readonly ThirdPersonDirectProfile _thirdPerson = new();
    private readonly IndependentMoveLookProfile _independent = new();
    private ClientPresentationChange[] _presentationChanges = [];
    private ClientPresentationBinding[] _presentationBindings = [];
    private ClientSemanticHash[] _semanticHashesBeforeRefresh = [];
    private ClientSemanticChunkHandle[] _actorPresentedChunks = [];
    private ClientActorPresentationChange[] _actorChanges = [];
    private readonly ClientActorPresentationRecord[] _actorRows =
        new ClientActorPresentationRecord[64];
    private readonly ClientPublicPresentationMetadata[] _sharedMetadata =
        new ClientPublicPresentationMetadata[128];
    private readonly ClientPublicPresentationMetadata[] _previousSharedMetadata =
        new ClientPublicPresentationMetadata[128];
    private int _previousSharedMetadataCount;
    private ClientPublicPresentationMetadataState? _publicPresentationMetadata;
    private ClientActorPresentationState? _actorState;
    private GodotActorPresenter? _actorPresenter;
    private readonly ClientChatOccurrence[] _chatRows = new ClientChatOccurrence[64];
    private ClientSemanticChunkInfo[] _chatChunkInfo = [];
    private Camera3D? _camera;
    private Label? _status;
    private Label? _details;
    private RichTextLabel? _chatFeed;
    private LineEdit? _chatInput;
    private OptionButton? _chatMode;
    private Label? _overheadSpeech;
    private GodotSystemChatBanner? _systemChatBanner;
    private GodotWorldHoverTooltip? _hoverTooltip;
    private Control? _ownedHudRoot;
    private Control? _ownedWorldUiRoot;
    private Control? _inventoryHost;
    private GodotUiRenderer? _uiRenderer;
    private UiClientRuntime? _uiClient;
    private LocalQueuedUiClient? _uiBridge;
    private Node3D? _humanPresentation;
    private int _humanMeshInstances;
    private float _cameraPivotMeters = CameraPivotMeters;
    private bool _addedPresentationRealized;
    private WorldPosition _player;
    private ClientControlWorldPoint _pointerPoint;
    private bool _pointerPointValid;
    private WorldPublicEntityId _pointerTarget;
    private uint _pointerTargetPrefab;
    private WorldPublicEntityId _selectedTarget;
    private bool _primaryDown;
    private bool _primaryPressed;
    private bool _primaryReleased;
    private bool _secondaryPressed;
    private bool _cancelPressed;
    private bool _contextInteractPending;
    private float _cameraYaw = -0.7f;
    private float _cameraPitch = OrbitDefaultPitchRadians;
    private float _cameraDistance = OrbitDefaultCameraDistanceMeters;
    private ControlProfile _profile;
    private bool _profileStopPending;
    private SelectionKind _selection;
    private string _lastOutcome = "Ready";
    private bool _initialized;
    private ClientChatState? _chatState;
    private ulong _chatAppliedPresentationCount;
    private ulong _nextChatSubmission = 1;
    private ulong _nextAdministrativeRequest = 1;
    private ulong _residentGenerationId;
    private ulong _observedCommandSequence;
    private ulong _observedAdministrativeSequence;
    private WorldPublicEntityId _overheadAnchor;
    private ulong _overheadOriginGenerationId;
    private ulong _overheadExpiresAtMilliseconds;
    private int _peakRenderedChunks;
    private int _effectiveRenderDistanceChunks;
    private bool _gameplayInputAllowed = true;
    private bool _gameplayInputAdmittedLastFrame;
    private bool _gameplayStopPending;
    private bool _shutdown;
    private string? _initializationError;

    public void Configure(
        CabinContentOracle contentOracle,
        CabinResolvedContent resolvedContent,
        string saveRoot,
        GodotSessionUiLease? sessionUi = null,
        CabinHumanBody humanBody = CabinHumanBody.XBot,
        ResolvedClientSettings? settings = null,
        bool useConceptDemoWorld = false,
        string? guestDisplayName = null,
        ILocalPlayableServerExtensionFactory? serverExtensionFactory = null,
        bool primitiveOnlyPresentation = false,
        string? presentationCacheRoot = null,
        string? officialContentRoot = null)
    {
        ArgumentNullException.ThrowIfNull(contentOracle);
        ArgumentNullException.ThrowIfNull(resolvedContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        if (IsInsideTree() || _contentOracle is not null)
            throw new InvalidOperationException("Cabin composition must be configured before entering the tree.");
        if (humanBody is not (CabinHumanBody.XBot or CabinHumanBody.YBot))
            throw new ArgumentOutOfRangeException(nameof(humanBody));
        _contentOracle = contentOracle;
        _resolvedContent = resolvedContent;
        _saveRoot = saveRoot;
        _sessionUi = sessionUi;
        _humanBody = humanBody;
        _guestAdmission = new GameGuestAdmissionRequest(
            humanBody == CabinHumanBody.XBot
                ? GameGuestBody.HumanFemale
                : GameGuestBody.HumanMale,
            guestDisplayName ?? (humanBody == CabinHumanBody.XBot
                ? "Human Female"
                : "Human Male"));
        _useConceptDemoWorld = useConceptDemoWorld;
        _primitiveOnlyPresentation = primitiveOnlyPresentation;
        _presentationCacheRoot = presentationCacheRoot;
        _officialContentRoot = officialContentRoot;
        _serverExtensionFactory = serverExtensionFactory;
        _settings = settings ?? ClientSettingsResolver.Resolve(ClientSettingsDocument.Empty);
    }

    public void SetGameplayInputAllowed(bool allowed)
    {
        _gameplayInputAllowed = allowed;
        if (!allowed) SetWorldRevealHeld(false);
    }

    public void ApplySettings(ResolvedClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        int previousRenderDistance = _settings?.RenderDistanceChunks ?? settings.RenderDistanceChunks;
        _settings = settings;
        ApplyResolvedSettings();
        if (_initialized && previousRenderDistance != settings.RenderDistanceChunks &&
            _localSession is not null && _localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
        {
            RebuildClientProjection(view, preserveSemanticCache: true);
            ClientStructuralResidencySnapshot residency = _residency?.GetSnapshot() ?? default;
            GodotWorldRendererSnapshot render = _renderer?.GetSnapshot() ?? default;
            string clamp = _effectiveRenderDistanceChunks == settings.RenderDistanceChunks
                ? string.Empty
                : $" (requested {settings.RenderDistanceChunks}; limited by this world)";
            _lastOutcome = $"Render distance applied: {_effectiveRenderDistanceChunks} chunks{clamp}; " +
                $"({residency.SemanticResidentChunks} resident / {render.ActiveRenderChunks} rendered)";
            UpdateHud();
        }
    }

    /// <summary>Programmatic F1.5 seam for a future settings UI; publication occurs next frame.</summary>
    public bool TrySetEnabledUserResourcePacks(
        IEnumerable<UserResourcePackIdentity> identities,
        out ContentCacheError error)
    {
        if (_presentationCache is null)
        {
            error = ContentCacheError.InvalidRequest;
            return false;
        }
        return _presentationCache.TrySetEnabledUserResourcePacks(identities, out error);
    }

    /// <summary>Permanent F1.6 product-path canary for the real Cabin wall consumer.</summary>
    public LocalF16DerivedAcceptanceSnapshot RunF16DerivedAcceptance()
    {
        if (!_initialized || _presentationCache is null || _presentationGeneration is null ||
            _derivedResources is null || _assetResolver is null || _cache is null ||
            _contentOracle is null)
            throw new InvalidOperationException(
                "F1.6 requires the initialized content-backed Local presentation path.");
        LocalCabinPresentationSnapshot lower = GetPresentationSnapshot();
        ClientSemanticHash semanticHash = lower.SemanticWorld.WorldSemanticHash;
        CabinLocalContentPackage userPack = _contentOracle.CreateUserWallTexturePack();
        CabinLocalContentPackage ineffectivePack = _contentOracle.CreateIneffectiveUserTexturePack();
        foreach (CabinLocalContentPackage pack in new[] { userPack, ineffectivePack })
            if (!_presentationCache.TryInstallUserResourcePack(
                    pack.Manifest, pack.Payloads, out _, out ContentCacheError installError))
                throw new InvalidOperationException(
                    $"The F1.6 Local user pack could not install: {installError}.");

        var userIdentity = new UserResourcePackIdentity(
            userPack.Manifest.PackageId, userPack.Manifest.ManifestHash);
        if (!TrySetEnabledUserResourcePacks([userIdentity], out ContentCacheError enableError))
            throw new InvalidOperationException(
                $"The F1.6 Local wall pack could not be enabled: {enableError}.");
        RefreshUserPresentationIfChanged();
        LocalCabinPresentationSnapshot user = GetPresentationSnapshot();
        if (user.WallTextureSource != PresentationSourceKind.User ||
            user.WallTextureHash == lower.WallTextureHash ||
            user.WallDerivedKey == lower.WallDerivedKey ||
            user.PresentationGeneration.StableFingerprint ==
                lower.PresentationGeneration.StableFingerprint ||
            user.SemanticWorld.WorldSemanticHash != semanticHash ||
            user.ContentFingerprint != lower.ContentFingerprint)
            throw new InvalidOperationException(
                "The F1.6 wall override changed semantic truth or failed to change presentation.");

        if (!TrySetEnabledUserResourcePacks([], out ContentCacheError disableError))
            throw new InvalidOperationException(
                $"The F1.6 Local wall pack could not be disabled: {disableError}.");
        RefreshUserPresentationIfChanged();
        LocalCabinPresentationSnapshot restored = GetPresentationSnapshot();
        if (restored.WallTextureSource != PresentationSourceKind.Local ||
            restored.WallTextureHash != lower.WallTextureHash ||
            restored.WallDerivedKey != lower.WallDerivedKey ||
            restored.PresentationGeneration.StableFingerprint !=
                lower.PresentationGeneration.StableFingerprint ||
            restored.SemanticWorld.WorldSemanticHash != semanticHash)
            throw new InvalidOperationException(
                "Disabling the F1.6 wall override did not restore exact lower presentation.");

        var ineffectiveIdentity = new UserResourcePackIdentity(
            ineffectivePack.Manifest.PackageId, ineffectivePack.Manifest.ManifestHash);
        if (!TrySetEnabledUserResourcePacks(
                [ineffectiveIdentity], out ContentCacheError noOpError))
            throw new InvalidOperationException(
                $"The ineffective F1.6 Local pack could not be selected: {noOpError}.");
        GodotDerivedResourceStoreSnapshot beforeNoOp = _derivedResources.GetSnapshot();
        RefreshUserPresentationIfChanged();
        LocalCabinPresentationSnapshot noOp = GetPresentationSnapshot();
        if (noOp.PresentationGeneration.StableFingerprint !=
                restored.PresentationGeneration.StableFingerprint ||
            noOp.WallDerivedKey != restored.WallDerivedKey ||
            noOp.DerivedResources.TextureBuilds != beforeNoOp.TextureBuilds ||
            noOp.DerivedResources.MaterialBuilds != beforeNoOp.MaterialBuilds ||
            noOp.SemanticWorld.WorldSemanticHash != semanticHash)
            throw new InvalidOperationException(
                "An ineffective F1.6 Local pack churned native or semantic presentation.");
        if (!TrySetEnabledUserResourcePacks([], out ContentCacheError clearError))
            throw new InvalidOperationException(
                $"The ineffective F1.6 Local pack could not be cleared: {clearError}.");
        RefreshUserPresentationIfChanged();

        return new LocalF16DerivedAcceptanceSnapshot(
            semanticHash,
            lower.PresentationGeneration.StableFingerprint,
            user.PresentationGeneration.StableFingerprint,
            restored.PresentationGeneration.StableFingerprint,
            lower.WallTextureHash,
            user.WallTextureHash,
            restored.WallTextureHash,
            lower.WallDerivedKey,
            user.WallDerivedKey,
            restored.WallDerivedKey,
            lower.DerivedResources,
            user.DerivedResources,
            restored.DerivedResources,
            noOp.DerivedResources);
    }

    public LocalPlayableServerExtensionSnapshot GetHostingSnapshot()
    {
        if (_localSession is null ||
            !_localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
            return default;
        return view.ServerExtension;
    }

    public bool TrySetHostingEnabled(bool enabled)
    {
        if (_localSession is null) return false;
        ulong request = _nextAdministrativeRequest++;
        bool queued = _localSession.TryRequestHosting(request, enabled);
        _lastOutcome = queued
            ? enabled ? "Starting remote hosting…" : "Stopping remote hosting…"
            : "Hosting request queue busy";
        UpdateHud();
        return queued;
    }

    public LocalCabinPresentationSnapshot GetPresentationSnapshot()
    {
        LocalPlayableSessionSnapshot local = _localSession?.GetSnapshot() ?? default;
        ClientStructuralResidencySnapshot residency = _residency?.GetSnapshot() ?? default;
        GodotWorldRendererSnapshot render = _renderer?.GetSnapshot() ?? default;
        ClientWorldSemanticCacheSnapshot semantic = _cache?.GetSnapshot() ?? default;
        GodotActorPresenterSnapshot actors = _actorPresenter?.GetSnapshot() ?? default;
        LocalCabinUiOwnershipSnapshot ui = GetUiOwnershipSnapshot();
        _peakRenderedChunks = Math.Max(_peakRenderedChunks, render.ActiveRenderChunks);
        LocalPlayableAuthorityView authority = default;
        if (_localSession is not null) _ = _localSession.TryGetLatestView(out authority);
        return new LocalCabinPresentationSnapshot(
            _initialized,
            _resolvedContent?.Variant ?? default,
            _resolvedContent?.Catalog.Fingerprint ?? default,
            _resolvedContent?.Catalog.Generation ?? default,
            _assetResolver?.WallTextureHash ?? default,
            _humanBody,
            _displayName,
            _humanMeshInstances,
            _cameraPivotMeters,
            NameplateHeightMeters,
            EffectsHeightMeters,
            _addedPresentationRealized,
            _localSession?.ApplicationSessionEpoch ?? 0,
            _localSession?.ControlSession.Value ?? 0,
            local.AuthorityTicks,
            authority.PlayerPosition,
            authority.DoorIsOpen,
            authority.BreadIsInBackpack,
            _uiClient?.OpenViewCount ?? 0,
            _settings?.ControlGrammar ?? default,
            _settings?.CameraProfile ?? default,
            _cameraDistance,
            _camera?.Fov ?? 0,
            _effectiveRenderDistanceChunks,
            residency.AuthoredChunks,
            residency.SemanticResidentChunks,
            residency.PeakSemanticResidentChunks,
            render.ActiveRenderChunks,
            _peakRenderedChunks,
            _initializationError,
            semantic,
            residency,
            render,
            actors,
            ui,
            _officialPresentation,
            _presentationCache?.GetSnapshot() ?? default,
            _presentationGeneration?.Snapshot ?? default,
            _actorVisuals?.GetSnapshot() ?? default,
            _derivedResources?.GetSnapshot() ?? default,
            _assetResolver?.WallTextureSource ?? default,
            _assetResolver?.WallDerivedKey ?? default);
    }

    private LocalCabinUiOwnershipSnapshot GetUiOwnershipSnapshot() => new(
        _ownedWorldUiRoot?.GetChildCount() ?? 0,
        _ownedHudRoot?.GetChildCount() ?? 0,
        _inventoryHost?.GetChildCount() ?? 0,
        _uiClient?.OpenViewCount ?? 0,
        _chatInput?.HasFocus() == true,
        _gameplayInputAllowed,
        _sessionUi is not null && ReferenceEquals(_ownedHudRoot?.GetParent(), _sessionUi.HudHost),
        _sessionUi is not null && ReferenceEquals(_ownedWorldUiRoot?.GetParent(), _sessionUi.WorldHost),
        _sessionUi is not null && ReferenceEquals(_inventoryHost, _sessionUi.Gumps.Host),
        _sessionUi is not null && ReferenceEquals(_systemChatBanner?.NativeParent, _ownedHudRoot),
        _sessionUi is not null && ReferenceEquals(_hoverTooltip?.NativeParent, _sessionUi.ContextHost));

    /// <summary>Permanent D0.1 canary for a live, settings-shaped window rebuild.</summary>
    public LocalCabinPresentationSnapshot RunD01Acceptance()
    {
        if (!_initialized || _settings is null || !_useConceptDemoWorld)
            throw new InvalidOperationException("D0.1 requires the initialized Concept Demo consumer.");
        ClientWorldSemanticCache semanticCache = _cache ??
            throw new InvalidOperationException("The D0.1 semantic cache was unavailable.");
        if (CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.Character) != 1)
            throw new InvalidOperationException(
                "The controlled actor was not visible before the D0.1 radius transition.");
        // Normalize through an actual resize even when D0.0 left the requested radius at one.
        ApplySettings(WithRenderDistance(_settings, 2));
        AssertSemanticContinuity("first radius-2 transition");
        ApplySettings(WithRenderDistance(_settings, 1));
        AssertSemanticContinuity("radius-1 transition");
        LocalCabinPresentationSnapshot before = GetPresentationSnapshot();
        ClientStructuralResidencySnapshot residencyBefore = _residency!.GetSnapshot();
        int terrainTrianglesBefore = CountRenderedTerrainTriangles();
        ClientWorldInputSnapshot inputBefore = _sequencer?.Snapshot() ?? default;
        if (before.RenderDistanceChunks != 1 ||
            before.AuthoredChunks != GamePlayableWorld.ConceptChunkCount ||
            residencyBefore.DesiredResidentChunks != 25 ||
            before.SemanticResidentChunks < residencyBefore.DesiredResidentChunks ||
            before.RenderedChunks != 9)
            throw new InvalidOperationException(
                $"The default D0.1 working set was unexpected: {before.AuthoredChunks}/" +
                $"{before.SemanticResidentChunks}/{before.RenderedChunks}.");

        ApplySettings(WithRenderDistance(_settings, 2));
        AssertSemanticContinuity("second radius-2 transition");

        LocalCabinPresentationSnapshot after = GetPresentationSnapshot();
        ClientStructuralResidencySnapshot residencyAfter = _residency!.GetSnapshot();
        int terrainTrianglesAfter = CountRenderedTerrainTriangles();
        ClientWorldInputSnapshot inputAfter = _sequencer?.Snapshot() ?? default;
        if (after.RenderDistanceChunks != 2 ||
            after.AuthoredChunks != GamePlayableWorld.ConceptChunkCount ||
            residencyAfter.DesiredResidentChunks != 49 ||
            after.SemanticResidentChunks < residencyAfter.DesiredResidentChunks ||
            after.RenderedChunks != 25 ||
            after.PeakSemanticResidentChunks < 49 || after.PeakRenderedChunks < 25 ||
            terrainTrianglesAfter <= terrainTrianglesBefore)
            throw new InvalidOperationException(
                $"The expanded D0.1 working set was unexpected: {after.AuthoredChunks}/" +
                $"{after.SemanticResidentChunks}/{after.RenderedChunks}; terrain triangles " +
                $"{terrainTrianglesBefore}->{terrainTrianglesAfter}.");
        if (inputBefore.ControlSession != inputAfter.ControlSession ||
            inputBefore.CommandsProduced != inputAfter.CommandsProduced ||
            inputBefore.LastSequence != inputAfter.LastSequence)
            throw new InvalidOperationException(
                "A render-distance projection rebuild reset the live control-session sequence.");
        ApplySettings(WithRenderDistance(_settings, 3));
        AssertSemanticContinuity("radius-3 transition");
        LocalCabinPresentationSnapshot maximum = GetPresentationSnapshot();
        ClientStructuralResidencySnapshot residencyMaximum = _residency!.GetSnapshot();
        if (!ReferenceEquals(semanticCache, _cache) ||
            maximum.RenderDistanceChunks != 3 ||
            residencyMaximum.DesiredResidentChunks != 81 ||
            maximum.SemanticResidentChunks < residencyMaximum.DesiredResidentChunks ||
            maximum.RenderedChunks != 49 ||
            CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.Character) != 1)
            throw new InvalidOperationException(
                "The maximum D0.1 window did not preserve its semantic cache and controlled actor: " +
                $"{maximum.AuthoredChunks}/{maximum.SemanticResidentChunks}/" +
                $"{maximum.RenderedChunks}; actors={CountPresentedCharacters()}.");

        WorldPosition movementStart = _player;
        if (_sequencer is null || !_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDirect(
                        checked((short)-GamePlayerCommand.MaximumAxisMagnitude), 0),
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand movement))
            throw new InvalidOperationException("The post-settings movement probe could not be formed.");
        ApplyCommand(movement, false, default, false);
        DriveLocalUntil(() =>
            _localSession!.TryGetLatestView(out LocalPlayableAuthorityView current) &&
            current.LastCommandSequence == movement.Sequence &&
            _player.GlobalXMillimeters < movementStart.GlobalXMillimeters);
        SubmitStopAfterSettings();
        return maximum;

        void AssertSemanticContinuity(string stage)
        {
            if (!ReferenceEquals(semanticCache, _cache) ||
                CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.Character) != 1)
                throw new InvalidOperationException(
                    $"The {stage} replaced semantic application state or retired the controlled actor.");
        }

        void SubmitStopAfterSettings()
        {
            if (!_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.None,
                        true),
                    out GamePlayerCommand stop))
                throw new InvalidOperationException("The post-settings movement stop could not be formed.");
            ApplyCommand(stop, false, default, false);
            DriveLocalUntil(() =>
                _localSession!.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence == stop.Sequence);
        }
    }

    /// <summary>
    /// Permanent D0.2 canary for sparse authored geography and reciprocal authoritative gates.
    /// The two structural clusters are separated by unauthored distance, so each crossing must
    /// retire and rebuild the bounded semantic/presentation working set.
    /// </summary>
    public LocalCabinPresentationSnapshot RunD02Acceptance()
    {
        if (!_initialized || _settings is null || !_useConceptDemoWorld ||
            _boundWorld is null || _cache is null || _residency is null ||
            _window is null || _renderer is null || _localSession is null || _sequencer is null)
            throw new InvalidOperationException("D0.2 requires the initialized Concept Demo consumer.");
        ApplySettings(WithRenderDistance(_settings, 1));
        SpatialChunkKey centralKey = GamePlayableWorld.PlayerStart.ToChunkKey(_boundWorld.Space);
        SpatialChunkKey farKey = GamePlayableWorld.FarGateDestination.ToChunkKey(_boundWorld.Space);
        var gapKey = new SpatialChunkKey(
            _boundWorld.Space,
            (GamePlayableWorld.ConceptMaximumChunk + GamePlayableWorld.FarIslandMinimumChunkX) / 2,
            0);
        if (_boundWorld.ChunkCount != GamePlayableWorld.ConceptChunkCount ||
            _boundWorld.TryGetBaseTerrain(gapKey, out _))
            throw new InvalidOperationException(
                "The Concept Demo does not contain two sparse authored clusters separated by unauthored distance.");

        ClientStructuralResidencySnapshot residencyBefore = _residency.GetSnapshot();
        ClientPresentationWindowSnapshot presentationBefore = _window.GetSnapshot();
        UseGate(GamePlayableWorld.CentralFarGatePosition, GamePlayableWorld.FarGateDestination);
        DriveLocalUntil(() =>
            _player == GamePlayableWorld.FarGateDestination &&
            !_cache.TryGetResidentHandle(centralKey, out _) &&
            _cache.TryGetResidentHandle(farKey, out _) &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.FarGate) == 1 &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.CabinDoor) == 0 &&
            _renderer.GetSnapshot().ActiveRenderChunks == 9);
        ClientStructuralResidencySnapshot farResidency = _residency.GetSnapshot();
        ClientPresentationWindowSnapshot farPresentation = _window.GetSnapshot();
        if (farResidency.DesiredResidentChunks != 25 || farResidency.SemanticResidentChunks != 25 ||
            farResidency.ResetTransitions != residencyBefore.ResetTransitions + 1 ||
            farPresentation.ResetTransitions != presentationBefore.ResetTransitions + 1 ||
            farPresentation.CenterChunk.Coord.X != GamePlayableWorld.FarIslandCenterChunkX)
            throw new InvalidOperationException(
                "The central-to-far gate did not perform one bounded structural/presentation reset.");

        ReleaseInteraction();
        UseGate(GamePlayableWorld.FarReturnGatePosition, GamePlayableWorld.CentralGateDestination);
        DriveLocalUntil(() =>
            _player == GamePlayableWorld.CentralGateDestination &&
            _cache.TryGetResidentHandle(centralKey, out _) &&
            !_cache.TryGetResidentHandle(farKey, out _) &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.FarGate) == 1 &&
            _renderer.GetSnapshot().ActiveRenderChunks == 9);
        ClientStructuralResidencySnapshot returnedResidency = _residency.GetSnapshot();
        ClientPresentationWindowSnapshot returnedPresentation = _window.GetSnapshot();
        if (returnedResidency.DesiredResidentChunks != 25 ||
            returnedResidency.SemanticResidentChunks != 25 ||
            returnedResidency.ResetTransitions != residencyBefore.ResetTransitions + 2 ||
            returnedPresentation.ResetTransitions != presentationBefore.ResetTransitions + 2)
            throw new InvalidOperationException(
                "The far-to-central gate did not retire and rebuild the bounded working set.");
        ReleaseInteraction();
        return GetPresentationSnapshot();

        void UseGate(in WorldPosition gate, in WorldPosition destination)
        {
            WorldPosition expectedDestination = destination;
            RelocateControlledActor(gate);
            SetProfile(ControlProfile.ThirdPersonDirect);
            if (!TrySetPointerWorldPoint(gate.GlobalXMillimeters, gate.GlobalZMillimeters))
                throw new InvalidOperationException("The authored Far Gate had no structural pointer support.");
            Select(new ClientControlWorldPoint(gate.GlobalXMillimeters, gate.GlobalZMillimeters));
            if (_selection != SelectionKind.FarGate ||
                !_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.Interact,
                        true),
                    out GamePlayerCommand command))
                throw new InvalidOperationException("The Far Gate interaction could not be formed.");
            ApplyCommand(command, false, default, false);
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
                view.LastCommandSequence == command.Sequence &&
                view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
                _player == expectedDestination);
        }

        void ReleaseInteraction()
        {
            if (!_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.None,
                        true),
                    out GamePlayerCommand release))
                throw new InvalidOperationException("The Far Gate interaction release could not be formed.");
            ApplyCommand(release, false, default, false);
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
                view.LastCommandSequence == release.Sequence);
        }
    }

    private static ResolvedClientSettings WithRenderDistance(
        ResolvedClientSettings settings,
        int renderDistanceChunks) =>
        ClientSettingsResolver.Resolve(new ClientSettingsDocument(new ClientSettingsLayer(
            settings.UiScale,
            settings.MasterVolume,
            settings.PointerSensitivity,
            settings.InvertY,
            settings.ControlGrammar,
            settings.CameraProfile,
            settings.CameraDistance,
            settings.CameraFovDegrees,
            settings.Bindings,
            renderDistanceChunks)));

    public override void _Ready()
    {
        try
        {
            if (_contentOracle is null)
            {
                _contentOracle = CabinContentOracle.Create();
                _resolvedContent = _contentOracle.Default;
                _saveRoot = ProjectSettings.GlobalizePath("user://t0-10-saves");
            }
            _settings ??= ClientSettingsResolver.Resolve(ClientSettingsDocument.Empty);
            ApplyResolvedSettings();
            CreateSceneFurniture();
            StartFreshWorld();
            CreateInventoryConsumer();
            RealizeAddedContent();
            _initialized = true;
            SetPhysicsProcess(true);
            if (OS.GetCmdlineUserArgs().Contains("--smoke", StringComparer.Ordinal))
                RunF02Acceptance();
            UpdateHud();
            GodotWorldRendererSnapshot render = _renderer?.GetSnapshot() ?? default;
            GD.Print(
                $"T0.10 PLAYABLE CLIENT READY chunks={render.ActiveRenderChunks} " +
                $"player={_player} profile={_profile}");
            if (OS.GetCmdlineUserArgs().Contains("--smoke", StringComparer.Ordinal))
            {
                GD.Print("T0.10 PLAYABLE CLIENT SMOKE PASS");
                GetTree().Quit(0);
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Playable client initialization failed: {exception}");
            _initializationError = exception.ToString();
            _lastOutcome = "Initialization failed — see Godot output";
            UpdateHud();
            SetPhysicsProcess(false);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_initialized || _localSession is null || _sequencer is null ||
            _inputScheduler is null || _camera is null)
            return;
        try
        {
            RefreshUserPresentationIfChanged();
            if (_localSession.TryGetLatestView(out LocalPlayableAuthorityView priorView))
                _inputScheduler.ObserveCompleted(priorView.LastCommandSequence);
            _ = _inputScheduler.TryPump(
                Stopwatch.GetTimestamp(), TrySubmitScheduledLocalCommand);
            UpdatePointerWorldPoint();
            _sessionUi?.ContextMenu.UpdateValidity();
            UpdateWorldHover();
            PumpChat();
            _ = _uiBridge?.Pump();
            bool gameplayInputAdmitted = ObserveGameplayInputAdmission();
            ClientWorldInputSample physical = GodotWorldInput.Capture(
                _settings ?? throw new InvalidOperationException("Client settings are unavailable."),
                gameplayInputAdmitted && GetWindow().HasFocus() &&
                    (_chatInput is null || !_chatInput.HasFocus()));
            if (_contextInteractPending)
                physical = physical with
                {
                    HeldActions = physical.HeldActions | GamePlayerActions.Interact,
                };
            ClientHorizontalCameraBasis basis = CameraBasis();
            ClientControlProfileDecision decision = EvaluateProfile(physical, basis);
            if (_gameplayStopPending)
            {
                decision = new ClientControlProfileDecision(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.None,
                        HasFocus: false),
                    false,
                    default,
                    false,
                    default);
            }
            if (decision.SelectionRequested)
                Select(decision.SelectionPoint);
            bool faceRequested = TryResolveProfileFacing(
                decision.Command.Locomotion,
                decision,
                out ClientControlWorldPoint facingPoint,
                out bool facingRequiresAcceptedLocomotion);
            ClientWorldCommandRequestFlags flags = faceRequested
                ? ClientWorldCommandRequestFlags.FaceRequested |
                  (facingRequiresAcceptedLocomotion
                      ? ClientWorldCommandRequestFlags.FacingRequiresAcceptedLocomotion
                      : ClientWorldCommandRequestFlags.None)
                : ClientWorldCommandRequestFlags.None;
            var context = new ClientWorldCommandContext(
                SelectedInteractionTarget(),
                flags,
                faceRequested ? facingPoint.XMillimeters : 0,
                faceRequested ? facingPoint.ZMillimeters : 0,
                (ulong)ResolveLocalSelection());
            long now = Stopwatch.GetTimestamp();
            if (!_inputScheduler.TryObserve(decision.Command, context, now))
                _lastOutcome = "Local input scheduler busy";
            else
            {
                _contextInteractPending = false;
                _gameplayStopPending = false;
                _ = _inputScheduler.TryPump(now, TrySubmitScheduledLocalCommand);
            }
            SynchronizeProjection();
            UpdateChatPresentation();
            UpdateCamera();
            UpdateHud();
        }
        catch (Exception exception)
        {
            GD.PushError($"Playable client frame failed: {exception}");
            _lastOutcome = "Frame failed — see Godot output";
            SetPhysicsProcess(false);
        }
        finally
        {
            _primaryPressed = false;
            _primaryReleased = false;
            _secondaryPressed = false;
            _cancelPressed = false;
        }
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (TryHandleFocusedChatInput(inputEvent)) return;
        if (!_gameplayInputAllowed) return;
        if (_sessionUi?.ContextMenu.ResolveInput(inputEvent) == true)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is InputEventKey uiKey && uiKey.Pressed && !uiKey.Echo &&
            TryHandleUiToggleKey(uiKey))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        GodotUiGameplayInputDisposition uiDisposition =
            _uiRenderer?.ResolveGameplayInput(inputEvent) ??
            GodotUiGameplayInputDisposition.Gameplay;
        if (uiDisposition != GodotUiGameplayInputDisposition.Gameplay)
        {
            if (uiDisposition is GodotUiGameplayInputDisposition.Blocked or
                GodotUiGameplayInputDisposition.OutsideCloseSubmitted)
                GetViewport().SetInputAsHandled();
            return;
        }
        if (TryGetPointerPosition(inputEvent, out Vector2 applicationPoint) &&
            _sessionUi?.ContainsWorldPointer(applicationPoint) == false)
        {
            _primaryDown = false;
            _primaryPressed = false;
            _primaryReleased = false;
            _pointerPointValid = false;
            _pointerTarget = default;
            _hoverTooltip?.Hide();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is InputEventKey reveal && !reveal.Echo && IsControlKey(reveal))
        {
            SetWorldRevealHeld(reveal.Pressed);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is InputEventKey chatKey && chatKey.Pressed && !chatKey.Echo &&
            _settings is not null &&
            GodotWorldInput.Matches(chatKey, _settings, ClientSemanticAction.Chat))
        {
            if (_chatInput is not null && !_chatInput.HasFocus())
            {
                _chatInput.GrabFocus();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        switch (inputEvent)
        {
            case InputEventMouseButton mouse:
                if (_settings is not null &&
                    (GodotWorldInput.Matches(mouse, _settings, ClientSemanticAction.Select) ||
                     GodotWorldInput.Matches(mouse, _settings, ClientSemanticAction.Destination)))
                {
                    _primaryDown = mouse.Pressed;
                    _primaryPressed |= mouse.Pressed;
                    _primaryReleased |= !mouse.Pressed;
                }
                else if (_settings is not null && mouse.Pressed &&
                    GodotWorldInput.Matches(mouse, _settings, ClientSemanticAction.ContextMenu))
                {
                    TryOpenWorldContext(mouse.Position);
                    GetViewport().SetInputAsHandled();
                }
                else if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed &&
                    _profile != ControlProfile.IndependentMoveLook)
                {
                    _cameraDistance = Math.Max(
                        MinimumZoomCameraDistanceMeters, _cameraDistance - 2f);
                }
                else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed &&
                    _profile != ControlProfile.IndependentMoveLook)
                {
                    _cameraDistance = Math.Min(55f, _cameraDistance + 2f);
                }
                break;
            case InputEventMouseMotion motion when
                (motion.ButtonMask & MouseButtonMask.Middle) != 0 &&
                _profile != ControlProfile.IndependentMoveLook:
                float sensitivity = (_settings?.PointerSensitivity ?? 1f) * 0.006f;
                float invert = _settings?.InvertY == true ? -1f : 1f;
                _cameraYaw -= motion.Relative.X * sensitivity;
                _cameraPitch = Math.Clamp(
                    _cameraPitch - motion.Relative.Y * sensitivity * invert,
                    0.2f,
                    1.35f);
                UpdateCamera();
                break;
            case InputEventKey key when key.Pressed && !key.Echo:
                HandleKey(key);
                break;
        }
    }

    public override void _ExitTree()
    {
        Shutdown();
    }

    public void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        SetPhysicsProcess(false);
        _uiBridge?.Dispose();
        _uiBridge = null;
        _uiClient?.Dispose();
        _uiClient = null;
        _actorPresenter?.Dispose();
        _actorPresenter = null;
        _actorVisuals?.Dispose();
        _actorVisuals = null;
        _systemChatBanner?.Dispose();
        _systemChatBanner = null;
        _hoverTooltip?.Dispose();
        _hoverTooltip = null;
        _actorState = null;
        _publicPresentationMetadata = null;
        _renderer?.Dispose();
        _renderer = null;
        _assetResolver?.Dispose();
        _assetResolver = null;
        _presentationGeneration?.Dispose();
        _presentationGeneration = null;
        _derivedResources?.Dispose();
        _derivedResources = null;
        _presentationCache?.Dispose();
        _presentationCache = null;
        _residency = null;
        _localSession?.Dispose();
        _localSession = null;
        _inputScheduler = null;
        ReleaseExternalUi(_ownedHudRoot);
        ReleaseExternalUi(_ownedWorldUiRoot);
        _sessionUi?.Dispose();
        _sessionUi = null;
        _ownedHudRoot = null;
        _ownedWorldUiRoot = null;
        _initialized = false;
    }

    private void StartFreshWorld()
    {
        if (_saveRoot is null || _resolvedContent is null || _contentOracle is null)
            throw new InvalidOperationException("Cabin composition was not configured.");
        _localSession = _useConceptDemoWorld
            ? LocalPlayableSession.CreateConceptDemo(
                _saveRoot, _guestAdmission, serverExtensionFactory: _serverExtensionFactory)
            : LocalPlayableSession.CreateCabin(_saveRoot, _guestAdmission);
        _localSession.WaitUntilReady(TimeSpan.FromSeconds(10));
        // Command sequencing belongs to the application/control-session lineage. Presentation
        // rebuilds (render-distance changes and Game-generation replacement) must not reset it.
        _sequencer = new ClientWorldInputSequencer(_localSession.ControlSession);
        // Packet-free Local resolves the same finite stock scheduling meaning directly.
        // No Network assembly or remote application proof is introduced for this path.
        _inputScheduler = new ClientWorldInputScheduler(
            new ClientInputSubmissionPolicy(60, 30, 8, 100));
        _boundWorld = _localSession.BoundWorld;
        _chatState = new ClientChatState(
            _localSession.ApplicationSessionEpoch,
            new ClientChatLimits(64, 256, 128));
        if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
            throw new InvalidOperationException("The local authority did not publish its initial view.");
        _humanBody = view.GuestIdentity.Body switch
        {
            GameGuestBody.HumanFemale => CabinHumanBody.XBot,
            GameGuestBody.HumanMale => CabinHumanBody.YBot,
            _ => throw new InvalidOperationException(
                "The authority published an unsupported guest body."),
        };
        _displayName = view.GuestIdentity.DisplayName ??
            throw new InvalidOperationException("The authority published no guest display name.");
        _publicPresentationMetadata = new ClientPublicPresentationMetadataState(128);
        _publicPresentationMetadata.ResetServerGeneration(view.GenerationId);
        if (!view.ServerExtension.Available)
            ApplyControlledActorMetadata(in view);
        SynchronizeColdPublicPresentationMetadata();
        _actorState = new ClientActorPresentationState(
            ClientActorDefinitionCatalog.ConceptDemo, 64);
        PrepareOptionalActorPresentation();
        _actorPresenter = new GodotActorPresenter(
            this,
            ClientActorDefinitionCatalog.ConceptDemo,
            64,
            _actorVisuals);
        _actorPresenter.SetControlledActorPose(
            view.PublicActor, view.PlayerPosition, view.PlayerOrientation, Time.GetTicksMsec());
        RebuildClientProjection(view);
        DriveLocalUntil(() =>
            CountPresentedCharacters() == 1 &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.CabinDoor) == 1 &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.WorldChest) == 1 &&
            (!_useConceptDemoWorld ||
                CountPresentedPrefab(GameWorldPublicPrefabIds.SkyTelepad) == 1 &&
                CountPresentedPrefab(GameWorldPublicPrefabIds.FarGate) == 1 &&
                CountPresentedPrefab(GameWorldPublicPrefabIds.InformationalSign) == 2));
        _lastOutcome = _useConceptDemoWorld
            ? "Fresh Concept Demo island started"
            : "Fresh woods/cabin world started";
    }

    /// <summary>
    /// Permanent D0.3 canary for a real authoritative informational sign. Hover uses only
    /// resident presentation truth; Interact crosses Game admission and the bounded UI bridge.
    /// </summary>
    public LocalCabinPresentationSnapshot RunD03Acceptance()
    {
        if (!_initialized || !_useConceptDemoWorld || _localSession is null ||
            _sequencer is null || _uiClient is null || _uiBridge is null ||
            _inventoryHost is null || _hoverTooltip is null)
            throw new InvalidOperationException("D0.3 requires the initialized Concept Demo UI consumer.");
        ValidateInitialNativePresentation();
        if (CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.InformationalSign) != 2 ||
            !TrySetPointerWorldPoint(
                GamePlayableWorld.WelcomeSignPosition.GlobalXMillimeters,
                GamePlayableWorld.WelcomeSignPosition.GlobalZMillimeters))
            throw new InvalidOperationException("The authored welcome sign was not resident and selectable.");

        UpdateWorldHover();
        if (!_hoverTooltip.Visible || _hoverTooltip.Title != "Sign")
            throw new InvalidOperationException("Known sign presentation did not produce its local hover label.");
        _hoverTooltip.Show("Sign", null, Vector2.Zero, new Vector2(1_920, 1_080));
        Vector2 signTooltipSize = _hoverTooltip.PanelSize;
        ulong signLayoutGeneration = _hoverTooltip.LayoutGeneration;
        Select(_pointerPoint);
        if (_selection != SelectionKind.InformationalSign)
            throw new InvalidOperationException("The known sign presentation was not selected as a sign.");

        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    GamePlayerActions.Interact,
                    true),
                out GamePlayerCommand use))
            throw new InvalidOperationException("The informational-sign interaction could not be formed.");
        ulong before = _localSession.GetSnapshot().UiOperationsApplied;
        ApplyCommand(use, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == use.Sequence &&
            view.LastSelection == LocalPlayableSelection.InformationalSign &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
            _uiClient.OpenViewCount == 1 &&
            _localSession.GetSnapshot().UiOperationsApplied > before);

        Control? root = null;
        foreach (Node child in _inventoryHost.GetChildren())
        {
            if (child is Control candidate && candidate.Name.ToString().StartsWith(
                    "UiView_", StringComparison.Ordinal))
            {
                root = candidate;
                break;
            }
        }
        Label? title = root?.FindChild("Element_title", recursive: true, owned: false) as Label;
        Label? body = root?.FindChild("Element_body", recursive: true, owned: false) as Label;
        Button? close = root?.FindChild("Element_close", recursive: true, owned: false) as Button;
        if (title?.Text != GameInformationalSigns.ConceptDemoWelcome.Title ||
            body?.Text != GameInformationalSigns.ConceptDemoWelcome.Body || close is null ||
            root is null || !Mathf.IsEqualApprox(root.AnchorLeft, 0.5f) ||
            !Mathf.IsEqualApprox(root.AnchorTop, 0.5f))
            throw new InvalidOperationException("Godot did not realize the authoritative sign title/body/actions.");

        close!.EmitSignal(BaseButton.SignalName.Pressed);
        DriveLocalUntil(() => _uiClient.OpenViewCount == 0);
        if (_inventoryHost.GetChildCount() != 0)
            throw new InvalidOperationException("Closing the informational sign left stale Godot UI presentation.");

        if (!TryFindPublicRecord(GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord sword))
            throw new InvalidOperationException("The world Iron Sword was not resident for hover acceptance.");
        _pointerTarget = sword.Id;
        _pointerTargetPrefab = sword.PrefabId;
        _pointerPoint = new ClientControlWorldPoint(
            sword.Position.GlobalXMillimeters,
            sword.Position.GlobalZMillimeters);
        _pointerPointValid = true;
        UpdateWorldHover();
        if (_publicPresentationMetadata?.TryGet(
                sword.Id, out ClientPublicPresentationMetadata swordMetadata) != true)
            throw new InvalidOperationException(
                "The world Iron Sword had no retained typed inspection properties.");
        _hoverTooltip.Show(
            swordMetadata.DisplayLabel,
            swordMetadata.InspectionProperties,
            Vector2.Zero,
            new Vector2(1_920, 1_080));
        if (!_hoverTooltip.Visible || _hoverTooltip.Title != "Iron Sword" ||
            !_hoverTooltip.Details.Contains("Damage ", StringComparison.Ordinal) ||
            !_hoverTooltip.Details.Contains("Quality ", StringComparison.Ordinal) ||
            !_hoverTooltip.Details.Contains("Durability ", StringComparison.Ordinal) ||
            _hoverTooltip.VisibleRowCount != 4 ||
            _hoverTooltip.PanelSize.X <= signTooltipSize.X ||
            _hoverTooltip.PanelSize.Y <= signTooltipSize.Y ||
            _hoverTooltip.LayoutGeneration <= signLayoutGeneration)
            throw new InvalidOperationException(
                "The world Iron Sword did not realize its authoritative content-sized property " +
                $"tooltip: visible={_hoverTooltip.Visible}, title='{_hoverTooltip.Title}', " +
                $"rows={_hoverTooltip.VisibleRowCount}, size={_hoverTooltip.PanelSize}, " +
                $"signSize={signTooltipSize}, generations=" +
                $"{signLayoutGeneration}->{_hoverTooltip.LayoutGeneration}, " +
                $"details='{_hoverTooltip.Details}'.");
        ulong swordLayoutGeneration = _hoverTooltip.LayoutGeneration;
        _hoverTooltip.Show(
            swordMetadata.DisplayLabel,
            swordMetadata.InspectionProperties,
            new Vector2(300, 200),
            new Vector2(1_920, 1_080));
        if (_hoverTooltip.LayoutGeneration != swordLayoutGeneration)
            throw new InvalidOperationException(
                "Moving an unchanged hover rebuilt its native tooltip layout.");
        // Freeze the gameplay pump so the native smoke can cross actual Godot layout frames
        // without the headless pointer replacing this deterministic sword hover.
        SetPhysicsProcess(false);
        return GetPresentationSnapshot();
    }

    /// <summary>
    /// Completes the D0.3/I0.13 tooltip canary after Godot has processed native container layout.
    /// A cached unchanged hover must shrink back to its exact current content bounds rather than
    /// retaining a transient wrapped height from an earlier frame.
    /// </summary>
    public void ValidateD03TooltipAfterNativeLayout()
    {
        if (_hoverTooltip is null || !_hoverTooltip.Visible ||
            _hoverTooltip.Title != "Iron Sword" || _hoverTooltip.VisibleRowCount != 4)
            throw new InvalidOperationException(
                "The native tooltip-layout canary lost its retained Iron Sword presentation.");
        Vector2 required = _hoverTooltip.BoundMinimumSize;
        Vector2 settled = _hoverTooltip.PanelSize;
        if (!Mathf.IsEqualApprox(settled.X, required.X) ||
            !Mathf.IsEqualApprox(settled.Y, required.Y))
            throw new InvalidOperationException(
                "Native tooltip layout retained stale surplus space after reflow: " +
                $"settled={settled}, required={required}.");

        ulong generation = _hoverTooltip.LayoutGeneration;
        if (!TryFindPublicRecord(GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord sword) ||
            _publicPresentationMetadata?.TryGet(
                sword.Id, out ClientPublicPresentationMetadata metadata) != true)
            throw new InvalidOperationException(
                "The native tooltip-layout canary lost its retained inspection metadata.");
        _hoverTooltip.Hide();
        _hoverTooltip.Show(
            metadata.DisplayLabel,
            metadata.InspectionProperties,
            Vector2.Zero,
            new Vector2(1_920, 1_080));
        Vector2 restored = _hoverTooltip.PanelSize;
        Vector2 restoredRequired = _hoverTooltip.BoundMinimumSize;
        if (_hoverTooltip.LayoutGeneration != generation ||
            _hoverTooltip.VisibleRowCount != 4 ||
            !Mathf.IsEqualApprox(restored.X, restoredRequired.X) ||
            !Mathf.IsEqualApprox(restored.Y, restoredRequired.Y))
            throw new InvalidOperationException(
                "A cached Iron Sword hover did not restore exact content-sized native layout: " +
                $"size={restored}, required={restoredRequired}, generations=" +
                $"{generation}->{_hoverTooltip.LayoutGeneration}.");
    }

    /// <summary>
    /// Permanent D0.4 canary for presentation-only missing-model/material fallbacks. The four
    /// objects retain immutable World identity and collision regardless of the chosen visual.
    /// </summary>
    public LocalCabinPresentationSnapshot RunD04Acceptance()
    {
        if (!_initialized || !_useConceptDemoWorld || _localSession is null ||
            _sequencer is null || _uiClient is null || _inventoryHost is null ||
            _cache is null || _renderer is null || _settings is null)
            throw new InvalidOperationException("D0.4 requires the initialized Concept Demo consumer.");
        if (default(GodotWorldMeshResolution).IsValid ||
            _renderer.FallbackMaterialMode != GodotWorldFallbackMaterialMode.Diagnostic)
            throw new InvalidOperationException("The fallback result or presentation policy did not fail closed.");
        ValidateInitialNativePresentation();
        SpatialChunkKey labKey = GamePlayableWorld.FallbackLabCorrectPosition.ToChunkKey(
            GameWorldState.DefaultSpace);
        if (!_cache.TryGetResidentHandle(labKey, out ClientSemanticChunkHandle handle) ||
            !_renderer.TryGetChunkDiagnostics(handle, out GodotWorldRenderChunkDiagnostics diagnostics) ||
            diagnostics.ModelFallbackGroups != 1 || diagnostics.TextureFallbackGroups != 1 ||
            diagnostics.ModelAndTextureFallbackGroups != 1)
            throw new InvalidOperationException(
                "The resident Fallback Lab did not retain one group for each failure class.");

        MultiMeshInstance3D correct = RequireFallbackLabGroup(
            GameWorldStaticVariants.FallbackLabCorrect, GodotWorldAssetAvailability.Available);
        MultiMeshInstance3D modelMissing = RequireFallbackLabGroup(
            GameWorldStaticVariants.FallbackLabModelMissing,
            GodotWorldAssetAvailability.ModelMissing);
        MultiMeshInstance3D textureMissing = RequireFallbackLabGroup(
            GameWorldStaticVariants.FallbackLabTextureMissing,
            GodotWorldAssetAvailability.TextureMissing);
        MultiMeshInstance3D bothMissing = RequireFallbackLabGroup(
            GameWorldStaticVariants.FallbackLabModelAndTextureMissing,
            GodotWorldAssetAvailability.ModelAndTextureMissing);
        if (correct.Multimesh!.Mesh!.GetInstanceId() != textureMissing.Multimesh!.Mesh!.GetInstanceId() ||
            modelMissing.Multimesh!.Mesh!.GetInstanceId() != bothMissing.Multimesh!.Mesh!.GetInstanceId() ||
            correct.Multimesh.Mesh.GetInstanceId() == modelMissing.Multimesh.Mesh.GetInstanceId() ||
            correct.MaterialOverride is not null || modelMissing.MaterialOverride is not null ||
            textureMissing.MaterialOverride is not ShaderMaterial ||
            bothMissing.MaterialOverride is not ShaderMaterial)
            throw new InvalidOperationException(
                "The Fallback Lab did not preserve model and material failure as independent axes.");
        if (!_cache.TryGetResidentView(handle, out ClientSemanticChunkView lab) ||
            !TryFindLabFragment(lab.MultiFragments,
                GameWorldStaticVariants.FallbackLabModelMissing,
                out WorldMultiFragmentRecord missingFragment))
            throw new InvalidOperationException("The fallback Multi fragment lost its structural source.");
        WorldStaticBounds declared = missingFragment.Bounds;
        long chunkX = (long)labKey.Coord.X * GameWorldSpatialProfile.WorldChunkEdgeUnits;
        long chunkZ = (long)labKey.Coord.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits;
        Vector3 expectedMinimum = new(
            (declared.MinimumXMillimeters - chunkX) * 0.001f,
            (declared.MinimumYMillimeters - diagnostics.YAnchorMillimeters) * 0.001f,
            (declared.MinimumZMillimeters - chunkZ) * 0.001f);
        Vector3 expectedSize = new(
            (declared.MaximumXExclusiveMillimeters - declared.MinimumXMillimeters) * 0.001f,
            (declared.MaximumYExclusiveMillimeters - declared.MinimumYMillimeters) * 0.001f,
            (declared.MaximumZExclusiveMillimeters - declared.MinimumZMillimeters) * 0.001f);
        if (!modelMissing.CustomAabb.Position.IsEqualApprox(expectedMinimum) ||
            !modelMissing.CustomAabb.Size.IsEqualApprox(expectedSize))
            throw new InvalidOperationException(
                $"The missing Multi model no longer matches declared structural bounds: " +
                $"actual={modelMissing.CustomAabb}, expected={expectedMinimum}/{expectedSize}.");

        RelocateControlledActor(GamePlayableWorld.FallbackLabApproachPosition);
        if (!TrySetPointerWorldPoint(
                GamePlayableWorld.FallbackLabSignPosition.GlobalXMillimeters,
                GamePlayableWorld.FallbackLabSignPosition.GlobalZMillimeters))
            throw new InvalidOperationException("The Fallback Lab sign had no selectable support point.");
        Select(_pointerPoint);
        if (_selection != SelectionKind.FallbackLabSign ||
            !_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    GamePlayerActions.Interact,
                    true),
                out GamePlayerCommand use))
            throw new InvalidOperationException("The Fallback Lab sign interaction could not be formed.");
        ApplyCommand(use, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == use.Sequence &&
            view.LastSelection == LocalPlayableSelection.FallbackLabSign &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
            _uiClient.OpenViewCount == 1);
        Control? signView = FindOpenUiView();
        Label? title = signView?.FindChild("Element_title", recursive: true, owned: false) as Label;
        Label? body = signView?.FindChild("Element_body", recursive: true, owned: false) as Label;
        Button? close = signView?.FindChild("Element_close", recursive: true, owned: false) as Button;
        if (title?.Text != GameInformationalSigns.FallbackLab.Title ||
            body?.Text != GameInformationalSigns.FallbackLab.Body || close is null)
            throw new InvalidOperationException(
                "Godot did not realize the authoritative Fallback Lab explanation.");
        close.EmitSignal(BaseButton.SignalName.Pressed);
        DriveLocalUntil(() => _uiClient.OpenViewCount == 0);
        return GetPresentationSnapshot();

        static bool TryFindLabFragment(ReadOnlySpan<WorldMultiFragmentRecord> fragments,
            ushort variant, out WorldMultiFragmentRecord found)
        {
            foreach (WorldMultiFragmentRecord fragment in fragments)
                if (fragment.Variant == variant)
                {
                    found = fragment;
                    return true;
                }
            found = default;
            return false;
        }
    }

    /// <summary>
    /// F1.3 acceptance over a normal required semantic world with both optional native actor art
    /// and the local static/public mesh resolver deliberately suppressed. This is not F1.4
    /// physical packaging or a different authority/content activation.
    /// </summary>
    public LocalCabinPresentationSnapshot RunF13PrimitiveOnlyAcceptance()
    {
        if (!_primitiveOnlyPresentation || !_initialized || !_useConceptDemoWorld ||
            _renderer is null || _actorPresenter is null || _cache is null)
            throw new InvalidOperationException("F1.3 requires an entered primitive-only Concept Demo.");
        LocalCabinPresentationSnapshot before = GetPresentationSnapshot();
        if (before.Actors.ActiveActors < 2 ||
            before.Actors.PrimitiveFallbackActors != before.Actors.ActiveActors ||
            before.Actors.AnimatedActors != 0 ||
            before.Renderer.ActiveStaticBatchGroups <= 0 ||
            before.Renderer.ActivePublicBatchGroups <= 0 ||
            before.Renderer.ActiveFallbackGroups <
                before.Renderer.ActiveStaticBatchGroups + before.Renderer.ActivePublicBatchGroups ||
            _renderer.FallbackMaterialMode != GodotWorldFallbackMaterialMode.Neutral)
            throw new InvalidOperationException(
                $"Primitive-only presentation did not cover disclosed actors/statics/Multis/public objects: {before}.");
        (int frozen, int multi, int publicRecords) = GetF13PrimitiveCensus();
        if (frozen <= 0 || multi <= 0 || publicRecords <= 0)
            throw new InvalidOperationException(
                $"Primitive-only world lacks frozen/Multi/public source facts: {frozen}/{multi}/{publicRecords}.");
        foreach (uint prefab in new[]
        {
            GameWorldPublicPrefabIds.CabinDoor, GameWorldPublicPrefabIds.WorldChest,
            GameWorldPublicPrefabIds.InformationalSign, GameWorldPublicPrefabIds.FarGate,
            GameWorldPublicPrefabIds.SkyTelepad,
        })
            if (!TryFindPublicRecord(prefab, out _) ||
                !ClientPrimitivePresentationCatalog.ForPublic(prefab).IsValid)
                throw new InvalidOperationException(
                    $"Disclosed public prefab {prefab} lost its primitive representation.");

        if (!TryFindPublicRecord(GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord sword) ||
            !GodotPublicPointerPicker.TryIntersect(
                sword.PrefabId,
                new Vector3(sword.Position.GlobalXMillimeters * 0.001f,
                    sword.Position.YMillimeters * 0.001f + 3f,
                    sword.Position.GlobalZMillimeters * 0.001f),
                Vector3.Down,
                new Vector3(sword.Position.GlobalXMillimeters * 0.001f,
                    sword.Position.YMillimeters * 0.001f,
                    sword.Position.GlobalZMillimeters * 0.001f), out _))
            throw new InvalidOperationException("The primitive sword lost its declared pick volume.");

        RunD00Acceptance(); // pickup/equip/Inventory, selection, telepad, jump/fall/recovery
        ProbeDoorAndChestInteraction();
        OpenServerMonitor();
        DriveLocalUntil(() => FindOpenServerMonitorView() is not null);
        OpenServerMonitor();
        DriveLocalUntil(() => _uiClient!.OpenViewCount == 0);
        if (_sequencer is null)
            throw new InvalidOperationException("The primitive-only action sequencer retired early.");
        ulong primaryBefore = _actorPresenter.GetSnapshot().PrimaryCuesPresented;
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(GameLocomotionUpdate.Stop,
                    GamePlayerActions.Primary, true), out GamePlayerCommand primary))
            throw new InvalidOperationException("The primitive-only Primary intent was not formed.");
        ApplyCommand(primary, false, default, false);
        DriveLocalUntil(() => _actorPresenter.GetSnapshot().PrimaryCuesPresented > primaryBefore);
        _ = RunD05Acceptance(_displayName, _humanBody); // actor/nameplate/Chat/overhead
        ProbeF13PrimitiveDropAndPickup();
        LocalCabinPresentationSnapshot after = GetPresentationSnapshot();
        if (!after.IsInitialized || after.Actors.PrimitiveFallbackActors !=
            after.Actors.ActiveActors || after.OpenUiViews != 0)
            throw new InvalidOperationException("The primitive-only journey lost actor or UI state.");
        return before;
    }

    private void ProbeF13PrimitiveDropAndPickup()
    {
        if (_uiClient is null || _uiBridge is null || _sequencer is null || _localSession is null)
            throw new InvalidOperationException("Primitive item acceptance lost its local session.");
        OpenInventory();
        DriveLocalUntil(() => _uiClient.OpenViewCount == 1);
        Control inventory = FindOpenInventoryView() ??
            throw new InvalidOperationException("Primitive item acceptance lost Inventory.");
        const string viewPrefix = "UiView_";
        string name = inventory.Name.ToString();
        if (!name.StartsWith(viewPrefix, StringComparison.Ordinal) ||
            !ulong.TryParse(name.AsSpan(viewPrefix.Length), out ulong value))
            throw new InvalidOperationException("Primitive item Inventory identity is invalid.");
        var viewId = new UiViewInstanceId(value);
        UiRowId swordRow = default;
        foreach (UiListRow row in _uiClient.GetRows(viewId, InventoryUi.Items))
            if (row.Values.Length != 0 && row.Values[0].Text == "Iron Sword")
            {
                swordRow = row.Id;
                break;
            }
        if (!swordRow.IsValid)
            throw new InvalidOperationException("The equipped primitive sword was absent from Inventory.");
        _uiClient.SubmitAction(viewId, InventoryUi.Select, swordRow);
        DriveLocalUntil(() => _uiClient.GetActionState(viewId, InventoryUi.Drop).Enabled);
        _uiClient.SubmitAction(viewId, InventoryUi.Drop);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.MainHandPrefabId == 0 &&
            TryFindPublicRecord(GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord dropped) &&
            (dropped.StateFlags & WorldPublicStateFlags.GroundItem) != 0);
        OpenInventory();
        DriveLocalUntil(() => _uiClient.OpenViewCount == 0);

        if (!TryFindPublicRecord(GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord sword))
            throw new InvalidOperationException("The dropped primitive sword was not public truth.");
        if (!WorldPosition.TryFromGlobalMillimeters(
                sword.Position.GlobalXMillimeters, GamePlayableWorld.PlayerStart.YMillimeters,
                sword.Position.GlobalZMillimeters, out WorldPosition pickupApproach))
            throw new InvalidOperationException("The dropped primitive sword position is invalid.");
        RelocateControlledActor(pickupApproach);
        _pointerTarget = sword.Id;
        _pointerTargetPrefab = sword.PrefabId;
        Select(new ClientControlWorldPoint(
            sword.Position.GlobalXMillimeters, sword.Position.GlobalZMillimeters));
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(GameLocomotionUpdate.Stop,
                    GamePlayerActions.Interact, true), out GamePlayerCommand pickup))
            throw new InvalidOperationException("Primitive sword re-pickup command failed to form.");
        ApplyCommand(pickup, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == pickup.Sequence);
        if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView picked) ||
            picked.LastCommandResult.Interaction != GameWorldActionStatus.Accepted)
            throw new InvalidOperationException(
                $"The dropped primitive sword was not picked up: " +
                $"result={picked.LastCommandResult.Interaction}, target={sword.Id}, " +
                $"actor={picked.PlayerPosition}, item={sword.Position}.");
        // The authored world may contain another sword; require retirement of this exact
        // dropped public identity, not absence of every sword-shaped object in the AOI.
        DriveLocalUntil(() => !TryFindPublicAnchor(sword.Id, out _));
    }

    /// <summary>Bounded acceptance-only census over currently rendered semantic chunks.</summary>
    public (int FrozenStatics, int MultiFragments, int PublicRecords) GetF13PrimitiveCensus()
    {
        if (!_primitiveOnlyPresentation || _cache is null || _renderer is null ||
            !_cache.TryCopyResidentInfo(_chatChunkInfo, out int count, out _))
            throw new InvalidOperationException("Primitive-only semantic census is unavailable.");
        int frozen = 0;
        int multi = 0;
        int publicRecords = 0;
        for (int index = 0; index < count; index++)
        {
            ClientSemanticChunkInfo info = _chatChunkInfo[index];
            if (!_renderer.TryGetChunkDiagnostics(info.Handle, out _)) continue;
            frozen = checked(frozen + info.FrozenStaticCount);
            multi = checked(multi + info.MultiFragmentCount);
            publicRecords = checked(publicRecords + info.PublicRecordCount);
        }
        return (frozen, multi, publicRecords);
    }

    /// <summary>
    /// Permanent D0.5 canary proving the accepted Game guest identity, selected body prefab,
    /// controlled presentation, nameplate, and Chat label all agree.
    /// </summary>
    public LocalCabinPresentationSnapshot RunD05Acceptance(
        string expectedDisplayName,
        CabinHumanBody expectedBody)
    {
        if (!_initialized || !_useConceptDemoWorld || _localSession is null ||
            _humanPresentation is null || _chatState is null)
            throw new InvalidOperationException(
                "D0.5 requires the initialized Concept Demo guest consumer.");
        if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView authority) ||
            authority.GuestIdentity.DisplayName != expectedDisplayName ||
            authority.GuestIdentity.Body != (expectedBody == CabinHumanBody.XBot
                ? GameGuestBody.HumanFemale
                : GameGuestBody.HumanMale) ||
            _displayName != expectedDisplayName || _humanBody != expectedBody)
            throw new InvalidOperationException(
                "The controlled presentation did not consume the authoritative guest identity.");
        if (_humanPresentation.FindChild(
                "Nameplate", recursive: true, owned: false) is not
            Label3D { Text: var nameplate } || nameplate != expectedDisplayName)
            throw new InvalidOperationException(
                "The controlled actor nameplate did not use the accepted guest display name.");
        uint expectedPrefab = expectedBody == CabinHumanBody.XBot
            ? GameWorldPublicPrefabIds.Character
            : GameWorldPublicPrefabIds.CharacterMale;
        uint otherPrefab = expectedBody == CabinHumanBody.XBot
            ? GameWorldPublicPrefabIds.CharacterMale
            : GameWorldPublicPrefabIds.Character;
        if (CountPresentedPrefab(expectedPrefab) != 1 || CountPresentedPrefab(otherPrefab) != 0)
            throw new InvalidOperationException(
                "The authoritative guest body did not select the matching public character prefab.");
        GodotActorPresenterSnapshot actors = _actorPresenter?.GetSnapshot() ?? default;
        if (actors.ActiveActors < 2 || actors.VisibleNameplates > 1)
            throw new InvalidOperationException(
                "The shared actor presenter did not retain the controlled actor and nearby animal.");
        SetWorldRevealHeld(true);
        if (_actorPresenter!.GetSnapshot().VisibleNameplates < 2)
            throw new InvalidOperationException(
                "Local Ctrl reveal did not expose already-known eligible actor labels.");
        SetWorldRevealHeld(false);
        if (_actorPresenter.GetSnapshot().VisibleNameplates > 1)
            throw new InvalidOperationException(
                "Releasing local Ctrl reveal did not restore the join-grace/hidden nameplate policy.");

        ulong before = _chatState.GetSnapshot().Applied;
        SubmitChat("guest identity canary");
        DriveLocalUntil(() => _chatState.GetSnapshot().Applied == before + 1);
        int count = _chatState.CopyRecent(_chatRows);
        if (count == 0 || _chatRows[count - 1].DisplayLabel != expectedDisplayName ||
            _chatRows[count - 1].Text != "guest identity canary" ||
            _actorPresenter.GetSnapshot().VisibleOverheadLabels != 1)
            throw new InvalidOperationException(
                "Spatial Chat did not use and anchor the accepted guest display name.");
        return GetPresentationSnapshot();
    }

    /// <summary>Native acceptance observation for shell/Chat focus ownership.</summary>
    public LocalChatInputSnapshot GetChatInputSnapshot()
    {
        if (!_initialized || _chatInput is null || _chatState is null)
            throw new InvalidOperationException("Chat input is not initialized.");
        return new LocalChatInputSnapshot(
            _chatInput.HasFocus(),
            _chatInput.Text,
            _gameplayInputAllowed,
            _chatState.GetSnapshot().Applied);
    }

    /// <summary>
    /// Deliberately leaves every major session-owned presentation surface live so F0.7 can prove
    /// that aggregate retirement removes state rather than merely succeeding from an empty scene.
    /// </summary>
    public LocalCabinLifecycleResidueSnapshot PrepareF07RetirementCanary(
        string expectedDisplayName,
        CabinHumanBody expectedBody)
    {
        _ = RunD05Acceptance(expectedDisplayName, expectedBody);
        if (_localSession is null || _uiClient is null || _uiBridge is null ||
            _chatInput is null || _actorPresenter is null || _renderer is null)
            throw new InvalidOperationException("The F0.7 local retirement canary is incomplete.");

        ulong before = _localSession.GetSnapshot().UiOperationsApplied;
        OpenInventory();
        DriveLocalUntil(() =>
        {
            _ = _uiBridge.Pump();
            return _uiClient.OpenViewCount == 1 &&
                _localSession.GetSnapshot().UiOperationsApplied > before;
        });
        SetWorldRevealHeld(true);
        _chatInput.Text = "F0.7 local draft must retire";
        _chatInput.GrabFocus();

        GodotActorPresenterSnapshot actors = _actorPresenter.GetSnapshot();
        GodotWorldRendererSnapshot renderer = _renderer.GetSnapshot();
        LocalCabinUiOwnershipSnapshot ui = GetUiOwnershipSnapshot();
        var result = new LocalCabinLifecycleResidueSnapshot(
            _uiClient.OpenViewCount,
            actors.ActiveActors,
            actors.VisibleNameplates,
            actors.VisibleOverheadLabels,
            renderer.ActiveRenderChunks,
            _chatInput.HasFocus(),
            _gameplayInputAllowed,
            renderer.ActiveNodes,
            renderer.ActiveResources,
            actors.PrimitiveFallbackActors,
            actors.AnimatedActors,
            actors.EquippedMainHands,
            ui);
        if (result.OpenUiViews != 1 || result.ActiveActors < 2 ||
            result.VisibleNameplates < 2 || result.VisibleOverheadLabels < 1 ||
            result.RenderedChunks <= 0 || !result.ChatFocused)
            throw new InvalidOperationException(
                $"The F0.7 local retirement canary did not populate every surface: {result}.");
        return result;
    }

    private MultiMeshInstance3D RequireFallbackLabGroup(
        ushort variant,
        GodotWorldAssetAvailability availability)
    {
        if (_renderer is null)
            throw new InvalidOperationException("The Fallback Lab renderer is unavailable.");
        string suffix = availability == GodotWorldAssetAvailability.Available
            ? string.Empty
            : $"_Fallback_{availability}";
        string expected = $"Static_{GameWorldStaticDefinitionIds.Rock}_{variant}_" +
            $"{(ushort)(WorldStaticFlags.Renderable | WorldStaticFlags.Collidable)}{suffix}";
        MultiMeshInstance3D? found = _renderer.Root.FindChild(
            expected, recursive: true, owned: false) as MultiMeshInstance3D;
        if (found?.Multimesh is not { InstanceCount: 1, Mesh: not null } ||
            found.CustomAabb.Size.LengthSquared() <= 0f)
            throw new InvalidOperationException($"Fallback Lab native group '{expected}' was incomplete.");
        return found;
    }

    private Control? FindOpenUiView()
    {
        return FindCompiledViewContaining(null);
    }

    private void CreateInventoryConsumer()
    {
        if (_localSession is null || _inventoryHost is null)
            throw new InvalidOperationException("Inventory presentation was composed out of order.");
        _uiRenderer = _sessionUi is not null
            ? new GodotUiRenderer(_sessionUi.Gumps)
            : new GodotUiRenderer(_inventoryHost);
        _uiClient = new UiClientRuntime(_uiRenderer, new UiClientLimits(8, 8));
        _uiBridge = _localSession.AttachUiClient(
            _uiClient,
            ClientStandardUiLibrary.Resolve);
    }

    private void OpenInventory()
    {
        if (_localSession is null || _uiClient is null) return;
        if (TryRequestCloseOpenInventory())
        {
            _lastOutcome = "Inventory close requested";
            return;
        }
        ulong request = _nextAdministrativeRequest++;
        _lastOutcome = _localSession.TryRequestOpenInventory(request)
            ? "Inventory requested"
            : "Inventory request queue busy";
    }

    private void OpenServerMonitor()
    {
        if (_localSession is null || _uiClient is null) return;
        if (TryRequestCloseOpenServerMonitor())
        {
            _lastOutcome = "Server Monitor close requested";
            return;
        }
        ulong request = _nextAdministrativeRequest++;
        _lastOutcome = _localSession.TryRequestOpenServerMonitor(request)
            ? "Server Monitor requested"
            : "Server Monitor request queue busy";
    }

    private bool TryRequestCloseOpenInventory()
    {
        Control? view = FindOpenInventoryView();
        Button? close = view?.FindChild(
            "Element_close", recursive: true, owned: false) as Button;
        if (close is null || close.Disabled)
            return false;
        close.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    private Control? FindOpenInventoryView()
    {
        return FindCompiledViewContaining("Element_items");
    }

    private bool TryRequestCloseOpenServerMonitor()
    {
        Control? view = FindOpenServerMonitorView();
        Button? close = view?.FindChild(
            "Element_close", recursive: true, owned: false) as Button;
        if (close is null || close.Disabled) return false;
        close.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    private Control? FindOpenServerMonitorView()
    {
        return FindCompiledViewContaining("Element_participants");
    }

    private Control? FindCompiledViewContaining(string? descendant)
    {
        if (_inventoryHost is null) return null;
        foreach (Node child in _inventoryHost.GetChildren())
        {
            Node? candidate = descendant is null
                ? FindCompiledView(child)
                : child.FindChild(descendant, recursive: true, owned: false);
            while (candidate is not null &&
                !candidate.Name.ToString().StartsWith("UiView_", StringComparison.Ordinal))
                candidate = candidate.GetParent();
            if (candidate is Control view) return view;
        }
        return null;
    }

    private static Control? FindCompiledView(Node node)
    {
        if (node is Control control && control.Name.ToString().StartsWith(
                "UiView_", StringComparison.Ordinal))
            return control;
        foreach (Node child in node.GetChildren())
            if (FindCompiledView(child) is Control found) return found;
        return null;
    }

    private void RealizeAddedContent()
    {
        if (_resolvedContent?.Variant != CabinContentVariant.Add || _assetResolver?.AddedMaterial is null)
            return;
        var badge = new MeshInstance3D
        {
            Name = "AddedCabinContent",
            Mesh = GodotMeshGeometry.CreateGroundedBox(
                new Vector3(0.8f, 0.12f, 0.8f), _assetResolver.AddedMaterial),
            Position = new Vector3(2.5f, 0.02f, -2.5f),
        };
        AddChild(badge);
        _addedPresentationRealized = true;
    }

    private void RebuildClientProjection(
        in LocalPlayableAuthorityView view,
        bool preserveSemanticCache = false)
    {
        if (_localSession is null || _boundWorld is null || !view.IsReady)
            throw new InvalidOperationException("Authority is not ready.");
        bool reuseSemanticCache = preserveSemanticCache && _cache is not null &&
            _residentGenerationId == view.GenerationId &&
            ReferenceEquals(_cache.PreparedWorld, view.PreparedWorld);
        if (preserveSemanticCache && !reuseSemanticCache)
            throw new InvalidOperationException(
                "A presentation-only rebuild cannot replace its semantic application lineage.");
        int requestedRenderRadius = _settings?.RenderDistanceChunks ??
            ClientSettingsResolver.MinimumRenderDistanceChunks;
        int maximumRenderRadius = MaximumRenderRadius(view.PreparedWorld.BaseWorld);
        int renderRadius = Math.Min(requestedRenderRadius, maximumRenderRadius);
        _effectiveRenderDistanceChunks = renderRadius;
        _renderer?.Dispose();
        ClientWorldSemanticCacheLimits cacheLimits;
        if (reuseSemanticCache)
        {
            cacheLimits = _cache!.Limits;
        }
        else
        {
            // The semantic cache is an application target for the complete authoritative
            // generation, not a disposable render-window detail. Admit its maximum supported
            // structural working set once so live radius changes can preserve public rows and
            // their already-proven delivery lineage.
            cacheLimits = ClientWorldSemanticCacheLimits.ForStructuralViewRadius(
                checked(maximumRenderRadius +
                    ClientStructuralResidency.DefaultStructuralFringeChunks),
                spareResidentChunks: 16,
                maximumFrozenStaticsPerChunk: 16,
                maximumMultiFragmentsPerChunk: 16,
                maximumPublicRecordsPerChunk: 16,
                maximumKnownPublicIdentities: 64);
            _cache = new ClientWorldSemanticCache(view.PreparedWorld, cacheLimits);
            _chatChunkInfo = new ClientSemanticChunkInfo[cacheLimits.MaximumResidentChunks];
        }
        _residency = new ClientStructuralResidency(_cache, renderRadius);

        _renderer = new GodotWorldRenderer(
            this,
            _cache,
            GodotWorldRendererLimits.ForStructuralWindow(renderRadius, 5, 32),
            _assetResolver,
            fallbackMaterialMode: _primitiveOnlyPresentation
                ? GodotWorldFallbackMaterialMode.Neutral
                : GodotWorldFallbackMaterialMode.Diagnostic,
            publicBatchFilter: _actorPresenter);
        _window = new GodotSlidingRenderWindow(_cache, _renderer, renderRadius);
        int maximumPresented = _window.MaximumActiveChunks;
        _presentationChanges = new ClientPresentationChange[checked(maximumPresented * 2)];
        _presentationBindings = new ClientPresentationBinding[maximumPresented];
        _semanticHashesBeforeRefresh = new ClientSemanticHash[maximumPresented];
        _actorPresentedChunks = new ClientSemanticChunkHandle[maximumPresented];
        _actorChanges = new ClientActorPresentationChange[128];
        _orbit.Reset();
        _residentGenerationId = view.GenerationId;
        _player = view.PlayerPosition;
        if (_publicPresentationMetadata?.GetSnapshot().ServerGenerationId != view.GenerationId)
        {
            _publicPresentationMetadata?.ResetServerGeneration(view.GenerationId);
            _previousSharedMetadataCount = 0;
            SynchronizeColdPublicPresentationMetadata();
        }
        if (!view.ServerExtension.Available)
            ApplyControlledActorMetadata(in view);
        _actorPresenter?.SetControlledActorPose(
            view.PublicActor, view.PlayerPosition, view.PlayerOrientation, Time.GetTicksMsec());
        TrackPresentation(reset: true);
        DrainRenderer();
        SynchronizeActorPresentation(in view);
    }

    private void ApplyControlledActorMetadata(in LocalPlayableAuthorityView view)
    {
        if (_publicPresentationMetadata is null || !view.PublicActor.IsValid ||
            !view.GuestIdentity.IsValid)
            throw new InvalidOperationException("The controlled actor metadata source was incomplete.");
        bool retained = _publicPresentationMetadata.TryGet(
            view.PublicActor, out ClientPublicPresentationMetadata prior);
        ClientPublicActorActionKind actorAction = retained
            ? prior.ActorAction
            : ClientPublicActorActionKind.None;
        ulong actorActionSequence = retained ? prior.ActorActionSequence : 0;
        if (view.LastCommandResult.Primary == GameWorldActionStatus.Accepted)
        {
            actorAction = ClientPublicActorActionKind.Primary;
            actorActionSequence = view.LastCommandSequence;
        }
        else if (view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted)
        {
            actorAction = ClientPublicActorActionKind.Interact;
            actorActionSequence = view.LastCommandSequence;
        }
        ClientPublicPresentationFlags flags = ClientPublicPresentationFlags.NameplateEligible |
            ClientPublicPresentationFlags.ActorState;
        ClientPublicActorVerticalPhase verticalPhase = view.VerticalMotion.Kind switch
        {
            GameVerticalMotionKind.Grounded => ClientPublicActorVerticalPhase.Grounded,
            GameVerticalMotionKind.Airborne when
                view.VerticalMotion.VelocityMillimetersPerSecond > 0 =>
                    ClientPublicActorVerticalPhase.Rising,
            GameVerticalMotionKind.Airborne => ClientPublicActorVerticalPhase.Falling,
            _ => throw new InvalidOperationException(
                "The local actor vertical state was not presentable."),
        };
        if (retained && prior.Flags == flags && prior.MainHandPrefabId == view.MainHandPrefabId &&
            prior.VerticalPhase == verticalPhase &&
            prior.ActorAction == actorAction && prior.ActorActionSequence == actorActionSequence &&
            string.Equals(prior.DisplayLabel, view.GuestIdentity.DisplayName, StringComparison.Ordinal))
            return;
        var metadata = new ClientPublicPresentationMetadata(
            view.GenerationId,
            view.PublicActor,
            retained ? checked(prior.Revision + 1) : 1,
            flags,
            view.GuestIdentity.DisplayName!,
            MainHandPrefabId: view.MainHandPrefabId,
            VerticalPhase: verticalPhase,
            ActorAction: actorAction,
            ActorActionSequence: actorActionSequence);
        ClientPublicPresentationApplyStatus applied = _publicPresentationMetadata.TryApply(
            ClientPublicPresentationOperation.Present, in metadata);
        if (applied is not (ClientPublicPresentationApplyStatus.Applied or
                ClientPublicPresentationApplyStatus.Duplicate))
            throw new InvalidOperationException($"Controlled actor metadata was rejected: {applied}.");
    }

    private void SynchronizeColdPublicPresentationMetadata()
    {
        if (_localSession is null || _publicPresentationMetadata is null) return;
        int count = _localSession.CopyPublicPresentationMetadata(_sharedMetadata);
        if (count < 0)
            throw new InvalidOperationException(
                "The cold public metadata exceeded the local presentation capacity.");
        for (int previous = 0; previous < _previousSharedMetadataCount; previous++)
        {
            ClientPublicPresentationMetadata old = _previousSharedMetadata[previous];
            bool retained = false;
            for (int current = 0; current < count; current++)
            {
                if (_sharedMetadata[current].Id != old.Id) continue;
                retained = true;
                break;
            }
            if (retained) continue;
            var removed = new ClientPublicPresentationMetadata(
                old.ServerGenerationId,
                old.Id,
                checked(old.Revision + 1),
                ClientPublicPresentationFlags.None,
                string.Empty);
            ClientPublicPresentationApplyStatus status = _publicPresentationMetadata.TryApply(
                ClientPublicPresentationOperation.Remove, in removed);
            if (status is not (ClientPublicPresentationApplyStatus.Applied or
                    ClientPublicPresentationApplyStatus.Duplicate))
                throw new InvalidOperationException(
                    $"Retired cold public metadata was rejected: {status}.");
        }
        for (int current = 0; current < count; current++)
        {
            ClientPublicPresentationMetadata metadata = _sharedMetadata[current];
            ClientPublicPresentationApplyStatus status = _publicPresentationMetadata.TryApply(
                ClientPublicPresentationOperation.Present, in metadata);
            if (status is not (ClientPublicPresentationApplyStatus.Applied or
                    ClientPublicPresentationApplyStatus.Duplicate))
                throw new InvalidOperationException(
                    $"Cold public metadata was rejected: {status}.");
        }
        _sharedMetadata.AsSpan(0, count).CopyTo(_previousSharedMetadata);
        if (count < _previousSharedMetadataCount)
            Array.Clear(
                _previousSharedMetadata,
                count,
                _previousSharedMetadataCount - count);
        _previousSharedMetadataCount = count;
    }

    private void SynchronizeActorPresentation(in LocalPlayableAuthorityView view)
    {
        if (_window is null || _cache is null || _actorState is null ||
            _publicPresentationMetadata is null || _actorPresenter is null)
            return;
        if (!view.ServerExtension.Available)
            ApplyControlledActorMetadata(in view);
        if (!_window.TryCopyBindings(_presentationBindings, out int count, out _))
            throw new InvalidOperationException("Actor presentation bindings exceeded capacity.");
        for (int index = 0; index < count; index++)
            _actorPresentedChunks[index] = _presentationBindings[index].SemanticHandle;
        ClientActorPresentationSyncStatus status = _actorState.TrySynchronize(
            _cache,
            _actorPresentedChunks.AsSpan(0, count),
            _publicPresentationMetadata,
            view.GenerationId,
            _actorChanges,
            out int changed,
            out _);
        if (status is not (ClientActorPresentationSyncStatus.Applied or
                ClientActorPresentationSyncStatus.NoChanges))
            throw new InvalidOperationException($"Actor presentation synchronization failed: {status}.");
        _actorPresenter.SetControlledActorPose(
            view.PublicActor, view.PlayerPosition, view.PlayerOrientation, Time.GetTicksMsec());
        _actorPresenter.Apply(
            _actorChanges.AsSpan(0, changed), _player, Time.GetTicksMsec());
        if (!_actorPresenter.TryGetActorRoot(view.PublicActor, out _humanPresentation) ||
            _humanPresentation is null)
        {
            _humanMeshInstances = 0;
            return;
        }
        _humanPresentation.Name = $"Controlled{_humanBody}";
        if (_actorState.TryGet(view.PublicActor, out ClientActorPresentationRecord controlled))
            _cameraPivotMeters = controlled.Definition.CameraPivotHeightMillimeters * 0.001f;
        _humanMeshInstances = CountMeshInstances(_humanPresentation);
        if (_humanMeshInstances == 0)
            throw new InvalidOperationException("The controlled actor contains no mesh presentation.");
    }

    private static int MaximumRenderRadius(BoundWorldPack world)
    {
        for (int radius = ClientSettingsResolver.MaximumRenderDistanceChunks;
             radius >= ClientSettingsResolver.MinimumRenderDistanceChunks;
             radius--)
        {
            for (int index = 0; index < world.ChunkCount; index++)
            {
                if (!world.TryGetChunkKey(index, out SpatialChunkKey center))
                    throw new InvalidOperationException("A validated WorldPack lost a chunk key.");
                if (IsComplete(center, radius)) return radius;
            }
        }
        throw new InvalidOperationException(
            "The authored world cannot contain the minimum client presentation window.");

        bool IsComplete(SpatialChunkKey center, int radius)
        {
            for (long x = (long)center.Coord.X - radius;
                 x <= (long)center.Coord.X + radius;
                 x++)
            for (long z = (long)center.Coord.Z - radius;
                 z <= (long)center.Coord.Z + radius;
                 z++)
            {
                if (x is < int.MinValue or > int.MaxValue || z is < int.MinValue or > int.MaxValue ||
                    !world.TryGetBaseTerrain(
                        new SpatialChunkKey(world.Space, checked((int)x), checked((int)z)), out _))
                    return false;
            }
            return true;
        }
    }

    private ClientControlProfileDecision EvaluateProfile(
        in ClientWorldInputSample physical,
        in ClientHorizontalCameraBasis basis)
    {
        // OrbitCommand intentionally retains its UO-like no-jump grammar; direct human-control
        // profiles may emit the common Game Jump action edge.
        ClientWorldInputSample admitted = _profile == ControlProfile.OrbitCommand
            ? physical with { HeldActions = physical.HeldActions & ~GamePlayerActions.Jump }
            : physical;
        if (_profileStopPending)
        {
            _profileStopPending = false;
            return new ClientControlProfileDecision(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    admitted.HasFocus ? admitted.HeldActions : GamePlayerActions.None,
                    admitted.HasFocus),
                false,
                default,
                false,
                default);
        }
        return _profile switch
        {
            ControlProfile.OrbitCommand => _orbit.Evaluate(new ClientOrbitCommandSample(
                admitted.HeldActions,
                admitted.HasFocus,
                _player.GlobalXMillimeters,
                _player.GlobalZMillimeters,
                _primaryDown,
                _primaryPressed,
                _primaryReleased,
                _secondaryPressed,
                _cancelPressed,
                _pointerPointValid,
                _pointerPoint,
                checked((long)Time.GetTicksMsec()))),
            ControlProfile.ThirdPersonDirect => AddDirectProfileSelection(
                new ClientControlProfileDecision(
                    new ClientWorldCommandIntent(
                        admitted.HasFocus
                            ? _thirdPerson.Evaluate(admitted.MoveX, admitted.MoveZ, basis)
                            : GameLocomotionUpdate.Stop,
                        admitted.HeldActions,
                        admitted.HasFocus),
                    false,
                    default,
                    false,
                    default)),
            ControlProfile.IndependentMoveLook => AddDirectProfileSelection(_independent.Evaluate(
                admitted.MoveX,
                admitted.MoveZ,
                admitted.HeldActions,
                admitted.HasFocus,
                _pointerPointValid,
                _pointerPoint)),
            _ => throw new InvalidOperationException("Unknown control profile."),
        };
    }

    private ClientControlProfileDecision AddDirectProfileSelection(
        in ClientControlProfileDecision decision) =>
        _primaryReleased && _pointerPointValid
            ? decision with
            {
                SelectionRequested = true,
                SelectionPoint = _pointerPoint,
            }
            : decision;

    private void ApplyCommand(
        in GamePlayerCommand command,
        bool faceLookPoint,
        in ClientControlWorldPoint lookPoint,
        bool facingRequiresAcceptedLocomotion)
    {
        if (_localSession is null)
            return;
        LocalPlayableSelection selection = _selection switch
        {
            SelectionKind.None => LocalPlayableSelection.None,
            SelectionKind.CabinDoor => LocalPlayableSelection.CabinDoor,
            SelectionKind.CabinChest => LocalPlayableSelection.CabinChest,
            SelectionKind.SkyTelepad => LocalPlayableSelection.SkyTelepad,
            SelectionKind.FarGate => LocalPlayableSelection.FarGate,
            SelectionKind.InformationalSign => LocalPlayableSelection.InformationalSign,
            SelectionKind.FallbackLabSign => LocalPlayableSelection.FallbackLabSign,
            SelectionKind.IronSword => LocalPlayableSelection.IronSword,
            _ => LocalPlayableSelection.Unsupported,
        };
        if (!_localSession.TrySubmit(new LocalPlayableCommand(
                command,
                selection,
                faceLookPoint,
                lookPoint,
                facingRequiresAcceptedLocomotion,
                SelectedInteractionTarget())))
            _lastOutcome = "Local command queue busy";
    }

    private bool TryResolveProfileFacing(
        in GameLocomotionUpdate locomotion,
        in ClientControlProfileDecision decision,
        out ClientControlWorldPoint facingPoint,
        out bool requiresAcceptedLocomotion)
    {
        switch (_profile)
        {
            case ControlProfile.OrbitCommand when
                decision.LookRequested &&
                locomotion.Kind is GameLocomotionUpdateKind.Direct or
                    GameLocomotionUpdateKind.Destination:
                facingPoint = decision.LookPoint;
                requiresAcceptedLocomotion = true;
                return true;
            case ControlProfile.ThirdPersonDirect when
                locomotion.Kind == GameLocomotionUpdateKind.Direct:
                facingPoint = new ClientControlWorldPoint(
                    checked(_player.GlobalXMillimeters + locomotion.DirectX),
                    checked(_player.GlobalZMillimeters + locomotion.DirectZ));
                requiresAcceptedLocomotion = true;
                return true;
            case ControlProfile.IndependentMoveLook when decision.LookRequested:
                facingPoint = decision.LookPoint;
                requiresAcceptedLocomotion = false;
                return true;
            default:
                facingPoint = default;
                requiresAcceptedLocomotion = false;
                return false;
        }
    }

    private LocalPlayableSelection ResolveLocalSelection() => _selection switch
    {
        SelectionKind.None => LocalPlayableSelection.None,
        SelectionKind.CabinDoor => LocalPlayableSelection.CabinDoor,
        SelectionKind.CabinChest => LocalPlayableSelection.CabinChest,
        SelectionKind.SkyTelepad => LocalPlayableSelection.SkyTelepad,
        SelectionKind.FarGate => LocalPlayableSelection.FarGate,
        SelectionKind.InformationalSign => LocalPlayableSelection.InformationalSign,
        SelectionKind.FallbackLabSign => LocalPlayableSelection.FallbackLabSign,
        SelectionKind.IronSword => LocalPlayableSelection.IronSword,
        _ => LocalPlayableSelection.Unsupported,
    };

    private bool TrySubmitScheduledLocalCommand(
        in ClientWorldScheduledCommand scheduled,
        out ulong sequence)
    {
        sequence = 0;
        if (_localSession is null || _sequencer is null ||
            scheduled.Context.DiagnosticTag > (ulong)LocalPlayableSelection.IronSword ||
            !_sequencer.TryCreateSemanticCommand(scheduled.Intent, out GamePlayerCommand command))
            return false;
        bool faceRequested = (scheduled.Context.Flags &
            ClientWorldCommandRequestFlags.FaceRequested) != 0;
        bool requiresAcceptedLocomotion = (scheduled.Context.Flags &
            ClientWorldCommandRequestFlags.FacingRequiresAcceptedLocomotion) != 0;
        if (!_localSession.TrySubmit(new LocalPlayableCommand(
                command,
                (LocalPlayableSelection)scheduled.Context.DiagnosticTag,
                faceRequested,
                new ClientControlWorldPoint(
                    scheduled.Context.FaceXMillimeters,
                    scheduled.Context.FaceZMillimeters),
                requiresAcceptedLocomotion,
                scheduled.Context.Target)))
            return false;
        sequence = command.Sequence;
        return true;
    }

    private void SynchronizeProjection()
    {
        if (_localSession is null || _cache is null || _renderer is null)
            return;
        if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
            throw new InvalidOperationException("The local authority view was unavailable.");
        if (view.GenerationId != _residentGenerationId)
        {
            RebuildClientProjection(view);
            _selection = SelectionKind.None;
            _lastOutcome = $"Entered authoritative generation {view.GenerationId}";
            return;
        }

        CaptureSemanticHashes();
        _ = _localSession.PumpWorld(_cache);
        QueueChangedPublicChunks();
        _player = view.PlayerPosition;
        ObserveAuthorityOutcomes(view);
        SynchronizeColdPublicPresentationMetadata();
        TrackPresentation(reset: false);
        _renderer.ProcessFrame(GodotWorldFrameBudget.Default);
        SynchronizeActorPresentation(in view);
    }

    private void CaptureSemanticHashes()
    {
        if (_cache is null || _window is null) return;
        if (!_window.TryCopyBindings(_presentationBindings, out int count, out _))
            throw new InvalidOperationException("The presentation bindings exceeded their fixed destination.");
        for (int index = 0; index < count; index++)
        {
            ClientPresentationBinding binding = _presentationBindings[index];
            if (!_cache.TryGetInfo(binding.SemanticHandle, out ClientSemanticChunkInfo info))
                throw new InvalidOperationException("A presented semantic chunk was unavailable.");
            _semanticHashesBeforeRefresh[index] = info.SemanticHash;
        }
        if (count < _presentationBindings.Length)
            _presentationBindings[count] = default;
    }

    private void QueueChangedPublicChunks()
    {
        if (_cache is null || _renderer is null) return;
        for (int index = 0; index < _presentationBindings.Length; index++)
        {
            ClientPresentationBinding binding = _presentationBindings[index];
            if (!binding.SemanticHandle.IsValid) break;
            if (!_cache.TryGetResidentHandle(binding.Key, out ClientSemanticChunkHandle handle) ||
                !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo after))
                throw new InvalidOperationException("A presented semantic chunk was unavailable after apply.");
            if (after.SemanticHash != _semanticHashesBeforeRefresh[index])
                _ = _renderer.TryQueuePublicRefresh(handle, out _);
        }
    }

    private void ObserveAuthorityOutcomes(in LocalPlayableAuthorityView view)
    {
        if (view.LastCommandSequence != 0 && view.LastCommandSequence != _observedCommandSequence)
        {
            _observedCommandSequence = view.LastCommandSequence;
            if (view.LastChestAction != GameWorldActionStatus.NotRequested)
            {
                _lastOutcome = view.LastChestAction == GameWorldActionStatus.Accepted
                    ? "Moved bread between backpack and cabin chest"
                    : view.LastChestAction == GameWorldActionStatus.MissingTarget
                        ? "Select the cabin door or chest before using Interact"
                        : $"Chest use: {view.LastChestAction}";
            }
            else if (view.LastCommandResult.Interaction != GameWorldActionStatus.NotRequested)
            {
                _lastOutcome = view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted
                    ? view.LastSelection switch
                    {
                        LocalPlayableSelection.CabinDoor =>
                            $"Cabin door {(view.LastCommandResult.DoorIsOpen ? "opened" : "closed")}",
                        LocalPlayableSelection.SkyTelepad => "Sky Island telepad activated",
                        LocalPlayableSelection.FarGate => "Far Gate activated",
                        LocalPlayableSelection.InformationalSign => "Sign opened",
                        LocalPlayableSelection.FallbackLabSign => "Fallback Lab sign opened",
                        LocalPlayableSelection.IronSword => "Picked up Iron Sword",
                        _ => "Interaction accepted",
                    }
                    : $"{view.LastSelection} interaction: {view.LastCommandResult.Interaction}";
            }
            else if (view.LastCommandResult.Jump != GameWorldActionStatus.NotRequested)
            {
                _lastOutcome = view.LastCommandResult.Jump == GameWorldActionStatus.Accepted
                    ? "Jumped"
                    : $"Jump: {view.LastCommandResult.Jump}";
            }
            else if (view.LastCommandResult.Primary != GameWorldActionStatus.NotRequested)
            {
                _lastOutcome = view.LastCommandResult.Primary == GameWorldActionStatus.Accepted
                    ? "Swung equipped weapon"
                    : "Primary requires an equipped weapon";
                if (view.LastCommandResult.Primary == GameWorldActionStatus.Accepted)
                    _ = _actorPresenter?.TryPresentPrimaryAction(
                        view.PublicActor, view.LastCommandSequence, Time.GetTicksMsec());
            }
        }
        if (view.LastAdministrativeSequence != 0 &&
            view.LastAdministrativeSequence != _observedAdministrativeSequence)
        {
            _observedAdministrativeSequence = view.LastAdministrativeSequence;
            _lastOutcome = view.LastAdministrativeMessage ??
                $"{view.LastAdministrativeKind}: {(view.LastAdministrativeSucceeded ? "accepted" : "rejected")}";
        }
    }

    private void TrackPresentation(bool reset)
    {
        if (_window is null || _residency is null)
            return;
        SpatialChunkKey center = _residency.GetPresentationCenter(_player);
        ClientPresentationWindowSnapshot current = _window.GetSnapshot();
        if (!reset && current.ActiveChunks != 0 &&
            PresentationWindowsAreDisjoint(current.CenterChunk, center, _window.RenderRadiusChunks))
        {
            if (!_window.TryResetForJump(
                    _player, _presentationChanges, out _, out _,
                    out ClientPresentationWindowError windowResetError,
                    out GodotWorldRenderQueueError rendererResetError))
            {
                _lastOutcome = $"Distant presentation reset: {windowResetError}/{rendererResetError}";
                return;
            }
            DrainRenderer();
            if (!_residency.TryPrepareReset(
                    out ClientStructuralResidencyPlan resetPlan, out _,
                    out ClientStructuralResidencyError residencyResetError))
            {
                _lastOutcome = $"Distant structural reset: {residencyResetError}";
                return;
            }
            _residency.Commit(resetPlan);
        }
        if (!_residency.TryPrepare(
                center,
                reset,
                out ClientStructuralResidencyPlan residencyPlan,
                out _,
                out ClientStructuralResidencyError residencyError))
        {
            _lastOutcome = $"Structural residency boundary: {residencyError}";
            return;
        }
        bool tracked = reset && _window.GetSnapshot().ActiveChunks != 0
            ? _window.TryResetForJump(
                _player, _presentationChanges, out _, out _, out ClientPresentationWindowError error,
                out GodotWorldRenderQueueError rendererError)
            : _window.TryTrackPlayer(
                _player, center, _presentationChanges,
                out _, out _, out error, out rendererError);
        if (!tracked)
        {
            _residency.Cancel(residencyPlan);
            _lastOutcome = $"Presentation boundary: {error}/{rendererError}";
            return;
        }
        _residency.Commit(residencyPlan);
    }

    private static bool PresentationWindowsAreDisjoint(
        SpatialChunkKey first,
        SpatialChunkKey second,
        int radius)
    {
        long maximumSharedCenterDistance = checked(radius * 2L);
        return Math.Abs((long)first.Coord.X - second.Coord.X) > maximumSharedCenterDistance ||
            Math.Abs((long)first.Coord.Z - second.Coord.Z) > maximumSharedCenterDistance;
    }

    private void DrainRenderer()
    {
        if (_renderer is null)
            return;
        for (int frame = 0; frame < 64; frame++)
        {
            GodotWorldRendererSnapshot snapshot = _renderer.GetSnapshot();
            if (snapshot.PendingBuilds == 0 && snapshot.PendingRetirements == 0)
                return;
            _renderer.ProcessFrame(new GodotWorldFrameBudget(16, 16));
        }
        throw new InvalidOperationException("Initial render window did not converge.");
    }

    private void Select(in ClientControlWorldPoint point)
    {
        if (_pointerTarget.IsValid && _pointerTargetPrefab == GameWorldPublicPrefabIds.IronSword)
        {
            _selection = SelectionKind.IronSword;
            _selectedTarget = _pointerTarget;
            _lastOutcome = $"Selected Iron Sword at ({point.XMillimeters / 1000d:F1}, " +
                $"{point.ZMillimeters / 1000d:F1}) m";
            return;
        }
        _selectedTarget = default;
        bool signPresented = _useConceptDemoWorld && IsKnownPresentedPrefabNear(
            GameWorldPublicPrefabIds.InformationalSign, point, radius: 1_500);
        _selection = signPresented && Nearest(
                point, GamePlayableWorld.FallbackLabSignPosition, 1_500)
            ? SelectionKind.FallbackLabSign
            : signPresented && Nearest(point, GamePlayableWorld.WelcomeSignPosition, 1_500)
            ? SelectionKind.InformationalSign
            : Nearest(point, GamePlayableWorld.DoorPosition, 1_750)
            ? SelectionKind.CabinDoor
            : Nearest(point, GamePlayableWorld.ChestPosition, 1_750)
                ? SelectionKind.CabinChest
                : Nearest(point, GamePlayableWorld.SkyTelepadPosition, 1_750)
                    ? SelectionKind.SkyTelepad
                    : Nearest(point, GamePlayableWorld.CentralFarGatePosition, 1_750) ||
                        Nearest(point, GamePlayableWorld.FarReturnGatePosition, 1_750)
                        ? SelectionKind.FarGate
                        : Nearest(point, GamePlayableWorld.AnimalPosition, 1_750)
                            ? SelectionKind.WoodlandAnimal
                            : SelectionKind.Terrain;
        _lastOutcome = _profile == ControlProfile.OrbitCommand && _selection == SelectionKind.Terrain
            ? "Terrain selected — hold LMB to steer or double-click to travel"
            : $"Selected {_selection} at ({point.XMillimeters / 1000d:F1}, " +
                $"{point.ZMillimeters / 1000d:F1}) m";
    }

    private void UpdateWorldHover()
    {
        if (_hoverTooltip is null)
            return;
        string? title = null;
        UltraSim.Game.Inspection.InspectionPropertySet? properties = null;
        if (_useConceptDemoWorld && _pointerTarget.IsValid &&
            _pointerTargetPrefab == GameWorldPublicPrefabIds.IronSword &&
            _publicPresentationMetadata?.TryGet(
                _pointerTarget, out ClientPublicPresentationMetadata item) == true &&
            (item.Flags & ClientPublicPresentationFlags.ItemProperties) != 0)
        {
            title = item.DisplayLabel;
            properties = item.InspectionProperties;
        }
        else if (_useConceptDemoWorld && _pointerPointValid &&
            IsKnownPresentedPrefabNear(
                GameWorldPublicPrefabIds.InformationalSign, _pointerPoint, radius: 1_500))
            title = "Sign";
        if (title is null)
        {
            _hoverTooltip.Hide();
            return;
        }
        _hoverTooltip.Show(
            title,
            properties,
            GetViewport().GetMousePosition(),
            GetViewport().GetVisibleRect().Size);
    }

    private bool IsKnownPresentedPrefabNear(
        uint prefabId,
        in ClientControlWorldPoint point,
        long radius)
    {
        if (_cache is null || CountPresentedPrefab(prefabId) == 0 ||
            !_cache.TryCopyResidentInfo(_chatChunkInfo, out int count, out _))
            return false;
        Int128 radiusSquared = (Int128)radius * radius;
        for (int chunk = 0; chunk < count; chunk++)
        {
            if (!_cache.TryGetResidentView(
                    _chatChunkInfo[chunk].Handle, out ClientSemanticChunkView view))
                continue;
            foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
            {
                if (record.PrefabId != prefabId)
                    continue;
                Int128 dx = (Int128)point.XMillimeters - record.Position.GlobalXMillimeters;
                Int128 dz = (Int128)point.ZMillimeters - record.Position.GlobalZMillimeters;
                if (dx * dx + dz * dz <= radiusSquared)
                    return true;
            }
        }
        return false;
    }

    private void UpdatePointerWorldPoint()
    {
        if (_camera is null || _cache is null || _window is null)
            return;
        Vector2 mouse = GetViewport().GetMousePosition();
        if (_sessionUi?.ContainsWorldPointer(mouse) == false)
        {
            _pointerPointValid = false;
            _pointerTarget = default;
            _pointerTargetPrefab = 0;
            return;
        }
        Vector3 origin = _camera.ProjectRayOrigin(mouse);
        Vector3 direction = _camera.ProjectRayNormal(mouse);
        if (TryPickPresentedPublicObject(
                origin,
                direction,
                out ClientControlWorldPoint objectPoint,
                out WorldPublicEntityId target,
                out uint prefabId))
        {
            _pointerPoint = objectPoint;
            _pointerPointValid = true;
            _pointerTarget = target;
            _pointerTargetPrefab = prefabId;
            return;
        }
        _pointerTarget = default;
        _pointerTargetPrefab = 0;
        if (Math.Abs(direction.Y) < 0.0001f)
        {
            _pointerPointValid = false;
            return;
        }
        float distance = -origin.Y / direction.Y;
        if (distance <= 0)
        {
            _pointerPointValid = false;
            return;
        }
        Vector3 hit = origin + direction * distance;
        long globalX = checked(_player.GlobalXMillimeters +
            (long)Math.Round(hit.X * 1000d, MidpointRounding.AwayFromZero));
        long globalZ = checked(_player.GlobalZMillimeters +
            (long)Math.Round(hit.Z * 1000d, MidpointRounding.AwayFromZero));
        _ = TrySetPointerWorldPoint(globalX, globalZ);
    }

    private void TryOpenWorldContext(Vector2 applicationPoint)
    {
        if (_sessionUi is null || !_pointerTarget.IsValid || !_pointerPointValid)
        {
            _sessionUi?.ContextMenu.Close();
            return;
        }
        WorldPublicEntityId target = _pointerTarget;
        ClientControlWorldPoint point = _pointerPoint;
        string title = _publicPresentationMetadata?.TryGet(
                target, out ClientPublicPresentationMetadata metadata) == true
            ? metadata.DisplayLabel
            : "World object";
        _sessionUi.ContextMenu.Show(
            title,
            applicationPoint,
            () =>
            {
                _pointerTarget = target;
                _pointerPoint = point;
                _pointerPointValid = true;
                Select(point);
            },
            () =>
            {
                _pointerTarget = target;
                _pointerPoint = point;
                _pointerPointValid = true;
                Select(point);
                _contextInteractPending = true;
            },
            () => _publicPresentationMetadata?.TryGet(target, out _) == true);
        _hoverTooltip?.Hide();
    }

    private static bool TryGetPointerPosition(InputEvent inputEvent, out Vector2 position)
    {
        switch (inputEvent)
        {
            case InputEventMouse mouse:
                position = mouse.Position;
                return true;
            case InputEventScreenTouch touch:
                position = touch.Position;
                return true;
            case InputEventScreenDrag drag:
                position = drag.Position;
                return true;
            default:
                position = default;
                return false;
        }
    }

    private bool TryPickPresentedPublicObject(
        in Vector3 rayOrigin,
        in Vector3 rayDirection,
        out ClientControlWorldPoint point,
        out WorldPublicEntityId target,
        out uint prefabId)
    {
        point = default;
        target = default;
        prefabId = 0;
        if (_cache is null || _window is null ||
            !_window.TryCopyBindings(_presentationBindings, out int count, out _))
            return false;

        float nearest = float.PositiveInfinity;
        WorldPosition selected = default;
        WorldPublicEntityId selectedTarget = default;
        uint selectedPrefab = 0;
        for (int index = 0; index < count; index++)
        {
            if (!_cache.TryGetResidentView(
                    _presentationBindings[index].SemanticHandle, out ClientSemanticChunkView view))
                continue;
            foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
            {
                if (!GodotPublicPointerPicker.IsPointerTarget(
                        record.PrefabId, record.StateFlags))
                    continue;
                Vector3 basePosition = new(
                    checked(record.Position.GlobalXMillimeters - _player.GlobalXMillimeters) * 0.001f,
                    checked(record.Position.YMillimeters - _player.YMillimeters) * 0.001f,
                    checked(record.Position.GlobalZMillimeters - _player.GlobalZMillimeters) * 0.001f);
                if (!GodotPublicPointerPicker.TryIntersect(
                        record.PrefabId, rayOrigin, rayDirection, basePosition, out float distance) ||
                    distance >= nearest)
                    continue;
                nearest = distance;
                selected = record.Position;
                selectedTarget = record.Id;
                selectedPrefab = record.PrefabId;
            }
        }
        if (!float.IsFinite(nearest))
            return false;
        point = new ClientControlWorldPoint(
            selected.GlobalXMillimeters,
            selected.GlobalZMillimeters);
        target = selectedTarget;
        prefabId = selectedPrefab;
        return true;
    }

    private bool TrySetPointerWorldPoint(long globalXMillimeters, long globalZMillimeters)
    {
        _pointerTarget = default;
        _pointerTargetPrefab = 0;
        _pointerPoint = new ClientControlWorldPoint(globalXMillimeters, globalZMillimeters);
        _pointerPointValid = WorldPosition.TryFromGlobalMillimeters(
                globalXMillimeters,
                _player.YMillimeters,
                globalZMillimeters,
                out WorldPosition candidate) &&
            _cache is not null &&
            _cache.PreparedWorld.TryFindNearestSupport(
                candidate,
                _player.YMillimeters,
                out WorldTerrainColumnSurface support) == WorldTerrainColumnQueryStatus.Complete &&
            support.IsValid && support.IsSupport;
        return _pointerPointValid;
    }

    private ClientHorizontalCameraBasis CameraBasis()
    {
        if (_camera is null)
            return ClientHorizontalCameraBasis.WorldAxes;
        Vector3 right = _camera.GlobalTransform.Basis.X;
        Vector3 forward = -_camera.GlobalTransform.Basis.Z;
        right.Y = 0;
        forward.Y = 0;
        right = right.Normalized();
        forward = forward.Normalized();
        return new ClientHorizontalCameraBasis(right.X, right.Z, forward.X, forward.Z);
    }

    private WorldPublicEntityId SelectedInteractionTarget() =>
        _selection == SelectionKind.IronSword ? _selectedTarget : default;

    private void UpdateCamera()
    {
        if (_camera is null)
            return;
        float horizontal = MathF.Cos(_cameraPitch) * _cameraDistance;
        Vector3 pivot = new(0, _cameraPivotMeters, 0);
        _camera.Position = new Vector3(
            MathF.Sin(_cameraYaw) * horizontal,
            MathF.Sin(_cameraPitch) * _cameraDistance + _cameraPivotMeters,
            MathF.Cos(_cameraYaw) * horizontal);
        _camera.LookAt(pivot, Vector3.Up);
    }

    private void HandleKey(InputEventKey input)
    {
        if (TryHandleUiToggleKey(input)) return;
        switch (input.Keycode)
        {
            case global::Godot.Key.Escape:
                _cancelPressed = true;
                break;
            case global::Godot.Key.F1:
            case global::Godot.Key.Key1:
                SetProfile(ControlProfile.OrbitCommand);
                break;
            case global::Godot.Key.F2:
            case global::Godot.Key.Key2:
                SetProfile(ControlProfile.ThirdPersonDirect);
                break;
            case global::Godot.Key.F3:
            case global::Godot.Key.Key3:
                SetProfile(ControlProfile.IndependentMoveLook);
                break;
            case global::Godot.Key.F5:
                SaveWorld();
                break;
            case global::Godot.Key.F9:
                LoadWorld();
                break;
        }
    }

    private bool TryHandleUiToggleKey(InputEventKey input)
    {
        if (_settings is not null &&
            GodotWorldInput.Matches(input, _settings, ClientSemanticAction.OpenInventory))
        {
            OpenInventory();
            return true;
        }
        if (_settings is not null &&
            GodotWorldInput.Matches(input, _settings, ClientSemanticAction.OpenServerMonitor))
        {
            OpenServerMonitor();
            return true;
        }
        return false;
    }

    private bool ObserveGameplayInputAdmission()
    {
        bool admitted = _gameplayInputAllowed && !(_uiRenderer?.BlocksGameplayInput ?? false);
        if (!admitted)
        {
            _primaryDown = false;
            _primaryPressed = false;
            _primaryReleased = false;
            _secondaryPressed = false;
            _cancelPressed = false;
            SetWorldRevealHeld(false);
            if (_gameplayInputAdmittedLastFrame)
            {
                _profileStopPending = true;
                _gameplayStopPending = true;
            }
        }
        _gameplayInputAdmittedLastFrame = admitted;
        return admitted;
    }

    private static bool IsControlKey(InputEventKey input) =>
        input.Keycode is global::Godot.Key.Ctrl or global::Godot.Key.Meta ||
        input.PhysicalKeycode is global::Godot.Key.Ctrl or global::Godot.Key.Meta;

    private void SetWorldRevealHeld(bool held)
    {
        _actorPresenter?.SetRevealHeld(held);
        _renderer?.SetPublicNamesRevealHeld(held);
    }

    private void ApplyResolvedSettings()
    {
        if (_settings is null) return;
        _profile = _settings.ControlGrammar switch
        {
            ClientControlGrammar.OrbitCommand => ControlProfile.OrbitCommand,
            ClientControlGrammar.ThirdPersonDirect => ControlProfile.ThirdPersonDirect,
            ClientControlGrammar.IndependentMoveLook => ControlProfile.IndependentMoveLook,
            _ => throw new InvalidOperationException("The resolved control grammar is unsupported."),
        };
        ApplyCameraProfileDefaults(_settings.CameraProfile);
        _cameraDistance = _settings.CameraDistance;
        if (_camera is not null) _camera.Fov = _settings.CameraFovDegrees;
        if (_ownedHudRoot is not null) _ownedHudRoot.Scale = Vector2.One * _settings.UiScale;
        if (_initialized) _profileStopPending = true;
        UpdateCamera();
    }

    private void ApplyCameraProfileDefaults(ClientCameraProfile profile)
    {
        switch (profile)
        {
            case ClientCameraProfile.Orbit:
                _cameraYaw = -0.7f;
                _cameraPitch = OrbitDefaultPitchRadians;
                break;
            case ClientCameraProfile.ThirdPersonFollow:
                _cameraYaw = -0.7f;
                _cameraPitch = 0.34f;
                break;
            case ClientCameraProfile.IndependentMoveLook:
                _cameraYaw = -MathF.PI * 0.25f;
                _cameraPitch = 0.61548f;
                break;
            default:
                throw new InvalidOperationException("The resolved camera profile is unsupported.");
        }
    }

    private void SetProfile(ControlProfile profile)
    {
        if (_profile == profile)
            return;
        _profile = profile;
        _orbit.Reset();
        _thirdPerson.Reset();
        _independent.Reset();
        _primaryDown = false;
        _primaryPressed = false;
        _primaryReleased = false;
        _profileStopPending = true;
        ApplyProfileCameraDefaults();
        UpdateCamera();
        _lastOutcome = $"Control profile: {profile}";
    }

    public void RunF02Acceptance()
    {
        if (_camera is null || _renderer is null || _localSession is null || _sequencer is null)
            throw new InvalidOperationException("The launch smoke started before composition completed.");

        Vector3 orbitCamera = _camera.Position;
        SetProfile(ControlProfile.ThirdPersonDirect);
        Vector3 thirdPersonCamera = _camera.Position;
        SetProfile(ControlProfile.IndependentMoveLook);
        Vector3 independentCamera = _camera.Position;
        SetProfile(ControlProfile.OrbitCommand);
        if (!(thirdPersonCamera.Length() < orbitCamera.Length() &&
            thirdPersonCamera.Y < orbitCamera.Y &&
            independentCamera.DistanceTo(thirdPersonCamera) > 0.5f &&
            Mathf.IsEqualApprox(_cameraPitch, OrbitDefaultPitchRadians) &&
            Mathf.IsEqualApprox(_cameraDistance, OrbitDefaultCameraDistanceMeters)))
            throw new InvalidOperationException("Control profile switches did not realize distinct camera modes.");

        ValidateCatalogAndHumanPresentation();
        ProbeSelectedPublicTargetRetention();
        ProbePlayableChat();
        ProbePlayableInventory();
        ProbeDoorAndChestInteraction();
        ProbeAuthoritativeJump();

        ProbeMovementFacing(
            ControlProfile.OrbitCommand,
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MinimumAxisMagnitude, 0),
            new ClientControlWorldPoint(
                _player.GlobalXMillimeters - 1_000,
                _player.GlobalZMillimeters),
            WorldYaw.ThreeQuarterTurn);
        ProbeMovementFacing(
            ControlProfile.ThirdPersonDirect,
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
            default,
            WorldYaw.QuarterTurn);
        SetProfile(ControlProfile.OrbitCommand);

        int before = CountPresentedCharacters();
        RelocateControlledActor(GamePlayableWorld.ClearCrossChunkPosition);
        DriveLocalUntil(() => _player.Chunk == GamePlayableWorld.ClearCrossChunkPosition.Chunk &&
            CountPresentedCharacters() == 1);
        int afterCrossing = CountPresentedCharacters();
        RelocateControlledActor(GamePlayableWorld.PlayerStart);
        DriveLocalUntil(() => _player == GamePlayableWorld.PlayerStart &&
            CountPresentedCharacters() == 1);
        int afterReturn = CountPresentedCharacters();
        if (before != 1 || afterCrossing != 1 || afterReturn != 1)
            throw new InvalidOperationException(
                $"Public presentation ghosted across a chunk move: {before}/{afterCrossing}/{afterReturn}.");

        int authoredMaximumChunk = _useConceptDemoWorld
            ? GamePlayableWorld.ConceptMaximumChunk
            : GamePlayableWorld.MaximumChunk;
        int renderRadius = _effectiveRenderDistanceChunks;
        var edge = new WorldPosition(
            authoredMaximumChunk,
            authoredMaximumChunk,
            15_000,
            14_000,
            0);
        RelocateControlledActor(edge);
        DriveLocalUntil(() => _player.Chunk == edge.Chunk &&
            _player.Local.XMillimeters == edge.Local.XMillimeters);
        SpatialChunkKey expectedCenter = ChunkKey(
            authoredMaximumChunk - renderRadius,
            authoredMaximumChunk - renderRadius);
        ClientPresentationWindowSnapshot edgeWindow = _window?.GetSnapshot() ?? default;
        if (_player.Chunk != edge.Chunk || edgeWindow.CenterChunk != expectedCenter ||
            !Mathf.IsEqualApprox(_renderer.Root.Position.X, -15f) ||
            !Mathf.IsEqualApprox(_renderer.Root.Position.Z, -14f))
            throw new InvalidOperationException(
                "The finite structural window detached the camera from the edge-chunk player origin.");

        var fartherInsideEdge = new WorldPosition(
            authoredMaximumChunk,
            authoredMaximumChunk,
            15_500,
            14_500,
            0);
        RelocateControlledActor(fartherInsideEdge);
        DriveLocalUntil(() =>
            _player.Chunk == fartherInsideEdge.Chunk &&
            _player.Local.XMillimeters == fartherInsideEdge.Local.XMillimeters &&
            _player.Local.ZMillimeters == fartherInsideEdge.Local.ZMillimeters);
        if (!Mathf.IsEqualApprox(_renderer.Root.Position.X, -15.5f) ||
            !Mathf.IsEqualApprox(_renderer.Root.Position.Z, -14.5f))
            throw new InvalidOperationException(
                "Same-edge-chunk movement did not keep the camera centered on the player.");

        RelocateControlledActor(GamePlayableWorld.PlayerStart);
        DriveLocalUntil(() => _player == GamePlayableWorld.PlayerStart);

        void ProbeMovementFacing(
            ControlProfile profile,
            in GameLocomotionUpdate locomotion,
            in ClientControlWorldPoint lookPoint,
            WorldYaw expected)
        {
            SetProfile(profile);
            var intent = new ClientWorldCommandIntent(
                locomotion,
                GamePlayerActions.None,
                true);
            if (!_sequencer.TryCreateSemanticCommand(intent, out GamePlayerCommand command))
                throw new InvalidOperationException($"{profile} facing probe could not form a command.");
            var decision = new ClientControlProfileDecision(
                intent,
                false,
                default,
                profile == ControlProfile.OrbitCommand,
                lookPoint);
            if (!TryResolveProfileFacing(
                    command.Locomotion,
                    decision,
                    out ClientControlWorldPoint facingPoint,
                    out bool requiresAcceptedLocomotion))
                throw new InvalidOperationException($"{profile} did not produce movement-facing intent.");
            ApplyCommand(command, true, facingPoint, requiresAcceptedLocomotion);
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence == command.Sequence &&
                current.PlayerOrientation == expected);
            if (ReadPlayerOrientation() != expected)
                throw new InvalidOperationException(
                    $"{profile} did not face its accepted movement direction.");
        }
    }

    private void ProbeSelectedPublicTargetRetention()
    {
        if (!_useConceptDemoWorld) return;
        if (_localSession is null || _sequencer is null || _inputScheduler is null)
            throw new InvalidOperationException(
                "The selected-target acceptance requires an active local input path.");
        if (!TryFindPublicRecord(GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord sword))
            throw new InvalidOperationException(
                "The selected-target acceptance could not find the public Iron Sword.");
        SelectionKind savedSelection = _selection;
        WorldPublicEntityId savedSelectedTarget = _selectedTarget;
        WorldPublicEntityId savedPointerTarget = _pointerTarget;
        uint savedPointerTargetPrefab = _pointerTargetPrefab;
        string savedOutcome = _lastOutcome;
        try
        {
            _pointerTarget = sword.Id;
            _pointerTargetPrefab = sword.PrefabId;
            Select(new ClientControlWorldPoint(
                sword.Position.GlobalXMillimeters,
                sword.Position.GlobalZMillimeters));
            _pointerTarget = default;
            _pointerTargetPrefab = 0;
            if (SelectedInteractionTarget() != sword.Id)
                throw new InvalidOperationException(
                    "The selected public target was incorrectly tied to transient hover state.");

            if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView before))
                throw new InvalidOperationException(
                    "The selected-target acceptance could not read the authoritative player.");
            var context = new ClientWorldCommandContext(
                sword.Id,
                ClientWorldCommandRequestFlags.None,
                0,
                0,
                (ulong)LocalPlayableSelection.IronSword);
            var move = new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
                GamePlayerActions.None,
                true);
            long moveObservedAt = Stopwatch.GetTimestamp();
            if (!_inputScheduler.TryObserve(move, context, moveObservedAt) ||
                !_inputScheduler.TryPump(moveObservedAt, TrySubmitScheduledLocalCommand))
                throw new InvalidOperationException(
                    "Selecting the Iron Sword blocked scheduled movement submission.");
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence > before.LastCommandSequence &&
                current.LastSelection == LocalPlayableSelection.IronSword &&
                current.LastCommandResult.Locomotion == GameWorldActionStatus.Accepted &&
                current.PlayerPosition != before.PlayerPosition);
            if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView moved))
                throw new InvalidOperationException(
                    "The selected-target movement result was unavailable.");

            var stop = new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop,
                GamePlayerActions.None,
                true);
            long stopObservedAt = Stopwatch.GetTimestamp();
            if (!_inputScheduler.TryObserve(stop, context, stopObservedAt) ||
                !_inputScheduler.TryPump(stopObservedAt, TrySubmitScheduledLocalCommand))
                throw new InvalidOperationException(
                    "The selected-target acceptance could not stop scheduled movement.");
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence > moved.LastCommandSequence &&
                current.LastSelection == LocalPlayableSelection.IronSword &&
                current.LastCommandResult.Locomotion == GameWorldActionStatus.Accepted);
        }
        finally
        {
            _selection = savedSelection;
            _selectedTarget = savedSelectedTarget;
            _pointerTarget = savedPointerTarget;
            _pointerTargetPrefab = savedPointerTargetPrefab;
            _lastOutcome = savedOutcome;
        }
    }

    /// <summary>
    /// D0.0 permanent-client journey over the first physical Concept Demo island. Administrative
    /// relocation is used only to choose deterministic starting points; jump, telepad interaction,
    /// falling, landing, and recovery cross the ordinary local Game command/semantic boundary.
    /// </summary>
    public void RunD00Acceptance()
    {
        if (_boundWorld is null || _cache is null || _renderer is null ||
            _localSession is null || _sequencer is null || _camera is null)
            throw new InvalidOperationException("The Concept Demo smoke started before composition completed.");
        if (!_useConceptDemoWorld)
            throw new InvalidOperationException("The Concept Demo smoke requires the Concept Demo world fixture.");
        if (_settings is null)
            throw new InvalidOperationException("The Concept Demo settings are unavailable.");
        ProbeSelectedPublicTargetRetention();
        ProbeInventoryOutsideInputAndEquipment();
        ApplySettings(WithRenderDistance(_settings, 1));

        if (!WorldPosition.TryFromGlobalMillimeters(63_000, 0, 60_000, out WorldPosition outerVoid) ||
            _boundWorld.ProbeBaseTerrainSurface(outerVoid).Status != BaseTerrainSurfaceStatus.Absent ||
            _boundWorld.ProbeBaseTerrainSurface(GamePlayableWorld.SkyTelepadDestination).Status !=
                BaseTerrainSurfaceStatus.Absent)
            throw new InvalidOperationException("The central island/sky gap is not intentional BaseTerrain void.");

        Span<WorldTerrainColumnSurface> column = stackalloc WorldTerrainColumnSurface[8];
        if (_boundWorld.TryCopyTerrainColumnSurfaces(
                GamePlayableWorld.CaveLedgePoint, column, out int caveCount, out _) !=
                WorldTerrainColumnQueryStatus.Complete || caveCount != 5 ||
            !column[..caveCount].Contains(new WorldTerrainColumnSurface(
                GamePlayableWorld.MountainSurface,
                WorldTerrainSurfaceFace.Bottom,
                8_000,
                GameTerrainMaterialIds.Rock)) ||
            !column[..caveCount].Contains(new WorldTerrainColumnSurface(
                GamePlayableWorld.CaveLedgeSurface,
                WorldTerrainSurfaceFace.Top,
                4_000,
                GameTerrainMaterialIds.Rock)) ||
            !column[..caveCount].Contains(new WorldTerrainColumnSurface(
                GamePlayableWorld.CaveLedgeSurface,
                WorldTerrainSurfaceFace.Bottom,
                3_500,
                GameTerrainMaterialIds.Rock)))
            throw new InvalidOperationException("The MountainShell/preserved-base cave column is incomplete.");
        if (_boundWorld.TryCopyTerrainColumnSurfaces(
                GamePlayableWorld.SkyTelepadDestination, column, out int skyCount, out _) !=
                WorldTerrainColumnQueryStatus.Complete || skyCount != 2 ||
            column[0].SurfaceId != GamePlayableWorld.SkyIslandSurface ||
            column[0].Face != WorldTerrainSurfaceFace.Top ||
            column[0].HeightMillimeters != 3_500)
            throw new InvalidOperationException("The detached Sky Island shell is incomplete.");

        ValidateRenderedShell(new SpatialChunkKey(GameWorldState.DefaultSpace, -1, 0), 2);
        ValidateRenderedShell(new SpatialChunkKey(GameWorldState.DefaultSpace, 1, 0), 1);

        // Enter the mountain through its authored south opening. The preserved BaseTerrain remains
        // the cave floor while the MountainShell Bottom is real overhead clearance geometry.
        RelocateControlledActor(GamePlayableWorld.CaveEntranceApproach);
        SetProfile(ControlProfile.ThirdPersonDirect);
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDirect(
                        0, GamePlayerCommand.MaximumAxisMagnitude),
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand enterCave))
            throw new InvalidOperationException("The mountain cave traversal could not form a command.");
        ApplyCommand(enterCave, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == enterCave.Sequence &&
            _player.GlobalZMillimeters >= GamePlayableWorld.CaveFloorPoint.GlobalZMillimeters &&
            _player.YMillimeters < 5_000);
        SubmitStop();

        // Third-person profile: cross the intentional gap on the authoritative takeoff trajectory.
        RelocateControlledActor(GamePlayableWorld.SkyJumpStart);
        SetProfile(ControlProfile.ThirdPersonDirect);
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
                    GamePlayerActions.Jump,
                    true),
                out GamePlayerCommand jump))
            throw new InvalidOperationException("The Sky Island jump could not form a command.");
        ApplyCommand(jump, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == jump.Sequence &&
            view.LastCommandResult.Jump == GameWorldActionStatus.Accepted &&
            _player.YMillimeters == GamePlayableWorld.SkyTelepadDestination.YMillimeters &&
            _player.GlobalXMillimeters >= 25_000);
        SubmitStop();

        // Pointer command/selection resolves against the actual nearby structural shell rather
        // than requiring BaseTerrain beneath a detached island.
        const long skyPointerX = 29_000;
        const long skyPointerZ = 9_000;
        if (!TrySetPointerWorldPoint(skyPointerX, skyPointerZ))
            throw new InvalidOperationException("A Sky Island structural pointer point was rejected.");
        SetProfile(ControlProfile.OrbitCommand);
        _ = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _primaryDown = true;
        _primaryPressed = true;
        _ = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _primaryPressed = false;
        _primaryDown = false;
        _primaryReleased = true;
        ClientControlProfileDecision skyTap = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _primaryReleased = false;
        _primaryDown = true;
        _primaryPressed = true;
        ClientControlProfileDecision skyDoubleClick = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _primaryPressed = false;
        _primaryDown = false;
        if (!skyTap.SelectionRequested ||
            skyDoubleClick.Command.Locomotion.Kind != GameLocomotionUpdateKind.Destination ||
            skyDoubleClick.Command.Locomotion.DestinationXMillimeters != skyPointerX ||
            skyDoubleClick.Command.Locomotion.DestinationZMillimeters != skyPointerZ)
            throw new InvalidOperationException(
                "OrbitCommand could not select or form a destination on the detached Sky Island.");

        // Orbit profile: Jump is absent from its grammar; the selected public telepad invokes the
        // server-owned destination and the accepted relocation path.
        RelocateControlledActor(GamePlayableWorld.SkyTelepadPosition);
        SetProfile(ControlProfile.OrbitCommand);
        ClientControlProfileDecision orbitJump = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.Jump, true), CameraBasis());
        if ((orbitJump.Command.HeldActions & GamePlayerActions.Jump) != 0)
            throw new InvalidOperationException("OrbitCommand unexpectedly admitted a Jump action.");
        SetProfile(ControlProfile.ThirdPersonDirect);
        _ = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _ = TrySetPointerWorldPoint(
            GamePlayableWorld.SkyTelepadPosition.GlobalXMillimeters,
            GamePlayableWorld.SkyTelepadPosition.GlobalZMillimeters);
        _primaryReleased = true;
        ClientControlProfileDecision directSelection = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _primaryReleased = false;
        if (!directSelection.SelectionRequested)
            throw new InvalidOperationException(
                "ThirdPersonDirect did not expose pointer selection for interaction.");
        Select(directSelection.SelectionPoint);
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    GamePlayerActions.Interact,
                    true),
                out GamePlayerCommand telepad))
            throw new InvalidOperationException("The Sky Island telepad interaction could not be submitted.");
        ApplyCommand(telepad, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == telepad.Sequence &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
            _player == GamePlayableWorld.SkyTelepadDestination);

        // Walking east eventually leaves both the Sky Island and central sheet. Game owns the
        // below-envelope recovery and clears retained locomotion at the safe occupied start point.
        SetProfile(ControlProfile.ThirdPersonDirect);
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand walkOff))
            throw new InvalidOperationException("The Concept Demo fall could not form a command.");
        ApplyCommand(walkOff, false, default, false);
        DriveLocalUntil(() => _player == GamePlayableWorld.PlayerStart);
        // Owner/private position may become visible one local-pump turn before the corresponding
        // dynamic-public move. Finish client semantic convergence before a following acceptance
        // mutates presentation policy.
        DriveLocalUntil(() =>
            CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.Character) == 1);
        SetProfile(ControlProfile.OrbitCommand);

        void ValidateRenderedShell(SpatialChunkKey key, int expectedFragments)
        {
            if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) ||
                !_renderer.TryGetChunkDiagnostics(handle, out GodotWorldRenderChunkDiagnostics diagnostics) ||
                diagnostics.AdditionalTerrainSurfaceFragmentCount != expectedFragments ||
                diagnostics.AdditionalTerrainTriangleCount <= 0)
                throw new InvalidOperationException($"Concept Demo shell chunk {key} was not realized.");
        }

        void SubmitStop()
        {
            if (!_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.None,
                        true),
                    out GamePlayerCommand stop))
                throw new InvalidOperationException("The Sky Island landing stop could not form a command.");
            ApplyCommand(stop, false, default, false);
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
                view.LastCommandSequence == stop.Sequence);
        }
    }

    /// <summary>Focused F1.7 native composition journey using the real Local UI authority path.</summary>
    public LocalF17UiAcceptanceSnapshot RunF17UiAcceptance()
    {
        if (!_initialized || _uiClient is null || _uiBridge is null || _sessionUi is null)
            throw new InvalidOperationException("F1.7 requires an initialized shell-composed Local session.");
        LocalCabinUiOwnershipSnapshot routes = GetUiOwnershipSnapshot();
        if (!routes.HudOwnedBySharedHud || !routes.WorldOwnedBySharedWorld ||
            !routes.GumpsOwnedBySharedManager || !routes.BannerOwnedByHud ||
            !routes.TooltipOwnedByContext)
            throw new InvalidOperationException($"F1.7 Local category routing drifted: {routes}.");
        OpenInventory();
        DriveLocalUntil(() => _uiClient.OpenViewCount == 1 && FindOpenInventoryView() is not null);
        OpenServerMonitor();
        DriveLocalUntil(() => _uiClient.OpenViewCount == 2 &&
            FindOpenInventoryView() is not null && FindOpenServerMonitorView() is not null);

        Control inventory = FindOpenInventoryView()!;
        Control monitor = FindOpenServerMonitorView()!;
        UiViewInstanceId inventoryId = ParseViewId(inventory);
        UiViewInstanceId monitorId = ParseViewId(monitor);
        GodotUiRenderHandle inventoryHandle = _sessionUi.Gumps.HandleForAcceptance(inventoryId);
        GodotUiRenderHandle monitorHandle = _sessionUi.Gumps.HandleForAcceptance(monitorId);
        Vector2 inventoryPosition = _sessionUi.Gumps.GetWindowPosition(inventoryHandle);
        Vector2 monitorPosition = _sessionUi.Gumps.GetWindowPosition(monitorHandle);
        if (inventoryPosition == monitorPosition ||
            !_sessionUi.Gumps.BringToFront(inventoryHandle) ||
            !ReferenceEquals(
                _sessionUi.Gumps.HandleAt(_sessionUi.Gumps.WindowCount - 1), inventoryHandle))
            throw new InvalidOperationException(
                $"F1.7 modeless gump cascade/z ordering failed: inventory={inventoryPosition}, " +
                $"monitor={monitorPosition}, order={string.Join(',', Enumerable.Range(0, _sessionUi.Gumps.WindowCount).Select(index => _sessionUi.Gumps.HandleAt(index).Instance.Value))}, " +
                $"inventory={inventoryId.Value} monitor={monitorId.Value}.");
        if (!_sessionUi.Gumps.DragForAcceptance(inventoryHandle, new Vector2(-4_000, -4_000)))
            throw new InvalidOperationException("F1.7 native gump drag was rejected.");
        Vector2 dragged = _sessionUi.Gumps.GetWindowPosition(inventoryHandle);
        if (dragged.X < 0 || dragged.Y < 0)
            throw new InvalidOperationException("F1.7 native gump escaped the application clamp.");

        bool selected = false;
        bool interacted = false;
        _sessionUi.ContextMenu.Show(
            "Disclosed acceptance target",
            new Vector2(4, 4),
            () => selected = true,
            () => interacted = true);
        Button select = _sessionUi.ContextHost.FindChild(
            "ContextSelect", recursive: true, owned: false) as Button ??
            throw new InvalidOperationException("F1.7 Select context action was absent.");
        select.EmitSignal(BaseButton.SignalName.Pressed);
        if (!selected || _sessionUi.ContextMenu.Visible)
            throw new InvalidOperationException("F1.7 Select context action did not close exactly once.");
        _sessionUi.ContextMenu.Show(
            "Disclosed acceptance target",
            new Vector2(4, 4),
            () => selected = true,
            () => interacted = true);
        Button interact = _sessionUi.ContextHost.FindChild(
            "ContextInteract", recursive: true, owned: false) as Button ??
            throw new InvalidOperationException("F1.7 Interact context action was absent.");
        interact.EmitSignal(BaseButton.SignalName.Pressed);
        if (!interacted || _sessionUi.ContextMenu.Visible)
            throw new InvalidOperationException("F1.7 Interact context action did not close exactly once.");

        if (!TryRequestCloseOpenInventory())
            throw new InvalidOperationException("F1.7 Inventory could not close independently.");
        DriveLocalUntil(() => _uiClient.OpenViewCount == 1 &&
            FindOpenInventoryView() is null && FindOpenServerMonitorView() is not null);
        if (!TryRequestCloseOpenServerMonitor())
            throw new InvalidOperationException("F1.7 Server Monitor could not close independently.");
        DriveLocalUntil(() => _uiClient.OpenViewCount == 0);
        GodotGumpWindowManagerSnapshot retired = _sessionUi.Gumps.GetSnapshot();
        if (retired.OpenWindows != 0 || retired.PointerCaptured)
            throw new InvalidOperationException("F1.7 closed gumps retained native manager state.");
        return new LocalF17UiAcceptanceSnapshot(
            2, 2, inventoryPosition, monitorPosition, dragged, selected, interacted, retired);

        static UiViewInstanceId ParseViewId(Control root)
        {
            const string prefix = "UiView_";
            string name = root.Name.ToString();
            if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                !ulong.TryParse(name.AsSpan(prefix.Length), out ulong value))
                throw new InvalidOperationException("A compiled UI view lost its exact instance identity.");
            return new UiViewInstanceId(value);
        }
    }

    private void ValidateCatalogAndHumanPresentation()
    {
        LocalCabinPresentationSnapshot snapshot = GetPresentationSnapshot();
        string expected = snapshot.ContentVariant switch
        {
            CabinContentVariant.Default => CabinContentFingerprints.DefaultCatalog,
            CabinContentVariant.Replace => CabinContentFingerprints.ReplaceCatalog,
            CabinContentVariant.Add => CabinContentFingerprints.AddCatalog,
            _ => throw new InvalidOperationException("The Cabin content variant is invalid."),
        };
        if (_resolvedContent is null ||
            !_resolvedContent.Catalog.TryGet(CabinContentIds.WallMaterial, out ResolvedContentEntry? wall) ||
            wall!.ReferencedFiles.Count != 1)
            throw new InvalidOperationException("The resolved Cabin wall material is unavailable.");
        if (snapshot.ContentFingerprint.ToString() != expected ||
            snapshot.WallTextureHash != wall.ReferencedFiles[0] || snapshot.HumanMeshInstances == 0 ||
            !Mathf.IsEqualApprox(snapshot.CameraPivotMeters, CameraPivotMeters) ||
            !Mathf.IsEqualApprox(snapshot.NameplateHeightMeters, NameplateHeightMeters) ||
            !Mathf.IsEqualApprox(snapshot.EffectsHeightMeters, EffectsHeightMeters) ||
            snapshot.AddedPresentationRealized !=
                (snapshot.ContentVariant == CabinContentVariant.Add))
            throw new InvalidOperationException("Cabin catalog/human calibration realization is incomplete.");
    }

    private void ProbePlayableInventory()
    {
        if (_localSession is null || _uiClient is null || _uiBridge is null ||
            _inventoryHost is null)
            throw new InvalidOperationException("Playable Inventory presentation was not composed.");
        ulong before = _localSession.GetSnapshot().UiOperationsApplied;
        OpenInventory();
        DriveLocalUntil(() =>
        {
            _ = _uiBridge.Pump();
            return _uiClient.OpenViewCount == 1 &&
                _localSession.GetSnapshot().UiOperationsApplied > before;
        });
        Control? inventory = FindOpenInventoryView();
        if (inventory is null ||
            inventory.FindChild("Element_background", recursive: true, owned: false) is not Panel ||
            inventory.FindChild("Element_close", recursive: true, owned: false) is not Button ||
            !Mathf.IsEqualApprox(inventory.AnchorLeft, 0.5f) ||
            !Mathf.IsEqualApprox(inventory.AnchorTop, 0.5f))
            throw new InvalidOperationException(
                "Playable Inventory was not centered, backed by a surface, and closable.");
        OpenInventory();
        DriveLocalUntil(() =>
        {
            _ = _uiBridge.Pump();
            return _uiClient.OpenViewCount == 0;
        });
    }

    private void ProbeInventoryOutsideInputAndEquipment()
    {
        if (_localSession is null || _sequencer is null || _inputScheduler is null ||
            _uiClient is null || _uiBridge is null || _actorPresenter is null)
            throw new InvalidOperationException(
                "The equipment/input acceptance requires the complete local product path.");
        if (!TryFindPublicRecord(GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord sword))
            throw new InvalidOperationException(
                "The equipment/input acceptance could not find the public Iron Sword.");

        _pointerTarget = sword.Id;
        _pointerTargetPrefab = sword.PrefabId;
        Select(new ClientControlWorldPoint(
            sword.Position.GlobalXMillimeters,
            sword.Position.GlobalZMillimeters));
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    GamePlayerActions.Interact,
                    true),
                out GamePlayerCommand pickup))
            throw new InvalidOperationException("The Iron Sword pickup command could not be formed.");
        ApplyCommand(pickup, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == pickup.Sequence &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
            !TryFindPublicRecord(GameWorldPublicPrefabIds.IronSword, out _));

        OpenInventory();
        DriveLocalUntil(() =>
        {
            _ = _uiBridge.Pump();
            return _uiClient.OpenViewCount == 1;
        });

        SetProfile(ControlProfile.ThirdPersonDirect);
        _ = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true),
            ClientHorizontalCameraBasis.WorldAxes);
        _gameplayInputAdmittedLastFrame = true;
        ClientControlProfileDecision moving = EvaluateProfile(
            new ClientWorldInputSample(1, 0, GamePlayerActions.Primary, true),
            ClientHorizontalCameraBasis.WorldAxes);
        bool admitted = ObserveGameplayInputAdmission();
        ClientControlProfileDecision whileOpen = EvaluateProfile(
            new ClientWorldInputSample(1, 0, GamePlayerActions.Primary, admitted),
            ClientHorizontalCameraBasis.WorldAxes);
        if (moving.Command.Locomotion.Kind != GameLocomotionUpdateKind.Direct || !admitted ||
            whileOpen.Command.Locomotion.Kind != GameLocomotionUpdateKind.Direct ||
            whileOpen.Command.HeldActions != GamePlayerActions.Primary ||
            !whileOpen.Command.HasFocus)
            throw new InvalidOperationException(
                "The modeless Inventory unexpectedly revoked outside gameplay movement/action focus.");

        Control inventoryRoot = FindOpenInventoryView() ??
            throw new InvalidOperationException("The native Inventory root was absent.");
        const string viewPrefix = "UiView_";
        string viewName = inventoryRoot.Name.ToString();
        if (!viewName.StartsWith(viewPrefix, StringComparison.Ordinal) ||
            !ulong.TryParse(viewName.AsSpan(viewPrefix.Length), out ulong viewValue) ||
            viewValue == 0)
            throw new InvalidOperationException("The native Inventory view identity was invalid.");
        var viewId = new UiViewInstanceId(viewValue);
        UiRowId swordRow = default;
        foreach (UiListRow row in _uiClient.GetRows(viewId, InventoryUi.Items))
        {
            if (row.Values.Length != 0 &&
                string.Equals(row.Values[0].Text, "Iron Sword", StringComparison.Ordinal))
            {
                swordRow = row.Id;
                break;
            }
        }
        if (!swordRow.IsValid)
            throw new InvalidOperationException("The picked-up Iron Sword was absent from Inventory.");
        _uiClient.SubmitAction(viewId, InventoryUi.Select, swordRow);
        DriveLocalUntil(() => _uiClient.GetActionState(viewId, InventoryUi.Equip).Enabled);
        _uiClient.SubmitAction(viewId, InventoryUi.Equip);
        DriveLocalUntil(() =>
        {
            if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView view) ||
                view.MainHandPrefabId != GameWorldPublicPrefabIds.IronSword ||
                _actorPresenter.GetSnapshot().EquippedMainHands != 1 ||
                !_actorPresenter.TryGetActorRoot(view.PublicActor, out Node3D? actor) || actor is null)
                return false;
            Node3D? socket = actor.FindChild(
                "MainHandSocket", recursive: true, owned: false) as Node3D;
            Node3D? presentation = socket?.FindChild(
                "MainHandPresentation", recursive: true, owned: false) as Node3D;
            MeshInstance3D? blade = presentation?.FindChild(
                "PrimitiveBlade", recursive: true, owned: false) as MeshInstance3D;
            return presentation?.Visible == true && presentation.IsVisibleInTree() &&
                presentation.GetParent() == socket && blade?.Mesh is not null &&
                blade.IsVisibleInTree() &&
                presentation.GlobalPosition.DistanceTo(actor.GlobalPosition) < 3f;
        });
        if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView presentedView) ||
            !_actorPresenter.TryGetActorRoot(presentedView.PublicActor, out Node3D? presentedActor) ||
            presentedActor is null ||
            presentedActor.FindChild(
                "PrimitiveBlade", recursive: true, owned: false) is not MeshInstance3D presentedBlade ||
            presentedBlade.Mesh is null)
            throw new InvalidOperationException("The equipped sword blade was not realized.");
        const float expectedBladeLengthMeters = 0.75f;
        Node3D presentedSocket = presentedBlade.GetParent().GetParent<Node3D>();
        if (presentedSocket.FindChild(
                "PrimitiveGrip", recursive: true, owned: false) is not MeshInstance3D presentedGrip ||
            presentedGrip.Mesh is null)
            throw new InvalidOperationException("The equipped sword grip was not realized.");
        Vector3 bladeSize = presentedBlade.Mesh.GetAabb().Size;
        Vector3 bladeBase = presentedBlade.ToGlobal(Vector3.Zero);
        Vector3 bladeTip = presentedBlade.ToGlobal(
            new Vector3(0, expectedBladeLengthMeters, 0));
        Vector3 gripBase = presentedGrip.ToGlobal(Vector3.Zero);
        Vector3 gripTip = presentedGrip.ToGlobal(new Vector3(0, 0.13f, 0));
        Vector3 gripCenter = (gripBase + gripTip) * 0.5f;
        Vector2 socketPlanar = new(
            presentedSocket.GlobalPosition.X - presentedActor.GlobalPosition.X,
            presentedSocket.GlobalPosition.Z - presentedActor.GlobalPosition.Z);
        Vector2 tipPlanar = new(
            bladeTip.X - presentedActor.GlobalPosition.X,
            bladeTip.Z - presentedActor.GlobalPosition.Z);
        if (!Mathf.IsEqualApprox(bladeSize.X, 0.055f) ||
            !Mathf.IsEqualApprox(bladeSize.Y, expectedBladeLengthMeters) ||
            !Mathf.IsEqualApprox(bladeSize.Z, 0.018f) ||
            !Mathf.IsEqualApprox(bladeTip.DistanceTo(bladeBase), expectedBladeLengthMeters) ||
            gripCenter.DistanceTo(presentedSocket.GlobalPosition) > 0.02f ||
            bladeTip.Y > presentedSocket.GlobalPosition.Y - 0.65f ||
            tipPlanar.Length() < socketPlanar.Length() + 0.10f)
            throw new InvalidOperationException(
                $"The equipped sword lost its one-handed proportions or outward/downward mount: " +
                $"aabb={presentedBlade.Mesh.GetAabb()}, socket={presentedSocket.GlobalPosition}, " +
                $"gripCenter={gripCenter}, base={bladeBase}, tip={bladeTip}.");
        WorldPublicRecord equipped = default;
        if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView equippedView) ||
            !TryFindEquippedPublicRecord(equippedView.PlayerPosition, out equipped))
            throw new InvalidOperationException(
                "The equipped Iron Sword did not return as visible, non-pickable owner-following " +
                $"public truth: found={equipped.Id.IsValid}, flags={equipped.StateFlags}, " +
                $"item={equipped.Position}, owner={equippedView.PlayerPosition}.");

        OpenInventory();
        DriveLocalUntil(() => _uiClient.OpenViewCount == 0);
        _gameplayInputAdmittedLastFrame = false;
        _gameplayStopPending = false;
        SetProfile(ControlProfile.OrbitCommand);
    }

    private void ProbeAuthoritativeJump()
    {
        if (_localSession is null || _sequencer is null)
            throw new InvalidOperationException("Playable vertical motion was not composed.");
        RelocateControlledActor(GamePlayableWorld.PlayerStart);
        SetProfile(ControlProfile.ThirdPersonDirect);
        WorldPosition start = _player;
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Unchanged,
                    GamePlayerActions.Jump,
                    true),
                out GamePlayerCommand jump))
            throw new InvalidOperationException("Playable Jump could not form a command edge.");
        ApplyCommand(jump, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == jump.Sequence &&
            view.LastCommandResult.Jump == GameWorldActionStatus.Accepted &&
            _player.YMillimeters > start.YMillimeters);

        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Unchanged,
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand release))
            throw new InvalidOperationException("Playable Jump release could not form a command.");
        ApplyCommand(release, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == release.Sequence && _player == start);
    }

    private void ProbeDoorAndChestInteraction()
    {
        if (_localSession is null || _sequencer is null)
            throw new InvalidOperationException("Playable interaction was not composed.");
        SubmitInteraction(LocalPlayableSelection.CabinDoor, out GamePlayerCommand doorCommand);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == doorCommand.Sequence && view.DoorIsOpen &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted);
        ReleaseInteraction();

        if (!WorldPosition.TryFromGlobalMillimeters(18_000, 0, 9_500, out WorldPosition chestApproach))
            throw new InvalidOperationException("The Cabin chest approach is not canonical.");
        RelocateControlledActor(chestApproach);
        SubmitInteraction(LocalPlayableSelection.CabinChest, out GamePlayerCommand chestCommand);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == chestCommand.Sequence &&
            view.LastChestAction == GameWorldActionStatus.Accepted && !view.BreadIsInBackpack);
        ReleaseInteraction();
        RelocateControlledActor(GamePlayableWorld.PlayerStart);

        void SubmitInteraction(LocalPlayableSelection selection, out GamePlayerCommand command)
        {
            if (!_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.Interact,
                        true),
                    out command) ||
                !_localSession.TrySubmit(new LocalPlayableCommand(
                    command, selection, false, default, false)))
                throw new InvalidOperationException($"Playable {selection} interaction could not be submitted.");
        }

        void ReleaseInteraction()
        {
            if (!_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.None,
                        true),
                    out GamePlayerCommand release) ||
                !_localSession.TrySubmit(new LocalPlayableCommand(
                    release, LocalPlayableSelection.None, false, default, false)))
                throw new InvalidOperationException("Playable interaction release could not be submitted.");
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
                view.LastCommandSequence == release.Sequence);
        }
    }

    private void ProbePlayableChat()
    {
        if (_chatState is null || _chatFeed is null || _overheadSpeech is null)
            throw new InvalidOperationException("Playable Chat presentation was not composed.");
        ulong before = _chatState.GetSnapshot().Applied;
        SubmitChat("playable speech");
        DriveLocalUntil(() => _chatState.GetSnapshot().Applied != before);
        if (_chatState.GetSnapshot().Applied != before + 1)
            throw new InvalidOperationException("Playable spatial speech did not apply.");
        UpdateChatPresentation();
        if (!_chatFeed.Text.Contains("playable speech", StringComparison.Ordinal) ||
            !_overheadAnchor.IsValid || !TryFindPublicAnchor(_overheadAnchor, out _))
            throw new InvalidOperationException("Playable speech did not reach feed and public-anchor presentation.");

        before = _chatState.GetSnapshot().Applied;
        SubmitChat("/where");
        DriveLocalUntil(() => _chatState.GetSnapshot().Applied != before);
        UpdateChatPresentation();
        if (_chatState.GetSnapshot().Applied != before + 1 ||
            !_chatFeed.Text.Contains("Where:", StringComparison.Ordinal))
            throw new InvalidOperationException("Playable private /where result did not reach Chat.");
    }

    private WorldYaw ReadPlayerOrientation()
    {
        if (_localSession is null ||
            !_localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
            throw new InvalidOperationException("The playable character orientation was unavailable.");
        return view.PlayerOrientation;
    }

    private void RelocateControlledActor(in WorldPosition requested)
    {
        if (_localSession is null)
            throw new InvalidOperationException("Authority is not available for relocation.");
        ulong request = _nextAdministrativeRequest++;
        if (!_localSession.TryRequestRelocation(request, requested))
            throw new InvalidOperationException("Launch-smoke relocation queue was full.");
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastAdministrativeSequence == request);
        _localSession.TryGetLatestView(out LocalPlayableAuthorityView completed);
        if (!completed.LastAdministrativeSucceeded)
            throw new InvalidOperationException(
                $"Launch-smoke relocation failed: {completed.LastAdministrativeMessage}.");

        // The authority may publish the administrative result after this frame copied its
        // presentation view. Do not let the next client-authored absolute facing point use the
        // pre-relocation _player origin. Cross the same semantic boundary as ordinary play:
        // authority commits first, then Client Core/presentation observes that exact position.
        WorldPosition authoritativePosition = completed.PlayerPosition;
        DriveLocalUntil(() => _player == authoritativePosition);
    }

    private int CountPresentedPrefab(uint prefabId)
    {
        if (ClientActorDefinitionCatalog.ConceptDemo.TryGet(prefabId, out _) &&
            _actorState is not null)
        {
            int actorCount = _actorState.CopyCurrent(_actorRows);
            int matched = 0;
            for (int index = 0; index < actorCount; index++)
                if (_actorRows[index].PrefabId == prefabId) matched++;
            return matched;
        }
        if (_renderer is null) return 0;
        string expectedName = $"Public_{prefabId}";
        int count = 0;
        Count(_renderer.Root);
        return count;

        void Count(Node node)
        {
            if (node is MultiMeshInstance3D instance && instance.Name == expectedName &&
                instance.Multimesh is not null)
                count += instance.Multimesh.InstanceCount;
            foreach (Node child in node.GetChildren()) Count(child);
        }
    }

    private int CountPresentedCharacters() =>
        CountPresentedPrefab(GameWorldPublicPrefabIds.Character) +
        CountPresentedPrefab(GameWorldPublicPrefabIds.CharacterMale);

    /// <summary>
    /// Verifies the actual native public-presentation resources after the shell has committed the
    /// session. This is deliberately later than initial semantic convergence: loading may build
    /// nodes before their render resources and bounds have been validated.
    /// </summary>
    private void ValidateInitialNativePresentation()
    {
        int characters = CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.Character) +
            CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.CharacterMale);
        int doors = CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.CabinDoor);
        int chests = CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.WorldChest);
        int telepads = _useConceptDemoWorld
            ? CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.SkyTelepad)
            : 0;
        int gates = _useConceptDemoWorld
            ? CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.FarGate)
            : 0;
        int signs = _useConceptDemoWorld
            ? CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.InformationalSign)
            : 0;
        if (characters != 1 || doors != 1 || chests != 1 ||
            _useConceptDemoWorld &&
            (telepads != 1 || gates != 1 || signs != 2))
            throw new InvalidOperationException(
                "The initial public world does not own complete visible native mesh presentation: " +
                $"characters={characters}, doors={doors}, chests={chests}, telepads={telepads}, " +
                $"gates={gates}, signs={signs}.");
    }

    private int CountVisiblyPresentedPrefab(uint prefabId)
    {
        if (ClientActorDefinitionCatalog.ConceptDemo.TryGet(prefabId, out _) &&
            _actorState is not null && _actorPresenter is not null)
        {
            int actorCount = _actorState.CopyCurrent(_actorRows);
            int matched = 0;
            for (int index = 0; index < actorCount; index++)
            {
                ref readonly ClientActorPresentationRecord actor = ref _actorRows[index];
                if (actor.PrefabId == prefabId &&
                    _actorPresenter.TryGetActorRoot(actor.Id, out Node3D? root) &&
                    root?.Visible == true && CountMeshInstances(root) != 0)
                    matched++;
            }
            return matched;
        }
        if (_renderer is null) return 0;
        string expectedName = $"Public_{prefabId}";
        int count = 0;
        Count(_renderer.Root);
        return count;

        void Count(Node node)
        {
            if (node is MultiMeshInstance3D instance && instance.Name == expectedName &&
                instance.Visible &&
                instance.Multimesh is { } multiMesh &&
                multiMesh.VisibleInstanceCount != 0 &&
                multiMesh.Mesh is { } mesh && mesh.GetSurfaceCount() > 0 &&
                instance.CustomAabb.Size.LengthSquared() > 0f)
                count += multiMesh.InstanceCount;
            foreach (Node child in node.GetChildren()) Count(child);
        }
    }

    private int CountRenderedTerrainTriangles()
    {
        if (_cache is null || _renderer is null ||
            !_cache.TryCopyResidentInfo(_chatChunkInfo, out int count, out _))
            return 0;
        int triangles = 0;
        for (int index = 0; index < count; index++)
        {
            if (_renderer.TryGetChunkDiagnostics(
                    _chatChunkInfo[index].Handle,
                    out GodotWorldRenderChunkDiagnostics diagnostics))
                triangles = checked(triangles + diagnostics.TerrainTriangleCount +
                    diagnostics.AdditionalTerrainTriangleCount);
        }
        return triangles;
    }

    private void ApplyProfileCameraDefaults()
    {
        switch (_profile)
        {
            case ControlProfile.OrbitCommand:
                _cameraYaw = -0.7f;
                _cameraPitch = OrbitDefaultPitchRadians;
                _cameraDistance = OrbitDefaultCameraDistanceMeters;
                break;
            case ControlProfile.ThirdPersonDirect:
                _cameraYaw = -0.7f;
                _cameraPitch = 0.34f;
                _cameraDistance = 8.5f;
                break;
            case ControlProfile.IndependentMoveLook:
                _cameraYaw = -MathF.PI * 0.25f;
                _cameraPitch = 0.61548f;
                _cameraDistance = 14f;
                break;
        }
    }

    private void SaveWorld()
    {
        if (_localSession is null)
            return;
        ulong request = _nextAdministrativeRequest++;
        _lastOutcome = _localSession.TryRequestSave(request)
            ? "Save requested"
            : "Save queue busy";
    }

    private void LoadWorld()
    {
        if (_localSession is null)
            return;
        ulong request = _nextAdministrativeRequest++;
        _lastOutcome = _localSession.TryRequestLoad(request)
            ? "Load requested"
            : "Load queue busy";
    }

    private void DriveLocalUntil(Func<bool> complete)
    {
        ArgumentNullException.ThrowIfNull(complete);
        for (int step = 0; step < 10_000; step++)
        {
            PumpChat();
            _ = _uiBridge?.Pump();
            SynchronizeProjection();
            UpdateChatPresentation();
            DrainRenderer();
            if (complete()) return;
            LocalPlayableSessionSnapshot session = _localSession?.GetSnapshot() ?? default;
            if (!string.IsNullOrEmpty(session.Fault))
                throw new InvalidOperationException(
                    $"The local authority faulted while the client was converging: {session.Fault}");
            Thread.Sleep(1);
        }
        LocalPlayableSessionSnapshot final = _localSession?.GetSnapshot() ?? default;
        LocalPlayableAuthorityView authority = default;
        if (_localSession is not null) _ = _localSession.TryGetLatestView(out authority);
        throw new TimeoutException(
            "The local authority/client semantic bridge did not converge. " +
            $"ticks={final.AuthorityTicks} pendingCommands={final.PendingCommands} " +
            $"lastCommand={authority.LastCommandSequence} position={authority.PlayerPosition} " +
            $"orientation={authority.PlayerOrientation} fault={final.Fault ?? "none"}.");
    }

    private static int CountMeshInstances(Node node)
    {
        int count = node is MeshInstance3D ? 1 : 0;
        foreach (Node child in node.GetChildren())
            count += CountMeshInstances(child);
        return count;
    }

    private static Control FullRectControl(string name)
    {
        var result = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        result.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return result;
    }

    private static void ReleaseExternalUi(Control? root)
    {
        if (root is null || !GodotObject.IsInstanceValid(root)) return;
        root.GetParent()?.RemoveChild(root);
        root.QueueFree();
    }

    private void CreateSceneFurniture()
    {
        _camera = new Camera3D
        {
            Name = "OrbitCamera",
            Current = true,
            Near = 0.1f,
            Far = 500f,
            Fov = _settings?.CameraFovDegrees ?? 70f,
        };
        AddChild(_camera);
        UpdateCamera();

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55, -35, 0),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        };
        AddChild(sun);
        var fill = new WorldEnvironment { Name = "Environment" };
        fill.Environment = new global::Godot.Environment
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.055f, 0.075f, 0.06f),
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.65f, 0.72f, 0.68f),
            AmbientLightEnergy = 0.65f,
        };
        AddChild(fill);

        _ownedHudRoot = FullRectControl("CabinHUD");
        _ownedHudRoot.Scale = Vector2.One * (_settings?.UiScale ?? 1f);
        if (_sessionUi is not null)
            _sessionUi.HudHost.AddChild(_ownedHudRoot);
        else
        {
            var layer = new CanvasLayer { Name = "HUD" };
            AddChild(layer);
            layer.AddChild(_ownedHudRoot);
        }
        var panel = new PanelContainer
        {
            OffsetLeft = 16,
            OffsetTop = 16,
            OffsetRight = 590,
            OffsetBottom = 220,
        };
        _ownedHudRoot.AddChild(panel);
        var stack = new VBoxContainer();
        panel.AddChild(stack);
        var title = new Label
        {
            Text = _useConceptDemoWorld
                ? "UltraSim — Concept Demo Island"
                : "UltraSim — T0.10 Woods / Cabin",
            ThemeTypeVariation = "HeaderMedium",
        };
        stack.AddChild(title);
        _status = new Label();
        _details = new Label();
        stack.AddChild(_status);
        stack.AddChild(_details);
        var profiles = new HBoxContainer();
        stack.AddChild(profiles);
        AddProfileButton("1/F1 Orbit", ControlProfile.OrbitCommand);
        AddProfileButton("2/F2 Third person", ControlProfile.ThirdPersonDirect);
        AddProfileButton("3/F3 Iso move/look", ControlProfile.IndependentMoveLook);
        stack.AddChild(new Label
        {
            Text = "F1: single-click selects • hold LMB steers • double-click ground travels • RMB context • Esc stop/options\n" +
                (_useConceptDemoWorld
                    ? "MMB drag camera • wheel zoom • E use selected door/chest/sign/telepad/gate • I inventory\n"
                    : "MMB drag camera • wheel zoom • E use selected door/chest • I inventory\n") +
                "Space jump (direct profiles) • 1/F1 Orbit • 2/F2 third-person • " +
                "3/F3 fixed iso move/look • F4 Server Monitor • F5 save • F9 load",
        });

        var chatPanel = new PanelContainer
        {
            OffsetLeft = 16,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetTop = -250,
            OffsetRight = 590,
            OffsetBottom = -16,
        };
        _ownedHudRoot.AddChild(chatPanel);
        var chatStack = new VBoxContainer();
        chatPanel.AddChild(chatStack);
        chatStack.AddChild(new Label { Text = "Chat", ThemeTypeVariation = "HeaderSmall" });
        _chatFeed = new RichTextLabel
        {
            Name = "ChatFeed",
            BbcodeEnabled = false,
            CustomMinimumSize = new Vector2(540, 145),
            ScrollFollowing = true,
        };
        chatStack.AddChild(_chatFeed);
        var chatEntry = new HBoxContainer();
        chatStack.AddChild(chatEntry);
        _chatMode = new OptionButton { CustomMinimumSize = new Vector2(100, 0) };
        _chatMode.AddItem("Whisper", (int)ClientChatSpeechMode.Whisper);
        _chatMode.AddItem("Say", (int)ClientChatSpeechMode.Say);
        _chatMode.AddItem("Yell", (int)ClientChatSpeechMode.Yell);
        _chatMode.AddItem("Global", (int)ClientChatSpeechMode.Global);
        _chatMode.Select(1);
        chatEntry.AddChild(_chatMode);
        _chatInput = new LineEdit
        {
            Name = "ChatInput",
            PlaceholderText = "Enter chat or /help /where",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MaxLength = MaximumChatTextCharacters,
        };
        _chatInput.TextSubmitted += SubmitChat;
        chatEntry.AddChild(_chatInput);
        _ownedWorldUiRoot = FullRectControl("CabinWorldUI");
        if (_sessionUi is not null)
            _sessionUi.WorldHost.AddChild(_ownedWorldUiRoot);
        else
            _ownedHudRoot.AddChild(_ownedWorldUiRoot);
        _overheadSpeech = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _ownedWorldUiRoot.AddChild(_overheadSpeech);
        _systemChatBanner = new GodotSystemChatBanner(_ownedHudRoot);

        _hoverTooltip = new GodotWorldHoverTooltip(_sessionUi?.ContextHost ?? _ownedWorldUiRoot);
        _sessionUi?.RegisterTransientSuppressor(_hoverTooltip.Hide);

        if (_sessionUi is not null)
            _inventoryHost = _sessionUi.Gumps.Host;
        else
        {
            _inventoryHost = FullRectControl("CabinInventory");
            _ownedHudRoot.AddChild(_inventoryHost);
        }

        void AddProfileButton(string text, ControlProfile profile)
        {
            var button = new Button { Text = text };
            button.Pressed += () => SetProfile(profile);
            profiles.AddChild(button);
        }
    }

    private void SubmitChat(string text)
    {
        if (_chatInput is null || _localSession is null)
            return;
        ClientChatSpeechMode mode = _chatMode is null
            ? ClientChatSpeechMode.Say
            : (ClientChatSpeechMode)_chatMode.GetSelectedId();
        ulong submission = _nextChatSubmission;
        if (_localSession.TrySubmitChat(submission, mode, text))
        {
            _nextChatSubmission = checked(submission + 1);
            _chatInput.Clear();
            _chatInput.ReleaseFocus();
            _lastOutcome = "Chat submitted";
        }
        else
        {
            _lastOutcome = "Chat input rejected or transport busy";
        }
    }

    private bool TryHandleFocusedChatInput(InputEvent inputEvent)
    {
        if (_chatInput?.HasFocus() != true) return false;
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (IsChatSubmitKey(key))
            {
                SubmitChat(_chatInput.Text);
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == global::Godot.Key.Escape ||
                     key.PhysicalKeycode == global::Godot.Key.Escape)
            {
                _chatInput.ReleaseFocus();
                _lastOutcome = "Chat focus released";
                GetViewport().SetInputAsHandled();
            }
        }
        else if (inputEvent is InputEventMouseButton mouse && mouse.Pressed &&
            !_chatInput.GetGlobalRect().HasPoint(mouse.Position))
        {
            // Do not mark this click handled: the clicked HUD control may still consume it. This
            // client stops here, so the same event can never also become a world interaction.
            _chatInput.ReleaseFocus();
            _lastOutcome = "Chat focus released";
        }
        return true;
    }

    private static bool IsChatSubmitKey(InputEventKey input) =>
        input.Keycode is global::Godot.Key.Enter or global::Godot.Key.KpEnter ||
        input.PhysicalKeycode is global::Godot.Key.Enter or global::Godot.Key.KpEnter;

    private void PumpChat()
    {
        if (_localSession is null || _chatState is null)
            return;
        _ = _localSession.PumpChat(_chatState);
    }

    private void UpdateChatPresentation()
    {
        if (_chatState is null || _chatFeed is null)
            return;
        ClientChatStateSnapshot snapshot = _chatState.GetSnapshot();
        if (snapshot.Applied != _chatAppliedPresentationCount)
        {
            int count = _chatState.CopyRecent(_chatRows);
            int first = Math.Max(0, count - 8);
            var lines = new string[count - first];
            for (int index = first; index < count; index++)
            {
                ClientChatOccurrence row = _chatRows[index];
                string prefix = row.Kind switch
                {
                    ClientChatOccurrenceKind.SpatialSpeech =>
                        $"[{row.SpeechMode}] {row.DisplayLabel}: ",
                    ClientChatOccurrenceKind.GlobalSpeech =>
                        $"[Global] {row.DisplayLabel}: ",
                    _ => $"[{row.DisplayLabel}] ",
                };
                lines[index - first] = prefix + row.Text;
            }
            _chatFeed.Text = string.Join('\n', lines);
            if (count != 0)
            {
                ClientChatOccurrence newest = _chatRows[count - 1];
                ulong residentGenerationId = ReadActiveGenerationId();
                if (newest.CanPresentOverhead(residentGenerationId))
                {
                    _overheadAnchor = newest.PublicAnchor;
                    _overheadOriginGenerationId = newest.ServerGenerationId;
                    _overheadExpiresAtMilliseconds = checked(Time.GetTicksMsec() + 4_000);
                    _ = _actorPresenter?.TryPresentOverhead(
                        in newest,
                        residentGenerationId,
                        _overheadExpiresAtMilliseconds);
                }
                else if (newest.IsSpatial)
                {
                    _overheadAnchor = default;
                    _overheadOriginGenerationId = 0;
                }
            }
            _systemChatBanner?.Observe(_chatRows.AsSpan(0, count), Time.GetTicksMsec());
            _chatAppliedPresentationCount = snapshot.Applied;
        }

        if (_overheadSpeech is not null) _overheadSpeech.Visible = false;
        _systemChatBanner?.Update(Time.GetTicksMsec());
        _actorPresenter?.UpdateOriginAndLifetimes(_player, Time.GetTicksMsec());
    }

    private ulong ReadActiveGenerationId()
    {
        return _localSession is not null &&
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view)
                ? view.GenerationId
                : 0;
    }

    private bool TryFindPublicAnchor(WorldPublicEntityId id, out WorldPosition position)
    {
        if (_cache is not null && _cache.TryCopyResidentInfo(
                _chatChunkInfo, out int count, out _))
        {
            for (int chunk = 0; chunk < count; chunk++)
            {
                if (!_cache.TryGetResidentView(_chatChunkInfo[chunk].Handle, out ClientSemanticChunkView view))
                    continue;
                foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
                {
                    if (record.Id != id) continue;
                    position = record.Position;
                    return true;
                }
            }
        }
        position = default;
        return false;
    }

    private bool TryFindPublicRecord(uint prefabId, out WorldPublicRecord found)
    {
        if (_cache is not null && _cache.TryCopyResidentInfo(
                _chatChunkInfo, out int count, out _))
        {
            for (int chunk = 0; chunk < count; chunk++)
            {
                if (!_cache.TryGetResidentView(
                        _chatChunkInfo[chunk].Handle, out ClientSemanticChunkView view))
                    continue;
                foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
                {
                    if (record.PrefabId != prefabId) continue;
                    found = record;
                    return true;
                }
            }
        }
        found = default;
        return false;
    }

    private bool TryFindEquippedPublicRecord(
        in WorldPosition ownerPosition,
        out WorldPublicRecord found)
    {
        if (_cache is not null && _cache.TryCopyResidentInfo(
                _chatChunkInfo, out int count, out _))
        {
            for (int chunk = 0; chunk < count; chunk++)
            {
                if (!_cache.TryGetResidentView(
                        _chatChunkInfo[chunk].Handle, out ClientSemanticChunkView view))
                    continue;
                foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
                {
                    if (record.PrefabId != GameWorldPublicPrefabIds.IronSword ||
                        record.Position != ownerPosition ||
                        (record.StateFlags & WorldPublicStateFlags.Visible) == 0 ||
                        (record.StateFlags & WorldPublicStateFlags.GroundItem) != 0)
                        continue;
                    found = record;
                    return true;
                }
            }
        }
        found = default;
        return false;
    }

    private void UpdateHud()
    {
        if (_status is null || _details is null)
            return;
        _status.Text = _lastOutcome;
        if (!_initialized || _localSession is null || _renderer is null ||
            !_localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
        {
            _details.Text = "Not running";
            return;
        }
        GodotWorldRendererSnapshot render = _renderer.GetSnapshot();
        _peakRenderedChunks = Math.Max(_peakRenderedChunks, render.ActiveRenderChunks);
        ClientStructuralResidencySnapshot residency = _residency?.GetSnapshot() ?? default;
        ClientPresentationWindowSnapshot presentation = _window?.GetSnapshot() ?? default;
        _details.Text =
            $"Profile {_profile} • Selected {_selection} • Tick {view.SimulationTick}\n" +
            "Ping Local • packet-free internal authority\n" +
            $"Player {_player.Chunk} + ({_player.Local.XMillimeters}, {_player.Local.ZMillimeters}) mm • " +
            $"door {(view.DoorIsOpen ? "open" : "closed")} • " +
            $"bread in {(view.BreadIsInBackpack ? "backpack" : "chest")}\n" +
            $"distance {residency.ConfiguredRenderRadiusChunks} chunks • " +
            $"authored/resident/rendered {residency.AuthoredChunks}/" +
            $"{residency.SemanticResidentChunks}/{render.ActiveRenderChunks} " +
            $"(peak {residency.PeakSemanticResidentChunks}/{_peakRenderedChunks})\n" +
            $"semantic enter/exit {residency.SemanticEnters}/{residency.SemanticExits} • " +
            $"presentation enter/exit {presentation.EnteredChunks}/{presentation.ExitedChunks} • " +
            $"resets {residency.ResetTransitions}/{presentation.ResetTransitions} • " +
            $"pending {render.PendingBuilds}/{render.PendingRetirements}";
    }

    private static SpatialChunkKey ChunkKey(int x, int z) =>
        new(GameWorldState.DefaultSpace, new SpatialChunkCoord(x, z));

    private static bool Nearest(
        in ClientControlWorldPoint point,
        in WorldPosition target,
        long radius)
    {
        Int128 dx = (Int128)point.XMillimeters - target.GlobalXMillimeters;
        Int128 dz = (Int128)point.ZMillimeters - target.GlobalZMillimeters;
        return dx * dx + dz * dz <= (Int128)radius * radius;
    }

    private void PrepareOptionalActorPresentation()
    {
        _officialPresentation = default;
        if (_primitiveOnlyPresentation || string.IsNullOrWhiteSpace(_presentationCacheRoot))
            return;
        if (!ContentCache.TryOpen(
                _presentationCacheRoot, out _presentationCache,
                out ContentCacheError cacheError))
        {
            _officialPresentation = new OfficialLocalPresentationResult(
                OfficialLocalPresentationStatus.CacheFailure, cacheError, default, default);
            return;
        }
        _ = OfficialLocalPresentation.TryOpenOrAcquire(
                _presentationCache!, _officialContentRoot,
                out ContentCatalogLease? localCatalog, out _officialPresentation);
        CabinLocalPresentationContent local =
            _contentOracle!.CreateLocalPresentation(_resolvedContent!.Variant);
        foreach (CabinLocalContentPackage package in local.Packages)
        {
            if (!_presentationCache!.TryInstallPackage(
                    ContentScopeKey.Local, package.Manifest, package.Payloads,
                    out _, out ContentCacheError installError))
                throw new InvalidOperationException(
                    $"The Cabin world.local presentation could not be installed: {installError}.");
        }
        if (!_presentationCache!.TryPublishCatalog(
                ContentBaseCatalog.Empty, local.Plan, persistentlyPinned: true,
                out ContentCatalogLease? resourceCatalog, out ContentCacheError publishError))
            throw new InvalidOperationException(
                $"The Cabin world.local presentation could not be published: {publishError}.");
        _cabinPresentationKey = local.ActivationKey;
        _derivedResources = new GodotDerivedResourceStore(
            _presentationCacheRoot,
            GodotDerivedResourcePolicy.ProductionStandalone(DerivedPlatformProfile()));
        UserResourcePackStackLease? user = OpenUserResourcePacks();
        try
        {
            _presentationGeneration = PresentationGeneration.CreateWithResources(
                user, null, localCatalog, null, resourceCatalog);
            _assetResolver = new CabinCatalogAssetResolver(
                _presentationGeneration, _derivedResources);
        }
        catch (InvalidDataException)
        {
            _assetResolver?.Dispose();
            _assetResolver = null;
            _presentationGeneration?.Dispose();
            _presentationGeneration = PresentationGeneration.CreateWithResources(
                null, null, OpenLocalPresentationCatalog(), null,
                OpenCabinResourceCatalog());
            _assetResolver = new CabinCatalogAssetResolver(
                _presentationGeneration, _derivedResources);
        }
        _presentationSelectionRevision = _presentationCache!.UserResourcePackSelectionRevision;
        _actorVisuals = new GodotContentActorVisualResolver(_presentationGeneration);
    }

    private UserResourcePackStackLease? OpenUserResourcePacks() =>
        _presentationCache is not null && _presentationCache.TryOpenEnabledUserResourcePacks(
            out UserResourcePackStackLease? lease, out _) ? lease : null;

    private ContentCatalogLease? OpenLocalPresentationCatalog() =>
        _presentationCache is not null && _presentationCache.TryOpenCatalog(
            ContentBaseCatalog.Empty, OfficialLocalPresentation.ActivationKey,
            out ContentCatalogLease? lease, out _) ? lease : null;

    private ContentCatalogLease? OpenCabinResourceCatalog() =>
        _presentationCache is not null && _cabinPresentationKey.IsValid &&
        _presentationCache.TryOpenCatalog(
            ContentBaseCatalog.Empty, _cabinPresentationKey,
            out ContentCatalogLease? lease, out _) ? lease : null;

    private static string DerivedPlatformProfile()
    {
        string platform = OS.GetName() switch
        {
            "Windows" => "windows",
            "macOS" => "macos",
            "Linux" => "linux",
            "Android" => "android",
            "iOS" => "ios",
            "Web" => "web",
            _ => "unknown",
        };
        return $"{platform}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
    }

    private void RefreshUserPresentationIfChanged()
    {
        if (_presentationCache is null || _presentationGeneration is null ||
            _actorVisuals is null || _actorPresenter is null || _derivedResources is null)
            return;
        ulong revision = _presentationCache.UserResourcePackSelectionRevision;
        if (revision == _presentationSelectionRevision) return;
        _presentationSelectionRevision = revision;
        PresentationGeneration? candidate = null;
        CabinCatalogAssetResolver? candidateResolver = null;
        try
        {
            candidate = PresentationGeneration.CreateWithResources(
                OpenUserResourcePacks(), null, OpenLocalPresentationCatalog(), null,
                OpenCabinResourceCatalog());
            if (candidate.StableFingerprint == _presentationGeneration.StableFingerprint)
            {
                candidate.Dispose();
                return;
            }
            candidateResolver = new CabinCatalogAssetResolver(candidate, _derivedResources);
            PresentationGeneration previous = _presentationGeneration;
            CabinCatalogAssetResolver? previousResolver = _assetResolver;
            _presentationGeneration = candidate;
            _assetResolver = candidateResolver;
            candidate = null;
            candidateResolver = null;
            _actorVisuals.UpdateSource(_presentationGeneration);
            if (_localSession is not null &&
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
                RebuildClientProjection(view, preserveSemanticCache: true);
            _ = _actorPresenter.RefreshResolvedVisuals();
            _actorVisuals.ReleaseRetiredResources();
            previousResolver?.Dispose();
            previous.Dispose();
        }
        catch (InvalidDataException)
        {
            // Candidate publication is atomic; retain the current native presentation.
        }
        finally
        {
            candidateResolver?.Dispose();
            candidate?.Dispose();
        }
    }

    private sealed class CabinCatalogAssetResolver : IGodotWorldAssetResolver, IDisposable
    {
        private readonly Dictionary<ushort, StandardMaterial3D> _terrain = new()
        {
            [GameTerrainMaterialIds.Water] = Material(new Color(0.08f, 0.28f, 0.5f)),
            [GameTerrainMaterialIds.Sand] = Material(new Color(0.62f, 0.55f, 0.34f)),
            [GameTerrainMaterialIds.Grass] = Material(new Color(0.16f, 0.38f, 0.14f)),
            [GameTerrainMaterialIds.Rock] = Material(new Color(0.32f, 0.34f, 0.31f)),
            [GameTerrainMaterialIds.Snow] = Material(new Color(0.78f, 0.84f, 0.82f)),
        };
        private readonly Mesh _wood = GroundedBox(
            new Vector3(0.75f, 4.5f, 0.75f), new Color(0.24f, 0.14f, 0.07f));
        private readonly Mesh _stone = GroundedBox(
            Vector3.One, new Color(0.34f, 0.36f, 0.34f));
        private readonly Mesh _wallEight;
        private readonly Mesh _wallSix;
        private readonly Mesh _wallThree;
        private readonly Mesh _roof;
        private readonly Mesh _shelf;
        private readonly Mesh _book;
        private readonly Mesh _fallbackStructure;
        private readonly Mesh _character = CharacterMesh();
        private readonly Mesh _door;
        private readonly Mesh _chest;
        private readonly Mesh _animal = GroundedBox(
            new Vector3(1.1f, 1.15f, 0.55f), new Color(0.48f, 0.35f, 0.23f));
        private readonly Mesh _telepad = GroundedBox(
            new Vector3(1.8f, 0.18f, 1.8f), new Color(0.12f, 0.72f, 0.9f));
        private readonly Mesh _farGate = GroundedBox(
            new Vector3(1.5f, 2.8f, 0.35f), new Color(0.68f, 0.22f, 0.86f));
        private readonly Mesh _informationalSign = GroundedBox(
            new Vector3(1.25f, 1.65f, 0.16f), new Color(0.52f, 0.31f, 0.12f));
        private readonly Mesh _sword = GroundedBox(
            new Vector3(0.12f, 1.15f, 0.20f), new Color(0.72f, 0.75f, 0.78f));
        private readonly Mesh _mountainWallEight = GroundedBox(
            new Vector3(8f, 7.2f, 0.4f), new Color(0.29f, 0.30f, 0.27f));
        private readonly Mesh _mountainWallTen = GroundedBox(
            new Vector3(10f, 7.2f, 0.4f), new Color(0.29f, 0.30f, 0.27f));
        private readonly Mesh _fallbackLabPedestal = GroundedBox(
            new Vector3(2f, 0.4f, 2f), new Color(0.23f, 0.24f, 0.26f));
        private readonly Mesh _fallbackLabModel = GodotMeshGeometry.CreateGroundedPointerWedge(
            0.5f,
            0.42f,
            0.72f,
            1.6f,
            Material(new Color(0.18f, 0.78f, 0.36f)));
        private readonly List<GodotDerivedMaterialLease> _materialLeases = [];
        private bool _disposed;

        public CabinCatalogAssetResolver(
            PresentationGeneration generation,
            GodotDerivedResourceStore derived)
        {
            try
            {
                GodotDerivedMaterialLease wallLease = AcquireMaterial(
                    generation, derived, CabinContentIds.WallMaterial,
                    out ContentHash wallHash, out PresentationSourceKind wallSource);
                GodotDerivedMaterialLease woodLease = AcquireMaterial(
                    generation, derived, CabinContentIds.WoodMaterial, out _, out _);
                StandardMaterial3D wall = wallLease.Material;
                StandardMaterial3D wood = woodLease.Material;
                WallTextureHash = wallHash;
                WallTextureSource = wallSource;
                WallDerivedKey = wallLease.Key;
                _wallEight = GroundedBox(new Vector3(8f, 2.5f, 0.3f), wall);
                _wallSix = GroundedBox(new Vector3(6f, 2.5f, 0.3f), wall);
                _wallThree = GroundedBox(new Vector3(3f, 2.5f, 0.3f), wall);
                _roof = GroundedBox(new Vector3(8.3f, 0.25f, 6.3f), wood, transparent: true);
                _shelf = GroundedBox(new Vector3(0.9f, 1.8f, 0.35f), wood);
                _book = GroundedBox(new Vector3(0.24f, 0.18f, 0.18f), wood);
                _fallbackStructure = GroundedBox(new Vector3(1f, 2f, 0.35f), wall);
                _door = GroundedBox(new Vector3(1.6f, 2.1f, 0.22f), wood);
                _chest = GroundedBox(new Vector3(1.2f, 0.8f, 0.8f), wood);
                if (generation.GetSource(CabinContentIds.BlueGlowMaterial) !=
                    PresentationSourceKind.Primitive)
                    AddedMaterial = AcquireMaterial(
                        generation, derived, CabinContentIds.BlueGlowMaterial,
                        out _, out _).Material;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public ContentHash WallTextureHash { get; }
        public PresentationSourceKind WallTextureSource { get; }
        public GodotDerivedResourceKey WallDerivedKey { get; }
        public StandardMaterial3D? AddedMaterial { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Mesh? mesh in new Mesh?[]
            {
                _wood, _stone, _wallEight, _wallSix, _wallThree, _roof, _shelf, _book,
                _fallbackStructure, _character, _door, _chest, _animal, _telepad, _farGate,
                _informationalSign, _sword, _mountainWallEight, _mountainWallTen,
                _fallbackLabPedestal, _fallbackLabModel,
            })
                mesh?.Dispose();
            foreach (StandardMaterial3D material in _terrain.Values) material.Dispose();
            for (int index = _materialLeases.Count - 1; index >= 0; index--)
                _materialLeases[index].Dispose();
            _materialLeases.Clear();
        }

        private GodotDerivedMaterialLease AcquireMaterial(
            PresentationGeneration generation,
            GodotDerivedResourceStore derived,
            LogicalContentId id,
            out ContentHash textureHash,
            out PresentationSourceKind textureSource)
        {
            textureHash = default;
            textureSource = PresentationSourceKind.Primitive;
            if (!generation.TryResolveMaterialPresentation(
                    id, out ResolvedMaterialPresentation? logical, out var resolutionError))
                throw new InvalidDataException(
                    $"Cabin material '{id}' is unavailable: {resolutionError}.");
            textureHash = logical!.AlbedoTexture?.File.Sha256 ?? default;
            textureSource = logical.AlbedoTexture?.Source ?? PresentationSourceKind.Primitive;
            if (!derived.TryAcquireMaterial(logical, out GodotDerivedMaterialLease? native,
                    out string? error))
            {
                logical.Dispose();
                throw new InvalidDataException(
                    $"Cabin material '{id}' could not be realized: {error}");
            }
            _materialLeases.Add(native!);
            return native!;
        }

        private GodotWorldMeshResolution ResolveFallbackLabMesh(ushort variant) => variant switch
        {
            GameWorldStaticVariants.FallbackLabCorrect =>
                GodotWorldMeshResolution.Available(_fallbackLabModel),
            GameWorldStaticVariants.FallbackLabModelMissing =>
                GodotWorldMeshResolution.MissingModel(),
            GameWorldStaticVariants.FallbackLabTextureMissing =>
                GodotWorldMeshResolution.MissingTexture(_fallbackLabModel),
            GameWorldStaticVariants.FallbackLabModelAndTextureMissing =>
                GodotWorldMeshResolution.MissingModelAndTexture(),
            _ => GodotWorldMeshResolution.Available(ResolveStaticMeshCore(
                new WorldStaticRenderKey(
                    new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Rock),
                    variant,
                    WorldStaticFlags.Renderable))),
        };

        private Mesh ResolveStaticMeshCore(WorldStaticRenderKey key) => key.DefinitionId switch
        {
            var id when id.Value == GameWorldStaticDefinitionIds.Tree => _wood,
            var id when id.Value == GameWorldStaticDefinitionIds.Rock => key.Variant switch
            {
                GameWorldStaticVariants.MountainWallEightMeters => _mountainWallEight,
                GameWorldStaticVariants.MountainWallTenMeters => _mountainWallTen,
                GameWorldStaticVariants.FallbackLabPedestal => _fallbackLabPedestal,
                GameWorldStaticVariants.FallbackLabCorrect or
                    GameWorldStaticVariants.FallbackLabModelMissing or
                    GameWorldStaticVariants.FallbackLabTextureMissing or
                    GameWorldStaticVariants.FallbackLabModelAndTextureMissing => _fallbackLabModel,
                _ => _stone,
            },
            var id when id.Value == GameWorldStaticDefinitionIds.CabinWall => key.Variant switch
            {
                GameWorldStaticVariants.CabinWallEightMeters => _wallEight,
                GameWorldStaticVariants.CabinWallSixMeters => _wallSix,
                GameWorldStaticVariants.CabinWallThreeMeters => _wallThree,
                _ => _fallbackStructure,
            },
            var id when id.Value == GameWorldStaticDefinitionIds.CabinRoof => _roof,
            var id when id.Value == GameWorldStaticDefinitionIds.Shelf => _shelf,
            var id when id.Value == GameWorldStaticDefinitionIds.Book => _book,
            _ => _fallbackStructure,
        };

        GodotWorldMeshResolution IGodotWorldAssetResolver.ResolveStaticMesh(
            WorldStaticRenderKey key) =>
            key.DefinitionId.Value == GameWorldStaticDefinitionIds.Rock &&
            key.Variant is >= GameWorldStaticVariants.FallbackLabCorrect and
                <= GameWorldStaticVariants.FallbackLabModelAndTextureMissing
                ? ResolveFallbackLabMesh(key.Variant)
                : GodotWorldMeshResolution.Available(ResolveStaticMeshCore(key));

        public GodotWorldMeshResolution ResolvePublicMesh(uint prefabId) =>
            GodotWorldMeshResolution.Available(ResolvePublicMeshCore(prefabId));

        private Mesh ResolvePublicMeshCore(uint prefabId) => prefabId switch
        {
            GameWorldPublicPrefabIds.Character or GameWorldPublicPrefabIds.CharacterMale => _character,
            GameWorldPublicPrefabIds.CabinDoor => _door,
            GameWorldPublicPrefabIds.WorldChest => _chest,
            GameWorldPublicPrefabIds.WoodlandAnimal => _animal,
            GameWorldPublicPrefabIds.SkyTelepad => _telepad,
            GameWorldPublicPrefabIds.FarGate => _farGate,
            GameWorldPublicPrefabIds.InformationalSign => _informationalSign,
            GameWorldPublicPrefabIds.IronSword => _sword,
            _ => _stone,
        };

        public Material? ResolveTerrainMaterial(ushort materialId) =>
            _terrain.TryGetValue(materialId, out StandardMaterial3D? material) ? material : null;

        private static StandardMaterial3D Material(Color color) => new()
        {
            AlbedoColor = color,
            Roughness = 0.95f,
        };

        private static ArrayMesh GroundedBox(Vector3 size, Color color, bool transparent = false)
        {
            StandardMaterial3D material = Material(color);
            if (transparent)
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            return GodotMeshGeometry.CreateGroundedBox(size, material);
        }

        private static ArrayMesh GroundedBox(
            Vector3 size,
            StandardMaterial3D material,
            bool transparent = false)
        {
            if (transparent)
            {
                material = (StandardMaterial3D)material.Duplicate();
                material.AlbedoColor = new Color(material.AlbedoColor, 0.42f);
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            }
            return GodotMeshGeometry.CreateGroundedBox(size, material);
        }

        private static ArrayMesh CharacterMesh()
        {
            StandardMaterial3D material = Material(new Color(0, 0, 0, 0));
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            return GodotMeshGeometry.CreateGroundedPointerWedge(
                0.42f, 0.42f, 0.68f, 1.8f, material);
        }

    }
}

public readonly record struct LocalChatInputSnapshot(
    bool HasFocus,
    string Draft,
    bool GameplayInputAllowed,
    ulong AppliedOccurrences);
