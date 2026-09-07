using UltraSim.Client.Core;
using UltraSim.Client.Content;
using UltraSim.Game.Admission;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Client.Network;

public readonly record struct RemotePlayableSessionSnapshot(
    bool SemanticConsumersActive,
    bool RequiredContentApplied,
    ulong MessagesProcessedBeforeAdmission,
    ulong RejectedPreAdmissionMessages,
    RemoteContentClientSnapshot Content,
    RemotePlayableClientSnapshot Playable,
    RemoteChunkWorldStateClientSnapshot World,
    RemoteChatClientSnapshot Chat,
    RemoteUiClientSnapshot Ui);

/// <summary>
/// One bounded engine-neutral F0.3b client composition over an already authenticated Direct
/// Connect peer and an already verified/preinstalled structural World view.
/// </summary>
/// <remarks>
/// Structural acquisition is deliberately not owned here. The caller supplies the semantic cache
/// only after binding the exact World artifact it is entitled to use. Admission proof opens the
/// public/Chat/UI consumers; it does not claim that downloaded or required content was applied.
/// </remarks>
public sealed class RemotePlayableSession : IDisposable
{
    private readonly NetworkProtocolPeer _peer;
    private readonly RemoteGameProtocolComposition _protocol;
    private readonly RemoteUiClientBridge _ui;
    private readonly RemoteContentClient? _content;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _semanticConsumersActive;
    private ulong _messagesBeforeAdmission;
    private ulong _rejectedBeforeAdmission;
    private bool _disposed;

    public RemotePlayableSession(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        ClientWorldSemanticCache worldCache,
        IUiRenderer uiRenderer,
        ClientChatLimits chatLimits = default,
        UiClientLimits uiLimits = default,
        int maximumPendingWorldProofs = 256,
        RemoteRequiredContentBinding? requiredContent = null)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        ArgumentNullException.ThrowIfNull(worldCache);
        ArgumentNullException.ThrowIfNull(uiRenderer);
        if (maximumPendingWorldProofs <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingWorldProofs));
        if (chatLimits == default) chatLimits = ClientChatLimits.Default;
        if (uiLimits == default) uiLimits = UiClientLimits.Default;

        Playable = new RemotePlayableClient(peer, applicationSessionEpoch);
        if (requiredContent is not null)
            _content = new RemoteContentClient(
                peer, applicationSessionEpoch, requiredContent.Realm,
                requiredContent.Cache, requiredContent.BaseCatalog, requiredContent.Source);
        World = new RemoteChunkWorldStateClient(
            peer, worldCache, applicationSessionEpoch,
            worldCache.Limits.MaximumPublicRecordsPerChunk,
            maximumPendingWorldProofs);
        ChatState = new ClientChatState(applicationSessionEpoch, chatLimits);
        Chat = new RemoteChatClient(peer, applicationSessionEpoch, ChatState);
        UiClient = new UiClientRuntime(uiRenderer, uiLimits);
        _ui = new RemoteUiClientBridge(
            peer,
            UiClient,
            contentDefinitionResolver: _content is null
                ? null
                : key => _content.TryResolveUiDefinition(
                    key, out CompiledUiDefinition? definition, out _) ? definition : null);
        _protocol = new RemoteGameProtocolComposition(5);
        if (_content is not null) _protocol.Add(_content);
        _protocol.Add(Playable);
        _protocol.Add(World);
        _protocol.Add(Chat);
        _ui.BindProtocolHandler(_protocol);
    }

    public RemotePlayableClient Playable { get; }
    public RemoteChunkWorldStateClient World { get; }
    public ClientChatState ChatState { get; }
    public RemoteChatClient Chat { get; }
    public UiClientRuntime UiClient { get; }
    public RemoteContentClient? Content => _content;

    public bool TrySubmitGuest(in GameGuestAdmissionRequest request)
    {
        EnsureOperational();
        if (_content is not null && !_content.Applied) return false;
        return Playable.TrySubmitGuest(in request);
    }

    /// <summary>Applies every currently delivered message and advances retained application proof.</summary>
    public int ProcessReceived()
    {
        EnsureOperational();
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
                claimed = _content?.TryProcess(in message, payload) == true ||
                    Playable.TryProcess(in message, payload);
            }
            finally { _ = _peer.TryConsumeReceived(); }
            processed++;
            SaturatingIncrement(ref _messagesBeforeAdmission);
            if (!claimed) SaturatingIncrement(ref _rejectedBeforeAdmission);
        }
        _ = _content?.TryProgress();
        _ = Playable.TryProgress();
        if (Playable.GetSnapshot().State == RemotePlayableClientState.Active)
        {
            RemotePlayableClientSnapshot admitted = Playable.GetSnapshot();
            if (!_ui.TryActivateAdmittedSession(
                    admitted.Participant, admitted.ApplicationSessionEpoch))
                throw new InvalidOperationException(
                    "The admitted remote UI semantic consumer could not activate.");
            _semanticConsumersActive = true;
        }
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
            Playable.GetSnapshot(),
            World.GetSnapshot(),
            Chat.GetSnapshot(),
            _ui.GetSnapshot());
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        _ui.Dispose();
        _content?.Dispose();
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
