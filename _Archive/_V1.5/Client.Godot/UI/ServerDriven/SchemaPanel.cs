using System;
using System.Collections.Generic;
using System.Text;
using Client.Godot.UI.ServerDriven.Renderer;
using Godot;
using UltraSim.UI.Authoring;
using UltraSim.UI.Codec;
using UltraSim.UI.Contracts;
using UltraSim.UI.Schema;

namespace Client.Godot.UI.ServerDriven
{
    /// <summary>
    /// Generic <see cref="IGodotUiPanel"/> for any schema-driven panel.
    ///
    /// On open:
    ///   1. Resolves the <see cref="UiSchemaDefinition"/> from the client's definition cache.
    ///   2. Reads the payload into slot values via <see cref="UiPayloadReader"/>.
    ///   3. Builds a Godot control tree via <see cref="UiSchemaRenderer"/>.
    ///
    /// On update:
    ///   - Reads the new payload, applies slot values to the already-built tree —
    ///     no rebuild required.
    ///
    /// On close:
    ///   - No-op (host removes this node from the scene tree).
    ///
    /// Extends PanelContainer for the themed background. Positions itself using
    /// center anchors applied in _Ready() based on the definition's declared size.
    /// </summary>
    public sealed partial class SchemaPanel : PanelContainer, IGodotUiPanel
    {
        private readonly Func<UiPanelTypeId, ushort, UiSchemaDefinition?> _definitionResolver;
        private readonly UiSchemaRenderer _renderer;

        private UiPanelInstanceId      _instanceId;
        private UiPanelFlags           _openFlags;
        private Action<UiPanelResponse>? _responseHandler;
        private UiSchemaDefinition?    _definition;
        private RenderedPanel?         _rendered;
        private object?[]              _slotValues = Array.Empty<object?>();
        private IReadOnlyDictionary<byte, UiRepeaterPayloadData> _repeaterValues = new Dictionary<byte, UiRepeaterPayloadData>();

        public SchemaPanel(
            Func<UiPanelTypeId, ushort, UiSchemaDefinition?> definitionResolver,
            UiSchemaRenderer renderer)
        {
            _definitionResolver = definitionResolver ?? throw new ArgumentNullException(nameof(definitionResolver));
            _renderer           = renderer           ?? throw new ArgumentNullException(nameof(renderer));
        }

        // -------------------------------------------------------------------------
        // Godot lifecycle
        // -------------------------------------------------------------------------

        public override void _Ready()
        {
            if (_definition is null)
                return;

            ApplyPosition(_definition.Position, _definition.Width, _definition.Height);
        }

        // -------------------------------------------------------------------------
        // IGodotUiPanel
        // -------------------------------------------------------------------------

        public void Initialize(UiPanelOpenCommand command, Action<UiPanelResponse>? responseHandler)
        {
            _instanceId      = command.PanelInstanceId;
            _openFlags       = command.Flags;
            _responseHandler = responseHandler;

            _definition = _definitionResolver(command.PanelTypeId, command.DefinitionRevision);
            if (_definition is null)
            {
                GD.PrintErr($"[SchemaPanel] Definition not found for typeId={command.PanelTypeId} rev={command.DefinitionRevision}");
                return;
            }

            if (_definition.Width > 0 || _definition.Height > 0)
                CustomMinimumSize = new Vector2(_definition.Width, _definition.Height);

            UiPayloadReader.DecodedPayload decoded = UiPayloadReader.ReadPayload(command.Payload, _definition);
            _slotValues = decoded.SlotValues;
            _repeaterValues = decoded.Repeaters;
            _rendered   = _renderer.Build(_definition, _slotValues, FireAction);
            _rendered.ApplySlots(_slotValues, _repeaterValues);
            AddChild(_rendered.Root);

            // QoL: submitting the password field on the login panel triggers the login action.
            if (command.PanelTypeId == SharedUiPanelIds.LoginPanel &&
                _rendered.TextInputs.TryGetValue(1, out LineEdit? passwordField))
            {
                passwordField.TextSubmitted += _ => FireAction(UiPanel.ActionId("login"), null);
            }
        }

        public void ApplyUpdate(UiPanelUpdateCommand command)
        {
            if (_rendered is null || _definition is null)
                return;

            if (command.UpdateMode == UiPanelUpdateMode.Delta)
            {
                UiPayloadReader.ApplyDelta(command.Payload, _definition.Slots, _slotValues);
            }
            else
            {
                UiPayloadReader.DecodedPayload decoded = UiPayloadReader.ReadPayload(command.Payload, _definition);
                _slotValues = decoded.SlotValues;
                _repeaterValues = decoded.Repeaters;
            }

            _rendered.ApplySlots(_slotValues, _repeaterValues);
        }

