
using Godot;

public partial class SimpleMovement : Node3D
{
    [Export] private CharacterBody3D characterBody;
    [Export] private Node animationDriver;
    [Export] private Node3D Visual;

    [Export] public float Speed = 6.0f;
    [Export] public float SprintMultiplier = 1.6f;

    public override void _Ready()
    {
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("interact"))
        {
            TryInteract();
        }

        if (@event.IsActionPressed("attack"))
        {
            ((AnimatorDriver)animationDriver).Attack();
        }
    }


    public override void _PhysicsProcess(double delta)
    {
        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 dir = new Vector3(input.X, 0, input.Y);

        if (dir.LengthSquared() > 1)
            dir = dir.Normalized();

        float currentSpeed = Speed;
        if (Input.IsActionPressed("sprint"))
            currentSpeed *= SprintMultiplier;

        characterBody.Velocity = dir * currentSpeed;
        characterBody.MoveAndSlide();
    }

public void TryInteract()
{
    GD.Print("Trying to interact...");

    var space = GetWorld3D().DirectSpaceState;

    Vector3 from = characterBody.GlobalTransform.Origin;
    Vector3 forward = Visual.GlobalTransform.Basis.Z;
    Vector3 to = from + forward * 2.0f;

    var query = PhysicsRayQueryParameters3D.Create(from, to);

    // Don't hit yourself
    query.Exclude = new Godot.Collections.Array<Rid> { characterBody.GetRid() };

    var result = space.IntersectRay(query);

    GD.Print($"Ray hit count: {result.Count}");

    if (result.Count == 0)
        return;

    var colliderNode = (Node)result["collider"];
    GD.Print($"Hit collider node: {colliderNode.Name} ({colliderNode.GetType().Name})");

    // Prefer script on the physics body
    if (colliderNode is IInteractable i)
    {
        i.Interact(this);
        ((AnimatorDriver)animationDriver).Interact();
        return;
    }

    // Common case: script is on parent
    if (colliderNode.GetParent() is IInteractable p)
    {
        p.Interact(this);
        ((AnimatorDriver)animationDriver).Interact();
        return;
    }

    GD.Print("Hit something, but it wasn't interactable (or script is on a different node).");
}


    public void Interact()
    {
            ((AnimatorDriver)animationDriver).Interact();
    }
}