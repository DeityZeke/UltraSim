using Godot;
using UltraSim.Client.Godot.Camera;
using UltraSim.Client.Godot.Content;
using UltraSim.Client.Godot.Controllers;
using UltraSim.Client.Godot.Mobiles;
using UltraSim.Client.Godot.UI;
using UltraSim.Client.Godot.World;
using UltraSim.Client.UI;
using UltraSim.UI;

namespace UltraSim.Client.Godot;

[GlobalClass]
public partial class GodotClientRuntimeRoot : Node
{
	private const string SmokeMessageBoxPanelId = "smoke/message_box/1";
	private const string SmokeMessageBoxActionId = "smoke.message_box.ok";
	private const string SmokeMessageBoxText = "Server-driven smoke message";
	private const string SmokeUnsupportedChildText = "Unsupported node child survived";
	private const string SmokeTextInputSlot = "displayName";
	private const string SmokeToggleSlot = "rememberAccount";
	private const string SmokeNumberInputSlot = "uiScale";
	private const string SmokeSliderSlot = "musicVolume";
	private const string SmokeStepperSlot = "chatLines";
	private const string SmokeSelectSlot = "chatChannel";

	[Export] public NodePath? CharacterWorldPath { get; set; }
	[Export] public NodePath? LocalPlayerControllerPath { get; set; }
	[Export] public NodePath? CameraRigPath { get; set; }
	[Export] public NodePath? UiManagerPath { get; set; }
	[Export] public bool StartPreviewOnReady { get; set; } = true;
	[Export] public string ContentCacheRootOverride { get; set; } = string.Empty;
	[Export(PropertyHint.Range, "0.0,32.0,0.1,or_greater")] public float LiveMoveSpeed { get; set; } = 4.0f;

	private readonly List<double> _smokeDisconnectAtSeconds = new();
	private readonly ClientUiPanelRegistry _serverDrivenPanels = new();

	public ClientCharacterWorld? CharacterWorld { get; private set; }
	public LocalPlayerController? LocalPlayerController { get; private set; }
	public OrbitFollowCameraRig? CameraRig { get; private set; }
	public UiManager? UiManager { get; private set; }
	public GodotClientContentServices? ContentServices { get; private set; }

	private GodotClientNetworkSessionDriver? _sessionDriver;
	private GodotNetworkPresentationBridge? _presentationBridge;
	private ClientDirectConnectRequest? _lastDirectConnectRequest;
	private uint _localClientTick;
	private bool _wasSessionReady;
	private bool _smokeRequireConnected;
	private bool _smokeSawConnected;
	private bool _smokeRequireDisconnected;
	private bool _smokeSawDisconnected;
	private bool _smokeRequireSessionFailed;
	private bool _smokeSawSessionFailed;
	private bool _smokeRequireContentCache;
	private bool _smokeRequireChatReceived;
	private bool _smokeRequireOwnerMovement;
	private bool _smokeServerUiMessageBox;
	private bool _smokeRequireServerUiMessageBox;
	private bool _smokeRequireServerUiAction;
	private double _smokeReconnectAfterSeconds = -1.0;
	private double _smokeChatAfterSeconds = -1.0;
	private double _smokeMoveAfterSeconds = -1.0;
	private double _smokeMoveDurationSeconds = 1.0;
	private double _smokeElapsedSeconds;
	private bool _smokeReconnectRequested;
	private bool _smokeChatRequested;
	private string _smokeChatText = "hello from smoke";
	private string? _smokeSessionFailedContains;
	private string? _smokeSessionFailureStatus;
	private short _smokeMoveX = 100;
	private short _smokeMoveY;
	private int _smokeConnectedCount;
	private int _smokeRequiredConnectedCount;
	private int _smokeChatReceivedCount;
	private int _smokeOwnerMovementCount;
	private int _nextSmokeDisconnectIndex;
	private bool _smokeChatReceiveLogged;
	private bool _smokeOwnerMovementLogged;
	private bool _smokeSawServerUiMessageBox;
	private bool _smokeSawServerUiText;
	private bool _smokeSawServerUiUnsupportedChild;
	private bool _smokeSawServerUiAction;
	private ClientMovementState _lastAuthoritativeOwnerState;

