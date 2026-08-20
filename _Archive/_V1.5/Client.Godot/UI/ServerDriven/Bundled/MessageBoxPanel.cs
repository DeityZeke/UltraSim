using System;
using Godot;
using UltraSim.UI.Contracts;
using UltraSim.UI.Contracts.Bundled;

namespace Client.Godot.UI.ServerDriven.Bundled
{
    public sealed partial class MessageBoxPanel : Control, IGodotUiPanel
    {
        private readonly Label _titleLabel;
        private readonly Label _bodyLabel;
        private readonly HBoxContainer _buttonRow;

        private UiPanelInstanceId _panelInstanceId;
        private UiPanelFlags _openFlags;
        private Action<UiPanelResponse>? _responseHandler;
        private bool _selfCloseOnAction;

        public MessageBoxPanel()
        {
            Name = "MessageBoxPanel";
            MouseFilter = MouseFilterEnum.Stop;
            AnchorsPreset = (int)LayoutPreset.FullRect;
            GrowHorizontal = GrowDirection.Both;
            GrowVertical = GrowDirection.Both;
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            CenterContainer center = new()
            {
                Name = "Center",
                AnchorsPreset = (int)LayoutPreset.FullRect,
                GrowHorizontal = GrowDirection.Both,
                GrowVertical = GrowDirection.Both,
                MouseFilter = MouseFilterEnum.Pass,
            };
            center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(center);

            PanelContainer panel = new()
            {
                Name = "Panel",
                CustomMinimumSize = new Vector2(360f, 0f),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Stop,
            };
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.08f, 0.08f, 0.08f, 1.0f),
                BorderColor = new Color(0.82f, 0.82f, 0.82f, 1.0f),
                BorderWidthLeft = 2,
                BorderWidthTop = 2,
                BorderWidthRight = 2,
                BorderWidthBottom = 2,
                CornerRadiusTopLeft = 8,
                CornerRadiusTopRight = 8,
                CornerRadiusBottomRight = 8,
                CornerRadiusBottomLeft = 8,
            });
            center.AddChild(panel);

            MarginContainer margin = new();
            margin.AddThemeConstantOverride("margin_left", 16);
            margin.AddThemeConstantOverride("margin_top", 16);
            margin.AddThemeConstantOverride("margin_right", 16);
            margin.AddThemeConstantOverride("margin_bottom", 16);
            panel.AddChild(margin);

            VBoxContainer root = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            margin.AddChild(root);

            _titleLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            root.AddChild(_titleLabel);

            _bodyLabel = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            root.AddChild(_bodyLabel);

            _buttonRow = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
            };
            root.AddChild(_buttonRow);
        }

        public void Initialize(UiPanelOpenCommand command, Action<UiPanelResponse>? responseHandler)
        {
            _panelInstanceId = command.PanelInstanceId;
            _openFlags       = command.Flags;
            _responseHandler = responseHandler;
            ApplyPayload(MessageBoxPayloadCodec.Decode(command.Payload));
        }

        public void ApplyUpdate(UiPanelUpdateCommand command)
        {
            ApplyPayload(MessageBoxPayloadCodec.Decode(command.Payload));
        }

        public void PrepareClose(UiPanelCloseCommand command)
        {
            if (command.CloseReason == UiPanelCloseReason.SessionLost &&
                (_openFlags & UiPanelFlags.PersistOnSessionLoss) != 0)
                _selfCloseOnAction = true;
        }

        private void ApplyPayload(MessageBoxPayload payload)
        {
            _titleLabel.Text = payload.Title ?? string.Empty;
            _bodyLabel.Text = payload.Body ?? string.Empty;

            foreach (Node child in _buttonRow.GetChildren())
                child.QueueFree();

            Button? defaultButton = null;
            for (int i = 0; i < payload.Buttons.Count; i++)
            {
                MessageBoxButtonDefinition buttonModel = payload.Buttons[i];
                Button button = new()
                {
                    Text = string.IsNullOrWhiteSpace(buttonModel.Label) ? $"Action {buttonModel.ActionId}" : buttonModel.Label,
                    SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                };

                uint actionId = buttonModel.ActionId;
                button.Pressed += () =>
                {
                    _responseHandler?.Invoke(new UiPanelResponse(_panelInstanceId, actionId));
                    if (_selfCloseOnAction) QueueFree();
                };
                _buttonRow.AddChild(button);

                if (buttonModel.IsDefault && defaultButton == null)
                    defaultButton = button;
            }

            if (defaultButton != null)
                defaultButton.CallDeferred(Button.MethodName.GrabFocus);
        }
    }
}
