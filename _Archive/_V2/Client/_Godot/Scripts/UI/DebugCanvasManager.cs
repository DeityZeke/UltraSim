using Godot;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class DebugCanvasManager : CanvasLayer
{
    private Label? _statusLabel;

    public override void _Ready()
    {
        _statusLabel = GetNodeOrNull<Label>("DebugLabel");
        if (_statusLabel is null)
        {
            _statusLabel = new Label
            {
                Name = "DebugLabel",
                Text = "Debug",
                Position = new Vector2(16, 44)
            };
            AddChild(_statusLabel);
        }
    }

    public void SetStatusText(string text)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = $"Debug: {text}";
        }
    }
}
