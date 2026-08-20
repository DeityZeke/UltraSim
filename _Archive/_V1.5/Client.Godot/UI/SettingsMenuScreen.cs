using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class SettingsMenuScreen : MenuScreenBase
    {
        [Signal] public delegate void BackRequestedEventHandler();

        [Export] public NodePath? RuntimeRootPath { get; set; }
        [Export] public NodePath? BackButtonPath { get; set; }
        [Export] public NodePath? OverheadChatTogglePath { get; set; }
        [Export] public NodePath? NameplatesTogglePath { get; set; }
        [Export] public NodePath? MaxWorldUiSpinBoxPath { get; set; }
        [Export] public NodePath? WorldUiScaleSliderPath { get; set; }
        [Export] public NodePath? WorldUiScaleValueLabelPath { get; set; }

        private GodotClientRuntimeRoot? _runtimeRoot;
        private CheckBox? _overheadChatToggle;
        private CheckBox? _nameplatesToggle;
        private SpinBox? _maxWorldUiSpinBox;
        private HSlider? _worldUiScaleSlider;
        private Label? _worldUiScaleValueLabel;
        private bool _updatingUi;

        public override void _Ready()
        {
            _runtimeRoot = ResolveRuntimeRoot();

            Button? backButton = GetNodeOrNull<Button>(BackButtonPath);
            if (backButton != null)
                backButton.Pressed += () => EmitSignal(SignalName.BackRequested);

            _overheadChatToggle = GetNodeOrNull<CheckBox>(OverheadChatTogglePath);
            _nameplatesToggle = GetNodeOrNull<CheckBox>(NameplatesTogglePath);
            _maxWorldUiSpinBox = GetNodeOrNull<SpinBox>(MaxWorldUiSpinBoxPath);
            _worldUiScaleSlider = GetNodeOrNull<HSlider>(WorldUiScaleSliderPath);
            _worldUiScaleValueLabel = GetNodeOrNull<Label>(WorldUiScaleValueLabelPath);

            if (_overheadChatToggle != null)
                _overheadChatToggle.Toggled += OnOverheadChatToggled;
            if (_nameplatesToggle != null)
                _nameplatesToggle.Toggled += OnNameplatesToggled;
            if (_maxWorldUiSpinBox != null)
                _maxWorldUiSpinBox.ValueChanged += OnMaxWorldUiChanged;
            if (_worldUiScaleSlider != null)
                _worldUiScaleSlider.ValueChanged += OnWorldUiScaleChanged;

            SyncFromRuntime();
        }

        public override void ShowScreen()
        {
            SyncFromRuntime();
            base.ShowScreen();
        }

        private void SyncFromRuntime()
        {
            if (_runtimeRoot == null)
                return;

            _updatingUi = true;
            if (_overheadChatToggle != null)
                _overheadChatToggle.ButtonPressed = _runtimeRoot.ShowOverheadChat;
            if (_nameplatesToggle != null)
                _nameplatesToggle.ButtonPressed = _runtimeRoot.ShowNameplates;
            if (_maxWorldUiSpinBox != null)
                _maxWorldUiSpinBox.Value = _runtimeRoot.MaxVisibleWorldUi;
            if (_worldUiScaleSlider != null)
                _worldUiScaleSlider.Value = _runtimeRoot.WorldUiScale;
            UpdateWorldUiScaleLabel(_runtimeRoot.WorldUiScale);
            _updatingUi = false;
        }

        private void OnOverheadChatToggled(bool enabled)
        {
            if (_updatingUi || _runtimeRoot == null)
                return;

            _runtimeRoot.ShowOverheadChat = enabled;
        }

        private void OnNameplatesToggled(bool enabled)
        {
            if (_updatingUi || _runtimeRoot == null)
                return;

            _runtimeRoot.ShowNameplates = enabled;
        }

        private void OnMaxWorldUiChanged(double value)
        {
            if (_updatingUi || _runtimeRoot == null)
                return;

            _runtimeRoot.MaxVisibleWorldUi = (int)value;
        }

        private void OnWorldUiScaleChanged(double value)
        {
            UpdateWorldUiScaleLabel((float)value);
            if (_updatingUi || _runtimeRoot == null)
                return;

            _runtimeRoot.WorldUiScale = (float)value;
        }

        private void UpdateWorldUiScaleLabel(float value)
        {
            if (_worldUiScaleValueLabel != null)
                _worldUiScaleValueLabel.Text = $"{value:0.00}x";
        }

        private GodotClientRuntimeRoot? ResolveRuntimeRoot()
        {
            GodotClientRuntimeRoot? explicitRuntimeRoot = RuntimeRootPath == null || RuntimeRootPath.IsEmpty
                ? null
                : GetNodeOrNull<GodotClientRuntimeRoot>(RuntimeRootPath);
            if (explicitRuntimeRoot != null)
                return explicitRuntimeRoot;

            Node? current = this;
            while (current != null)
            {
                if (current is GodotClientRuntimeRoot runtimeRoot)
                    return runtimeRoot;

                current = current.GetParent();
            }

            return null;
        }
    }
}
