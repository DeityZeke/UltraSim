using Godot;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class HudCanvasManager : CanvasLayer
{
    private Label? _statusLabel;

    public override void _Ready()
    {
        _statusLabel = GetNodeOrNull<Label>("StatusLabel");
        if (_statusLabel is null)
        {
            _statusLabel = new Label
            {
                Name = "StatusLabel",
                Text = "UltraSim",
                Position = new Vector2(16, 16)
            };
            AddChild(_statusLabel);
        }
    }

    public void SetStatusText(string text)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = text;
        }
    }
}
