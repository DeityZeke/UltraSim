using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class StartMenuScreen : MenuScreenBase
    {
        [Signal] public delegate void SinglePlayerRequestedEventHandler();
        [Signal] public delegate void MultiplayerRequestedEventHandler();
        [Signal] public delegate void SettingsRequestedEventHandler();
        [Signal] public delegate void ExitRequestedEventHandler();

        [Export] public NodePath? SinglePlayerButtonPath { get; set; }
        [Export] public NodePath? MultiplayerButtonPath { get; set; }
        [Export] public NodePath? SettingsButtonPath { get; set; }
        [Export] public NodePath? ExitButtonPath { get; set; }
        [Export] public NodePath? StatusLabelPath { get; set; }

        private Label? _statusLabel;

        public override void _Ready()
        {
            Button? singlePlayerButton = GetNodeOrNull<Button>(SinglePlayerButtonPath);
            Button? multiplayerButton = GetNodeOrNull<Button>(MultiplayerButtonPath);
            Button? settingsButton = GetNodeOrNull<Button>(SettingsButtonPath);
            Button? exitButton = GetNodeOrNull<Button>(ExitButtonPath);
            if (singlePlayerButton != null)
                singlePlayerButton.Pressed += () => EmitSignal(SignalName.SinglePlayerRequested);
            if (multiplayerButton != null)
                multiplayerButton.Pressed += () => EmitSignal(SignalName.MultiplayerRequested);
            if (settingsButton != null)
                settingsButton.Pressed += () => EmitSignal(SignalName.SettingsRequested);
            if (exitButton != null)
                exitButton.Pressed += () => EmitSignal(SignalName.ExitRequested);
            _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        }

        public void SetStatusText(string text)
        {
            if (_statusLabel != null)
                _statusLabel.Text = text ?? string.Empty;
        }
    }
}
