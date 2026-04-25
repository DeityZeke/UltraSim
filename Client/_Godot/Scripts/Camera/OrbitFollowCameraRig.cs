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
    [Export] public Vector3 FollowOffset { get; set; } = new(0.0f, 0.25f, 0.0f);
    [Export(PropertyHint.Range, "1.0,179.0,0.1,or_greater")] public float CameraFovDegrees { get; set; } = 65.0f;

    public Node3D YawPivot { get; private set; } = null!;
    public Node3D PitchPivot { get; private set; } = null!;
    public SpringArm3D SpringArm { get; private set; } = null!;
    public Camera3D Camera { get; private set; } = null!;

    private Node3D? _target;

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
}
