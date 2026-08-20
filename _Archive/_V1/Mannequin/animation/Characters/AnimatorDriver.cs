
using Godot;

public partial class AnimatorDriver : Node
{
    [Export] public AnimationTree AnimTree;
    [Export] public CharacterBody3D Body;
    [Export] public Node3D Visual;

    [Export] public float SprintSpeed = 10.0f;

    private AnimationNodeStateMachinePlayback _sm;

    public override void _Ready()
    {
        _sm = AnimTree.Get("parameters/playback").As<AnimationNodeStateMachinePlayback>();
    }

    public void Interact()
    {
        _sm?.Travel("Interact");
    }

    public void Attack()
    {
        _sm?.Travel("Attack");
    }

    public override void _Process(double delta)
    {
        // 1. Locomotion blend (in-place)
        Vector3 v = Body.Velocity;
        v.Y = 0;

        float speed = v.Length();
        float speed01 = Mathf.Clamp(speed / SprintSpeed, 0f, 1f);

        AnimTree.Set( "parameters/Locomotion/blend_position", speed01 * 3.0f );

        // 2. Facing (visual only)
        if (v.LengthSquared() > 0.001f)
        {
            float yaw = Mathf.Atan2(v.X, v.Z);
            Visual.Rotation = new Vector3(0, yaw, 0);
        }
    }
}