	public override void _Ready()
	{
		_smokeRequireConnected = HasUserArg("--us-smoke-require-connected");
		_smokeRequireDisconnected = HasUserArg("--us-smoke-require-disconnected");
		_smokeRequireSessionFailed = HasUserArg("--us-smoke-require-session-failed");
		_smokeRequireContentCache = HasUserArg("--us-smoke-require-content-cache");
		_smokeRequireChatReceived = HasUserArg("--us-smoke-require-chat-received");
		_smokeRequireOwnerMovement = HasUserArg("--us-smoke-require-owner-movement");
		_smokeServerUiMessageBox = HasUserArg("--us-smoke-server-ui-message-box");
		_smokeRequireServerUiMessageBox = HasUserArg("--us-smoke-require-server-ui-message-box");
		_smokeRequireServerUiAction = HasUserArg("--us-smoke-require-server-ui-action");
		_smokeReconnectAfterSeconds = ReadPositiveUserArg("--us-smoke-reconnect-after=");
		_smokeChatAfterSeconds = ReadPositiveUserArg("--us-smoke-chat-after=");
		_smokeMoveAfterSeconds = ReadPositiveUserArg("--us-smoke-move-after=");
		_smokeMoveDurationSeconds = ReadPositiveUserArg("--us-smoke-move-duration=");
		_smokeChatText = ReadStringUserArg("--us-smoke-chat-text=") ?? _smokeChatText;
		_smokeSessionFailedContains = ReadStringUserArg("--us-smoke-session-failed-contains=");
		_smokeMoveX = (short)Math.Clamp(ReadIntUserArg("--us-smoke-move-x=", _smokeMoveX), -100, 100);
		_smokeMoveY = (short)Math.Clamp(ReadIntUserArg("--us-smoke-move-y=", _smokeMoveY), -100, 100);
		_smokeRequiredConnectedCount = ReadPositiveIntUserArg("--us-smoke-require-connected-count=");
		ReadSmokeDisconnectSchedule();
		CharacterWorld = ResolveNode<ClientCharacterWorld>(CharacterWorldPath);
		LocalPlayerController = ResolveNode<LocalPlayerController>(LocalPlayerControllerPath);
		CameraRig = ResolveNode<OrbitFollowCameraRig>(CameraRigPath);
		UiManager = ResolveNode<UiManager>(UiManagerPath);
		ContentServices = GodotClientContentServices.Load(ResolveContentCacheRoot(), GD.PushWarning);
		SetStatusText($"Content cache ready: {ContentServices.PathPolicy.RootPath}");

		if (LocalPlayerController is not null && CameraRig is not null)
		{
			LocalPlayerController.SetCameraRig(CameraRig);
		}

		if (CharacterWorld is not null)
		{
			_presentationBridge = new GodotNetworkPresentationBridge(CharacterWorld);
		}

		_sessionDriver = new GodotClientNetworkSessionDriver(
			SetStatusText,
			HandleChatMessage,
			HandleOwnerMovementState,
			HandleObserverState,
			HandleObserverStateReset,
			HandleSessionFailed);
		ApplyNetworkDriverUserOptions(_sessionDriver);
		if (UiManager?.Menus is not null)
		{
			UiManager.Menus.DirectConnectRequested += HandleDirectConnectRequested;
			UiManager.Menus.DisconnectRequested += HandleDisconnectRequested;
			UiManager.Menus.PreviewRequested += HandlePreviewRequested;
			UiManager.Menus.ChatSubmitted += HandleChatSubmitted;
			UiManager.Menus.ServerDrivenPanelRegistry = _serverDrivenPanels;
			UiManager.Menus.ServerDrivenActionRouted += HandleServerDrivenActionRouted;
		}

		ScheduleSmokeQuitIfRequested();

		if (_smokeServerUiMessageBox)
		{
			OpenSmokeServerUiMessageBox();
		}

		if (TryReadAutoConnectRequest(out ClientDirectConnectRequest autoConnectRequest, out string? autoConnectError))
		{
			HandleDirectConnectRequested(autoConnectRequest);
			return;
		}

		if (!string.IsNullOrWhiteSpace(autoConnectError))
		{
			SetStatusText(autoConnectError);
			UiManager?.Menus?.ShowConnectionError();
			return;
		}

		if (StartPreviewOnReady)
		{
			CharacterWorld?.SpawnPreviewActor();
			SetStatusText("Preview shell ready");
		}

	}

	public override void _Process(double delta)
	{
		_sessionDriver?.Pump();
		UpdateReadySessionTransition();
		UpdateSmokeSchedule(delta);
		SendMovementInputIfReady(delta);
		UpdateNetworkSummary();

		if (LocalPlayerController is not null)
		{
			LocalPlayerController.GameplayInputBlocked = UiManager?.BlocksGameplayInput == true;
		}
	}

	public override void _ExitTree()
	{
		if (UiManager?.Menus is not null)
		{
			UiManager.Menus.DirectConnectRequested -= HandleDirectConnectRequested;
			UiManager.Menus.DisconnectRequested -= HandleDisconnectRequested;
			UiManager.Menus.PreviewRequested -= HandlePreviewRequested;
			UiManager.Menus.ChatSubmitted -= HandleChatSubmitted;
			UiManager.Menus.ServerDrivenActionRouted -= HandleServerDrivenActionRouted;
		}

		_sessionDriver?.Dispose();
		_sessionDriver = null;
		ContentServices?.SaveIndex();
		ContentServices = null;
		_presentationBridge = null;
	}

