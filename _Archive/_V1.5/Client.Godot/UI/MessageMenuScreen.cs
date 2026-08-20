using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class MessageMenuScreen : MenuScreenBase
    {
        [Signal] public delegate void BackRequestedEventHandler();

        [Export] public NodePath? TitleLabelPath { get; set; }
        [Export] public NodePath? MessageLabelPath { get; set; }
        [Export] public NodePath? BackButtonPath { get; set; }

        private Label? _titleLabel;
        private Label? _messageLabel;

        public override void _Ready()
        {
            _titleLabel = GetNodeOrNull<Label>(TitleLabelPath);
            _messageLabel = GetNodeOrNull<Label>(MessageLabelPath);

            Button? backButton = GetNodeOrNull<Button>(BackButtonPath);
            if (backButton != null)
                backButton.Pressed += () => EmitSignal(SignalName.BackRequested);
        }

        public void SetContent(string title, string message)
        {
            if (_titleLabel != null)
                _titleLabel.Text = title ?? string.Empty;

            if (_messageLabel != null)
                _messageLabel.Text = message ?? string.Empty;
        }
    }
}
