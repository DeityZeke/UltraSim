using Godot;
using System.Globalization;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class MultiplayerMenuScreen : MenuScreenBase
    {
        [Signal] public delegate void BackRequestedEventHandler();
        [Signal] public delegate void ConnectRequestedEventHandler(string host, int port);
        [Signal] public delegate void ProviderPreviousRequestedEventHandler();
        [Signal] public delegate void ProviderNextRequestedEventHandler();
        [Signal] public delegate void RefreshRequestedEventHandler();
        [Signal] public delegate void ServerRowSelectedEventHandler(long index);
        [Signal] public delegate void ServerRowActivatedEventHandler(long index);
        [Signal] public delegate void DirectConnectRequestedEventHandler();
        [Signal] public delegate void FiltersChangedEventHandler();

        [Export] public NodePath? ProviderLabelPath { get; set; }
        [Export] public NodePath? SearchInputPath { get; set; }
        [Export] public NodePath? RegionInputPath { get; set; }
        [Export] public NodePath? LanguageInputPath { get; set; }
        [Export] public NodePath? TrustFilterPath { get; set; }
        [Export] public NodePath? PasswordFilterPath { get; set; }
        [Export] public NodePath? WhitelistFilterPath { get; set; }
        [Export] public NodePath? ServerListPath { get; set; }
        [Export] public NodePath? ServerDetailLabelPath { get; set; }
        [Export] public NodePath? StatusLabelPath { get; set; }
        [Export] public NodePath? JoinButtonPath { get; set; }
        [Export] public NodePath? BackButtonPath { get; set; }
        [Export] public NodePath? DirectConnectButtonPath { get; set; }
        [Export] public NodePath? ProviderPreviousButtonPath { get; set; }
        [Export] public NodePath? ProviderNextButtonPath { get; set; }
        [Export] public NodePath? RefreshButtonPath { get; set; }
        [Export] public NodePath? DirectConnectOverlayPath { get; set; }
        [Export] public NodePath? DirectConnectHostInputPath { get; set; }
        [Export] public NodePath? DirectConnectPortInputPath { get; set; }
        [Export] public NodePath? DirectConnectStatusLabelPath { get; set; }
        [Export] public NodePath? DirectConnectConnectButtonPath { get; set; }
        [Export] public NodePath? DirectConnectBackButtonPath { get; set; }

        private Label? _providerLabel;
        private LineEdit? _searchInput;
        private LineEdit? _regionInput;
        private LineEdit? _languageInput;
        private OptionButton? _trustFilter;
        private OptionButton? _passwordFilter;
        private OptionButton? _whitelistFilter;
        private ItemList? _serverList;
        private Label? _serverDetailLabel;
        private Label? _statusLabel;
        private Button? _joinButton;
        private Control? _directConnectOverlay;
        private LineEdit? _directConnectHostInput;
        private LineEdit? _directConnectPortInput;
        private Label? _directConnectStatusLabel;
        private Button? _directConnectConnectButton;
        private string _selectedJoinHost = string.Empty;
        private int _selectedJoinPort;

        public override void _Ready()
        {
            _providerLabel = GetNodeOrNull<Label>(ProviderLabelPath);
            _searchInput = GetNodeOrNull<LineEdit>(SearchInputPath);
            _regionInput = GetNodeOrNull<LineEdit>(RegionInputPath);
            _languageInput = GetNodeOrNull<LineEdit>(LanguageInputPath);
            _trustFilter = GetNodeOrNull<OptionButton>(TrustFilterPath);
            _passwordFilter = GetNodeOrNull<OptionButton>(PasswordFilterPath);
            _whitelistFilter = GetNodeOrNull<OptionButton>(WhitelistFilterPath);
            _serverList = GetNodeOrNull<ItemList>(ServerListPath);
            _serverDetailLabel = GetNodeOrNull<Label>(ServerDetailLabelPath);
            _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
            _joinButton = GetNodeOrNull<Button>(JoinButtonPath);
            Button? backButton = GetNodeOrNull<Button>(BackButtonPath);
            Button? directConnectButton = GetNodeOrNull<Button>(DirectConnectButtonPath);
            Button? previousProviderButton = GetNodeOrNull<Button>(ProviderPreviousButtonPath);
            Button? nextProviderButton = GetNodeOrNull<Button>(ProviderNextButtonPath);
            Button? refreshButton = GetNodeOrNull<Button>(RefreshButtonPath);
            _directConnectOverlay = GetNodeOrNull<Control>(DirectConnectOverlayPath);
            _directConnectHostInput = GetNodeOrNull<LineEdit>(DirectConnectHostInputPath);
            _directConnectPortInput = GetNodeOrNull<LineEdit>(DirectConnectPortInputPath);
            _directConnectStatusLabel = GetNodeOrNull<Label>(DirectConnectStatusLabelPath);
            _directConnectConnectButton = GetNodeOrNull<Button>(DirectConnectConnectButtonPath);
            Button? directConnectBackButton = GetNodeOrNull<Button>(DirectConnectBackButtonPath);

            if (_joinButton != null)
                _joinButton.Pressed += EmitJoinRequested;
            if (backButton != null)
                backButton.Pressed += () => EmitSignal(SignalName.BackRequested);
            if (directConnectButton != null)
                directConnectButton.Pressed += () => EmitSignal(SignalName.DirectConnectRequested);
            if (previousProviderButton != null)
                previousProviderButton.Pressed += () => EmitSignal(SignalName.ProviderPreviousRequested);
            if (nextProviderButton != null)
                nextProviderButton.Pressed += () => EmitSignal(SignalName.ProviderNextRequested);
            if (refreshButton != null)
                refreshButton.Pressed += () => EmitSignal(SignalName.RefreshRequested);
            if (_serverList != null)
            {
                _serverList.ItemSelected += index => EmitSignal(SignalName.ServerRowSelected, index);
                _serverList.ItemActivated += index => EmitSignal(SignalName.ServerRowActivated, index);
            }
            ConfigureFilterControls();
            if (_directConnectConnectButton != null)
                _directConnectConnectButton.Pressed += EmitDirectConnectRequested;
            if (directConnectBackButton != null)
                directConnectBackButton.Pressed += HideDirectConnectDialog;
            if (_directConnectHostInput != null)
                _directConnectHostInput.TextSubmitted += _ => EmitDirectConnectRequested();
            if (_directConnectPortInput != null)
                _directConnectPortInput.TextSubmitted += _ => EmitDirectConnectRequested();

            HideDirectConnectDialog();
            SetJoinTarget(string.Empty, 0, enabled: false);
        }

        public string GetSearchText() => _searchInput?.Text?.Trim() ?? string.Empty;

        public string GetRegionText() => _regionInput?.Text?.Trim() ?? string.Empty;

        public string GetLanguageText() => _languageInput?.Text?.Trim() ?? string.Empty;

        public int GetSelectedTrustFilterIndex() => _trustFilter?.Selected ?? 0;

        public int GetSelectedPasswordFilterIndex() => _passwordFilter?.Selected ?? 0;

        public int GetSelectedWhitelistFilterIndex() => _whitelistFilter?.Selected ?? 0;

        public void SetFiltersEnabled(bool enabled)
        {
            if (_searchInput != null)
                _searchInput.Editable = enabled;
            if (_regionInput != null)
                _regionInput.Editable = enabled;
            if (_languageInput != null)
                _languageInput.Editable = enabled;
            if (_trustFilter != null)
                _trustFilter.Disabled = !enabled;
            if (_passwordFilter != null)
                _passwordFilter.Disabled = !enabled;
            if (_whitelistFilter != null)
                _whitelistFilter.Disabled = !enabled;
        }

        public void SetDirectConnectDefaults(string host, int port)
        {
            if (_directConnectHostInput != null)
                _directConnectHostInput.Text = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            if (_directConnectPortInput != null)
                _directConnectPortInput.Text = port.ToString(CultureInfo.InvariantCulture);
        }

        public void SetStatusText(string text)
        {
            if (_statusLabel != null)
                _statusLabel.Text = text ?? string.Empty;
        }

        public void SetProviderText(string text)
        {
            if (_providerLabel != null)
                _providerLabel.Text = text ?? string.Empty;
        }

        public void SetServerListEntries(System.Collections.Generic.IReadOnlyList<string> entries, int selectedIndex = -1)
        {
            if (_serverList == null)
                return;

            _serverList.Clear();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                    _serverList.AddItem(entries[i] ?? string.Empty);
            }

            if (selectedIndex >= 0 && selectedIndex < _serverList.ItemCount)
                _serverList.Select(selectedIndex);
        }

        public void SetServerDetailText(string text)
        {
            if (_serverDetailLabel != null)
                _serverDetailLabel.Text = text ?? string.Empty;
        }

        public void SetJoinTarget(string host, int port, bool enabled)
        {
            _selectedJoinHost = enabled ? (host ?? string.Empty) : string.Empty;
            _selectedJoinPort = enabled ? port : 0;

            if (_joinButton != null)
                _joinButton.Disabled = !enabled;
        }

        public void ShowDirectConnectDialog()
        {
            if (_directConnectOverlay != null)
                _directConnectOverlay.Visible = true;

            if (_directConnectHostInput != null)
                _directConnectHostInput.CallDeferred(Control.MethodName.GrabFocus);
        }

        public void HideDirectConnectDialog()
        {
            if (_directConnectOverlay != null)
                _directConnectOverlay.Visible = false;
        }

        public void SetDirectConnectStatusText(string text)
        {
            if (_directConnectStatusLabel != null)
                _directConnectStatusLabel.Text = text ?? string.Empty;
        }

        public void SetBusy(bool busy)
        {
            if (_joinButton != null && string.IsNullOrWhiteSpace(_selectedJoinHost))
                _joinButton.Disabled = true;
            else if (_joinButton != null)
                _joinButton.Disabled = busy;

            if (_directConnectHostInput != null)
                _directConnectHostInput.Editable = !busy;
            if (_directConnectPortInput != null)
                _directConnectPortInput.Editable = !busy;
            if (_directConnectConnectButton != null)
                _directConnectConnectButton.Disabled = busy;
        }

        private void EmitJoinRequested()
        {
            if (string.IsNullOrWhiteSpace(_selectedJoinHost) || _selectedJoinPort <= 0)
                return;

            EmitSignal(SignalName.ConnectRequested, _selectedJoinHost, _selectedJoinPort);
        }

        private void EmitDirectConnectRequested()
        {
            string host = _directConnectHostInput?.Text?.Trim() ?? string.Empty;
            if (!int.TryParse(_directConnectPortInput?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
                port = 7777;
            EmitSignal(SignalName.ConnectRequested, host, port);
        }

        private void ConfigureFilterControls()
        {
            if (_trustFilter != null)
            {
                _trustFilter.Clear();
                _trustFilter.AddItem("Trust: Any");
                _trustFilter.AddItem("Trust: Unverified");
                _trustFilter.AddItem("Trust: Verified");
                _trustFilter.AddItem("Trust: Official");
                _trustFilter.ItemSelected += _ => EmitSignal(SignalName.FiltersChanged);
            }

            if (_passwordFilter != null)
            {
                _passwordFilter.Clear();
                _passwordFilter.AddItem("Password: Any");
                _passwordFilter.AddItem("Password: Open");
                _passwordFilter.AddItem("Password: Required");
                _passwordFilter.ItemSelected += _ => EmitSignal(SignalName.FiltersChanged);
            }

            if (_whitelistFilter != null)
            {
                _whitelistFilter.Clear();
                _whitelistFilter.AddItem("Whitelist: Any");
                _whitelistFilter.AddItem("Whitelist: Off");
                _whitelistFilter.AddItem("Whitelist: On");
                _whitelistFilter.ItemSelected += _ => EmitSignal(SignalName.FiltersChanged);
            }

            if (_searchInput != null)
                _searchInput.TextChanged += _ => EmitSignal(SignalName.FiltersChanged);
            if (_regionInput != null)
                _regionInput.TextChanged += _ => EmitSignal(SignalName.FiltersChanged);
            if (_languageInput != null)
                _languageInput.TextChanged += _ => EmitSignal(SignalName.FiltersChanged);
        }
    }
}
