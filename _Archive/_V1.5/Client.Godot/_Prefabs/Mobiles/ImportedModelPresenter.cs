using Godot;

namespace Client.Godot.Prefabs.Mobiles
{
    [GlobalClass]
    public partial class ImportedModelPresenter : CharacterModelPresenter
    {
        [Export] public NodePath? ImportedModelPath { get; set; }
        [Export] public float FacingYawOffsetDegrees { get; set; } = 0.0f;

        public Node3D? ImportedModel { get; private set; }
        private Vector3 _baseRotationDegrees;

        protected override void OnAttached(CharacterRoot character, MobileDefinition definition)
        {
            ImportedModel = GetNodeOrNull<Node3D>(ImportedModelPath);
            _baseRotationDegrees = ImportedModel?.RotationDegrees ?? Vector3.Zero;
        }

        protected override void OnDetaching()
        {
            ImportedModel = null;
            _baseRotationDegrees = Vector3.Zero;
        }

        protected override void OnFacingYawChanged(float facingYawDegrees)
        {
            if (ImportedModel == null)
                return;

            ImportedModel.RotationDegrees = new Vector3(
                _baseRotationDegrees.X,
                _baseRotationDegrees.Y + FacingYawOffsetDegrees + facingYawDegrees,
                _baseRotationDegrees.Z);
        }
    }
}
