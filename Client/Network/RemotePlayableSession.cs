using UltraSim.Client.Core;
using UltraSim.Client.Content;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Client.Network;

public sealed class RemoteRequiredWorldBinding
{
    public RemoteRequiredWorldBinding(
        LogicalContentId definitionId,
        WorldPackFingerprint expectedPackFingerprint,
        WorldContentBinding contentBinding,
        ClientWorldSemanticCacheLimits semanticLimits = default)
    {
        if (!definitionId.IsValid) throw new ArgumentOutOfRangeException(nameof(definitionId));
        if (!expectedPackFingerprint.IsValid)
            throw new ArgumentOutOfRangeException(nameof(expectedPackFingerprint));
        DefinitionId = definitionId;
        ExpectedPackFingerprint = expectedPackFingerprint;
        ContentBinding = contentBinding ?? throw new ArgumentNullException(nameof(contentBinding));
        SemanticLimits = semanticLimits == default
            ? ClientWorldSemanticCacheLimits.Default
            : semanticLimits;
    }

    public LogicalContentId DefinitionId { get; }
    public WorldPackFingerprint ExpectedPackFingerprint { get; }
    public WorldContentBinding ContentBinding { get; }
    public ClientWorldSemanticCacheLimits SemanticLimits { get; }
}

/// <summary>
/// Compatible-client structural profile for the current Concept Demo deployment. This is stable
/// vocabulary/fingerprint data only; the Online product still contains no WorldPack bytes.
/// </summary>
public static class RemoteConceptDemoWorldProfile
{
    public static RemoteRequiredWorldBinding CreateRequiredBinding(
        ClientWorldSemanticCacheLimits semanticLimits = default)
    {
        if (!LogicalContentId.TryCreate(
                GameWorldContentProfile.ConceptDemoDefinitionId, out LogicalContentId definition) ||
            !WorldPackFingerprint.TryParse(
                GameWorldContentProfile.ConceptDemoPackFingerprint, out WorldPackFingerprint pack))
            throw new InvalidOperationException("The compiled Concept Demo world profile is invalid.");
        var content = new WorldContentBinding(
            new WorldContentFingerprint(
                GameWorldContentProfile.ContentFingerprintLow,
                GameWorldContentProfile.ContentFingerprintHigh),
            GameWorldContentProfile.TerrainMaterialIds.ToArray(),
            GameWorldContentProfile.StaticDefinitionIds.ToArray());
        return new RemoteRequiredWorldBinding(
            definition,
            pack,
            content,
            semanticLimits == default
                ? new ClientWorldSemanticCacheLimits(192, 256, 32, 64, 4_096)
                : semanticLimits);
    }
}

public enum RemoteStructuralWorldState : byte
{
    None = 0,
    Preinstalled = 1,
    AwaitingAppliedCatalog = 2,
    Ready = 3,
    Rejected = 4,
}

public readonly record struct RemoteStructuralWorldSnapshot(
    RemoteStructuralWorldState State,
    ContentWorldPackResolutionResult Resolution,
    ClientSemanticChunkError PublicationError,
    int PublishedStructuralChunks,
    WorldPackFingerprint WorldPackFingerprint,
    WorldContentFingerprint WorldContentFingerprint);

public readonly record struct RemotePlayableSessionSnapshot(
    bool SemanticConsumersActive,
    bool RequiredContentApplied,
    ulong MessagesProcessedBeforeAdmission,
    ulong RejectedPreAdmissionMessages,
    RemoteContentClientSnapshot Content,
    RemoteServerPerformanceClientSnapshot Performance,
    RemotePlayableClientSnapshot Playable,
    RemoteChunkWorldStateClientSnapshot World,
    RemoteChatClientSnapshot Chat,
    RemotePublicPresentationMetadataClientSnapshot PublicPresentation,
    RemoteUiClientSnapshot Ui,
    RemoteStructuralWorldSnapshot StructuralWorld,
    RemotePlayableSessionTimingSnapshot Timing)
{
    public RemotePresentationClientSnapshot Presentation { get; init; }
}

