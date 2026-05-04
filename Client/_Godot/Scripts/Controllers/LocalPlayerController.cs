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
    private bool _fallbackJumpWasPressed;

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

        if (!TryReadMoveDirection(out Vector3 moveDirection))
        {
            ControlledCharacter.SetPresentationMoving(false);
            return;
        }

        ControlledCharacter.GlobalPosition += moveDirection * PreviewMoveSpeed * (float)delta;

        float facingYawDegrees = Mathf.RadToDeg(Mathf.Atan2(moveDirection.X, moveDirection.Z));
        ControlledCharacter.SetPresentationFacingYawDegrees(facingYawDegrees);
        ControlledCharacter.SetPresentationMoving(true);
    }

    public bool TryReadMoveDirection(out Vector3 moveDirection)
    {
        return TryResolveMoveDirection(ReadMoveInputAxes(), out moveDirection);
    }

    public bool TryResolveMoveDirection(Vector2 input, out Vector3 moveDirection)
    {
        if (input.LengthSquared() <= 0.0001f)
        {
            moveDirection = Vector3.Zero;
            return false;
        }

        moveDirection = ResolveMoveDirection(input);
        if (moveDirection.LengthSquared() <= 0.0001f)
        {
            moveDirection = Vector3.Zero;
            return false;
        }

        moveDirection = moveDirection.Normalized();
        return true;
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

        float horizontal = GetMovementAxis(new[] { "walk_left", "move_left" }, new[] { "walk_right", "move_right" }, Key.A, Key.D);
        float vertical = GetMovementAxis(new[] { "walk_forward", "move_forward" }, new[] { "walk_back", "move_back" }, Key.W, Key.S);
        return new Vector2(horizontal, vertical);
    }

    public bool ReadSprintPressed()
    {
        if (IsGameplayInputBlocked())
            return false;

        return IsAnyActionPressed(new[] { "sprint" }) || Input.IsKeyPressed(Key.Shift);
    }

    public bool ReadJumpJustPressed()
    {
        if (IsGameplayInputBlocked())
            return false;

        bool fallbackPressed = Input.IsKeyPressed(Key.Space);
        bool fallbackJustPressed = fallbackPressed && !_fallbackJumpWasPressed;
        _fallbackJumpWasPressed = fallbackPressed;
        return IsAnyActionJustPressed(new[] { "jump" }) || fallbackJustPressed;
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

    private static float GetMovementAxis(string[] negativeActions, string[] positiveActions, Key negativeKey, Key positiveKey)
    {
        bool negativePressed = IsAnyActionPressed(negativeActions) || Input.IsKeyPressed(negativeKey);
        bool positivePressed = IsAnyActionPressed(positiveActions) || Input.IsKeyPressed(positiveKey);

        return (positivePressed ? 1.0f : 0.0f) - (negativePressed ? 1.0f : 0.0f);
    }

    private static bool IsAnyActionPressed(string[] actions)
    {
        for (int i = 0; i < actions.Length; i++)
        {
            if (InputMap.HasAction(actions[i]) && Input.IsActionPressed(actions[i]))
                return true;
        }

        return false;
    }

    private static bool IsAnyActionJustPressed(string[] actions)
    {
        for (int i = 0; i < actions.Length; i++)
        {
            if (InputMap.HasAction(actions[i]) && Input.IsActionJustPressed(actions[i]))
                return true;
        }

        return false;
    }

    private bool IsGameplayInputBlocked()
    {
        var focus = new ClientInputFocusState(
            TextInputFocused: IsTextInputFocused(),
            MenuOpen: false,
            ModalInputCaptured: false,
            ExplicitGameplayBlock: GameplayInputBlocked);

        return focus.BlocksGameplayMovement;
    }

    private bool IsTextInputFocused()
    {
        Control? focusOwner = GetViewport()?.GuiGetFocusOwner();
        return focusOwner is LineEdit or TextEdit or CodeEdit;
    }
}