        public void PrepareClose(UiPanelCloseCommand command)
        {
            if (command.CloseReason == UiPanelCloseReason.SessionLost &&
                (_openFlags & UiPanelFlags.PersistOnSessionLoss) != 0)
            {
                // Explicitly marked to persist — rewire response handler to self-close on dismiss.
                var original = _responseHandler;
                _responseHandler = response =>
                {
                    original?.Invoke(response);
                    QueueFree();
                };
            }
        }

        // -------------------------------------------------------------------------
        // Action routing
        // -------------------------------------------------------------------------

        private void FireAction(uint actionId, ulong? rowKey)
        {
            byte[] payload = _rendered is { TextInputs.Count: > 0 }
                ? PackTextInputs(_rendered.TextInputs)
                : new byte[] { 0 };

            if (rowKey.HasValue)
                payload = AppendActionContext(payload, rowKey.Value);

            _responseHandler?.Invoke(new UiPanelResponse(_instanceId, actionId, payload));
        }

        private static byte[] PackTextInputs(Dictionary<byte, LineEdit> inputs)
        {
            var buf = new List<byte>(inputs.Count * 32);
            buf.Add((byte)inputs.Count);
            foreach ((byte fieldIndex, LineEdit lineEdit) in inputs)
            {
                buf.Add(fieldIndex);
                byte[] utf8 = Encoding.UTF8.GetBytes(lineEdit.Text ?? string.Empty);
                int len = Math.Min(utf8.Length, ushort.MaxValue);
                buf.Add((byte)len);
                buf.Add((byte)(len >> 8));
                if (len > 0)
                    buf.AddRange(new ReadOnlySpan<byte>(utf8, 0, len).ToArray());
            }
            return buf.ToArray();
        }

        private static byte[] AppendActionContext(byte[] payload, ulong rowKey)
        {
            var buf = new byte[payload.Length + 1 + 8];
            System.Buffer.BlockCopy(payload, 0, buf, 0, payload.Length);
            int offset = payload.Length;
            buf[offset++] = 0x01;
            for (int i = 0; i < 8; i++)
                buf[offset + i] = (byte)(rowKey >> (8 * i));
            return buf;
        }

        // -------------------------------------------------------------------------
        // Positioning
        // -------------------------------------------------------------------------

        private void ApplyPosition(UiPanelPosition position, int width, int height)
        {
            float hw = width  * 0.5f;
            float hh = height * 0.5f;

            switch (position)
            {
                case UiPanelPosition.TopLeft:
                    AnchorLeft = AnchorTop = AnchorRight = AnchorBottom = 0f;
                    OffsetLeft = 0f;     OffsetTop = 0f;
                    OffsetRight = width; OffsetBottom = height;
                    break;

                case UiPanelPosition.TopCenter:
                    AnchorLeft = AnchorRight = 0.5f;
                    AnchorTop = AnchorBottom = 0f;
                    OffsetLeft = -hw;    OffsetTop = 0f;
                    OffsetRight = hw;    OffsetBottom = height;
                    break;

                case UiPanelPosition.TopRight:
                    AnchorLeft = AnchorRight = 1f;
                    AnchorTop = AnchorBottom = 0f;
                    OffsetLeft = -width; OffsetTop = 0f;
                    OffsetRight = 0f;    OffsetBottom = height;
                    break;

                case UiPanelPosition.BottomLeft:
                    AnchorLeft = AnchorRight = 0f;
                    AnchorTop = AnchorBottom = 1f;
                    OffsetLeft = 0f;     OffsetTop = -height;
                    OffsetRight = width; OffsetBottom = 0f;
                    break;

                case UiPanelPosition.BottomCenter:
                    AnchorLeft = AnchorRight = 0.5f;
                    AnchorTop = AnchorBottom = 1f;
                    OffsetLeft = -hw;    OffsetTop = -height;
                    OffsetRight = hw;    OffsetBottom = 0f;
                    break;

                case UiPanelPosition.BottomRight:
                    AnchorLeft = AnchorRight = AnchorTop = AnchorBottom = 1f;
                    OffsetLeft = -width; OffsetTop = -height;
                    OffsetRight = 0f;    OffsetBottom = 0f;
                    break;

                default: // Center
                    AnchorLeft = AnchorRight = AnchorTop = AnchorBottom = 0.5f;
                    OffsetLeft = -hw; OffsetTop = -hh;
                    OffsetRight = hw; OffsetBottom = hh;
                    break;
            }
        }
    }
}