/// <summary>
/// One bounded engine-neutral remote client composition over an already authenticated Direct
/// Connect peer and either a verified preinstalled structural World view or one exact WorldPack
/// selected by the applied realm catalog.
/// </summary>
/// <remarks>
/// Content supplies authorized bytes, World opens/binds them, and a private semantic cache becomes
/// visible only after every initial structural chunk publishes. Guest admission remains unavailable
/// until that composition succeeds. This does not define overlay or independent chunk artifacts.
/// </remarks>
public sealed class RemotePlayableSession : IDisposable
{
    private readonly NetworkProtocolPeer _peer;
    private readonly RemoteGameProtocolComposition _protocol;
    private readonly RemoteUiClientBridge _ui;
    private readonly RemoteContentClient? _content;
    private readonly RemotePresentationClient? _presentation;
    private readonly RemoteRequiredWorldBinding? _requiredWorld;
    private readonly ulong _applicationSessionEpoch;
    private readonly int _maximumPendingWorldProofs;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ClientWorldSemanticCache? _worldCache;
    private RemoteChunkWorldStateClient? _world;
    private RemoteStructuralWorldSnapshot _structuralWorld;
    private bool _semanticConsumersActive;
    private ulong _messagesBeforeAdmission;
    private ulong _rejectedBeforeAdmission;
    private bool _disposed;
    private NetworkTelemetryLevel _telemetryLevel;
    private readonly ClientTimingWindow _processReceivedTiming = new();

    public RemotePlayableSession(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        ClientWorldSemanticCache worldCache,
        IUiRenderer uiRenderer,
        ClientChatLimits chatLimits = default,
        UiClientLimits uiLimits = default,
        int maximumPendingWorldProofs = 256,
        RemoteRequiredContentBinding? requiredContent = null,
        Func<UiDefinitionCacheKey, CompiledUiDefinition?>? standardUiDefinitionResolver = null)
        : this(peer, applicationSessionEpoch, worldCache, requiredContent, null, uiRenderer,
            chatLimits, uiLimits, maximumPendingWorldProofs, standardUiDefinitionResolver)
    {
    }

    public RemotePlayableSession(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        RemoteRequiredContentBinding requiredContent,
        RemoteRequiredWorldBinding requiredWorld,
        IUiRenderer uiRenderer,
        ClientChatLimits chatLimits = default,
        UiClientLimits uiLimits = default,
        int maximumPendingWorldProofs = 256,
        Func<UiDefinitionCacheKey, CompiledUiDefinition?>? standardUiDefinitionResolver = null)
        : this(peer, applicationSessionEpoch, null, requiredContent, requiredWorld, uiRenderer,
            chatLimits, uiLimits, maximumPendingWorldProofs, standardUiDefinitionResolver)
    {
    }

