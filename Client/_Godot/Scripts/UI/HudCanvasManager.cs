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

        PlaceLabel(_statusLabel, new Vector2(16, 16), new Vector2(560, 22));

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

        PlaceLabel(_networkSummaryLabel, new Vector2(16, 40), new Vector2(760, 22));
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

    private static void PlaceLabel(Label label, Vector2 position, Vector2 size)
    {
        label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        label.OffsetLeft = position.X;
        label.OffsetTop = position.Y;
        label.OffsetRight = position.X + size.X;
        label.OffsetBottom = position.Y + size.Y;
    }
}
