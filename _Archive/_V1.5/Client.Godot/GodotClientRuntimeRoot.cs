using Client.Godot.World;
using Godot;
using UltraSim.Client.Runtime;
using Client.Godot.Controllers;
using Client.Godot.Prefabs.Camera;
using Client.Godot.UI;
using System.Collections.Generic;

namespace Client.Godot
{
    [GlobalClass]
    public partial class GodotClientRuntimeRoot : Node
    {
        private const string ClientSettingsPath = "user://client_settings.cfg";
        private const string WorldUiSettingsSection = "world_ui";

        [Signal]
        public delegate void AppStateChangedEventHandler(int appState, string detail);

        [Export] public NodePath? CharacterWorldPath { get; set; }
        [Export] public NodePath? LocalPlayerControllerPath { get; set; }
        [Export] public NodePath? CameraRigPath { get; set; }
        [Export] public NodePath? WorldCanvasManagerPath { get; set; }
        [Export] public NodePath? MenuCanvasManagerPath { get; set; }
        [Export] public ClientSessionDriverMode SessionMode { get; set; } = ClientSessionDriverMode.Preview;
        [Export] public bool AutoStartSessionOnReady { get; set; } = false;
        [Export] public bool SpawnPreviewActorOnReady { get; set; } = true;
        [Export] public ulong PreviewActorId { get; set; } = 1;
        [Export] public Vector3 PreviewSpawnPosition { get; set; } = Vector3.Zero;
        [Export] public string NetworkHost { get; set; } = "127.0.0.1";
        [Export] public int NetworkPort { get; set; } = 7777;

        public GodotClientSession? Session { get; private set; }
        public ClientCharacterWorld CharacterWorld { get; private set; } = null!;
        public LocalPlayerController? LocalPlayerController { get; private set; }
        public OrbitFollowCameraRig? CameraRig { get; private set; }
        public WorldCanvasManager? WorldCanvasManager { get; private set; }
        public MenuCanvasManager? MenuCanvasManager { get; private set; }
        public IClientSessionDriver? SessionDriver { get; private set; }
        public ClientAppState CurrentAppState { get; private set; } = ClientAppState.Boot;
        public string CurrentAppStateDetail { get; private set; } = "boot";
        private bool _suppressSettingsSave;
        private bool _serverGrantedInGame;
        private string _inGameDetail = string.Empty;
        public bool IsSessionRunning => Session != null;
        public bool IsNetworkConnected => SessionDriver is NetworkClientSessionDriver networkDriver && networkDriver.IsConnected;
        public bool CanSendChat => SessionDriver is NetworkClientSessionDriver networkChatDriver && networkChatDriver.IsConnected;
        public float LastPingMs => SessionDriver is NetworkClientSessionDriver networkDriver ? networkDriver.LastPingMs : 0.0f;
        public float SmoothedPingMs => SessionDriver is NetworkClientSessionDriver networkDriver ? networkDriver.SmoothedPingMs : 0.0f;
        public bool HasNetworkError => SessionDriver is NetworkClientSessionDriver networkDriver && networkDriver.HasConnectionError;
        public string NetworkStatusText => SessionDriver is NetworkClientSessionDriver networkDriver ? networkDriver.StatusText : "preview";
        public IReadOnlyList<string> ChatFeedLines => SessionDriver is NetworkClientSessionDriver networkDriver ? networkDriver.ChatFeedLines : System.Array.Empty<string>();
        public bool ShowOverheadChat
        {
            get => WorldCanvasManager?.ShowOverheadChat ?? true;
            set
            {
                if (WorldCanvasManager == null || WorldCanvasManager.ShowOverheadChat == value)
                    return;

                WorldCanvasManager.ShowOverheadChat = value;
                SaveClientSettings();
            }
        }
        public bool ShowNameplates
        {
            get => WorldCanvasManager?.ShowNameplates ?? true;
            set
            {
                if (WorldCanvasManager == null || WorldCanvasManager.ShowNameplates == value)
                    return;

                WorldCanvasManager.ShowNameplates = value;
                SaveClientSettings();
            }
        }
        public int MaxVisibleWorldUi
        {
            get => WorldCanvasManager?.MaxVisibleWorldUi ?? 32;
            set
            {
                if (WorldCanvasManager == null)
                    return;

                int clamped = Mathf.Max(1, value);
                if (WorldCanvasManager.MaxVisibleWorldUi == clamped)
                    return;

                WorldCanvasManager.MaxVisibleWorldUi = clamped;
                SaveClientSettings();
            }
        }
        public float WorldUiScale
        {
            get => WorldCanvasManager?.WorldUiScale ?? 1.0f;
            set
            {
                if (WorldCanvasManager == null)
                    return;

                float clamped = Mathf.Clamp(value, 0.5f, 3.0f);
                if (Mathf.IsEqualApprox(WorldCanvasManager.WorldUiScale, clamped))
                    return;

                WorldCanvasManager.WorldUiScale = clamped;
                SaveClientSettings();
            }
        }

