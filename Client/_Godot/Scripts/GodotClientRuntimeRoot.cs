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

        if (StartPreviewOnReady)
        {
            EnterPreviewMode();
        }
    }

    public void EnterPreviewMode()
    {
        CharacterWorld?.SpawnPreviewActor();
        UiManager?.SetStatusText("Preview shell ready");
    }

    public void ClearSessionWorld()
    {
        CharacterWorld?.ClearAllActors();
        UiManager?.SetStatusText("Session cleared");
    }

    private TNode? ResolveNode<TNode>(NodePath? path)
        where TNode : Node
    {
        return path is null || path.IsEmpty
            ? null
            : GetNodeOrNull<TNode>(path);
    }
}
