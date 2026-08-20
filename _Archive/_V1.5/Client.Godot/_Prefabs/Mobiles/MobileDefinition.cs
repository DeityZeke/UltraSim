using Godot;

namespace Client.Godot.Prefabs.Mobiles
{
    [GlobalClass]
    public partial class MobileDefinition : Resource
    {
        // Runtime-facing identity so gameplay/content layers can request this definition by id.
        [Export] public StringName Id { get; set; } = new("mobile.unnamed");

        // Wrapped visual scene, not a raw imported asset, so we can normalize sockets/scale/root setup.
        [Export] public PackedScene? VisualScene { get; set; }

        // Optional collision package scene. This can contain one or more CollisionShape3D nodes.
        [Export] public PackedScene? CollisionScene { get; set; }

        // Logical animation/presentation profile used by the presenter layer.
        [Export] public StringName AnimationProfileId { get; set; } = new("default");

        // Gameplay footprint data stays explicit instead of being inferred from the art scene.
        [Export(PropertyHint.Range, "0.0,16.0,0.01,or_greater")] public float CollisionRadius { get; set; } = 0.5f;
        [Export(PropertyHint.Range, "0.0,16.0,0.01,or_greater")] public float CollisionHeight { get; set; } = 1.8f;

        // Standard anchor offsets used by the runtime if the visual scene does not provide explicit markers.
        [Export] public Vector3 VisualOffset { get; set; } = Vector3.Zero;
        [Export] public Vector3 CameraPivotOffset { get; set; } = new(0f, 1.6f, 0f);
        [Export] public Vector3 NameplateOffset { get; set; } = new(0f, 2.0f, 0f);
        [Export] public Vector3 EffectsOffset { get; set; } = Vector3.Zero;

        // Optional rotation/scale normalization for wrapped imported scenes.
        [Export] public Vector3 VisualRotationDegrees { get; set; } = Vector3.Zero;
        [Export] public Vector3 VisualScale { get; set; } = Vector3.One;
    }
}