	public void EnterPreviewMode()
	{
		if (LocalPlayerController is not null)
		{
			LocalPlayerController.EnablePreviewControls = true;
		}

		CharacterWorld?.SpawnPreviewActor();
		UiManager?.Menus?.EnterPreviewSession();
		SetStatusText("Preview shell ready");
	}

	public void ClearSessionWorld()
	{
		if (LocalPlayerController is not null)
		{
			LocalPlayerController.EnablePreviewControls = true;
		}

		CharacterWorld?.ClearAllActors();
		SetStatusText("Session cleared");
	}

	private static bool TryReadAutoConnectRequest(out ClientDirectConnectRequest request, out string? error)
	{
		request = default;
		error = null;
		string? host = null;
		string? port = null;

		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--us-connect=", StringComparison.OrdinalIgnoreCase))
			{
				string value = arg["--us-connect=".Length..].Trim();
				int separator = value.LastIndexOf(':');
				if (separator <= 0 || separator == value.Length - 1)
				{
					error = "Auto-connect expects --us-connect=host:port.";
					return false;
				}

				host = value[..separator];
				port = value[(separator + 1)..];
			}
			else if (arg.StartsWith("--us-connect-host=", StringComparison.OrdinalIgnoreCase))
			{
				host = arg["--us-connect-host=".Length..];
			}
			else if (arg.StartsWith("--us-connect-port=", StringComparison.OrdinalIgnoreCase))
			{
				port = arg["--us-connect-port=".Length..];
			}
		}

		if (host is null && port is null)
		{
			return false;
		}

		if (!ClientDirectConnectRequest.TryCreate(host, port, out request, out string validationError))
		{
			error = validationError;
			return false;
		}

