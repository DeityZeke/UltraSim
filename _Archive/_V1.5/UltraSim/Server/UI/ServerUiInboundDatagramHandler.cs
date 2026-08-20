using System;
using UltraSim.UI.Contracts;

namespace UltraSim.Server.UI
{
    public sealed class ServerUiInboundDatagramHandler
    {
        private readonly ServerUiPanelService _panelService;

        /// <summary>
        /// Optional handler for LoginRequestPackets (0xE8).
        /// Receives: (sessionId, instanceId, username, passwordHash[32] copy, challengeResponse[32] copy).
        /// Register this to implement the challenge-response login flow.
        /// </summary>
        public Action<long, UiPanelInstanceId, string, byte[], byte[]>? LoginRequestHandler { get; set; }

        public ServerUiInboundDatagramHandler(ServerUiPanelService panelService)
        {
            _panelService = panelService ?? throw new ArgumentNullException(nameof(panelService));
        }

        public bool TryHandle(long sessionId, ReadOnlySpan<byte> payload)
        {
            if (payload.IsEmpty)
                return false;

            // LoginRequestPacket — handled before the generic panel response path
            if (payload[0] == LoginPacketCodec.LoginRequestMessageType)
            {
                if (LoginRequestHandler == null)
                    return false;

                if (!LoginPacketCodec.TryDecodeLoginRequest(
                    payload,
                    out UiPanelInstanceId instanceId,
                    out string username,
                    out ReadOnlySpan<byte> passwordHash,
                    out ReadOnlySpan<byte> challengeResponse))
                    return false;

                // Copy spans to arrays before invoking the delegate (spans can't cross the delegate boundary)
                byte[] passwordHashCopy = passwordHash.ToArray();
                byte[] challengeResponseCopy = challengeResponse.ToArray();
                LoginRequestHandler.Invoke(sessionId, instanceId, username, passwordHashCopy, challengeResponseCopy);
                return true;
            }

            if (UiPanelNetworkCodec.TryDecodePanelAck(payload, out UiPanelAck ack))
                return _panelService.HandleAck(sessionId, ack);

            if (UiPanelNetworkCodec.TryDecodePanelResponse(payload, out UiPanelResponse response))
                return _panelService.HandleResponse(sessionId, response);

            return false;
        }
    }
}
