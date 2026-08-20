using System;
using System.Collections.Generic;
using System.Text;
using UltraSim.Network;
using UltraSim.UI.Authoring;
using UltraSim.UI.Contracts;
using UltraSim.UI.Contracts.Bundled;
using UiSchema = UltraSim.UI.Schema;

namespace UltraSim.Server.UI
{
    public sealed class ServerUiPanelService
    {
        public delegate void UiPanelResponseHandler(long sessionId, UiPanelResponse response);

        private readonly INetworkTransport _transport;
        private readonly NetworkSessionManager _sessions;
        private readonly Dictionary<(long SessionId, uint PanelInstanceId), ActivePanelState> _activePanels = new();
        private readonly Dictionary<(ushort PanelTypeId, ushort Revision), UiPanelDefinition> _definitions = new();
        private uint _nextPanelInstanceId = 1;

        public ServerUiPanelService(INetworkTransport transport, NetworkSessionManager sessions)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public event Action<long, UiPanelAck>? PanelAckReceived;
        public event Action<long, UiPanelResponse>? PanelResponseReceived;

        public void RegisterDefinition(UiPanelDefinition definition)
        {
            _definitions[(definition.PanelTypeId.Value, definition.DefinitionRevision)] = definition;
        }

        public UiPanelInstanceId OpenMessageBox(
            long sessionId,
            string title,
            string body,
            IReadOnlyList<MessageBoxButtonDefinition> buttons,
            ushort definitionRevision = 1,
            UiPanelPresentationMode presentationMode = UiPanelPresentationMode.ExclusiveMenuOnly,
            UiPanelFlags flags = UiPanelFlags.BringToFront | UiPanelFlags.HasCancelAction,
            UiPanelResponseHandler? responseHandler = null,
            bool closeOnResponse = true)
        {
            ReadOnlyMemory<byte> payload = MessageBoxPayloadCodec.Encode(new MessageBoxPayload(title, body, buttons));
            return OpenPanel(
                sessionId,
                SharedUiPanelIds.MessageBox,
                UiPanelCategory.Menu,
                presentationMode,
                definitionRevision,
                flags,
                payload,
                responseHandler,
                closeOnResponse);
        }

        public UiPanelInstanceId OpenPanel(
            long sessionId,
            UiPanelTypeId panelTypeId,
            UiPanelCategory category,
            UiPanelPresentationMode presentationMode,
            ushort definitionRevision,
            UiPanelFlags flags,
            ReadOnlyMemory<byte> payload,
            UiPanelResponseHandler? responseHandler = null,
            bool closeOnResponse = false)
        {
            UiPanelInstanceId instanceId = new(AllocatePanelInstanceId());
            UiPanelOpenCommand command = new(
                instanceId,
                panelTypeId,
                category,
                presentationMode,
                definitionRevision,
                flags,
                payload);

            if (TrySendOpen(sessionId, command))
                _activePanels[(sessionId, instanceId.Value)] = new ActivePanelState(command, responseHandler, closeOnResponse, null);

            return instanceId;
        }

        public bool UpdatePanel(long sessionId, UiPanelInstanceId panelInstanceId, ReadOnlyMemory<byte> payload, UiPanelUpdateMode updateMode = UiPanelUpdateMode.Full)
        {
            if (!_activePanels.ContainsKey((sessionId, panelInstanceId.Value)))
                return false;

            return TrySendUpdate(sessionId, new UiPanelUpdateCommand(panelInstanceId, payload, updateMode));
        }

        public bool ClosePanel(long sessionId, UiPanelInstanceId panelInstanceId, UiPanelCloseReason reason = UiPanelCloseReason.ClosedByServer)
        {
            if (!_activePanels.Remove((sessionId, panelInstanceId.Value)))
                return false;

            return TrySendClose(sessionId, new UiPanelCloseCommand(panelInstanceId, reason));
        }

