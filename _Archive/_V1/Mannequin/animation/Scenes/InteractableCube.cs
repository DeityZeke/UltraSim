using Godot;

public partial class InteractableCube : Node3D, IInteractable
{
    public void Interact(Node instigator)
    {
        GD.Print("Interacted!");
    }
}