		return true;
	}

	private void HandleDirectConnectRequested(ClientDirectConnectRequest request)
	{
		if (_sessionDriver is null)
		{
			SetStatusText("Network driver is unavailable.");
			UiManager?.Menus?.ShowConnectionError();
			return;
		}

		if (!_sessionDriver.TryConnect(request, out string error))
		{
			SetStatusText(error);
			UiManager?.Menus?.ShowConnectionError();
			return;
		}

		_lastDirectConnectRequest = request;
		_wasSessionReady = false;
		CharacterWorld?.ClearAllActors();
		UiManager?.Menus?.ShowSessionStatus();
	}

	private static void ApplyNetworkDriverUserOptions(GodotClientNetworkSessionDriver driver)
	{
		int maxHandshakeAttempts = ReadPositiveIntUserArg("--us-connect-max-handshake-attempts=");
		if (maxHandshakeAttempts > 0)
		{
			driver.MaxHandshakeAttempts = maxHandshakeAttempts;
		}

		double handshakeRetrySeconds = ReadPositiveUserArg("--us-connect-handshake-retry-seconds=");
		if (handshakeRetrySeconds > 0.0)
		{
			driver.HandshakeRetryIntervalSeconds = handshakeRetrySeconds;
		}

		double keepAliveTimeoutSeconds = ReadPositiveUserArg("--us-connect-ready-keepalive-timeout-seconds=");
		if (keepAliveTimeoutSeconds > 0.0)
		{
			driver.ReadyKeepAliveTimeoutSeconds = keepAliveTimeoutSeconds;
		}
	}

	private string ResolveContentCacheRoot()
	{
		string? argRoot = ReadStringUserArg("--us-content-cache-root=");
		string root = !string.IsNullOrWhiteSpace(argRoot)
			? argRoot
			: !string.IsNullOrWhiteSpace(ContentCacheRootOverride)
				? ContentCacheRootOverride
				: ProjectSettings.GlobalizePath("user://UltraSimCache");
		return root;
	}

	private void HandleDisconnectRequested()
	{
		_sessionDriver?.Disconnect();
		_wasSessionReady = false;
		_smokeSawDisconnected = true;
		ClearSessionWorld();
	}

	private void HandlePreviewRequested()
	{
		EnterPreviewMode();
	}

	private void HandleSessionFailed(string status)
	{
		_wasSessionReady = false;
		_smokeSawDisconnected = true;
		_smokeSawSessionFailed = true;
		_smokeSessionFailureStatus = status;
		CharacterWorld?.ClearAllActors();
		if (LocalPlayerController is not null)
		{
			LocalPlayerController.EnablePreviewControls = true;
		}

		UiManager?.Menus?.ShowConnectionError(status);
		UiManager?.Menus?.AppendSystemChatLine(status);
	}

	private void HandleChatSubmitted(string text)
	{
		if (_sessionDriver?.TrySendChat(text) == true)
		{
			return;
		}

		UiManager?.Menus?.AppendSystemChatLine("Chat is not connected yet.");
	}

	private void HandleServerDrivenActionRouted(ClientUiActionRouteResult result)
	{
		if (result.ReadyToSend &&
			result.Request is not null &&
			string.Equals(result.Request.ActionId, SmokeMessageBoxActionId, StringComparison.Ordinal))
		{
			_smokeSawServerUiAction = true;
			SetStatusText($"Server UI smoke action routed: {result.Request.ActionId}");
			return;
		}

		if (!result.ReadyToSend)
		{
			GD.PushWarning($"Server UI action route failed: {result.Status} {result.Error}");
		}
	}

	private void HandleChatMessage(UltraSim.Network.ChatMessageEventPacket packet)
	{
		if (string.Equals(packet.Text, _smokeChatText, StringComparison.Ordinal))
		{
			_smokeChatReceivedCount++;
			if (!_smokeChatReceiveLogged)
			{
				_smokeChatReceiveLogged = true;
				SetStatusText($"Smoke chat received: {packet.Text}");
			}
		}

		UiManager?.Menus?.AppendChatLine(packet.SenderName, packet.Text);
		UiManager?.World?.ShowOverheadChat(packet.SenderEntityId, packet.Text);
	}

	private void HandleOwnerMovementState(ClientMovementStateReceiveResult result)
	{
		_lastAuthoritativeOwnerState = result.AuthoritativeState;
		if (result.LastProcessedInputSequence != 0)
		{
			_smokeOwnerMovementCount++;
			if (!_smokeOwnerMovementLogged)
			{
				_smokeOwnerMovementLogged = true;
				SetStatusText($"Smoke owner movement acknowledged seq={result.LastProcessedInputSequence}.");
			}
		}

		_presentationBridge?.ApplyOwnerMovement(result);
	}

	private void HandleObserverState(ulong itemKey, ulong sequence, ReadOnlySpan<byte> payload)
	{
		_presentationBridge?.ApplyObserverState(itemKey, sequence, payload);
	}

	private void HandleObserverStateReset()
	{
		_presentationBridge?.ResetObserverState();
	}

	private void SendMovementInputIfReady(double delta)
	{
		if (_sessionDriver is null ||
			!_sessionDriver.IsReady ||
			LocalPlayerController is null ||
			LocalPlayerController.ControlledCharacter is null)
		{
			return;
		}

		bool hasMoveInput = LocalPlayerController.TryReadMoveDirection(out Vector3 moveDirection);
		bool hasSmokeMoveInput = TryReadSmokeMoveInput(out short smokeMoveX, out short smokeMoveY);
		var input = new ClientMovementInput(
			MoveX: hasSmokeMoveInput ? smokeMoveX : hasMoveInput ? (short)Mathf.RoundToInt(Mathf.Clamp(moveDirection.X, -1.0f, 1.0f) * 100.0f) : (short)0,
			MoveY: hasSmokeMoveInput ? smokeMoveY : hasMoveInput ? (short)Mathf.RoundToInt(Mathf.Clamp(moveDirection.Z, -1.0f, 1.0f) * 100.0f) : (short)0,
			Flags: 0);

		global::Godot.Vector3 position = LocalPlayerController.ControlledCharacter.GlobalPosition;
		float velocityX = input.MoveX / 100.0f * LiveMoveSpeed;
		float velocityY = input.MoveY / 100.0f * LiveMoveSpeed;
		var predicted = new ClientMovementState(
			position.X + velocityX * (float)delta,
			position.Z + velocityY * (float)delta,
			velocityX,
			velocityY);

		ApplyLocalPredictedMovement(predicted, hasMoveInput || hasSmokeMoveInput);

		_localClientTick++;
		_sessionDriver.TrySendMovementInput(input, predicted);
	}

	private void ApplyLocalPredictedMovement(in ClientMovementState predicted, bool isMoving)
	{
		if (LocalPlayerController?.ControlledCharacter is null)
		{
			return;
		}

		CharacterRoot character = LocalPlayerController.ControlledCharacter;
		character.GlobalPosition = new Vector3(predicted.PositionX, 0.0f, predicted.PositionY);
		if (isMoving)
		{
			float yawDegrees = Mathf.RadToDeg(Mathf.Atan2(predicted.VelocityX, predicted.VelocityY));
			character.SetPresentationFacingYawDegrees(yawDegrees);
		}

		character.SetPresentationMoving(isMoving);
	}

	private void UpdateReadySessionTransition()
	{
		bool isReady = _sessionDriver?.IsReady == true;
		if (!isReady)
		{
			_wasSessionReady = false;
			return;
		}

		if (_wasSessionReady)
		{
			return;
		}

		_wasSessionReady = true;
		_smokeSawConnected = true;
		_smokeConnectedCount++;
		if (LocalPlayerController is not null)
		{
			LocalPlayerController.EnablePreviewControls = false;
		}

		UiManager?.Menus?.EnterConnectedSession();
		UiManager?.Menus?.AppendSystemChatLine("Connected.");
	}

	private void SetStatusText(string text)
	{
		GD.Print($"UltraSim client: {text}");
		UiManager?.SetStatusText(text);
	}

	private void ScheduleSmokeQuitIfRequested()
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (!arg.StartsWith("--us-smoke-quit-after=", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (!double.TryParse(arg["--us-smoke-quit-after=".Length..], out double seconds) || seconds <= 0.0)
			{
				GD.PushWarning("Invalid --us-smoke-quit-after value.");
				return;
			}

			SceneTreeTimer timer = GetTree().CreateTimer(seconds);
			timer.Timeout += QuitForSmoke;
			return;
		}
	}

	private void QuitForSmoke()
	{
		if (_smokeRequireConnected && !_smokeSawConnected)
		{
			GD.PushError("UltraSim client smoke failed: session never reached Connected.");
			GetTree().Quit(1);
			return;
		}

		if (_smokeRequiredConnectedCount > 0 && _smokeConnectedCount < _smokeRequiredConnectedCount)
		{
			GD.PushError($"UltraSim client smoke failed: expected {_smokeRequiredConnectedCount} connected transition(s), saw {_smokeConnectedCount}.");
			GetTree().Quit(1);
			return;
		}

		if (_smokeRequireContentCache && ContentServices is null)
		{
			GD.PushError("UltraSim client smoke failed: content cache services were not initialized.");
			GetTree().Quit(1);
			return;
		}

		if (_smokeRequireSessionFailed)
		{
			if (!_smokeSawSessionFailed)
			{
				GD.PushError("UltraSim client smoke failed: expected session failure but saw none.");
				GetTree().Quit(1);
				return;
			}

			if (_sessionDriver?.IsRunning == true)
			{
				GD.PushError("UltraSim client smoke failed: network session is still running after expected failure.");
				GetTree().Quit(1);
				return;
			}

			if (!string.IsNullOrWhiteSpace(_smokeSessionFailedContains) &&
				(_smokeSessionFailureStatus is null ||
				 !_smokeSessionFailureStatus.Contains(_smokeSessionFailedContains, StringComparison.OrdinalIgnoreCase)))
			{
				GD.PushError($"UltraSim client smoke failed: expected failure status containing '{_smokeSessionFailedContains}', got '{_smokeSessionFailureStatus ?? "<none>"}'.");
				GetTree().Quit(1);
				return;
			}
		}

		if (_smokeRequireChatReceived && _smokeChatReceivedCount <= 0)
		{
			GD.PushError($"UltraSim client smoke failed: expected chat echo '{_smokeChatText}' but received none.");
			GetTree().Quit(1);
			return;
		}

		if (_smokeRequireOwnerMovement && _smokeOwnerMovementCount <= 0)
		{
			GD.PushError("UltraSim client smoke failed: expected acknowledged owner movement but received none.");
			GetTree().Quit(1);
			return;
		}

		if (_smokeRequireServerUiMessageBox)
		{
			if (!_smokeSawServerUiMessageBox)
			{
				GD.PushError("UltraSim client smoke failed: server-driven message box did not mount.");
				GetTree().Quit(1);
				return;
			}

			if (!_smokeSawServerUiText)
			{
				GD.PushError("UltraSim client smoke failed: server-driven message box dynamic text was not rendered.");
				GetTree().Quit(1);
				return;
			}

			if (!_smokeSawServerUiUnsupportedChild)
			{
				GD.PushError("UltraSim client smoke failed: unsupported server-driven node did not preserve child rendering.");
				GetTree().Quit(1);
				return;
			}
		}

		if (_smokeRequireServerUiAction && !_smokeSawServerUiAction)
		{
			GD.PushError("UltraSim client smoke failed: server-driven message box action did not route.");
			GetTree().Quit(1);
			return;
		}

		if (_smokeRequireDisconnected)
		{
			if (!_smokeSawDisconnected)
			{
				GD.PushError("UltraSim client smoke failed: session never disconnected.");
				GetTree().Quit(1);
				return;
			}

			if (_sessionDriver?.IsRunning == true)
			{
				GD.PushError("UltraSim client smoke failed: network session is still running.");
				GetTree().Quit(1);
				return;
			}
		}

		GetTree().Quit();
	}

	private void OpenSmokeServerUiMessageBox()
	{
		if (UiManager?.Menus is null)
		{
			GD.PushError("Server UI smoke failed: menu canvas is unavailable.");
			return;
		}

		UiSchemaDocument schema = CreateSmokeMessageBoxSchema();
		var model = new UiModelDocument(
			schema.Identity,
			[
				UiModelSlotValue.String("messageText", SmokeMessageBoxText),
				UiModelSlotValue.String(SmokeTextInputSlot, "Test Pilot"),
				UiModelSlotValue.Boolean(SmokeToggleSlot, true),
				UiModelSlotValue.Number(SmokeNumberInputSlot, 1.25),
				UiModelSlotValue.Number(SmokeSliderSlot, 0.75),
				UiModelSlotValue.Integer(SmokeStepperSlot, 8),
				UiModelSlotValue.String(SmokeSelectSlot, "guild"),
			]);
		var open = new UiPanelOpenDocument(
			SmokeMessageBoxPanelId,
			schema.Identity,
			CatalogIdentity: null,
			model,
			SchemaDefinition: schema);

		ClientUiPanelOpenResult opened = _serverDrivenPanels.OpenPanel(open, rendererCapabilities: CreateSmokeRendererCapabilities());
		if (!opened.Opened || opened.Panel is null)
		{
			GD.PushError($"Server UI smoke failed: panel open returned {opened.Status} {opened.Error}");
			return;
		}

		if (!UiManager.Menus.TryMountServerDrivenPanel(opened.Panel, out string mountError))
		{
			GD.PushError($"Server UI smoke failed: {mountError}");
			return;
		}

		_smokeSawServerUiMessageBox = true;
		if (UiManager.Menus.TryFindServerDrivenText(opened.Panel.PanelInstanceId, SmokeMessageBoxText, out string textError))
		{
			_smokeSawServerUiText = true;
		}
		else
		{
			GD.PushError($"Server UI smoke failed: {textError}");
		}

		if (UiManager.Menus.TryFindServerDrivenText(opened.Panel.PanelInstanceId, SmokeUnsupportedChildText, out string childTextError))
		{
			_smokeSawServerUiUnsupportedChild = true;
		}
		else
		{
			GD.PushError($"Server UI smoke failed: {childTextError}");
		}

		if (!UiManager.Menus.TryFindServerDrivenSlotControl(opened.Panel.PanelInstanceId, SmokeTextInputSlot, out string textInputError))
		{
			GD.PushError($"Server UI smoke failed: {textInputError}");
			return;
		}

		if (!UiManager.Menus.TryFindServerDrivenSlotControl(opened.Panel.PanelInstanceId, SmokeToggleSlot, out string toggleError))
		{
			GD.PushError($"Server UI smoke failed: {toggleError}");
			return;
		}

		if (!UiManager.Menus.TryFindServerDrivenSlotControl(opened.Panel.PanelInstanceId, SmokeNumberInputSlot, out string numberInputError))
		{
			GD.PushError($"Server UI smoke failed: {numberInputError}");
			return;
		}

		if (!UiManager.Menus.TryFindServerDrivenSlotControl(opened.Panel.PanelInstanceId, SmokeSliderSlot, out string sliderError))
		{
			GD.PushError($"Server UI smoke failed: {sliderError}");
			return;
		}

		if (!UiManager.Menus.TryFindServerDrivenSlotControl(opened.Panel.PanelInstanceId, SmokeStepperSlot, out string stepperError))
		{
			GD.PushError($"Server UI smoke failed: {stepperError}");
			return;
		}

		if (!UiManager.Menus.TryFindServerDrivenSlotControl(opened.Panel.PanelInstanceId, SmokeSelectSlot, out string selectError))
		{
			GD.PushError($"Server UI smoke failed: {selectError}");
			return;
		}

		if (!UiManager.Menus.TryPressServerDrivenActionButton(opened.Panel.PanelInstanceId, SmokeMessageBoxActionId, out string pressError))
		{
			GD.PushError($"Server UI smoke failed: {pressError}");
			return;
		}

		SetStatusText("Server UI smoke message box mounted.");
	}

	private static UiSchemaDocument CreateSmokeMessageBoxSchema()
	{
		if (!UiSchemaIdentity.TryCreate(
			"local-smoke",
			"smoke.message_box",
			1,
			new string('f', 64),
			1,
			out UiSchemaIdentity identity,
			out string error))
		{
			throw new InvalidOperationException(error);
		}

		var metadata = new UiPanelMetadata(
			"smoke.message_box",
			UiSurfaceTarget.Menu,
			UiFocusMode.Normal,
			ExclusiveGroup: null,
			ZOrderClass: 10,
			UiOpenPolicy.ReplaceCurrent,
			UiBackPolicy.CloseAndFocusParent,
			UiTooltipPolicy.Allow,
			BlocksGameplayInput: true,
			PersistenceKey: null);

		return new UiSchemaDocument(
			identity,
			metadata,
			RootNodeId: 1,
			Nodes:
			[
				new UiSchemaNodeDescriptor(1, UiNodeKind.Panel, null, null, [2]),
				new UiSchemaNodeDescriptor(2, UiNodeKind.Column, null, null, [3, 4, 6, 7, 10]),
				new UiSchemaNodeDescriptor(3, UiNodeKind.DynamicText, "messageText", "Message", []),
				new UiSchemaNodeDescriptor(4, UiNodeKind.Image, null, null, [5]),
				new UiSchemaNodeDescriptor(5, UiNodeKind.Text, null, SmokeUnsupportedChildText, []),
				new UiSchemaNodeDescriptor(6, UiNodeKind.Separator, null, null, []),
				new UiSchemaNodeDescriptor(7, UiNodeKind.Group, null, "Smoke Settings", [8, 9, 12, 13, 14, 15]),
				new UiSchemaNodeDescriptor(8, UiNodeKind.TextInput, SmokeTextInputSlot, "Display Name", []),
				new UiSchemaNodeDescriptor(9, UiNodeKind.Toggle, SmokeToggleSlot, "Remember Account", []),
				new UiSchemaNodeDescriptor(10, UiNodeKind.Row, null, null, [11]),
				new UiSchemaNodeDescriptor(11, UiNodeKind.Button, null, "OK", [], ActionId: SmokeMessageBoxActionId),
				new UiSchemaNodeDescriptor(12, UiNodeKind.NumberInput, SmokeNumberInputSlot, "UI Scale", []),
				new UiSchemaNodeDescriptor(13, UiNodeKind.Slider, SmokeSliderSlot, "Music Volume", []),
				new UiSchemaNodeDescriptor(14, UiNodeKind.Stepper, SmokeStepperSlot, "Chat Lines", []),
				new UiSchemaNodeDescriptor(15, UiNodeKind.Select, SmokeSelectSlot, "Chat Channel", []),
			],
			PayloadSlots:
			[
				new UiPayloadSlotDescriptor("messageText", UiModelValueKind.String, Required: true, MaxStringLength: 128),
				new UiPayloadSlotDescriptor(SmokeTextInputSlot, UiModelValueKind.String, Required: true, MaxStringLength: 64),
				new UiPayloadSlotDescriptor(SmokeToggleSlot, UiModelValueKind.Boolean, Required: true),
				new UiPayloadSlotDescriptor(SmokeNumberInputSlot, UiModelValueKind.Number, Required: true, MinNumber: 0.5, MaxNumber: 3.0),
				new UiPayloadSlotDescriptor(SmokeSliderSlot, UiModelValueKind.Number, Required: true, MinNumber: 0.0, MaxNumber: 1.0),
				new UiPayloadSlotDescriptor(SmokeStepperSlot, UiModelValueKind.Integer, Required: true, MinNumber: 1, MaxNumber: 20),
				new UiPayloadSlotDescriptor(SmokeSelectSlot, UiModelValueKind.String, Required: true, MaxStringLength: 32),
			],
			Actions:
			[
				new UiActionDescriptor(
					SmokeMessageBoxActionId,
					"OK",
					IconId: null,
					UiActionRole.Primary,
					UiActionContextKind.Panel,
					RequiresConfirm: false,
					ChildMenuKey: null),
			],
			StateBindings: [],
			CatalogIdentity: null,
			SelectOptions:
			[
				new UiSelectOptionDescriptor(SmokeSelectSlot, "local", "Local"),
				new UiSelectOptionDescriptor(SmokeSelectSlot, "guild", "Guild"),
				new UiSelectOptionDescriptor(SmokeSelectSlot, "party", "Party", Disabled: true),
			]);
	}

	private static UiRendererCapabilities CreateSmokeRendererCapabilities()
	{
		return new UiRendererCapabilities(
			ProtocolVersion: 1,
			CapabilityFlags: UiCapabilityFlags.None,
			MaxSchemaBytes: 64 * 1024,
			MaxModelBytes: 16 * 1024,
			MaxNodeCount: 512,
			MaxTreeDepth: 32,
			MaxRepeaterRows: 256,
			MaxTableColumns: 24);
	}

	private void UpdateSmokeSchedule(double delta)
	{
		_smokeElapsedSeconds += Math.Max(0.0, delta);
		if (_nextSmokeDisconnectIndex < _smokeDisconnectAtSeconds.Count &&
			_smokeElapsedSeconds >= _smokeDisconnectAtSeconds[_nextSmokeDisconnectIndex])
		{
			_nextSmokeDisconnectIndex++;
			SetStatusText("Smoke disconnect requested.");
			HandleDisconnectRequested();
		}

		if (!_smokeChatRequested &&
			_smokeChatAfterSeconds > 0.0 &&
			_smokeElapsedSeconds >= _smokeChatAfterSeconds)
		{
			if (_sessionDriver?.IsReady != true)
			{
				return;
			}

			_smokeChatRequested = true;
			SetStatusText("Smoke chat requested.");
			if (_sessionDriver.TrySendChat(_smokeChatText))
			{
				UiManager?.Menus?.AppendSystemChatLine($"Smoke chat sent: {_smokeChatText}");
			}
			else
			{
				GD.PushWarning("Smoke chat send failed.");
			}
		}

		if (!_smokeReconnectRequested &&
			_smokeReconnectAfterSeconds > 0.0 &&
			_smokeElapsedSeconds >= _smokeReconnectAfterSeconds)
		{
			_smokeReconnectRequested = true;
			if (_sessionDriver?.IsRunning == true)
			{
				GD.PushWarning("Ignoring smoke reconnect because a network session is still running.");
				return;
			}

			if (!_lastDirectConnectRequest.HasValue)
			{
				GD.PushWarning("Ignoring smoke reconnect because no direct-connect request is available.");
				return;
			}

			SetStatusText("Smoke reconnect requested.");
			HandleDirectConnectRequested(_lastDirectConnectRequest.Value);
		}
	}

	private bool TryReadSmokeMoveInput(out short moveX, out short moveY)
	{
		moveX = _smokeMoveX;
		moveY = _smokeMoveY;
		if (_smokeMoveAfterSeconds <= 0.0)
		{
			return false;
		}

		if (_smokeElapsedSeconds < _smokeMoveAfterSeconds)
		{
			return false;
		}

		double duration = _smokeMoveDurationSeconds <= 0.0 ? 1.0 : _smokeMoveDurationSeconds;
		if (_smokeElapsedSeconds > _smokeMoveAfterSeconds + duration)
		{
			return false;
		}

		return moveX != 0 || moveY != 0;
	}

	private void ReadSmokeDisconnectSchedule()
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--us-smoke-disconnect-after=", StringComparison.OrdinalIgnoreCase))
			{
				AddSmokeDisconnectTimes(arg["--us-smoke-disconnect-after=".Length..], "--us-smoke-disconnect-after");
			}
			else if (arg.StartsWith("--us-smoke-final-disconnect-after=", StringComparison.OrdinalIgnoreCase))
			{
				AddSmokeDisconnectTimes(arg["--us-smoke-final-disconnect-after=".Length..], "--us-smoke-final-disconnect-after");
			}
		}

		_smokeDisconnectAtSeconds.Sort();
	}

	private void AddSmokeDisconnectTimes(string value, string name)
	{
		string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0)
		{
			GD.PushWarning($"Invalid {name} value.");
			return;
		}

		foreach (string part in parts)
		{
			if (double.TryParse(part, out double seconds) && seconds > 0.0)
			{
				_smokeDisconnectAtSeconds.Add(seconds);
				continue;
			}

			GD.PushWarning($"Invalid {name} value '{part}'.");
		}
	}

	private static bool HasUserArg(string expected)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static double ReadPositiveUserArg(string prefix)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (double.TryParse(arg[prefix.Length..], out double seconds) && seconds > 0.0)
			{
				return seconds;
			}

			GD.PushWarning($"Invalid {prefix.TrimEnd('=')} value.");
			return -1.0;
		}

		return -1.0;
	}

	private static int ReadPositiveIntUserArg(string prefix)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (int.TryParse(arg[prefix.Length..], out int value) && value > 0)
			{
				return value;
			}

			GD.PushWarning($"Invalid {prefix.TrimEnd('=')} value.");
			return 0;
		}

		return 0;
	}

	private static int ReadIntUserArg(string prefix, int defaultValue)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (int.TryParse(arg[prefix.Length..], out int value))
			{
				return value;
			}

			GD.PushWarning($"Invalid {prefix.TrimEnd('=')} value.");
			return defaultValue;
		}

		return defaultValue;
	}

	private static string? ReadStringUserArg(string prefix)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string value = arg[prefix.Length..];
			return string.IsNullOrWhiteSpace(value) ? null : value;
		}

		return null;
	}

	private void UpdateNetworkSummary()
	{
		if (UiManager is null)
		{
			return;
		}

		if (_sessionDriver is null || !_sessionDriver.IsRunning)
		{
			UiManager.SetNetworkSummaryText(ClientNetworkSummaryFormatter.FormatOffline());
			return;
		}

		UiManager.SetNetworkSummaryText(ClientNetworkSummaryFormatter.Format(
			_sessionDriver.IsReady,
			_sessionDriver.SessionId,
			_sessionDriver.LastPingRoundTripMilliseconds,
			_sessionDriver.LastPumpProfile));
	}

	private TNode? ResolveNode<TNode>(NodePath? path)
		where TNode : Node
	{
		return path is null || path.IsEmpty
			? null
			: GetNodeOrNull<TNode>(path);
	}
}
