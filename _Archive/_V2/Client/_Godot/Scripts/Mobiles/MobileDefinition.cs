using Godot;

namespace UltraSim.Client.Godot.Mobiles;

[GlobalClass]
public partial class MobileDefinition : Resource
{
    [Export] public string DefinitionId { get; set; } = "default";
    [Export] public string DisplayName { get; set; } = "Traveler";
    [Export] public PackedScene? VisualScene { get; set; }
    [Export] public Vector3 VisualOffset { get; set; } = Vector3.Zero;
    [Export] public Vector3 VisualRotationDegrees { get; set; } = Vector3.Zero;
    [Export] public Vector3 VisualScale { get; set; } = Vector3.One;
    [Export] public Vector3 CameraPivotOffset { get; set; } = new(0.0f, 1.45f, 0.0f);
    [Export] public Vector3 NameplateOffset { get; set; } = new(0.0f, 2.05f, 0.0f);
    [Export] public Vector3 EffectsOffset { get; set; } = new(0.0f, 1.0f, 0.0f);
    [Export] public Color FallbackBodyColor { get; set; } = new(0.25f, 0.62f, 0.86f);
}
