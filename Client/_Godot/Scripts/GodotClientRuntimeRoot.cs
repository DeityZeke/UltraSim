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

        _sessionDriver = new GodotClientNetworkSessionDriver(SetStatusText, HandleChatMessage);
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

        UiManager?.Menus?.ShowSessionStatus();
    }

    private void HandleDisconnectRequested()
    {
        _sessionDriver?.Disconnect();
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
