using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class ClientRuntimeHud : Control
    {
        [Export] public NodePath? RuntimeRootPath { get; set; }
        [Export] public NodePath? StatusLabelPath { get; set; }
        [Export] public NodePath? PingLabelPath { get; set; }

        private GodotClientRuntimeRoot? _runtimeRoot;
        private Label? _statusLabel;
        private Label? _pingLabel;

        public override void _Ready()
        {
            _runtimeRoot = RuntimeRootPath == null || RuntimeRootPath.IsEmpty
                ? null
                : GetNodeOrNull<GodotClientRuntimeRoot>(RuntimeRootPath);
            _statusLabel = StatusLabelPath == null || StatusLabelPath.IsEmpty
                ? null
                : GetNodeOrNull<Label>(StatusLabelPath);
            _pingLabel = PingLabelPath == null || PingLabelPath.IsEmpty
                ? null
                : GetNodeOrNull<Label>(PingLabelPath);

            UpdateLabels();
        }

        public override void _Process(double delta)
        {
            UpdateLabels();
        }

        private void UpdateLabels()
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = BuildStatusText();
                _statusLabel.Modulate = BuildStatusColor();
            }

            if (_pingLabel != null)
            {
                _pingLabel.Text = BuildPingText();
                _pingLabel.Modulate = BuildPingColor();
            }
        }

        private string BuildStatusText()
        {
            if (_runtimeRoot == null)
                return "status: no runtime";

            if (_runtimeRoot.CurrentAppState == ClientAppState.InGame &&
                !string.IsNullOrWhiteSpace(_runtimeRoot.CurrentAppStateDetail))
            {
                return $"status: in game\ncharacter: {_runtimeRoot.CurrentAppStateDetail}";
            }

            return $"status: {BuildAppStateText()}";
        }

        private string BuildPingText()
        {
            if (_runtimeRoot == null || _runtimeRoot.SessionMode != ClientSessionDriverMode.Network)
                return "ping: n/a";

            if (!_runtimeRoot.IsNetworkConnected)
                return "ping: connecting";

            return $"ping: last {_runtimeRoot.LastPingMs:0.0} ms  avg {_runtimeRoot.SmoothedPingMs:0.0} ms";
        }

        private Color BuildPingColor()
        {
            if (_runtimeRoot == null || _runtimeRoot.CurrentAppState != ClientAppState.InGame)
                return Colors.White;

            float ping = _runtimeRoot.SmoothedPingMs;
            if (ping <= 30.0f)
                return Colors.Gold;
            if (ping <= 45.0f)
                return Colors.LightGreen;
            if (ping <= 60.0f)
                return new Color(0.68f, 0.85f, 0.18f, 1.0f);
            if (ping <= 75.0f)
                return Colors.Yellow;
            if (ping <= 90.0f)
                return new Color(0.8f, 0.33f, 0.0f, 1.0f);
            return Colors.IndianRed;
        }

        private Color BuildStatusColor()
        {
            if (_runtimeRoot == null)
                return Colors.White;

            return _runtimeRoot.CurrentAppState switch
            {
                ClientAppState.InGame => Colors.LightGreen,
                ClientAppState.Connecting => Colors.Khaki,
                ClientAppState.Disconnected => new Color(0.95f, 0.55f, 0.2f, 1.0f),
                ClientAppState.Error => Colors.IndianRed,
                _ => Colors.White,
            };
        }
        private string BuildAppStateText()
        {
            if (_runtimeRoot == null)
                return "no runtime";

            return _runtimeRoot.CurrentAppState switch
            {
                ClientAppState.Preview => "preview",
                ClientAppState.Boot => "boot",
                _ => _runtimeRoot.CurrentAppStateDetail,
            };
        }
    }
}
