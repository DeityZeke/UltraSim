using Godot;
using UltraSim.Client.Godot.World;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class WorldCanvasManager : CanvasLayer
{
    [Export] public NodePath? CharacterWorldPath { get; set; }

    public ClientCharacterWorld? CharacterWorld { get; private set; }

    public override void _Ready()
    {
        CharacterWorld = CharacterWorldPath is null || CharacterWorldPath.IsEmpty
            ? null
            : GetNodeOrNull<ClientCharacterWorld>(CharacterWorldPath);
    }
}
