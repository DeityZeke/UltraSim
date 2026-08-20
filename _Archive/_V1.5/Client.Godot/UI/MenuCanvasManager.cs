using System;
using System.Security.Cryptography;
using System.Text;
using Godot;
using System.Collections.Generic;
using UltraSim.Client.Engines.Networking;
using UltraSim.Client.Runtime;
using UltraSim.Client.Runtime.UI;
using UltraSim.UI.Authoring;
using UltraSim.UI.Contracts;
using UltraSim.UI.Contracts.Bundled;
using Client.Godot.UI.ServerDriven.Bundled;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class MenuCanvasManager : UiCanvasManager, IUiCommandSink
    {
        [Export] public PackedScene? AppShellScene { get; set; }
        [Export] public PackedScene? SharedChatScene { get; set; }
        [Export] public NodePath? ScreenHostPath { get; set; } = new NodePath("ScreenHost");

        private Control? _appShellInstance;
        private Control? _serverDrivenHost;
        private ClientChatMenu? _sharedChatInstance;
        private MenuScreenHost? _screenHost;
        private GodotUiCommandConsumer? _serverDrivenConsumer;
        private ClientUiPanelRuntime? _serverDrivenRuntime;
        private readonly GodotUiPanelRegistry _serverDrivenRegistry = new();
        private readonly List<StringName> _screenHistory = new();
        private uint _nextLocalPanelInstanceId = 0x8000_0000;
        private IContentResolver? _contentResolver;

        public event System.Action<UiPanelResponse>? PanelResponseSubmitted;
        public event System.Action<UiPanelAck>? PanelAckSubmitted;

        /// <summary>
        /// Called by the packet binding when the user submits the login form.
        /// Set by <see cref="NetworkClientSessionDriver"/> to route the packet to the transport.
        /// Receives: (instanceId, usernameUtf8, passwordHash32, challengeResponse32).
        /// </summary>
        public Action<UiPanelInstanceId, byte[], byte[], byte[]>? LoginRequestSender { get; set; }

        public StringName CurrentScreenId => _screenHost?.CurrentScreenId ?? new StringName();
        public bool HasVisibleScreen => !CurrentScreenId.IsEmpty;

        public override void _Ready()
        {
            EnsureServerDrivenHost();
            EnsureServerDrivenConsumer();
            RegisterBundledServerDrivenPanels();
            RegisterLoginPanelBinding();
            RegisterMessageBoxBindings();
        }

        public ClientAppShell? AppShell => _appShellInstance as ClientAppShell;
        public T? GetScreen<T>(StringName screenId) where T : MenuScreenBase
        {
            EnsureAppShellInstance();
            ResolveScreenHost();
            return _screenHost?.GetScreen<T>(screenId);
        }

        public void ShowSharedChat()
        {
            EnsureSharedChatInstance();
            if (_sharedChatInstance != null)
            {
                _sharedChatInstance.SetChatVisible(true);
                MoveChild(_sharedChatInstance, GetChildCount() - 1);
            }
        }

        public void HideSharedChat()
        {
            if (_sharedChatInstance == null)
                return;

            _sharedChatInstance.SetChatVisible(false);
        }

        public void ShowMenus()
        {
            EnsureAppShellInstance();
        }

        public void HideMenus(bool clearHistory = true)
        {
            ClearAppShellInstance(clearHistory);
        }

        public void ShowRootScreen(StringName screenId)
        {
            _screenHistory.Clear();
            ShowScreenInternal(screenId);
        }

        public void PushScreen(StringName screenId)
        {
            StringName current = CurrentScreenId;
            if (!current.IsEmpty && current != screenId)
                _screenHistory.Add(current);

            ShowScreenInternal(screenId);
        }

        public void ReplaceScreen(StringName screenId)
        {
            ShowScreenInternal(screenId);
        }

        public bool TryPopScreen()
        {
            if (_screenHistory.Count == 0)
                return false;

            StringName previous = _screenHistory[_screenHistory.Count - 1];
            _screenHistory.RemoveAt(_screenHistory.Count - 1);
            ShowScreenInternal(previous);
            return true;
        }

        public void HideAllScreens(bool clearHistory = true)
        {
            if (clearHistory)
                _screenHistory.Clear();

            _screenHost?.HideAllScreens();
        }

        public void ConfigureServerDrivenUi(IContentResolver? contentResolver)
        {
            _contentResolver = contentResolver;
            EnsureServerDrivenConsumer();
            EnsureServerDrivenRuntime();
        }

        public void OpenPanel(UiPanelOpenCommand command)
        {
            EnsureServerDrivenRuntime();
            _serverDrivenRuntime?.OpenPanel(command);
        }

        public void UpdatePanel(UiPanelUpdateCommand command)
        {
            EnsureServerDrivenRuntime();
            _serverDrivenRuntime?.UpdatePanel(command);
        }

        public void ClosePanel(UiPanelCloseCommand command)
        {
            _serverDrivenRuntime?.ClosePanel(command);
        }

        public void AcceptPanelDefinition(UiPanelDefinition definition)
        {
            EnsureServerDrivenRuntime();
            _serverDrivenRuntime?.AcceptDefinition(definition);
        }

        /// <summary>
        /// True while a session-loss-persistent panel (e.g. a ban/rejection message) is still
        /// visible and waiting for the player to dismiss it.
        /// Used by UiManager to defer disconnect navigation in that specific case only.
        /// A crash or unexpected disconnect closes all non-persistent panels immediately.
        /// </summary>
        public bool HasSessionLostPanels =>
            (_serverDrivenConsumer?.SessionLostPanelCount ?? 0) > 0;

        public bool HasBlockingPanels =>
            (_serverDrivenConsumer?.BlockingPanelCount ?? 0) > 0;

        public void ClearServerDrivenSessionState()
        {
            _serverDrivenRuntime?.CloseAllForSessionLoss();
        }

        private void ShowScreenInternal(StringName screenId)
        {
            EnsureAppShellInstance();
            ResolveScreenHost();
            if (_screenHost == null)
                return;

            if (screenId.IsEmpty)
                _screenHost.HideAllScreens();
            else
                _screenHost.ShowScreen(screenId);
        }

        private void ResolveScreenHost()
        {
            if (_screenHost == null && _appShellInstance != null)
                _screenHost = _appShellInstance.GetNodeOrNull<MenuScreenHost>(ScreenHostPath);
        }

        private void EnsureAppShellInstance()
        {
            if (_appShellInstance != null || AppShellScene == null)
                return;

            _appShellInstance = AppShellScene.Instantiate<Control>();
            AddChild(_appShellInstance);
            _screenHost = null;
        }

        private void EnsureServerDrivenHost()
        {
            if (_serverDrivenHost != null)
                return;

            _serverDrivenHost = new Control
            {
                Name = "ServerDrivenPanels",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            };
            _serverDrivenHost.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            AddChild(_serverDrivenHost);
        }

        private void EnsureServerDrivenConsumer()
        {
            EnsureServerDrivenHost();
            if (_serverDrivenHost == null || _serverDrivenConsumer != null)
                return;

            _serverDrivenConsumer = new GodotUiCommandConsumer(
                _serverDrivenHost,
                _serverDrivenRegistry,
                _contentResolver,
                HandlePanelResponse,
                () => _serverDrivenRuntime);
        }

        private void EnsureServerDrivenRuntime()
        {
            EnsureServerDrivenConsumer();
            if (_serverDrivenConsumer == null || _serverDrivenRuntime != null)
                return;

            _serverDrivenRuntime = new ClientUiPanelRuntime(
                _serverDrivenConsumer.OpenPanel,
                _serverDrivenConsumer.UpdatePanel,
                _serverDrivenConsumer.ClosePanel,
                HandlePanelAck);

            _serverDrivenRuntime.RegisterBundledDefinition(
                new UiPanelDefinition(SharedUiPanelIds.MessageBox, 1, ReadOnlyMemory<byte>.Empty),
                warmCache: true);
        }

        private void RegisterBundledServerDrivenPanels()
        {
            _serverDrivenRegistry.Register(SharedUiPanelIds.MessageBox, static (in UiPanelOpenCommand _) => new MessageBoxPanel());
        }

        private void RegisterLoginPanelBinding()
        {
            EnsureServerDrivenConsumer();
            if (_serverDrivenConsumer == null)
                return;

            _serverDrivenConsumer.RegisterPacketBinding(
                new UiPanelTypeId(0xFF02),
                UiPanel.ActionId("login"),
                HandleLoginPanelAction);
        }

        private void RegisterMessageBoxBindings()
        {
            EnsureServerDrivenConsumer();
            if (_serverDrivenConsumer == null)
                return;

            _serverDrivenConsumer.RegisterPacketBinding(
                SharedUiPanelIds.MessageBox,
                SharedUiActionIds.QuitToMainMenu,
                (_, _, _) =>
                {
                    EnsureAppShellInstance();
                    AppShell?.ShowStartMenu();
                    return true;
                });
            _serverDrivenConsumer.RegisterPacketBinding(
                SharedUiPanelIds.MessageBox,
                SharedUiActionIds.DismissLocalMessageBox,
                (instanceId, _, _) =>
                {
                    _serverDrivenRuntime?.ClosePanel(new UiPanelCloseCommand(instanceId, UiPanelCloseReason.ClosedByServer));
                    return true;
                });
        }

        private bool HandleLoginPanelAction(
            UiPanelInstanceId instanceId,
            ReadOnlyMemory<byte> openPayload,
            ReadOnlyMemory<byte> textInputPayload)
        {
            // Read challenge from open payload
            if (!LoginPacketCodec.TryReadChallengePayload(openPayload, out ReadOnlyMemory<byte> challenge))
                return false;

            // Unpack text inputs: format is [1 count] ([1 fieldIndex][2 len LE][N utf8])*
            ReadOnlySpan<byte> inputs = textInputPayload.Span;
            if (inputs.IsEmpty)
                return false;

            int pos = 0;
            int fieldCount = inputs[pos++];
            string username = string.Empty;
            string password = string.Empty;
            for (int i = 0; i < fieldCount && pos < inputs.Length; i++)
            {
                byte fieldIndex = inputs[pos++];
                if (pos + 2 > inputs.Length) break;
                int len = inputs[pos] | (inputs[pos + 1] << 8); pos += 2;
                if (pos + len > inputs.Length) break;
                string text = len > 0 ? Encoding.UTF8.GetString(inputs.Slice(pos, len)) : string.Empty;
                pos += len;
                if (fieldIndex == 0) username = text;
                else if (fieldIndex == 1) password = text;
            }

            username = username.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                OpenLocalMessageBox(
                    "Login Required",
                    "Username and password are required.");
                return true;
            }

            // Compute challenge-response:
            //   passwordHash      = SHA-256(password)
            //   challengeResponse = SHA-256(UTF8(Hex(passwordHash) + ":" + Hex(challenge16)))
            byte[] passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            string hashHex      = Convert.ToHexString(passwordHash);
            string challengeHex = Convert.ToHexString(challenge.Span);
            byte[] challengeResponse = SHA256.HashData(Encoding.UTF8.GetBytes(hashHex + ":" + challengeHex));

            byte[] usernameUtf8 = Encoding.UTF8.GetBytes(username);
            LoginRequestSender?.Invoke(instanceId, usernameUtf8, passwordHash, challengeResponse);
            return true; // consumed — do not send UiPanelResponse
        }

        /// <summary>
        /// Called by <see cref="NetworkClientSessionDriver"/> when the server rejects login.
        /// Updates the stored challenge so the next retry uses the new nonce.
        /// </summary>
        public void OnLoginResponse(LoginResponsePayload payload)
        {
            EnsureServerDrivenConsumer();
            _serverDrivenConsumer?.UpdateOpenPayload(
                payload.InstanceId,
                LoginPacketCodec.EncodeChallengePayload(payload.NewChallenge.Span));
        }

        private void HandlePanelResponse(UiPanelResponse response)
        {
            GD.Print($"[MenuCanvasManager] UiPanelResponse instance={response.PanelInstanceId.Value} action={response.ActionId} payloadBytes={response.Payload.Length}");
            PanelResponseSubmitted?.Invoke(response);
        }

        private void HandlePanelAck(UiPanelAck ack)
        {
            GD.Print($"[MenuCanvasManager] UiPanelAck instance={ack.PanelInstanceId.Value} type=0x{ack.PanelTypeId.Value:X4} rev={ack.DefinitionRevision} status={ack.Status}");
            PanelAckSubmitted?.Invoke(ack);
        }

        private void EnsureSharedChatInstance()
        {
            if (_sharedChatInstance != null || SharedChatScene == null)
                return;

            _sharedChatInstance = SharedChatScene.Instantiate<ClientChatMenu>();
            AddChild(_sharedChatInstance);
        }

        private void ClearAppShellInstance(bool clearHistory)
        {
            if (clearHistory)
                _screenHistory.Clear();

            _screenHost = null;

            if (_appShellInstance == null)
                return;

            _appShellInstance.QueueFree();
            _appShellInstance = null;
        }

        private void OpenLocalMessageBox(string title, string body)
        {
            EnsureServerDrivenRuntime();
            if (_serverDrivenRuntime == null)
                return;

            UiPanelInstanceId instanceId = new(_nextLocalPanelInstanceId++);
            _serverDrivenRuntime.OpenPanel(new UiPanelOpenCommand(
                instanceId,
                SharedUiPanelIds.MessageBox,
                UiPanelCategory.Menu,
                UiPanelPresentationMode.Shared,
                1,
                UiPanelFlags.BringToFront | UiPanelFlags.HasCancelAction | UiPanelFlags.BlocksGameplayInput,
                MessageBoxPayloadCodec.Encode(new MessageBoxPayload(
                    title,
                    body,
                    new[]
                    {
                        new MessageBoxButtonDefinition(SharedUiActionIds.DismissLocalMessageBox, "OK", IsDefault: true, IsCancel: true)
                    }))));
        }
    }
}
