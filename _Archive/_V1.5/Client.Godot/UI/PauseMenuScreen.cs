using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class PauseMenuScreen : MenuScreenBase
    {
        [Signal] public delegate void ResumeRequestedEventHandler();
        [Signal] public delegate void SettingsRequestedEventHandler();
        [Signal] public delegate void ReturnToCharacterSelectRequestedEventHandler();
        [Signal] public delegate void ReturnToMainMenuRequestedEventHandler();

        [Export] public NodePath? ResumeButtonPath { get; set; }
        [Export] public NodePath? SettingsButtonPath { get; set; }
        [Export] public NodePath? ReturnToCharacterSelectButtonPath { get; set; }
        [Export] public NodePath? ReturnToMainMenuButtonPath { get; set; }
        [Export] public NodePath? StatusLabelPath { get; set; }

        private Label? _statusLabel;

        public override void _Ready()
        {
            Button? resumeButton = GetNodeOrNull<Button>(ResumeButtonPath);
            Button? settingsButton = GetNodeOrNull<Button>(SettingsButtonPath);
            Button? returnToCharacterSelectButton = GetNodeOrNull<Button>(ReturnToCharacterSelectButtonPath);
            Button? returnToMainMenuButton = GetNodeOrNull<Button>(ReturnToMainMenuButtonPath);
            _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);

            if (resumeButton != null)
                resumeButton.Pressed += () => EmitSignal(SignalName.ResumeRequested);
            if (settingsButton != null)
                settingsButton.Pressed += () => EmitSignal(SignalName.SettingsRequested);
            if (returnToCharacterSelectButton != null)
                returnToCharacterSelectButton.Pressed += () => EmitSignal(SignalName.ReturnToCharacterSelectRequested);
            if (returnToMainMenuButton != null)
                returnToMainMenuButton.Pressed += () => EmitSignal(SignalName.ReturnToMainMenuRequested);
        }

        public void SetStatusText(string text)
        {
            if (_statusLabel != null)
                _statusLabel.Text = text ?? string.Empty;
        }
    }
}
