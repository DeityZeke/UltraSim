using System;
using System.Collections.Generic;
using System.Text;
using UltraSim.Client.Runtime;
using UltraSim.Client.Runtime.UI;
using UltraSim.UI.Codec;
using UltraSim.UI.Contracts;
using Client.Godot.UI.ServerDriven;
using Client.Godot.UI.ServerDriven.Renderer;
using Godot;

namespace Client.Godot
{
    /// <summary>
    /// Intercepts a panel action before it is forwarded as a normal <see cref="UiPanelResponse"/>.
    /// Return <c>true</c> to consume the action (suppress the normal response path).
    /// Receives: (instanceId, openPayload, packed text-input payload).
    /// </summary>
    public delegate bool PacketBindingHandler(
        UiPanelInstanceId instanceId,
        ReadOnlyMemory<byte> openPayload,
        ReadOnlyMemory<byte> textInputPayload);

    public sealed class GodotUiCommandConsumer : IUiCommandSink
    {
        private readonly Control _host;
        private readonly GodotUiPanelRegistry _registry;
        private readonly IContentResolver? _contentResolver;
        private readonly Action<UiPanelResponse>? _responseHandler;
        private readonly Func<ClientUiPanelRuntime?>? _panelRuntimeGetter;
        private readonly UiSchemaRenderer _schemaRenderer = new(styleWarning: msg => GD.PushWarning(msg));
        private readonly Dictionary<uint, Control> _panelsByInstance = new();
        private readonly Dictionary<uint, UiPanelFlags> _flagsByInstance = new();

        // Packet binding registry — maps (typeId, actionId) to a handler that bypasses UiPanelResponse.
        private readonly Dictionary<(ushort TypeId, uint ActionId), PacketBindingHandler> _packetBindings = new();
        // Per-instance open payload cache (needed by packet bindings to read e.g. the challenge nonce).
        private readonly Dictionary<uint, ReadOnlyMemory<byte>> _openPayloadByInstance = new();
        // Per-instance typeId cache (needed to look up bindings in the response interceptor).
        private readonly Dictionary<uint, ushort> _typeIdByInstance = new();

        /// <summary>
        /// Number of panels currently surviving a session loss (e.g. rejection messages).
        /// These panels stay visible until dismissed. All other panels are force-closed on disconnect.
        /// </summary>
        public int SessionLostPanelCount { get; private set; }

        /// <summary>
        /// Number of panels currently open with <see cref="UiPanelFlags.BlocksGameplayInput"/>.
        /// While non-zero the client should keep gameplay input blocked.
        /// </summary>
        public int BlockingPanelCount { get; private set; }

        /// <summary>
        /// Registers a packet binding for a specific (typeId, actionId) pair.
        /// When a panel of <paramref name="panelTypeId"/> fires <paramref name="actionId"/>,
        /// the binding is called instead of forwarding a normal <see cref="UiPanelResponse"/>.
        /// </summary>
        public void RegisterPacketBinding(UiPanelTypeId panelTypeId, uint actionId, PacketBindingHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _packetBindings[(panelTypeId.Value, actionId)] = handler;
        }

        /// <summary>
        /// Replaces the stored open payload for an existing panel instance.
        /// Used when a server-side retry sends a new challenge nonce (LoginResponsePacket).
        /// </summary>
        public void UpdateOpenPayload(UiPanelInstanceId instanceId, ReadOnlyMemory<byte> newPayload)
        {
            if (_panelsByInstance.ContainsKey(instanceId.Value))
                _openPayloadByInstance[instanceId.Value] = newPayload;
        }

        public GodotUiCommandConsumer(
            Control host,
            GodotUiPanelRegistry registry,
            IContentResolver? contentResolver = null,
            Action<UiPanelResponse>? responseHandler = null,
            Func<ClientUiPanelRuntime?>? panelRuntimeGetter = null)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(registry);
            _host = host;
            _registry = registry;
            _contentResolver = contentResolver;
            _responseHandler = responseHandler;
            _panelRuntimeGetter = panelRuntimeGetter;
        }

