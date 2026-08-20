using System;
using UltraSim.Network;
using UltraSim.Server.Engines.Accounting;
using UltraSim.UI.Contracts;

namespace UltraSim.Server
{
    public readonly record struct ServerRuntimeStateChangedEvent(
        ServerRuntimeState PreviousState,
        ServerRuntimeState CurrentState);

    public readonly record struct ServerSessionAcceptedEvent(
        long SessionId,
        string RemoteIp);

    public readonly record struct ServerAccountVerifiedEvent(
        long SessionId,
        string RemoteIp,
        string Username,
        AccountModel Account,
        bool IsNewAccount);

    public readonly record struct ServerCharacterSelectionEvent(
        long SessionId,
        AccountModel Account,
        ulong CharacterId,
        string CharacterName);

    public readonly record struct ServerCharacterCreateRequestedEvent(
        long SessionId,
        AccountModel Account);

    public readonly record struct ServerCharacterCreatedEvent(
        long SessionId,
        AccountModel Account,
        string CharacterName,
        ushort ArchetypeId);

    public sealed class ServerEventHub
    {
        public event Action<ServerRuntimeStateChangedEvent>? StateChanged;
        public event Action<ServerRuntimeFault>? Faulted;
        public event Action<long, UiPanelAck>? PanelAckReceived;
        public event Action<long, UiPanelResponse>? PanelResponseReceived;
        public event Action<long>? ClientConnected;
        public event Action<long, SessionDisconnectReason>? ClientDisconnected;
        public event Action<ServerSessionAcceptedEvent>? SessionAccepted;
        public event Action<ServerAccountVerifiedEvent>? AccountVerified;
        public event Action<ServerCharacterSelectionEvent>? CharacterSelected;
        public event Action<ServerCharacterCreateRequestedEvent>? CharacterCreateRequested;
        public event Action<ServerCharacterCreatedEvent>? CharacterCreated;
        public event Action<long>? ReturnToCharacterSelectRequested;
        public event Action<long>? SessionReady;

        internal void RaiseStateChanged(ServerRuntimeState previousState, ServerRuntimeState currentState) =>
            StateChanged?.Invoke(new ServerRuntimeStateChangedEvent(previousState, currentState));

        internal void RaiseFaulted(ServerRuntimeFault fault) =>
            Faulted?.Invoke(fault);

        internal void RaisePanelAckReceived(long sessionId, UiPanelAck ack) =>
            PanelAckReceived?.Invoke(sessionId, ack);

        internal void RaisePanelResponseReceived(long sessionId, UiPanelResponse response) =>
            PanelResponseReceived?.Invoke(sessionId, response);

        public void PublishClientConnected(long sessionId) =>
            ClientConnected?.Invoke(sessionId);

        public void PublishClientDisconnected(long sessionId, SessionDisconnectReason reason) =>
            ClientDisconnected?.Invoke(sessionId, reason);

        public void PublishSessionAccepted(long sessionId, string remoteIp) =>
            SessionAccepted?.Invoke(new ServerSessionAcceptedEvent(sessionId, remoteIp));

        public void PublishAccountVerified(long sessionId, string remoteIp, string username, AccountModel account, bool isNewAccount) =>
            AccountVerified?.Invoke(new ServerAccountVerifiedEvent(sessionId, remoteIp, username, account, isNewAccount));

        public void PublishCharacterSelected(long sessionId, AccountModel account, ulong characterId, string characterName) =>
            CharacterSelected?.Invoke(new ServerCharacterSelectionEvent(sessionId, account, characterId, characterName));

        public void PublishCharacterCreateRequested(long sessionId, AccountModel account) =>
            CharacterCreateRequested?.Invoke(new ServerCharacterCreateRequestedEvent(sessionId, account));

        public void PublishCharacterCreated(long sessionId, AccountModel account, string characterName, ushort archetypeId) =>
            CharacterCreated?.Invoke(new ServerCharacterCreatedEvent(sessionId, account, characterName, archetypeId));

        public void PublishReturnToCharacterSelectRequested(long sessionId) =>
            ReturnToCharacterSelectRequested?.Invoke(sessionId);

        public void PublishSessionReady(long sessionId) =>
            SessionReady?.Invoke(sessionId);
    }
}
