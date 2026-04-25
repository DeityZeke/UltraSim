using Godot;
using UltraSim.Client;
using UltraSim.Client.Godot.Camera;
using UltraSim.Client.Godot.Mobiles;

namespace UltraSim.Client.Godot.Controllers;

[GlobalClass]
public partial class LocalPlayerController : Node
{
    [Export] public NodePath? ControlledCharacterPath { get; set; }
    [Export] public NodePath? CameraRigPath { get; set; }
    [Export] public bool EnablePreviewControls { get; set; } = true;
    [Export(PropertyHint.Range, "0.0,32.0,0.1,or_greater")] public float PreviewMoveSpeed { get; set; } = 4.0f;
    [Export] public bool GameplayInputBlocked { get; set; }

    public CharacterRoot? ControlledCharacter { get; private set; }
    public OrbitFollowCameraRig? CameraRig { get; private set; }

    public override void _Ready()
    {
        ResolveBindings();
        BindCameraRig();
        CallDeferred(nameof(RefreshBindings));
    }

    public override void _Process(double delta)
    {
        if (!EnablePreviewControls || ControlledCharacter is null)
        {
            return;
        }

        Vector2 input = ReadMoveInputAxes();
        if (input.LengthSquared() <= 0.0001f)
        {
            ControlledCharacter.SetPresentationMoving(false);
            return;
        }

        Vector3 moveDirection = ResolveMoveDirection(input);
        if (moveDirection.LengthSquared() <= 0.0001f)
        {
            ControlledCharacter.SetPresentationMoving(false);
            return;
        }

        moveDirection = moveDirection.Normalized();
        ControlledCharacter.GlobalPosition += moveDirection * PreviewMoveSpeed * (float)delta;

        float facingYawDegrees = Mathf.RadToDeg(Mathf.Atan2(moveDirection.X, moveDirection.Z));
        ControlledCharacter.SetPresentationFacingYawDegrees(facingYawDegrees);
        ControlledCharacter.SetPresentationMoving(true);
    }

    public Vector2 ReadMoveInputAxes()
    {
        var focus = new ClientInputFocusState(
            TextInputFocused: IsTextInputFocused(),
            MenuOpen: false,
            ModalInputCaptured: false,
            ExplicitGameplayBlock: GameplayInputBlocked);

        if (focus.BlocksGameplayMovement)
        {
            return Vector2.Zero;
        }

        float horizontal = GetMovementAxis("move_left", "move_right", Key.A, Key.D);
        float vertical = GetMovementAxis("move_forward", "move_back", Key.W, Key.S);
        return new Vector2(horizontal, vertical);
    }

    public void SetControlledCharacter(CharacterRoot? character)
    {
        ControlledCharacter = character;
        BindCameraRig();
    }

    public void SetCameraRig(OrbitFollowCameraRig? cameraRig)
    {
        CameraRig = cameraRig;
        BindCameraRig();
    }

    private void ResolveBindings()
    {
        ControlledCharacter = ControlledCharacterPath is null || ControlledCharacterPath.IsEmpty
            ? ControlledCharacter
            : GetNodeOrNull<CharacterRoot>(ControlledCharacterPath);
        CameraRig = CameraRigPath is null || CameraRigPath.IsEmpty
            ? CameraRig
            : GetNodeOrNull<OrbitFollowCameraRig>(CameraRigPath);
    }

    private void RefreshBindings()
    {
        ResolveBindings();
        BindCameraRig();
    }

    private void BindCameraRig()
    {
        CameraRig?.SetTarget(ControlledCharacter?.CameraPivot);
    }

    private Vector3 ResolveMoveDirection(Vector2 input)
    {
        if (CameraRig?.Camera is null)
        {
            return new Vector3(input.X, 0.0f, -input.Y);
        }

        Basis basis = CameraRig.Camera.GlobalBasis;
        Vector3 cameraForward = -basis.Z;
        Vector3 cameraRight = basis.X;
        cameraForward.Y = 0.0f;
        cameraRight.Y = 0.0f;

        if (cameraForward.LengthSquared() > 0.0001f)
        {
            cameraForward = cameraForward.Normalized();
        }

        if (cameraRight.LengthSquared() > 0.0001f)
        {
            cameraRight = cameraRight.Normalized();
        }

        return (cameraRight * input.X) + (cameraForward * -input.Y);
    }

    private static float GetMovementAxis(string negativeAction, string positiveAction, Key negativeKey, Key positiveKey)
    {
        bool negativePressed = InputMap.HasAction(negativeAction)
            ? Input.IsActionPressed(negativeAction)
            : Input.IsKeyPressed(negativeKey);
        bool positivePressed = InputMap.HasAction(positiveAction)
            ? Input.IsActionPressed(positiveAction)
            : Input.IsKeyPressed(positiveKey);

        return (positivePressed ? 1.0f : 0.0f) - (negativePressed ? 1.0f : 0.0f);
    }

    private bool IsTextInputFocused()
    {
        Control? focusOwner = GetViewport()?.GuiGetFocusOwner();
        return focusOwner is LineEdit or TextEdit or CodeEdit;
    }
}
