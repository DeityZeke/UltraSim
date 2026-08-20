using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class SessionStatusScreen : MenuScreenBase
    {
        [Signal] public delegate void RetryRequestedEventHandler();
        [Signal] public delegate void EditConnectionRequestedEventHandler();
        [Signal] public delegate void ReturnToMainMenuRequestedEventHandler();

        [Export] public NodePath? TitleLabelPath { get; set; }
        [Export] public NodePath? DetailLabelPath { get; set; }
        [Export] public NodePath? RetryButtonPath { get; set; }
        [Export] public NodePath? EditConnectionButtonPath { get; set; }
        [Export] public NodePath? ReturnToMainMenuButtonPath { get; set; }

        private Label? _titleLabel;
        private Label? _detailLabel;
        private Button? _retryButton;

        public override void _Ready()
        {
            _titleLabel = GetNodeOrNull<Label>(TitleLabelPath);
            _detailLabel = GetNodeOrNull<Label>(DetailLabelPath);
            _retryButton = GetNodeOrNull<Button>(RetryButtonPath);

            if (_retryButton != null)
                _retryButton.Pressed += () => EmitSignal(SignalName.RetryRequested);

            Button? editConnectionButton = GetNodeOrNull<Button>(EditConnectionButtonPath);
            if (editConnectionButton != null)
                editConnectionButton.Pressed += () => EmitSignal(SignalName.EditConnectionRequested);

            Button? returnToMainMenuButton = GetNodeOrNull<Button>(ReturnToMainMenuButtonPath);
            if (returnToMainMenuButton != null)
                returnToMainMenuButton.Pressed += () => EmitSignal(SignalName.ReturnToMainMenuRequested);
        }

        public void SetContent(string title, string detail, bool allowRetry)
        {
            if (_titleLabel != null)
                _titleLabel.Text = title ?? string.Empty;

            if (_detailLabel != null)
                _detailLabel.Text = detail ?? string.Empty;

            if (_retryButton != null)
                _retryButton.Disabled = !allowRetry;
        }
    }
}