        public void CloseAllForSession(long sessionId, UiPanelCloseReason reason = UiPanelCloseReason.SessionLost)
        {
            if (!_sessions.TryGetSnapshot(sessionId, out _))
            {
                RemoveSessionPanels(sessionId);
                return;
            }

            List<uint> panelIds = new();
            foreach ((long trackedSessionId, uint panelInstanceId) in _activePanels.Keys)
            {
                if (trackedSessionId == sessionId)
                    panelIds.Add(panelInstanceId);
            }

            for (int i = 0; i < panelIds.Count; i++)
                ClosePanel(sessionId, new UiPanelInstanceId(panelIds[i]), reason);
        }

        public void RemoveSessionPanels(long sessionId)
        {
            List<(long SessionId, uint PanelInstanceId)> keysToRemove = new();
            foreach ((long trackedSessionId, uint panelInstanceId) in _activePanels.Keys)
            {
                if (trackedSessionId == sessionId)
                    keysToRemove.Add((trackedSessionId, panelInstanceId));
            }

            for (int i = 0; i < keysToRemove.Count; i++)
                _activePanels.Remove(keysToRemove[i]);
        }

        public bool HandleAck(long sessionId, UiPanelAck ack)
        {
            PanelAckReceived?.Invoke(sessionId, ack);

            if (ack.Status != UiAckStatus.NeedDefinition)
                return true;

            if (_definitions.TryGetValue((ack.PanelTypeId.Value, ack.DefinitionRevision), out UiPanelDefinition definition))
                return TrySendDefinition(sessionId, definition);

            return false;
        }

        public bool HandleResponse(long sessionId, UiPanelResponse response)
        {
            PanelResponseReceived?.Invoke(sessionId, response);

            if (_activePanels.TryGetValue((sessionId, response.PanelInstanceId.Value), out ActivePanelState active))
            {
                active.ResponseHandler?.Invoke(sessionId, response);

                if (active.Panel is not null)
                {
                    UiSchema.UiSchemaDefinition def = active.Panel.ExtractDefinition();

                    // Reject responses with action IDs that don't exist in the panel schema.
                    // This closes the trust boundary: a client cannot trigger arbitrary handler
                    // code paths by sending unrecognised action IDs.
                    if (!def.IsKnownActionId(response.ActionId))
                        return true;

                    var ctx = new PanelContext(sessionId, response.PanelInstanceId, response.Payload, this, def);
                    active.Panel.OnResponse(ctx, response.ActionId);
                }

                if (active.CloseOnResponse && _activePanels.ContainsKey((sessionId, response.PanelInstanceId.Value)))
                    ClosePanel(sessionId, response.PanelInstanceId, UiPanelCloseReason.ClosedByServer);
            }

            return true;
        }

        public bool TrySendOpen(long sessionId, in UiPanelOpenCommand command)
        {
            if (!TryResolveConnectionId(sessionId, out int connectionId))
                return false;

            byte[] payload = new byte[UiPanelNetworkCodec.UiPanelOpenHeaderBytes + command.Payload.Length];
            if (!UiPanelNetworkCodec.TryEncodePanelOpen(payload, in command, out int written) || written <= 0)
                return false;

            _transport.EnqueueOutbound(new OutboundDatagram(connectionId, ChannelType.ReliableOrdered, new ReadOnlyMemory<byte>(payload, 0, written)));
            return true;
        }

        public bool TrySendUpdate(long sessionId, in UiPanelUpdateCommand command)
        {
            if (!TryResolveConnectionId(sessionId, out int connectionId))
                return false;

            byte[] payload = new byte[UiPanelNetworkCodec.UiPanelUpdateHeaderBytes + command.Payload.Length];
            if (!UiPanelNetworkCodec.TryEncodePanelUpdate(payload, in command, out int written) || written <= 0)
                return false;

            _transport.EnqueueOutbound(new OutboundDatagram(connectionId, ChannelType.ReliableOrdered, new ReadOnlyMemory<byte>(payload, 0, written)));
            return true;
        }