        public override void _Ready()
        {
            CharacterWorld = ResolveCharacterWorld();
            LocalPlayerController = ResolveLocalPlayerController();
            CameraRig = ResolveCameraRig();
            WorldCanvasManager = ResolveWorldCanvasManager();
            MenuCanvasManager = ResolveMenuCanvasManager();
            LoadClientSettings();
            if (AutoStartSessionOnReady)
                StartSession();
            else
                UpdateAppState(forceSignal: true);
        }

        public override void _Process(double delta)
        {
            if (Session == null)
            {
                UpdateAppState();
                return;
            }

            Session.Tick((float)delta);
            SessionDriver?.Tick((float)delta);
            UpdateAppState();
        }

        public void QueueSetActorPose(ClientEntityViewId entityId, in ClientWorldPose pose) =>
            Session?.QueueSetActorPose(entityId, in pose);

        public void QueueSetActorDefinition(ClientEntityViewId entityId, ClientActorDefinitionId definitionId) =>
            Session?.QueueSetActorDefinition(entityId, definitionId);

        public void QueuePlayAnimation(ClientEntityViewId entityId, AnimationCue cue) =>
            Session?.QueuePlayAnimation(entityId, cue);

        public void QueueDespawnActor(ClientEntityViewId entityId) =>
            Session?.QueueDespawnActor(entityId);

        public bool TrySendChatText(string text) =>
            SessionDriver is NetworkClientSessionDriver networkDriver && networkDriver.TrySendChatText(text);

        public void SetGameplayInputBlocked(bool blocked)
        {
            if (LocalPlayerController != null)
                LocalPlayerController.GameplayInputBlocked = blocked;
        }

        public override void _ExitTree()
        {
            StopSession();
        }

        public void StartSession()
        {
            if (Session != null)
                return;

            CharacterWorld.SetLocalPlayerController(LocalPlayerController);
            LocalPlayerController?.SetCameraRig(CameraRig);
            WorldCanvasManager?.ClearAll();
            Session = new GodotClientSession(CharacterWorld, WorldCanvasManager, MenuCanvasManager);
            Session.LocalPlayerController = LocalPlayerController;
            if (LocalPlayerController != null)
                LocalPlayerController.EnablePreviewControls = SessionMode == ClientSessionDriverMode.Preview;

            Session.OnEnterGame = NotifyEnterGame;
            SessionDriver = CreateSessionDriver();
            SessionDriver?.Initialize(Session);
            UpdateAppState(forceSignal: true);
        }

        public void NotifyEnterGame(string detail)
        {
            _serverGrantedInGame = true;
            _inGameDetail = detail;
            UpdateAppState(forceSignal: true);
        }

        public bool RequestReturnToCharacterSelect()
        {
            if (SessionDriver is not NetworkClientSessionDriver networkDriver ||
                !networkDriver.TryRequestReturnToCharacterSelect())
            {
                return false;
            }

            _serverGrantedInGame = false;
            _inGameDetail = string.Empty;
            CharacterWorld.ClearAllActors();
            WorldCanvasManager?.ClearAll();
            SetGameplayInputBlocked(true);
            UpdateAppState(forceSignal: true);
            return true;
        }

        public void StopSession()
        {
            SessionDriver?.Shutdown();
            SessionDriver = null;
            Session = null;
            _serverGrantedInGame = false;
            _inGameDetail = string.Empty;
            MenuCanvasManager?.ClearServerDrivenSessionState();
            CharacterWorld.ClearAllActors();
            WorldCanvasManager?.ClearAll();
            UpdateAppState(forceSignal: true);
        }

        public void RestartSession()
        {
            StopSession();
            StartSession();
        }

        public void StartPreviewSession()
        {
            SessionMode = ClientSessionDriverMode.Preview;
            RestartSession();
        }

        public void StartNetworkSession(string host, int port)
        {
            NetworkHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            NetworkPort = port <= 0 ? 7777 : port;
            SessionMode = ClientSessionDriverMode.Network;
            RestartSession();
        }

        private ClientCharacterWorld ResolveCharacterWorld()
        {
            ClientCharacterWorld? world = GetNodeOrNull<ClientCharacterWorld>(CharacterWorldPath);
            if (world == null)
            {
                throw new System.InvalidOperationException("GodotClientRuntimeRoot requires a valid CharacterWorldPath.");
            }

            return world;
        }

        private LocalPlayerController? ResolveLocalPlayerController()
        {
            if (LocalPlayerControllerPath == null || LocalPlayerControllerPath.IsEmpty)
            {
                return null;
            }

            return GetNodeOrNull<LocalPlayerController>(LocalPlayerControllerPath);
        }

