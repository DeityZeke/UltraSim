using Godot;

namespace UltraSim.Client.Godot.Camera;

[GlobalClass]
public partial class OrbitFollowCameraRig : Node3D
{
    [Export] public NodePath? TargetPath { get; set; }
    [Export(PropertyHint.Range, "0.0,64.0,0.1,or_greater")] public float FollowSharpness { get; set; } = 12.0f;
    [Export] public bool SnapToTargetOnReady { get; set; } = true;
    [Export] public float YawDegrees { get; set; } = 180.0f;
    [Export] public float PitchDegrees { get; set; } = -15.0f;
    [Export(PropertyHint.Range, "0.1,64.0,0.1,or_greater")] public float ArmLength { get; set; } = 4.5f;
    [Export(PropertyHint.Range, "0.1,64.0,0.1,or_greater")] public float MinArmLength { get; set; } = 2.0f;
    [Export(PropertyHint.Range, "0.1,64.0,0.1,or_greater")] public float MaxArmLength { get; set; } = 12.0f;
    [Export] public Vector3 FollowOffset { get; set; } = new(0.0f, 0.25f, 0.0f);
    [Export(PropertyHint.Range, "1.0,179.0,0.1,or_greater")] public float CameraFovDegrees { get; set; } = 65.0f;
    [Export] public bool EnableMouseOrbit { get; set; } = true;
    [Export(PropertyHint.Range, "0.01,2.0,0.01,or_greater")] public float MouseOrbitSensitivity { get; set; } = 0.18f;
    [Export(PropertyHint.Range, "0.1,8.0,0.1,or_greater")] public float ZoomStep { get; set; } = 0.75f;
    [Export(PropertyHint.Range, "-89.0,0.0,0.1")] public float MinPitchDegrees { get; set; } = -65.0f;
    [Export(PropertyHint.Range, "-89.0,0.0,0.1")] public float MaxPitchDegrees { get; set; } = -5.0f;

    public Node3D YawPivot { get; private set; } = null!;
    public Node3D PitchPivot { get; private set; } = null!;
    public SpringArm3D SpringArm { get; private set; } = null!;
    public Camera3D Camera { get; private set; } = null!;

    private Node3D? _target;
    private bool _isOrbiting;

    public override void _Ready()
    {
        CacheNodes();
        ApplyRigSettings();
        ResolveTarget();

        if (SnapToTargetOnReady && _target is not null)
        {
            GlobalPosition = _target.GlobalPosition + FollowOffset;
        }
    }

    public override void _Process(double delta)
    {
        ResolveTarget();
        if (_target is null)
        {
            return;
        }

        Vector3 desired = _target.GlobalPosition + FollowOffset;
        float alpha = 1.0f - Mathf.Exp(-FollowSharpness * (float)delta);
        GlobalPosition = GlobalPosition.Lerp(desired, alpha);
    }

    public override void _ExitTree()
    {
        if (_isOrbiting)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!EnableMouseOrbit)
        {
            return;
        }

        if (@event is InputEventMouseButton mouseButton)
        {
            HandleMouseButton(mouseButton);
            return;
        }

        if (_isOrbiting && @event is InputEventMouseMotion mouseMotion)
        {
            YawDegrees -= mouseMotion.Relative.X * MouseOrbitSensitivity;
            PitchDegrees = Mathf.Clamp(
                PitchDegrees - mouseMotion.Relative.Y * MouseOrbitSensitivity,
                MinPitchDegrees,
                MaxPitchDegrees);
            ApplyRigSettings();
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetTarget(Node3D? target)
    {
        bool targetChanged = !ReferenceEquals(_target, target);
        _target = target;

        if (targetChanged && SnapToTargetOnReady && _target is not null)
        {
            GlobalPosition = _target.GlobalPosition + FollowOffset;
        }
    }

    private void CacheNodes()
    {
        YawPivot ??= GetNode<Node3D>("YawPivot");
        PitchPivot ??= GetNode<Node3D>("YawPivot/PitchPivot");
        SpringArm ??= GetNode<SpringArm3D>("YawPivot/PitchPivot/SpringArm3D");
        Camera ??= GetNode<Camera3D>("YawPivot/PitchPivot/SpringArm3D/Camera3D");
    }

    private void ApplyRigSettings()
    {
        ArmLength = Mathf.Clamp(ArmLength, MinArmLength, MaxArmLength);
        PitchDegrees = Mathf.Clamp(PitchDegrees, MinPitchDegrees, MaxPitchDegrees);
        YawPivot.RotationDegrees = new Vector3(0.0f, YawDegrees, 0.0f);
        PitchPivot.RotationDegrees = new Vector3(PitchDegrees, 0.0f, 0.0f);
        SpringArm.SpringLength = ArmLength;
        Camera.Fov = CameraFovDegrees;
        Camera.Current = true;
    }

    private void ResolveTarget()
    {
        if (_target is not null && IsInstanceValid(_target))
        {
            return;
        }

        _target = null;
        if (TargetPath is null || TargetPath.IsEmpty)
        {
            return;
        }

        _target = GetNodeOrNull<Node3D>(TargetPath);
    }

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        if (mouseButton.ButtonIndex == MouseButton.Right)
        {
            _isOrbiting = mouseButton.Pressed;
            Input.MouseMode = _isOrbiting
                ? Input.MouseModeEnum.Captured
                : Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!mouseButton.Pressed)
        {
            return;
        }

        if (mouseButton.ButtonIndex == MouseButton.WheelUp)
        {
            ArmLength -= ZoomStep;
            ApplyRigSettings();
            GetViewport().SetInputAsHandled();
        }
        else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
        {
            ArmLength += ZoomStep;
            ApplyRigSettings();
            GetViewport().SetInputAsHandled();
        }
    }
}
