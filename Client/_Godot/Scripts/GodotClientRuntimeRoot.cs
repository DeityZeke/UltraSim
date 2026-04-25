using Godot;
using UltraSim.Client.Godot.Camera;
using UltraSim.Client.Godot.Controllers;
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

    public ClientCharacterWorld? CharacterWorld { get; private set; }
    public LocalPlayerController? LocalPlayerController { get; private set; }
    public OrbitFollowCameraRig? CameraRig { get; private set; }
    public UiManager? UiManager { get; private set; }

    private GodotClientNetworkSessionDriver? _sessionDriver;
    private GodotNetworkPresentationBridge? _presentationBridge;
    private uint _localClientTick;
    private bool _wasSessionReady;

    public override void _Ready()
    {
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
            HandleObserverStateReset);
        if (UiManager?.Menus is not null)
        {
            UiManager.Menus.DirectConnectRequested += HandleDirectConnectRequested;
            UiManager.Menus.DisconnectRequested += HandleDisconnectRequested;
            UiManager.Menus.ChatSubmitted += HandleChatSubmitted;
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
            UiManager.Menus.ChatSubmitted -= HandleChatSubmitted;
        }

        _sessionDriver?.Dispose();
        _sessionDriver = null;
        _presentationBridge = null;
    }

    public void EnterPreviewMode()
    {
        CharacterWorld?.SpawnPreviewActor();
        UiManager?.Menus?.EnterPreviewSession();
        SetStatusText("Preview shell ready");
    }

    public void ClearSessionWorld()
    {
        CharacterWorld?.ClearAllActors();
        SetStatusText("Session cleared");
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

        _wasSessionReady = false;
        CharacterWorld?.ClearAllActors();
        UiManager?.Menus?.ShowSessionStatus();
    }

    private void HandleDisconnectRequested()
    {
        _sessionDriver?.Disconnect();
        _wasSessionReady = false;
        ClearSessionWorld();
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
        UiManager?.Menus?.AppendChatLine(packet.SenderName, packet.Text);
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

        Vector2 axes = LocalPlayerController.ReadMoveInputAxes();
        var input = new ClientMovementInput(
            MoveX: (short)Mathf.RoundToInt(Mathf.Clamp(axes.X, -1.0f, 1.0f) * 100.0f),
            MoveY: (short)Mathf.RoundToInt(Mathf.Clamp(-axes.Y, -1.0f, 1.0f) * 100.0f),
            Flags: 0);

        global::Godot.Vector3 position = LocalPlayerController.ControlledCharacter.GlobalPosition;
        float velocityX = input.MoveX / 100.0f;
        float velocityY = input.MoveY / 100.0f;
        var predicted = new ClientMovementState(
            position.X + velocityX * (float)delta,
            position.Z + velocityY * (float)delta,
            velocityX,
            velocityY);

        _localClientTick++;
        _sessionDriver.TrySendMovementInput(input, predicted);
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
        UiManager?.Menus?.EnterConnectedSession();
        UiManager?.Menus?.AppendSystemChatLine("Connected.");
    }

    private void SetStatusText(string text)
    {
        UiManager?.SetStatusText(text);
    }

    private TNode? ResolveNode<TNode>(NodePath? path)
        where TNode : Node
    {
        return path is null || path.IsEmpty
            ? null
            : GetNodeOrNull<TNode>(path);
    }
}