        private OrbitFollowCameraRig? ResolveCameraRig()
        {
            if (CameraRigPath == null || CameraRigPath.IsEmpty)
            {
                return null;
            }

            return GetNodeOrNull<OrbitFollowCameraRig>(CameraRigPath);
        }

        private WorldCanvasManager? ResolveWorldCanvasManager()
        {
            if (WorldCanvasManagerPath == null || WorldCanvasManagerPath.IsEmpty)
                return null;

            return GetNodeOrNull<WorldCanvasManager>(WorldCanvasManagerPath);
        }

        private MenuCanvasManager? ResolveMenuCanvasManager()
        {
            if (MenuCanvasManagerPath == null || MenuCanvasManagerPath.IsEmpty)
                return null;

            return GetNodeOrNull<MenuCanvasManager>(MenuCanvasManagerPath);
        }

        private void LoadClientSettings()
        {
            if (WorldCanvasManager == null)
                return;

            ConfigFile config = new();
            if (config.Load(ClientSettingsPath) != Error.Ok)
                return;

            _suppressSettingsSave = true;
            ShowOverheadChat = (bool)config.GetValue(WorldUiSettingsSection, "show_overhead_chat", ShowOverheadChat);
            ShowNameplates = (bool)config.GetValue(WorldUiSettingsSection, "show_nameplates", ShowNameplates);
            MaxVisibleWorldUi = (int)config.GetValue(WorldUiSettingsSection, "max_visible_world_ui", MaxVisibleWorldUi);
            WorldUiScale = (float)config.GetValue(WorldUiSettingsSection, "world_ui_scale", WorldUiScale);
            _suppressSettingsSave = false;
        }

        private void SaveClientSettings()
        {
            if (_suppressSettingsSave || WorldCanvasManager == null)
                return;

            ConfigFile config = new();
            config.SetValue(WorldUiSettingsSection, "show_overhead_chat", WorldCanvasManager.ShowOverheadChat);
            config.SetValue(WorldUiSettingsSection, "show_nameplates", WorldCanvasManager.ShowNameplates);
            config.SetValue(WorldUiSettingsSection, "max_visible_world_ui", WorldCanvasManager.MaxVisibleWorldUi);
            config.SetValue(WorldUiSettingsSection, "world_ui_scale", WorldCanvasManager.WorldUiScale);
            config.Save(ClientSettingsPath);
        }

        private IClientSessionDriver? CreateSessionDriver()
        {
            if (SessionMode == ClientSessionDriverMode.Network)
            {
                return new NetworkClientSessionDriver(NetworkHost, NetworkPort);
            }

            if (!SpawnPreviewActorOnReady)
            {
                return null;
            }

            return new PreviewClientSessionDriver(PreviewActorId, PreviewSpawnPosition);
        }

        private void UpdateAppState(bool forceSignal = false)
        {
            ClientAppState nextState;
            string nextDetail;

            if (Session == null || SessionDriver == null)
            {
                nextState = ClientAppState.Boot;
                nextDetail = "boot";
            }
            else if (SessionMode == ClientSessionDriverMode.Preview)
            {
                nextState = ClientAppState.Preview;
                nextDetail = "preview";
            }
            else if (SessionDriver is NetworkClientSessionDriver networkDriver)
            {
                switch (networkDriver.ConnectionState)
                {
                    case NetworkDriverConnectionState.Connected:
                        nextState = _serverGrantedInGame ? ClientAppState.InGame : ClientAppState.Connected;
                        nextDetail = _serverGrantedInGame ? _inGameDetail : networkDriver.StatusText;
                        break;
                    case NetworkDriverConnectionState.Connecting:
                        nextState = ClientAppState.Connecting;
                        nextDetail = networkDriver.StatusText;
                        break;
                    case NetworkDriverConnectionState.Disconnected:
                        nextState = ClientAppState.Disconnected;
                        nextDetail = networkDriver.StatusText;
                        break;
                    case NetworkDriverConnectionState.Error:
                        nextState = ClientAppState.Error;
                        nextDetail = networkDriver.StatusText;
                        break;
                    default:
                        nextState = ClientAppState.Boot;
                        nextDetail = "idle";
                        break;
                }
            }
            else
            {
                nextState = ClientAppState.Boot;
                nextDetail = "boot";
            }

            if (!forceSignal &&
                nextState == CurrentAppState &&
                string.Equals(nextDetail, CurrentAppStateDetail, System.StringComparison.Ordinal))
            {
                return;
            }

            CurrentAppState = nextState;
            CurrentAppStateDetail = nextDetail;
            EmitSignal(SignalName.AppStateChanged, (int)nextState, nextDetail);
        }
    }
}