        public void OpenPanel(UiPanelOpenCommand command)
        {
            if (_panelsByInstance.TryGetValue(command.PanelInstanceId.Value, out Control? existing))
            {
                // Decrement before overwriting the flags entry so the count stays balanced.
                if (_flagsByInstance.TryGetValue(command.PanelInstanceId.Value, out UiPanelFlags existingFlags) &&
                    (existingFlags & UiPanelFlags.BlocksGameplayInput) != 0)
                    BlockingPanelCount = Math.Max(0, BlockingPanelCount - 1);

                _panelsByInstance.Remove(command.PanelInstanceId.Value);
                _openPayloadByInstance.Remove(command.PanelInstanceId.Value);
                _typeIdByInstance.Remove(command.PanelInstanceId.Value);
                if (existing is IGodotUiPanel existingPanel)
                    existingPanel.PrepareClose(new UiPanelCloseCommand(command.PanelInstanceId, UiPanelCloseReason.Replaced));
                if (existing.IsInsideTree())
                    existing.QueueFree();
            }

            if (!_registry.TryCreate(in command, out Control? panel) || panel == null)
                panel = TryCreateFromContentResolver(in command);

            if (panel == null)
                panel = TryCreateSchemaPanel(in command);

            if (panel == null)
                panel = CreateFallbackPanel(in command);
            else if (panel is IGodotUiPanel typedPanel)
            {
                // Build a response handler that checks for packet bindings before
                // forwarding to the normal UiPanelResponse path.
                ushort typeId = command.PanelTypeId.Value;
                uint instanceIdValue = command.PanelInstanceId.Value;
                Action<UiPanelResponse> interceptingHandler = response =>
                {
                    if (_openPayloadByInstance.TryGetValue(instanceIdValue, out ReadOnlyMemory<byte> openPayload) &&
                        _packetBindings.TryGetValue((typeId, response.ActionId), out PacketBindingHandler? binding) &&
                        binding(response.PanelInstanceId, openPayload, response.Payload))
                    {
                        return; // binding consumed the action
                    }
                    _responseHandler?.Invoke(response);
                };
                typedPanel.Initialize(command, interceptingHandler);
            }

            panel.Name = $"UiPanel_{command.PanelInstanceId.Value:X8}_{command.PanelTypeId.Value:X4}";
            _host.AddChild(panel);
            if (command.BringToFront)
                panel.MoveToFront();

            _panelsByInstance[command.PanelInstanceId.Value] = panel;
            _flagsByInstance[command.PanelInstanceId.Value] = command.Flags;
            _openPayloadByInstance[command.PanelInstanceId.Value] = command.Payload;
            _typeIdByInstance[command.PanelInstanceId.Value] = command.PanelTypeId.Value;

            if ((command.Flags & UiPanelFlags.BlocksGameplayInput) != 0)
                BlockingPanelCount++;
        }

        public void UpdatePanel(UiPanelUpdateCommand command)
        {
            if (!_panelsByInstance.TryGetValue(command.PanelInstanceId.Value, out Control? panel))
                return;

            if (panel is IGodotUiPanel typedPanel)
            {
                typedPanel.ApplyUpdate(command);
                return;
            }

            if (panel.HasMeta("payload_label_name"))
            {
                string labelName = panel.GetMeta("payload_label_name", string.Empty).AsString();
                if (!string.IsNullOrWhiteSpace(labelName) && panel.FindChild(labelName, recursive: true, owned: false) is Label label)
                    label.Text = $"Payload ({command.Payload.Length} bytes): {TruncateUtf8(command.Payload.Span, 120)}";
            }
        }

