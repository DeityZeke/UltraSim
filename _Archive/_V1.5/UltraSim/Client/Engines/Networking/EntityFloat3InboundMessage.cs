using System;
using UltraSim.Network;
using UltraSim.Network.Protocol;
using UltraSim.UI.Contracts;

namespace UltraSim.Client.Engines.Networking
{
    public enum EntityFloat3InboundMessageKind : byte
    {
        Unknown = 0,
        Correction = 1,
        Despawn = 2,
        Chat = 3,
        ProbePong = 4,
        UiPanelOpen = 5,
        UiPanelUpdate = 6,
        UiPanelClose = 7,
        UiPanelDefinition = 8,
        EnterGame = 9,
        LoginResponse = 10,
        Nameplate = 11,
    }

    /// <summary>Payload for a <see cref="EntityFloat3InboundMessageKind.LoginResponse"/> message.</summary>
    public readonly struct LoginResponsePayload
    {
        public LoginResponsePayload(
            UiPanelInstanceId instanceId,
            LoginPacketCodec.LoginResponseResult result,
            ReadOnlyMemory<byte> newChallenge,
            string detail)
        {
            InstanceId = instanceId;
            Result = result;
            NewChallenge = newChallenge;
            Detail = detail;
        }

        public UiPanelInstanceId InstanceId { get; }
        public LoginPacketCodec.LoginResponseResult Result { get; }
        public ReadOnlyMemory<byte> NewChallenge { get; }
        public string Detail { get; }
    }

    public readonly struct EntityFloat3InboundMessage
    {
        private EntityFloat3InboundMessage(
            EntityFloat3InboundMessageKind kind,
            in ClientInboundCorrection<EntityFloat3Correction> correction,
            NetworkEntityId despawnEntityId,
            in ServerChatMessage chat,
            in ProbePongMessage probePong,
            in UiPanelOpenCommand uiPanelOpen,
            in UiPanelUpdateCommand uiPanelUpdate,
            in UiPanelCloseCommand uiPanelClose,
            in UiPanelDefinition uiPanelDefinition,
            string? enterGameDetail,
            in LoginResponsePayload loginResponse,
            in ServerNameplateMessage nameplate)
        {
            Kind = kind;
            Correction = correction;
            DespawnEntityId = despawnEntityId;
            Chat = chat;
            ProbePong = probePong;
            UiPanelOpen = uiPanelOpen;
            UiPanelUpdate = uiPanelUpdate;
            UiPanelClose = uiPanelClose;
            UiPanelDefinition = uiPanelDefinition;
            EnterGameDetail = enterGameDetail;
            LoginResponse = loginResponse;
            Nameplate = nameplate;
        }

        public EntityFloat3InboundMessageKind Kind { get; }
        public ClientInboundCorrection<EntityFloat3Correction> Correction { get; }
        public NetworkEntityId DespawnEntityId { get; }
        public ServerChatMessage Chat { get; }
        public ProbePongMessage ProbePong { get; }
        public UiPanelOpenCommand UiPanelOpen { get; }
        public UiPanelUpdateCommand UiPanelUpdate { get; }
        public UiPanelCloseCommand UiPanelClose { get; }
        public UiPanelDefinition UiPanelDefinition { get; }
        public string? EnterGameDetail { get; }
        public LoginResponsePayload LoginResponse { get; }
        public ServerNameplateMessage Nameplate { get; }

        public static EntityFloat3InboundMessage ForCorrection(in ClientInboundCorrection<EntityFloat3Correction> correction) =>
            new(EntityFloat3InboundMessageKind.Correction, in correction, default, default, default, default, default, default, default, null, default, default);

        public static EntityFloat3InboundMessage ForDespawn(NetworkEntityId entityId) =>
            new(EntityFloat3InboundMessageKind.Despawn, default, entityId, default, default, default, default, default, default, null, default, default);

        public static EntityFloat3InboundMessage ForChat(in ServerChatMessage chat) =>
            new(EntityFloat3InboundMessageKind.Chat, default, default, in chat, default, default, default, default, default, null, default, default);

        public static EntityFloat3InboundMessage ForProbePong(in ProbePongMessage probePong) =>
            new(EntityFloat3InboundMessageKind.ProbePong, default, default, default, in probePong, default, default, default, default, null, default, default);

        public static EntityFloat3InboundMessage ForUiPanelOpen(in UiPanelOpenCommand command) =>
            new(EntityFloat3InboundMessageKind.UiPanelOpen, default, default, default, default, in command, default, default, default, null, default, default);

        public static EntityFloat3InboundMessage ForUiPanelUpdate(in UiPanelUpdateCommand command) =>
            new(EntityFloat3InboundMessageKind.UiPanelUpdate, default, default, default, default, default, in command, default, default, null, default, default);

        public static EntityFloat3InboundMessage ForUiPanelClose(in UiPanelCloseCommand command) =>
            new(EntityFloat3InboundMessageKind.UiPanelClose, default, default, default, default, default, default, in command, default, null, default, default);

        public static EntityFloat3InboundMessage ForUiPanelDefinition(in UiPanelDefinition definition) =>
            new(EntityFloat3InboundMessageKind.UiPanelDefinition, default, default, default, default, default, default, default, in definition, null, default, default);

        public static EntityFloat3InboundMessage ForEnterGame(string detail) =>
            new(EntityFloat3InboundMessageKind.EnterGame, default, default, default, default, default, default, default, default, detail, default, default);

        public static EntityFloat3InboundMessage ForLoginResponse(in LoginResponsePayload loginResponse) =>
            new(EntityFloat3InboundMessageKind.LoginResponse, default, default, default, default, default, default, default, default, null, in loginResponse, default);

        public static EntityFloat3InboundMessage ForNameplate(in ServerNameplateMessage nameplate) =>
            new(EntityFloat3InboundMessageKind.Nameplate, default, default, default, default, default, default, default, default, null, default, in nameplate);
    }
}