        public bool TrySendClose(long sessionId, in UiPanelCloseCommand command)
        {
            if (!TryResolveConnectionId(sessionId, out int connectionId))
                return false;

            byte[] payload = new byte[UiPanelNetworkCodec.UiPanelClosePayloadBytes];
            if (!UiPanelNetworkCodec.TryEncodePanelClose(payload, in command))
                return false;

            _transport.EnqueueOutbound(new OutboundDatagram(connectionId, ChannelType.ReliableOrdered, payload));
            return true;
        }

        public bool TrySendDefinition(long sessionId, in UiPanelDefinition definition)
        {
            if (!TryResolveConnectionId(sessionId, out int connectionId))
                return false;

            byte[] payload = new byte[UiPanelNetworkCodec.UiPanelDefinitionHeaderBytes + definition.DefinitionPayload.Length];
            if (!UiPanelNetworkCodec.TryEncodePanelDefinition(payload, in definition, out int written) || written <= 0)
                return false;

            _transport.EnqueueOutbound(new OutboundDatagram(connectionId, ChannelType.ReliableOrdered, new ReadOnlyMemory<byte>(payload, 0, written)));
            return true;
        }

        private bool TryResolveConnectionId(long sessionId, out int connectionId)
        {
            connectionId = 0;
            if (!_sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot))
                return false;

            connectionId = snapshot.ConnectionId;
            return connectionId > 0;
        }

        private uint AllocatePanelInstanceId()
        {
            while (_nextPanelInstanceId == 0)
                _nextPanelInstanceId++;

            return _nextPanelInstanceId++;
        }

        private readonly record struct ActivePanelState(
            UiPanelOpenCommand    Open,
            UiPanelResponseHandler? ResponseHandler,
            bool                  CloseOnResponse,
            UiPanel?              Panel);  // null for legacy raw-payload opens

        // -------------------------------------------------------------------------
        // UiPanel overloads — clean authoring path
        // -------------------------------------------------------------------------

        /// <summary>
        /// Opens a panel built with <see cref="UiPanel"/>. Automatically registers the
        /// definition on first use and routes responses back to <see cref="UiPanel.OnResponse"/>.
        /// </summary>
        public UiPanelInstanceId Open(
            long sessionId,
            UiPanel panel,
            UiPanelCategory category = UiPanelCategory.Menu,
            UiPanelPresentationMode presentationMode = UiPanelPresentationMode.ExclusiveMenuOnly,
            UiPanelFlags flags = UiPanelFlags.BringToFront)
        {
            var definition = panel.ExtractDefinition();
            var defKey = (definition.TypeId.Value, definition.Revision);
            if (!_definitions.ContainsKey(defKey))
                _definitions[defKey] = definition.ToWireRecord();

            byte[] payload = panel.ExtractPayload();

            UiPanelInstanceId instanceId = new(AllocatePanelInstanceId());
            UiPanelOpenCommand command = new(
                instanceId,
                definition.TypeId,
                category,
                presentationMode,
                definition.Revision,
                flags,
                payload);

            if (TrySendOpen(sessionId, command))
                _activePanels[(sessionId, instanceId.Value)] = new ActivePanelState(command, null, false, panel);

            return instanceId;
        }

        /// <summary>
        /// Sends updated payload for an existing panel instance.
        /// Construct a fresh <see cref="UiPanel"/> with the latest data and pass it here.
        /// </summary>
        public bool Update(long sessionId, UiPanelInstanceId instanceId, UiPanel panel)
        {
            if (!_activePanels.TryGetValue((sessionId, instanceId.Value), out ActivePanelState active))
                return false;

            _activePanels[(sessionId, instanceId.Value)] = active with { Panel = panel };
            return TrySendUpdate(sessionId, new UiPanelUpdateCommand(instanceId, panel.ExtractPayload()));
        }

        // -------------------------------------------------------------------------
        // IUiPanelContext implementation
        // -------------------------------------------------------------------------