        public void ClosePanel(UiPanelCloseCommand command)
        {
            _flagsByInstance.Remove(command.PanelInstanceId.Value, out UiPanelFlags openFlags);
            _openPayloadByInstance.Remove(command.PanelInstanceId.Value);
            _typeIdByInstance.Remove(command.PanelInstanceId.Value);
            if (!_panelsByInstance.Remove(command.PanelInstanceId.Value, out Control? panel))
                return;

            if (panel is IGodotUiPanel typedPanel)
                typedPanel.PrepareClose(command);

            if ((openFlags & UiPanelFlags.BlocksGameplayInput) != 0)
                BlockingPanelCount = Math.Max(0, BlockingPanelCount - 1);

            // Only panels explicitly opened with PersistOnSessionLoss survive a session loss.
            // Everything else (inventory, character sheet, etc.) is force-closed immediately,
            // so a crash or unexpected disconnect always returns to main menu without blocking.
            if (command.CloseReason == UiPanelCloseReason.SessionLost &&
                (openFlags & UiPanelFlags.PersistOnSessionLoss) != 0)
            {
                SessionLostPanelCount++;
                panel.TreeExiting += () => SessionLostPanelCount--;
                return;
            }

            if (panel.IsInsideTree())
                panel.QueueFree();
        }

        private SchemaPanel? TryCreateSchemaPanel(in UiPanelOpenCommand command)
        {
            ClientUiPanelRuntime? runtime = _panelRuntimeGetter?.Invoke();
            if (runtime == null)
                return null;

            if (!runtime.TryGetDefinition(command.PanelTypeId, command.DefinitionRevision, out _))
                return null;

            var panel = new SchemaPanel(
                (typeId, revision) =>
                {
                    if (!runtime.TryGetDefinition(typeId, revision, out UiPanelDefinition def))
                        return null;
                    return UiSchemaCodec.Read(typeId, revision, def.DefinitionPayload);
                },
                _schemaRenderer);

            return panel;
        }

        private Control? TryCreateFromContentResolver(in UiPanelOpenCommand command)
        {
            if (_contentResolver == null)
                return null;

            string logicalId = $"ui.panel.{command.PanelTypeId.Value:x8}";
            if (!_contentResolver.TryResolve(ClientAssetKind.UiPanel, logicalId, out ClientResolvedAssetRef assetRef) ||
                assetRef.IsEmpty)
                return null;

            PackedScene? scene = GD.Load<PackedScene>(assetRef.Value);
            if (scene?.Instantiate() is Control control)
                return control;

            return null;
        }

        private static Control CreateFallbackPanel(in UiPanelOpenCommand command)
        {
            PanelContainer panel = new()
            {
                OffsetLeft = 500,
                OffsetTop = 24,
                OffsetRight = 860,
                OffsetBottom = 180,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };

            VBoxContainer root = new();
            panel.AddChild(root);

            root.AddChild(new Label { Text = $"Panel 0x{command.PanelTypeId.Value:X4} (Instance {command.PanelInstanceId.Value})" });
            root.AddChild(new Label { Text = $"Modal: {(command.IsModal ? 1 : 0)}" });

            Label payloadLabel = new()
            {
                Name = "PayloadLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Text = $"Payload ({command.Payload.Length} bytes): {TruncateUtf8(command.Payload.Span, 120)}"
            };
            root.AddChild(payloadLabel);
            panel.SetMeta("payload_label_name", payloadLabel.Name);

            Button closeButton = new() { Text = "Close (local)" };
            closeButton.Pressed += panel.QueueFree;
            root.AddChild(closeButton);

            return panel;
        }

        private static string TruncateUtf8(ReadOnlySpan<byte> payload, int maxChars)
        {
            if (payload.IsEmpty)
                return string.Empty;

            string text;
            try
            {
                text = Encoding.UTF8.GetString(payload);
            }
            catch
            {
                text = Convert.ToHexString(payload);
            }

            if (text.Length <= maxChars)
                return text;

            return text.Substring(0, Math.Max(0, maxChars)) + "...";
        }
    }
}
