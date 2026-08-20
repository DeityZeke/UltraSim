using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;
using UltraSim.Client.Runtime.Discovery;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class ClientAppShell : Control
    {
        private static readonly StringName StartMenuScreenId = "start_menu";
        private static readonly StringName MultiplayerScreenId = "multiplayer";
        private static readonly StringName SinglePlayerMessageScreenId = "single_player_message";
        private static readonly StringName PauseScreenId = "pause";
        private static readonly StringName SettingsScreenId = "settings";
        private static readonly StringName SessionStatusScreenId = "session_status";

        [Export] public NodePath? RuntimeRootPath { get; set; }
        [Export] public NodePath? UiManagerPath { get; set; }
        [Export] public string OfficialRegistryBaseUrl { get; set; } = "http://127.0.0.1:5180";
        [Export] public bool EnableMultiplayerAutoRefresh { get; set; } = true;
        [Export] public float MultiplayerAutoRefreshEverySeconds { get; set; } = 10f;

        private GodotClientRuntimeRoot? _runtimeRoot;
        private UiManager? _uiManager;
        private MenuCanvasManager? _menuCanvas;
        private StartMenuScreen? _startMenu;
        private MultiplayerMenuScreen? _multiplayerMenu;
        private MessageMenuScreen? _singlePlayerMessage;
        private PauseMenuScreen? _pauseMenu;
        private SettingsMenuScreen? _settingsMenu;
        private SessionStatusScreen? _sessionStatus;
        private ClientMultiplayerBrowserController? _multiplayerBrowser;
        private HttpClient? _officialRegistryHttpClient;
        private OfficialRegistryDiscoveryQuery? _officialRegistryQuery;
        private CancellationTokenSource? _multiplayerRefreshCancellation;
        private Task<ServerBrowserSnapshot>? _multiplayerRefreshTask;
        private double _multiplayerAutoRefreshAccumulatorSeconds;
        private bool _returnSettingsToPauseMenu;

        public override void _Ready()
        {
            _runtimeRoot = GetNodeOrNull<GodotClientRuntimeRoot>(RuntimeRootPath);
            _uiManager = GetNodeOrNull<UiManager>(UiManagerPath);
            _menuCanvas = GetParent() as MenuCanvasManager ?? _uiManager?.MenuCanvas;
            _multiplayerBrowser = new ClientMultiplayerBrowserController(CreateOfficialRegistryQuery());

            _startMenu = _menuCanvas?.GetScreen<StartMenuScreen>(StartMenuScreenId);
            _multiplayerMenu = _menuCanvas?.GetScreen<MultiplayerMenuScreen>(MultiplayerScreenId);
            _singlePlayerMessage = _menuCanvas?.GetScreen<MessageMenuScreen>(SinglePlayerMessageScreenId);
            _pauseMenu = _menuCanvas?.GetScreen<PauseMenuScreen>(PauseScreenId);
            _settingsMenu = _menuCanvas?.GetScreen<SettingsMenuScreen>(SettingsScreenId);
            _sessionStatus = _menuCanvas?.GetScreen<SessionStatusScreen>(SessionStatusScreenId);

            if (_startMenu != null)
            {
                _startMenu.SinglePlayerRequested += OnSinglePlayerRequested;
                _startMenu.MultiplayerRequested += OnMultiplayerRequested;
                _startMenu.SettingsRequested += OnSettingsRequested;
                _startMenu.ExitRequested += () => GetTree().Quit();
            }

            if (_multiplayerMenu != null)
            {
                _multiplayerMenu.BackRequested += ShowStartMenu;
                _multiplayerMenu.ConnectRequested += OnConnectRequested;
                _multiplayerMenu.ProviderPreviousRequested += OnMultiplayerPreviousProviderRequested;
                _multiplayerMenu.ProviderNextRequested += OnMultiplayerNextProviderRequested;
                _multiplayerMenu.RefreshRequested += OnMultiplayerRefreshRequested;
                _multiplayerMenu.ServerRowSelected += OnMultiplayerServerRowSelected;
                _multiplayerMenu.ServerRowActivated += OnMultiplayerServerRowActivated;
                _multiplayerMenu.DirectConnectRequested += OnMultiplayerDirectConnectRequested;
                _multiplayerMenu.FiltersChanged += OnMultiplayerFiltersChanged;
                if (_runtimeRoot != null)
                    _multiplayerMenu.SetDirectConnectDefaults(_runtimeRoot.NetworkHost, _runtimeRoot.NetworkPort);
            }

            if (_singlePlayerMessage != null)
                _singlePlayerMessage.BackRequested += ShowStartMenu;

            if (_pauseMenu != null)
            {
                _pauseMenu.ResumeRequested += ResumeFromPause;
                _pauseMenu.SettingsRequested += OnPauseSettingsRequested;
                _pauseMenu.ReturnToCharacterSelectRequested += OnReturnToCharacterSelectRequested;
                _pauseMenu.ReturnToMainMenuRequested += OnReturnToMainMenuRequested;
            }

            if (_settingsMenu != null)
                _settingsMenu.BackRequested += OnSettingsBackRequested;

            if (_sessionStatus != null)
            {
                _sessionStatus.RetryRequested += OnRetryNetworkRequested;
                _sessionStatus.EditConnectionRequested += OnEditConnectionRequested;
                _sessionStatus.ReturnToMainMenuRequested += OnReturnToMainMenuRequested;
            }

        }

        private void OnSinglePlayerRequested()
        {
            _singlePlayerMessage?.SetContent(
                "Single Player",
                "Single player is not implemented yet.\nCheck back later.");
            PushScreen(SinglePlayerMessageScreenId);
        }

        private void OnMultiplayerRequested()
        {
            _multiplayerAutoRefreshAccumulatorSeconds = 0d;
            PushScreen(MultiplayerScreenId);
            RefreshMultiplayerBrowser();
        }

        private void OnSettingsRequested()
        {
            _returnSettingsToPauseMenu = false;
            PushScreen(SettingsScreenId);
        }

        private void OnPauseSettingsRequested()
        {
            _returnSettingsToPauseMenu = true;
            PushScreen(SettingsScreenId);
        }

        private void OnConnectRequested(string host, int port)
        {
            if (_runtimeRoot == null)
                return;

            _multiplayerBrowser?.SetManualEndpoint(host, port);
            _multiplayerMenu?.SetBusy(true);
            _multiplayerMenu?.SetStatusText("connecting...");
            _multiplayerMenu?.SetDirectConnectStatusText("connecting...");
            _multiplayerMenu?.HideDirectConnectDialog();
            _runtimeRoot.StartNetworkSession(host, port);
            _runtimeRoot.SetGameplayInputBlocked(true);
        }

        public void ShowStartMenu()
        {
            ShowStartMenu(stopSession: true);
        }

        public void ShowStartMenu(bool stopSession)
        {
            if (stopSession && _runtimeRoot?.IsSessionRunning == true)
                _runtimeRoot.StopSession();

            _returnSettingsToPauseMenu = false;
            _startMenu?.SetStatusText("select a mode");
            ShowRootScreen(StartMenuScreenId);
        }

        public void ShowPauseMenu(string detail)
        {
            _returnSettingsToPauseMenu = false;
            _pauseMenu?.SetStatusText(detail);
            ReplaceScreen(PauseScreenId);
        }

        public void ShowConnecting(string detail)
        {
            _multiplayerMenu?.SetBusy(true);
            _multiplayerMenu?.SetStatusText(detail);
            ShowRootScreen(MultiplayerScreenId);
        }

        public void ShowSessionStatus(string title, string detail, bool allowRetry)
        {
            _sessionStatus?.SetContent(title, detail, allowRetry);
            ShowRootScreen(SessionStatusScreenId);
        }

        public void ResumeFromPause()
        {
            _returnSettingsToPauseMenu = false;
            HideMenusAndReturnToHud();
            _menuCanvas?.ShowSharedChat();
        }

        private void OnReturnToMainMenuRequested()
        {
            if (_runtimeRoot?.IsSessionRunning == true)
                _runtimeRoot.StopSession();

            ShowStartMenu(stopSession: false);
        }

        private void OnReturnToCharacterSelectRequested()
        {
            if (_runtimeRoot == null)
                return;

            if (_runtimeRoot.RequestReturnToCharacterSelect())
            {
                _returnSettingsToPauseMenu = false;
                _menuCanvas?.HideMenus(clearHistory: true);
                _menuCanvas?.HideSharedChat();
            }
        }

        private void OnRetryNetworkRequested()
        {
            if (_runtimeRoot == null)
                return;

            _runtimeRoot.StartNetworkSession(_runtimeRoot.NetworkHost, _runtimeRoot.NetworkPort);
        }

        private void OnEditConnectionRequested()
        {
            RefreshMultiplayerBrowser();
            PushScreen(MultiplayerScreenId);
        }

        private void OnMultiplayerPreviousProviderRequested()
        {
            _multiplayerBrowser?.SelectPreviousProvider();
            RefreshMultiplayerBrowser();
        }

        private void OnMultiplayerNextProviderRequested()
        {
            _multiplayerBrowser?.SelectNextProvider();
            RefreshMultiplayerBrowser();
        }

        private void OnMultiplayerRefreshRequested()
        {
            _multiplayerAutoRefreshAccumulatorSeconds = 0d;
            RefreshMultiplayerBrowser();
        }

        private void OnMultiplayerFiltersChanged()
        {
            _multiplayerAutoRefreshAccumulatorSeconds = 0d;
            RefreshMultiplayerBrowser(showBusy: false);
        }

        private void OnMultiplayerServerRowSelected(long index)
        {
            if (_multiplayerBrowser == null)
                return;

            if (_multiplayerBrowser.SelectRow((int)index))
                RenderMultiplayerSnapshot(_multiplayerBrowser.GetSnapshot());
        }

        private void OnMultiplayerServerRowActivated(long index)
        {
            if (_multiplayerBrowser == null)
                return;

            if (!_multiplayerBrowser.SelectRow((int)index))
                return;

            ServerBrowserSnapshot snapshot = _multiplayerBrowser.GetSnapshot();
            RenderMultiplayerSnapshot(snapshot);

            if (_multiplayerBrowser.TryGetSelectedConnectTarget(snapshot, out string host, out int port))
                OnConnectRequested(host, port);
        }

        private void OnMultiplayerDirectConnectRequested()
        {
            if (_runtimeRoot != null)
                _multiplayerMenu?.SetDirectConnectDefaults(_runtimeRoot.NetworkHost, _runtimeRoot.NetworkPort);

            _multiplayerMenu?.SetDirectConnectStatusText("Enter an IP and port.");
            _multiplayerMenu?.ShowDirectConnectDialog();
        }

        private void RefreshMultiplayerBrowser(bool showBusy = true)
        {
            if (_multiplayerMenu == null)
                return;

            PrepareMultiplayerBrowserInputs();

            if (_multiplayerBrowser == null)
            {
                RenderMultiplayerUnavailable();
                return;
            }

            if (showBusy)
            {
                _multiplayerMenu.SetBusy(true);
                _multiplayerMenu.SetStatusText("refreshing server list...");
            }

            CancelPendingMultiplayerRefresh();
            _multiplayerRefreshCancellation = new CancellationTokenSource();
            _multiplayerRefreshTask = _multiplayerBrowser.RefreshAsync(_multiplayerRefreshCancellation.Token).AsTask();
        }

        public override void _Process(double delta)
        {
            PumpMultiplayerRefresh();
            PumpMultiplayerAutoRefresh(delta);
        }

        public override void _ExitTree()
        {
            CancelPendingMultiplayerRefresh();
            _officialRegistryHttpClient?.Dispose();
            _officialRegistryHttpClient = null;
        }

        private void SeedStubOfficialRows(string host, int port)
        {
            if (_multiplayerBrowser == null)
                return;

            string resolvedHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            int resolvedPort = port <= 0 ? 7777 : port;

            _multiplayerBrowser.SetOfficialRows(new[]
            {
                new ServerDiscoveryRow(
                    ProviderId: "official",
                    ProviderKind: ServerDiscoveryProviderKind.Official,
                    ServerId: "local-preview",
                    DisplayName: "Local Preview",
                    CurrentUsers: 0,
                    PeakUsers: 12,
                    Region: "local",
                    Uptime: TimeSpan.FromMinutes(98),
                    ConnectHint: $"{resolvedHost}:{resolvedPort}"),
            });
        }

        private static string GetSelectedProviderText(ServerBrowserSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.Providers.Count; i++)
            {
                if (snapshot.Providers[i].IsSelected)
                    return snapshot.Providers[i].DisplayName;
            }

            return snapshot.ActiveProviderId;
        }

        private void PrepareMultiplayerBrowserInputs()
        {
            ApplyOfficialRegistryQueryFilters();

            if (_runtimeRoot != null)
            {
                _multiplayerBrowser?.SetManualEndpoint(_runtimeRoot.NetworkHost, _runtimeRoot.NetworkPort);
                if (_multiplayerBrowser?.CanInjectOfficialRows == true)
                    SeedStubOfficialRows(_runtimeRoot.NetworkHost, _runtimeRoot.NetworkPort);
                _multiplayerMenu?.SetDirectConnectDefaults(_runtimeRoot.NetworkHost, _runtimeRoot.NetworkPort);
            }
        }

        private void RenderMultiplayerUnavailable()
        {
            if (_multiplayerMenu == null)
                return;

            _multiplayerMenu.SetBusy(false);
            _multiplayerMenu.SetFiltersEnabled(false);
            _multiplayerMenu.SetProviderText("Manual");
            _multiplayerMenu.SetServerListEntries(new[] { "No servers available." }, selectedIndex: -1);
            _multiplayerMenu.SetServerDetailText("Select a provider or enter a direct connect target below.");
            _multiplayerMenu.SetJoinTarget(string.Empty, 0, enabled: false);
            _multiplayerMenu.SetStatusText("enter host and port");
        }

        private void RenderMultiplayerSnapshot(ServerBrowserSnapshot snapshot)
        {
            if (_multiplayerMenu == null || _multiplayerBrowser == null)
                return;

            _multiplayerMenu.SetBusy(false);
            _multiplayerMenu.SetProviderText(GetSelectedProviderText(snapshot));
            _multiplayerMenu.SetFiltersEnabled(string.Equals(snapshot.ActiveProviderId, "official", StringComparison.Ordinal));
            _multiplayerMenu.SetServerListEntries(
                _multiplayerBrowser.BuildServerListEntries(snapshot),
                _multiplayerBrowser.SelectedRowIndex);
            _multiplayerMenu.SetServerDetailText(_multiplayerBrowser.BuildServerDetailText(snapshot));
            _multiplayerMenu.SetStatusText(_multiplayerBrowser.BuildStatusText(snapshot));

            if (_multiplayerBrowser.TryGetSelectedConnectTarget(snapshot, out string selectedHost, out int selectedPort))
                _multiplayerMenu.SetJoinTarget(selectedHost, selectedPort, enabled: true);
            else
                _multiplayerMenu.SetJoinTarget(string.Empty, 0, enabled: false);
        }

        private void PumpMultiplayerRefresh()
        {
            if (_multiplayerRefreshTask == null || !_multiplayerRefreshTask.IsCompleted)
                return;

            Task<ServerBrowserSnapshot> completedTask = _multiplayerRefreshTask;
            CancellationTokenSource? completedCancellation = _multiplayerRefreshCancellation;
            _multiplayerRefreshTask = null;
            _multiplayerRefreshCancellation = null;
            completedCancellation?.Dispose();

            try
            {
                RenderMultiplayerSnapshot(completedTask.GetAwaiter().GetResult());
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (_multiplayerMenu == null)
                    return;

                _multiplayerMenu.SetBusy(false);
                _multiplayerMenu.SetStatusText("Could not connect to server registry. Please try again later.");
                _multiplayerMenu.SetServerListEntries(new[] { "Could not connect to server registry." }, selectedIndex: -1);
                _multiplayerMenu.SetServerDetailText("Please try again later or use direct connect.");
                _multiplayerMenu.SetJoinTarget(string.Empty, 0, enabled: false);
            }
        }

        private void PumpMultiplayerAutoRefresh(double delta)
        {
            if (!EnableMultiplayerAutoRefresh || MultiplayerAutoRefreshEverySeconds <= 0f)
                return;

            if (_menuCanvas?.CurrentScreenId != MultiplayerScreenId)
            {
                _multiplayerAutoRefreshAccumulatorSeconds = 0d;
                return;
            }

            if (_runtimeRoot?.IsSessionRunning == true)
            {
                _multiplayerAutoRefreshAccumulatorSeconds = 0d;
                return;
            }

            if (_multiplayerRefreshTask != null)
                return;

            _multiplayerAutoRefreshAccumulatorSeconds += Math.Max(0d, delta);
            if (_multiplayerAutoRefreshAccumulatorSeconds < Math.Max(1f, MultiplayerAutoRefreshEverySeconds))
                return;

            _multiplayerAutoRefreshAccumulatorSeconds = 0d;
            RefreshMultiplayerBrowser(showBusy: false);
        }

        private void CancelPendingMultiplayerRefresh()
        {
            _multiplayerRefreshCancellation?.Cancel();
            _multiplayerRefreshCancellation?.Dispose();
            _multiplayerRefreshCancellation = null;
            _multiplayerRefreshTask = null;
        }

        private IServerDiscoveryQuery? CreateOfficialRegistryQuery()
        {
            string? resolvedBaseUrl = ResolveOfficialRegistryBaseUrl();
            if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
                return null;

            _officialRegistryHttpClient = new HttpClient
            {
                BaseAddress = new Uri(resolvedBaseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(10),
            };

            _officialRegistryQuery = new OfficialRegistryDiscoveryQuery(_officialRegistryHttpClient);
            return _officialRegistryQuery;
        }

        private void ApplyOfficialRegistryQueryFilters()
        {
            if (_officialRegistryQuery == null || _multiplayerMenu == null)
                return;

            _officialRegistryQuery.SetQuery(new OfficialRegistryServerQuery
            {
                Search = EmptyToNull(_multiplayerMenu.GetSearchText()),
                Region = EmptyToNull(_multiplayerMenu.GetRegionText()),
                Language = EmptyToNull(_multiplayerMenu.GetLanguageText()),
                TrustLevel = ResolveTrustLevelFilter(_multiplayerMenu.GetSelectedTrustFilterIndex()),
                PasswordRequired = ResolveTriStateFilter(_multiplayerMenu.GetSelectedPasswordFilterIndex()),
                WhitelistOnly = ResolveTriStateFilter(_multiplayerMenu.GetSelectedWhitelistFilterIndex()),
                SortBy = "players",
                Descending = true,
                Limit = 100,
            });
        }

        private static string? EmptyToNull(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static ServerListingTrustLevel? ResolveTrustLevelFilter(int selectedIndex)
        {
            return selectedIndex switch
            {
                1 => ServerListingTrustLevel.Unverified,
                2 => ServerListingTrustLevel.Verified,
                3 => ServerListingTrustLevel.Official,
                _ => null,
            };
        }

        private static bool? ResolveTriStateFilter(int selectedIndex)
        {
            return selectedIndex switch
            {
                1 => false,
                2 => true,
                _ => null,
            };
        }

        private string? ResolveOfficialRegistryBaseUrl()
        {
            string configured = OfficialRegistryBaseUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configured))
                return TryBuildRegistryBaseUrlFromNetworkHost();

            if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri? configuredUri))
                return configured;

            if (!IsLoopbackHost(configuredUri.Host))
                return configuredUri.ToString();

            string? derived = TryBuildRegistryBaseUrlFromNetworkHost(configuredUri);
            return string.IsNullOrWhiteSpace(derived) ? configuredUri.ToString() : derived;
        }

        private string? TryBuildRegistryBaseUrlFromNetworkHost(Uri? templateUri = null)
        {
            string networkHost = _runtimeRoot?.NetworkHost?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(networkHost) || IsLoopbackHost(networkHost))
                return null;

            if (templateUri is null)
                return $"http://{networkHost}:5180";

            UriBuilder builder = new(templateUri)
            {
                Host = networkHost,
            };
            return builder.Uri.ToString();
        }

        private static bool IsLoopbackHost(string host) =>
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);

        private void OnSettingsBackRequested()
        {
            if (_menuCanvas?.TryPopScreen() == true)
                return;

            if (_returnSettingsToPauseMenu)
                ShowPauseMenu(_runtimeRoot?.CurrentAppStateDetail ?? "paused");
            else
                ShowStartMenu();
        }

        private void ShowRootScreen(StringName screenId)
        {
            _menuCanvas?.ShowRootScreen(screenId);
            _runtimeRoot?.SetGameplayInputBlocked(true);
        }

        private void PushScreen(StringName screenId)
        {
            _menuCanvas?.PushScreen(screenId);
            _runtimeRoot?.SetGameplayInputBlocked(true);
        }

        private void ReplaceScreen(StringName screenId)
        {
            _menuCanvas?.ReplaceScreen(screenId);
            _runtimeRoot?.SetGameplayInputBlocked(true);
        }

        private void HideMenusAndReturnToHud()
        {
            _menuCanvas?.HideAllScreens(clearHistory: true);
            _runtimeRoot?.SetGameplayInputBlocked(false);
        }
    }
}
