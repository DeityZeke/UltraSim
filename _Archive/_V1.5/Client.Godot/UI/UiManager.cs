using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class UiManager : Node
    {
        [Export] public bool ShowMainMenuOnReady { get; set; } = true;
        [Export] public NodePath? RuntimeRootPath { get; set; }
        [Export] public NodePath? WorldCanvasPath { get; set; }
        [Export] public NodePath? HudCanvasPath { get; set; }
        [Export] public NodePath? MenuCanvasPath { get; set; }
        [Export] public NodePath? DebugCanvasPath { get; set; }

        private GodotClientRuntimeRoot? _runtimeRoot;
        private WorldCanvasManager? _worldCanvas;
        private HudCanvasManager? _hudCanvas;
        private MenuCanvasManager? _menuCanvas;
        private DebugCanvasManager? _debugCanvas;
        private (ClientAppState State, string Detail)? _pendingDisconnectNav;
        private bool _appStateWantsInputBlocked = true;
        private bool _hadBlockingPanels;

        public WorldCanvasManager? WorldCanvas
        {
            get
            {
                ResolveManagers();
                return _worldCanvas;
            }
        }

        public HudCanvasManager? HudCanvas
        {
            get
            {
                ResolveManagers();
                return _hudCanvas;
            }
        }

        public MenuCanvasManager? MenuCanvas
        {
            get
            {
                ResolveManagers();
                return _menuCanvas;
            }
        }

        public DebugCanvasManager? DebugCanvas
        {
            get
            {
                ResolveManagers();
                return _debugCanvas;
            }
        }

        public override void _Ready()
        {
            _runtimeRoot = GetNodeOrNull<GodotClientRuntimeRoot>(RuntimeRootPath);
            if (_runtimeRoot != null)
                _runtimeRoot.AppStateChanged += OnAppStateChanged;

            ResolveManagers();

            if (ShowMainMenuOnReady)
                ShowMainMenu();
        }

        public override void _Process(double delta)
        {
            // Enforce input block while any BlocksGameplayInput panel is open.
            bool hasBlocking = _menuCanvas?.HasBlockingPanels == true;
            if (hasBlocking)
            {
                _runtimeRoot?.SetGameplayInputBlocked(true);
            }
            else if (_hadBlockingPanels)
            {
                // Last blocking panel just closed — restore whatever the app state wants.
                _runtimeRoot?.SetGameplayInputBlocked(_appStateWantsInputBlocked);
            }
            _hadBlockingPanels = hasBlocking;

            if (_pendingDisconnectNav is not { } pending)
                return;

            // Wait until all server-driven panels have been dismissed before navigating.
            if (_menuCanvas?.HasSessionLostPanels == true)
                return;

            _pendingDisconnectNav = null;
            ApplyDisconnectNav(pending.State, pending.Detail);
        }

        public override void _ExitTree()
        {
            if (_runtimeRoot != null)
                _runtimeRoot.AppStateChanged -= OnAppStateChanged;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (_runtimeRoot == null)
                return;

            if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo || keyEvent.Keycode != Key.Escape)
                return;

            if (_runtimeRoot.CurrentAppState != ClientAppState.InGame && _runtimeRoot.CurrentAppState != ClientAppState.Preview)
                return;

            if (_menuCanvas?.HasVisibleScreen == true)
            {
                _menuCanvas.HideMenus(clearHistory: true);
                _menuCanvas.ShowSharedChat();
                _runtimeRoot.SetGameplayInputBlocked(false);
            }
            else
            {
                _menuCanvas?.ShowMenus();
                _menuCanvas?.HideSharedChat();
                _menuCanvas?.AppShell?.ShowPauseMenu(_runtimeRoot.CurrentAppStateDetail);
                _runtimeRoot.SetGameplayInputBlocked(true);
            }

            GetViewport()?.SetInputAsHandled();
        }

        public void SetHudVisible(bool visible)
        {
            ResolveManagers();
            if (visible)
                _hudCanvas?.ShowHud();
            else
                _hudCanvas?.HideHud();
        }

        public void SetMenusVisible(bool visible)
        {
            ResolveManagers();
            if (visible)
                _menuCanvas?.ShowMenus();
            else
                _menuCanvas?.HideMenus(clearHistory: true);
        }

        public void ShowMainMenu()
        {
            SetHudVisible(false);
            _menuCanvas?.HideSharedChat();
            _menuCanvas?.ShowMenus();
            _menuCanvas?.AppShell?.ShowStartMenu(stopSession: false);
        }

        private void OnAppStateChanged(int appStateValue, string detail)
        {
            ClientAppState appState = (ClientAppState)appStateValue;
            switch (appState)
            {
                case ClientAppState.InGame:
                case ClientAppState.Preview:
                    _appStateWantsInputBlocked = false;
                    SetHudVisible(true);
                    _menuCanvas?.HideMenus(clearHistory: true);
                    _menuCanvas?.ShowSharedChat();
                    _runtimeRoot?.SetGameplayInputBlocked(_menuCanvas?.HasBlockingPanels == true);
                    break;
                case ClientAppState.Connecting:
                    _appStateWantsInputBlocked = true;
                    SetHudVisible(false);
                    _menuCanvas?.HideSharedChat();
                    _menuCanvas?.ShowMenus();
                    _menuCanvas?.AppShell?.ShowConnecting(detail);
                    _runtimeRoot?.SetGameplayInputBlocked(true);
                    break;
                case ClientAppState.Connected:
                    // TCP is up — server is now driving UI via server-driven panels.
                    // Clear the app shell screen so panels are not obscured.
                    _appStateWantsInputBlocked = true;
                    SetHudVisible(false);
                    _menuCanvas?.HideSharedChat();
                    _menuCanvas?.HideMenus(clearHistory: true);
                    _runtimeRoot?.SetGameplayInputBlocked(true);
                    break;
                case ClientAppState.Disconnected:
                case ClientAppState.Error:
                    _appStateWantsInputBlocked = true;
                    _runtimeRoot?.SetGameplayInputBlocked(true);
                    if (_menuCanvas?.HasSessionLostPanels == true)
                        _pendingDisconnectNav = (appState, detail);  // rejection panel visible — wait for dismiss
                    else
                        ApplyDisconnectNav(appState, detail);
                    break;
                default:
                    _appStateWantsInputBlocked = true;
                    SetHudVisible(false);
                    _menuCanvas?.HideSharedChat();
                    _menuCanvas?.ShowMenus();
                    _menuCanvas?.AppShell?.ShowStartMenu(stopSession: false);
                    _runtimeRoot?.SetGameplayInputBlocked(true);
                    break;
            }
        }

        private void ApplyDisconnectNav(ClientAppState appState, string detail)
        {
            SetHudVisible(false);
            _menuCanvas?.HideSharedChat();
            _menuCanvas?.ShowMenus();
            _menuCanvas?.AppShell?.ShowSessionStatus(
                appState == ClientAppState.Error ? "Connection Error" : "Disconnected",
                detail,
                allowRetry: !string.IsNullOrWhiteSpace(_runtimeRoot?.NetworkHost));
        }

        private void ResolveManagers()
        {
            _worldCanvas ??= GetNodeOrNull<WorldCanvasManager>(WorldCanvasPath);
            _hudCanvas ??= GetNodeOrNull<HudCanvasManager>(HudCanvasPath);
            _menuCanvas ??= GetNodeOrNull<MenuCanvasManager>(MenuCanvasPath);
            _debugCanvas ??= GetNodeOrNull<DebugCanvasManager>(DebugCanvasPath);
        }
    }
}