    private RemotePlayableSession(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        ClientWorldSemanticCache? worldCache,
        RemoteRequiredContentBinding? requiredContent,
        RemoteRequiredWorldBinding? requiredWorld,
        IUiRenderer uiRenderer,
        ClientChatLimits chatLimits,
        UiClientLimits uiLimits,
        int maximumPendingWorldProofs,
        Func<UiDefinitionCacheKey, CompiledUiDefinition?>? standardUiDefinitionResolver)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        ArgumentNullException.ThrowIfNull(uiRenderer);
        if ((worldCache is null) == (requiredWorld is null))
            throw new ArgumentException(
                "Exactly one preinstalled cache or required WorldPack binding must be supplied.");
        if (requiredWorld is not null && requiredContent is null)
            throw new ArgumentException(
                "A required WorldPack needs an authenticated content binding.", nameof(requiredWorld));
        if (maximumPendingWorldProofs <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingWorldProofs));
        if (chatLimits == default) chatLimits = ClientChatLimits.Default;
        if (uiLimits == default) uiLimits = UiClientLimits.Default;

        _applicationSessionEpoch = applicationSessionEpoch;
        _maximumPendingWorldProofs = maximumPendingWorldProofs;
        _requiredWorld = requiredWorld;
        Performance = new RemoteServerPerformanceClient(peer, applicationSessionEpoch);
        Playable = new RemotePlayableClient(peer, applicationSessionEpoch);
        if (requiredContent is not null)
        {
            _content = new RemoteContentClient(
                peer, applicationSessionEpoch, requiredContent.Realm,
                requiredContent.Cache, requiredContent.BaseCatalog, requiredContent.Source);
            _presentation = new RemotePresentationClient(
                applicationSessionEpoch, requiredContent.Realm, requiredContent.Cache,
                requiredContent.Source, _content, requiredContent.LocalPresentationKey);
        }
        ChatState = new ClientChatState(applicationSessionEpoch, chatLimits);
        Chat = new RemoteChatClient(peer, applicationSessionEpoch, ChatState);
        UiClient = new UiClientRuntime(uiRenderer, uiLimits);
        _ui = new RemoteUiClientBridge(
            peer,
            UiClient,
            standardDefinitionResolver: standardUiDefinitionResolver,
            contentDefinitionResolver: _content is null
                ? null
                : key => _content.TryResolveUiDefinition(
                    key, out CompiledUiDefinition? definition, out _) ? definition : null);
        PublicPresentationState = new ClientPublicPresentationMetadataState(256);
        PublicPresentation = new RemotePublicPresentationMetadataClient(
            peer, applicationSessionEpoch, PublicPresentationState);
        _protocol = new RemoteGameProtocolComposition(7);
        _protocol.Add(Performance);
        if (_content is not null) _protocol.Add(_content);
        if (_presentation is not null) _protocol.Add(_presentation);
        _protocol.Add(Playable);
        _protocol.Add(PublicPresentation);
        if (worldCache is not null)
        {
            ActivateWorld(worldCache);
            _structuralWorld = new RemoteStructuralWorldSnapshot(
                RemoteStructuralWorldState.Preinstalled, default,
                ClientSemanticChunkError.None,
                worldCache.GetSnapshot().ResidentChunks,
                worldCache.BaseWorld.Fingerprint,
                worldCache.BaseWorld.Content.Fingerprint);
        }
        else
        {
            _structuralWorld = new RemoteStructuralWorldSnapshot(
                RemoteStructuralWorldState.AwaitingAppliedCatalog,
                default, ClientSemanticChunkError.None, 0, default, default);
        }
        _protocol.Add(Chat);
        _ui.BindProtocolHandler(_protocol);
    }

    public RemotePlayableClient Playable { get; }
    public RemoteServerPerformanceClient Performance { get; }
    public RemoteChunkWorldStateClient World
    {
        get
        {
            EnsureOperational();
            return _world ?? throw new InvalidOperationException(
                "The required structural WorldPack is not published.");
        }
    }
    public ClientWorldSemanticCache WorldCache
    {
        get
        {
            EnsureOperational();
            return _worldCache ?? throw new InvalidOperationException(
                "The required structural WorldPack is not published.");
        }
    }
    public ClientChatState ChatState { get; }
    public RemoteChatClient Chat { get; }
    public ClientPublicPresentationMetadataState PublicPresentationState { get; }
    public RemotePublicPresentationMetadataClient PublicPresentation { get; }
    public UiClientRuntime UiClient { get; }
    public RemoteContentClient? Content => _content;
    public RemotePresentationClient? Presentation => _presentation;

    public bool TrySubmitGuest(in GameGuestAdmissionRequest request)
    {
        EnsureOperational();
        if (_content is not null && !_content.Applied) return false;
        if (!TryPublishRequiredWorld()) return false;
        return Playable.TrySubmitGuest(in request);
    }

    /// <summary>Applies every currently delivered message and advances retained application proof.</summary>
    public int ProcessReceived()
    {
        EnsureOperational();
        long timing = _telemetryLevel == NetworkTelemetryLevel.Detailed
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0;
        try { return ProcessReceivedCore(); }
        finally
        {
            if (_telemetryLevel == NetworkTelemetryLevel.Detailed)
                _processReceivedTiming.RecordElapsed(timing);
        }
    }

    private int ProcessReceivedCore()
    {
        if (_semanticConsumersActive)
            return _ui.ProcessReceived();

        int processed = 0;
        while (_peer.TryPeekReceived(
                   out NetworkDeliveredMessage message,
                   out ReadOnlySpan<byte> payload))
        {
            bool claimed;
            try
            {
                claimed = Performance.TryProcess(in message, payload) ||
                    _content?.TryProcess(in message, payload) == true ||
                    _presentation?.TryProcess(in message, payload) == true ||
                    Playable.TryProcess(in message, payload) ||
                    PublicPresentation.TryProcess(in message, payload);
            }
            finally { _ = _peer.TryConsumeReceived(); }
            processed++;
            SaturatingIncrement(ref _messagesBeforeAdmission);
            if (!claimed) SaturatingIncrement(ref _rejectedBeforeAdmission);

            // Admission and the first retained world deliveries can legitimately share one
            // reliable receive batch. Activate immediately after applying the admission result;
            // otherwise the pre-admission loop would consume those already-authorized semantic
            // messages as unclaimed before the normal protocol composition can see them.
            if (Playable.GetSnapshot().State == RemotePlayableClientState.Active)
            {
                ActivateSemanticConsumers();
                processed += _ui.ProcessReceived();
                break;
            }
        }
        _ = Performance.TryProgress();
        _ = _content?.TryProgress();
        _ = _presentation?.TryProgress();
        _ = TryPublishRequiredWorld();
        _ = Playable.TryProgress();
        if (Playable.GetSnapshot().State == RemotePlayableClientState.Active)
            ActivateSemanticConsumers();
        return processed;
    }

    public RemotePlayableSessionSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new RemotePlayableSessionSnapshot(
            _semanticConsumersActive, _content is null || _content.Applied,
            _messagesBeforeAdmission,
            _rejectedBeforeAdmission,
            _content?.GetSnapshot() ?? default,
            Performance.GetSnapshot(),
            Playable.GetSnapshot(),
            _world?.GetSnapshot() ?? default,
            Chat.GetSnapshot(),
            PublicPresentation.GetSnapshot(),
            _ui.GetSnapshot(),
            _structuralWorld,
            GetTimingSnapshot())
        {
            Presentation = _presentation?.GetSnapshot() ?? default,
        };
    }

    public void ConfigureTelemetry(NetworkTelemetryLevel level)
    {
        EnsureOperational();
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        _telemetryLevel = level;
        _processReceivedTiming.Clear();
        Playable.ConfigureTelemetry(level);
        _world?.ConfigureTelemetry(level);
        PublicPresentation.ConfigureTelemetry(level);
        Chat.ConfigureTelemetry(level);
        _ui.ConfigureTelemetry(level);
    }

    public RemotePlayableSessionTimingSnapshot GetTimingSnapshot()
    {
        EnsureOperational();
        return new(_telemetryLevel, _processReceivedTiming.Capture(),
            Playable.GetTimingSnapshot(), _world?.GetTimingSnapshot() ?? default,
            PublicPresentation.GetTimingSnapshot(), Chat.GetTimingSnapshot(),
            _ui.GetTimingSnapshot());
    }

    /// <summary>
    /// Retains one authenticated application detach before the transport begins draining. This
    /// retires only this participant/control lineage; it never owns the shared Game world.
    /// </summary>
    public bool TryRequestDetach()
    {
        EnsureOperational();
        var detach = new RemoteSessionDetachMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
        };
        Span<byte> payload = stackalloc byte[RemoteSessionDetachMessageCodec.EncodedSize];
        return RemoteSessionDetachMessageCodec.TryWriteExact(payload, in detach) &&
            _peer.TryQueue(RemoteSessionDetachMessageCodec.Descriptor.Id, payload) ==
                NetworkQueueStatus.Accepted;
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        _ui.Dispose();
        _presentation?.Dispose();
        _content?.Dispose();
    }

    private void ActivateSemanticConsumers()
    {
        if (_semanticConsumersActive) return;
        RemotePlayableClientSnapshot admitted = Playable.GetSnapshot();
        if (!_ui.TryActivateAdmittedSession(
                admitted.Participant, admitted.ApplicationSessionEpoch))
            throw new InvalidOperationException(
                "The admitted remote UI semantic consumer could not activate.");
        _semanticConsumersActive = true;
    }

    private bool TryPublishRequiredWorld()
    {
        if (_requiredWorld is null || _world is not null) return true;
        if (_structuralWorld.State == RemoteStructuralWorldState.Rejected) return false;
        if (_content is null || !_content.Applied) return false;
        if (!_content.TryResolveWorldPack(
                _requiredWorld.DefinitionId,
                _requiredWorld.ExpectedPackFingerprint,
                _requiredWorld.ContentBinding,
                out BoundWorldPack? bound,
                out ContentWorldPackResolutionResult resolution))
        {
            _structuralWorld = new RemoteStructuralWorldSnapshot(
                RemoteStructuralWorldState.Rejected, resolution,
                ClientSemanticChunkError.None, 0, default, default);
            return false;
        }
        if (!TryCreateInitialStructuralCache(
                bound!, _requiredWorld.SemanticLimits,
                out ClientWorldSemanticCache? cache, out int published,
                out ClientSemanticChunkError publicationError))
        {
            _structuralWorld = new RemoteStructuralWorldSnapshot(
                RemoteStructuralWorldState.Rejected, resolution, publicationError, 0,
                bound!.Fingerprint, bound.Content.Fingerprint);
            return false;
        }
        ActivateWorld(cache!);
        _structuralWorld = new RemoteStructuralWorldSnapshot(
            RemoteStructuralWorldState.Ready, resolution, ClientSemanticChunkError.None, published,
            bound!.Fingerprint, bound.Content.Fingerprint);
        return true;
    }

    private void ActivateWorld(ClientWorldSemanticCache cache)
    {
        if (_world is not null) throw new InvalidOperationException("The remote World is already active.");
        _worldCache = cache;
        _world = new RemoteChunkWorldStateClient(
            _peer, cache, _applicationSessionEpoch,
            cache.Limits.MaximumPublicRecordsPerChunk,
            _maximumPendingWorldProofs);
        _world.ConfigureTelemetry(_telemetryLevel);
        _protocol.Add(_world);
    }

    private static bool TryCreateInitialStructuralCache(
        BoundWorldPack world,
        ClientWorldSemanticCacheLimits limits,
        out ClientWorldSemanticCache? cache,
        out int published,
        out ClientSemanticChunkError error)
    {
        cache = null;
        published = 0;
        error = ClientSemanticChunkError.None;
        if (world.ChunkCount > limits.MaximumResidentChunks)
        {
            error = ClientSemanticChunkError.CapacityExceeded;
            return false;
        }
        var candidate = new ClientWorldSemanticCache(world, limits);
        for (int index = 0; index < world.ChunkCount; index++)
        {
            if (!world.TryGetChunkKey(index, out var key))
            {
                error = ClientSemanticChunkError.InvalidKey;
                return false;
            }
            if (!candidate.TryRequest(key, out ClientSemanticChunkHandle handle, out error) ||
                !candidate.TryStageFromBaseWorld(handle, [], default, out error) ||
                !candidate.TryPublish(handle, out error))
                return false;
            published++;
        }
        cache = candidate;
        return true;
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Remote playable session is owner-thread-bound.");
    }
}