        private sealed class PanelContext : IUiPanelContext
        {
            private readonly long _sessionId;
            private readonly ReadOnlyMemory<byte> _responsePayload;
            private readonly ServerUiPanelService _service;
            private readonly UiSchema.UiSchemaDefinition _definition;
            private Dictionary<byte, string>? _textFields; // decoded lazily

            public UiPanelInstanceId InstanceId { get; }

            public PanelContext(long sessionId, UiPanelInstanceId instanceId,
                ReadOnlyMemory<byte> responsePayload, ServerUiPanelService service,
                UiSchema.UiSchemaDefinition definition)
            {
                _sessionId       = sessionId;
                InstanceId       = instanceId;
                _responsePayload = responsePayload;
                _service         = service;
                _definition      = definition;
            }

            public ReadOnlyMemory<byte> ResponsePayload => _responsePayload;

            public void Close(UiPanelCloseReason reason = UiPanelCloseReason.ClosedByServer)
                => _service.ClosePanel(_sessionId, InstanceId, reason);

            public void Update(UiPanel panel)
                => _service.Update(_sessionId, InstanceId, panel);

            public string? GetTextField(byte fieldIndex)
            {
                // Reject field indices that were never declared in the schema.
                if (!_definition.IsKnownTextField(fieldIndex))
                    return null;

                _textFields ??= DecodeTextFields(_responsePayload);
                return _textFields.TryGetValue(fieldIndex, out string? value) ? value : null;
            }

            public bool TryGetActionRowKey(out ulong rowKey)
            {
                rowKey = 0;
                int offset = GetTextFieldPayloadLength(_responsePayload.Span);
                ReadOnlySpan<byte> span = _responsePayload.Span;
                if (offset >= span.Length)
                    return false;

                byte flags = span[offset++];
                if ((flags & 0x01) == 0 || offset + 8 > span.Length)
                    return false;

                rowKey =
                    span[offset] |
                    ((ulong)span[offset + 1] << 8) |
                    ((ulong)span[offset + 2] << 16) |
                    ((ulong)span[offset + 3] << 24) |
                    ((ulong)span[offset + 4] << 32) |
                    ((ulong)span[offset + 5] << 40) |
                    ((ulong)span[offset + 6] << 48) |
                    ((ulong)span[offset + 7] << 56);
                return true;
            }

            /// <summary>
            /// Decodes the response payload written by SchemaPanel.PackTextInputs.
            /// Wire format: [1] count, then per field: [1] fieldIndex [2] utf8Len [N] utf8Bytes.
            /// </summary>
            private static Dictionary<byte, string> DecodeTextFields(ReadOnlyMemory<byte> payload)
            {
                var result = new Dictionary<byte, string>();
                ReadOnlySpan<byte> span = payload.Span;
                int offset = 0;

                if (offset >= span.Length) return result;
                byte count = span[offset++];

                for (int i = 0; i < count && offset < span.Length; i++)
                {
                    if (offset >= span.Length) break;
                    byte fieldIndex = span[offset++];

                    if (offset + 2 > span.Length) break;
                    int len = span[offset] | (span[offset + 1] << 8);
                    offset += 2;

                    if (len == 0) { result[fieldIndex] = string.Empty; continue; }
                    if (offset + len > span.Length) break;

                    result[fieldIndex] = Encoding.UTF8.GetString(span.Slice(offset, len));
                    offset += len;
                }

                return result;
            }

            private static int GetTextFieldPayloadLength(ReadOnlySpan<byte> span)
            {
                int offset = 0;
                if (offset >= span.Length)
                    return 0;

                byte count = span[offset++];
                for (int i = 0; i < count && offset < span.Length; i++)
                {
                    if (offset >= span.Length)
                        return span.Length;

                    offset++;
                    if (offset + 2 > span.Length)
                        return span.Length;

                    int len = span[offset] | (span[offset + 1] << 8);
                    offset += 2;
                    if (offset + len > span.Length)
                        return span.Length;

                    offset += len;
                }

                return offset;
            }
        }
    }
}
