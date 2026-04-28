using Godot;
using UltraSim.Client.Godot.Camera;
using UltraSim.Client.Godot.Controllers;
using UltraSim.Client.Godot.Mobiles;
using UltraSim.Client.Godot.UI;
using UltraSim.Client.Godot.World;

namespace UltraSim.Client.Godot;

[GlobalClass]
public partial class GodotClientRuntimeRoot : Node
{
	[Export] public NodePath? CharacterWorldPath { get; set; }
	[Export] public NodePath? LocalPlayerControllerPath { get; set; }
	[Export] public NodePath? CameraRigPath { get; set; }
	[Export] public NodePath? UiManagerPath { get; set; }
	[Export] public bool StartPreviewOnReady { get; set; } = true;
	[Export(PropertyHint.Range, "0.0,32.0,0.1,or_greater")] public float LiveMoveSpeed { get; set; } = 4.0f;

	private readonly List<double> _smokeDisconnectAtSeconds = new();

	public ClientCharacterWorld? CharacterWorld { get; private set; }
	public LocalPlayerController? LocalPlayerController { get; private set; }
	public OrbitFollowCameraRig? CameraRig { get; private set; }
	public UiManager? UiManager { get; private set; }

	private GodotClientNetworkSessionDriver? _sessionDriver;
	private GodotNetworkPresentationBridge? _presentationBridge;
	private ClientDirectConnectRequest? _lastDirectConnectRequest;
	private uint _localClientTick;
	private bool _wasSessionReady;
	private bool _smokeRequireConnected;
	private bool _smokeSawConnected;
	private bool _smokeRequireDisconnected;
	private bool _smokeSawDisconnected;
	private bool _smokeRequireChatReceived;
	private double _smokeReconnectAfterSeconds = -1.0;
	private double _smokeChatAfterSeconds = -1.0;
	private double _smokeElapsedSeconds;
	private bool _smokeReconnectRequested;
	private bool _smokeChatRequested;
	private string _smokeChatText = "hello from smoke";
	private int _smokeConnectedCount;
	private int _smokeRequiredConnectedCount;
	private int _smokeChatReceivedCount;
	private int _nextSmokeDisconnectIndex;

	public override void _Ready()
	{
		_smokeRequireConnected = HasUserArg("--us-smoke-require-connected");
		_smokeRequireDisconnected = HasUserArg("--us-smoke-require-disconnected");
		_smokeRequireChatReceived = HasUserArg("--us-smoke-require-chat-received");
		_smokeReconnectAfterSeconds = ReadPositiveUserArg("--us-smoke-reconnect-after=");
		_smokeChatAfterSeconds = ReadPositiveUserArg("--us-smoke-chat-after=");
		_smokeChatText = ReadStringUserArg("--us-smoke-chat-text=") ?? _smokeChatText;
		_smokeRequiredConnectedCount = ReadPositiveIntUserArg("--us-smoke-require-connected-count=");
		ReadSmokeDisconnectSchedule();
		CharacterWorld = ResolveNode<ClientCharacterWorld>(CharacterWorldPath);
		LocalPlayerController = ResolveNode<LocalPlayerController>(LocalPlayerControllerPath);
		CameraRig = ResolveNode<OrbitFollowCameraRig>(CameraRigPath);
		UiManager = ResolveNode<UiManager>(UiManagerPath);

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
		if (UiManager?.Menus is not null)
		{
			UiManager.Menus.DirectConnectRequested += HandleDirectConnectRequested;
			UiManager.Menus.DisconnectRequested += HandleDisconnectRequested;
			UiManager.Menus.PreviewRequested += HandlePreviewRequested;
			UiManager.Menus.ChatSubmitted += HandleChatSubmitted;
		}

		ScheduleSmokeQuitIfRequested();

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
		SendMovementInputIfReady(delta);
		UpdateNetworkSummary();
		UpdateSmokeSchedule(delta);

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
		}

		_sessionDriver?.Dispose();
		_sessionDriver = null;
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

	private void HandleChatMessage(UltraSim.Network.ChatMessageEventPacket packet)
	{
		if (string.Equals(packet.Text, _smokeChatText, StringComparison.Ordinal))
		{
			_smokeChatReceivedCount++;
		}

		UiManager?.Menus?.AppendChatLine(packet.SenderName, packet.Text);
		UiManager?.World?.ShowOverheadChat(packet.SenderEntityId, packet.Text);
	}

	private void HandleOwnerMovementState(ClientMovementStateReceiveResult result)
	{
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
		var input = new ClientMovementInput(
			MoveX: hasMoveInput ? (short)Mathf.RoundToInt(Mathf.Clamp(moveDirection.X, -1.0f, 1.0f) * 100.0f) : (short)0,
			MoveY: hasMoveInput ? (short)Mathf.RoundToInt(Mathf.Clamp(moveDirection.Z, -1.0f, 1.0f) * 100.0f) : (short)0,
			Flags: 0);

		global::Godot.Vector3 position = LocalPlayerController.ControlledCharacter.GlobalPosition;
		float velocityX = input.MoveX / 100.0f * LiveMoveSpeed;
		float velocityY = input.MoveY / 100.0f * LiveMoveSpeed;
		var predicted = new ClientMovementState(
			position.X + velocityX * (float)delta,
			position.Z + velocityY * (float)delta,
			velocityX,
			velocityY);

		ApplyLocalPredictedMovement(predicted, hasMoveInput);

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

		if (_smokeRequireChatReceived && _smokeChatReceivedCount <= 0)
		{
			GD.PushError($"UltraSim client smoke failed: expected chat echo '{_smokeChatText}' but received none.");
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
