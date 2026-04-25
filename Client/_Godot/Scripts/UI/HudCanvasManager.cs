using Godot;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class HudCanvasManager : CanvasLayer
{
    private Label? _statusLabel;
    private Label? _networkSummaryLabel;

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

        _statusLabel.Position = new Vector2(16, 16);
        _statusLabel.Size = new Vector2(560, 22);

        _networkSummaryLabel = GetNodeOrNull<Label>("NetworkSummaryLabel");
        if (_networkSummaryLabel is null)
        {
            _networkSummaryLabel = new Label
            {
                Name = "NetworkSummaryLabel",
                Text = "Network: offline",
                Position = new Vector2(16, 40)
            };
            AddChild(_networkSummaryLabel);
        }

        _networkSummaryLabel.Position = new Vector2(16, 40);
        _networkSummaryLabel.Size = new Vector2(760, 22);
    }

    public void SetStatusText(string text)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = text;
        }
    }

    public void SetNetworkSummaryText(string text)
    {
        if (_networkSummaryLabel is not null)
        {
            _networkSummaryLabel.Text = text;
        }
    }
}
