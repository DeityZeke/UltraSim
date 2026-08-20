using Godot;
using UltraSim.Client.Runtime;

namespace Client.Godot.Prefabs.Mobiles
{
    [GlobalClass]
    public partial class CharacterRoot : Node3D
    {
        private const string CameraPivotNodeName = "CameraPivot";
        private const string NameplatePivotNodeName = "NameplatePivot";
        private const string EffectsRootNodeName = "EffectsRoot";

        [Export] public MobileDefinition? Definition { get; set; }
        [Export] public bool ApplyDefinitionOnReady { get; set; } = true;

        public Node3D VisualRoot { get; private set; } = null!;
        public Node3D CollisionRoot { get; private set; } = null!;
        public Node3D AnchorsRoot { get; private set; } = null!;
        public Node3D CameraPivot { get; private set; } = null!;
        public Node3D NameplatePivot { get; private set; } = null!;
        public Node3D EffectsRoot { get; private set; } = null!;

        public MobileDefinition? AppliedDefinition { get; private set; }
        public Node? VisualInstance { get; private set; }
        public Node? CollisionInstance { get; private set; }
        public CharacterModelPresenter? ModelPresenter { get; private set; }
        public float PresentationFacingYawDegrees { get; private set; }
        public CharacterLocomotionMode PresentationLocomotionMode { get; private set; } = CharacterLocomotionMode.Idle;
        public float PresentationLocomotionSpeedNormalized { get; private set; }
        public AnimationCue? LastAnimationCue { get; private set; }

        public override void _Ready()
        {
            CacheNodes();

            if (ApplyDefinitionOnReady && Definition != null)
                ApplyDefinition(Definition);
        }

        public void ApplyDefinition(MobileDefinition definition)
        {
            CacheNodes();

            ClearRuntimeChildren();

            AppliedDefinition = definition;
            Definition = definition;

            if (definition.VisualScene != null)
            {
                VisualInstance = definition.VisualScene.Instantiate();
                VisualRoot.AddChild(VisualInstance);

                if (VisualInstance is Node3D visualNode)
                {
                    visualNode.Position = definition.VisualOffset;
                    visualNode.RotationDegrees = definition.VisualRotationDegrees;
                    visualNode.Scale = definition.VisualScale;
                }

                ModelPresenter = FindModelPresenter(VisualInstance);
                ModelPresenter?.Attach(this, definition);
                SyncPresentationStateToPresenter();
            }

            if (definition.CollisionScene != null)
            {
                CollisionInstance = definition.CollisionScene.Instantiate();
                CollisionRoot.AddChild(CollisionInstance);
            }

            ApplyAnchorTransforms(definition);
        }

        public void ClearDefinition()
        {
            CacheNodes();
            ClearRuntimeChildren();
            AppliedDefinition = null;
        }

        public void SetPresentationFacingYawDegrees(float facingYawDegrees)
        {
            PresentationFacingYawDegrees = facingYawDegrees;
            ModelPresenter?.SetFacingYawDegrees(facingYawDegrees);
        }

        public void SetPresentationLocomotion(CharacterLocomotionMode locomotionMode, float locomotionSpeedNormalized = 0.0f)
        {
            PresentationLocomotionMode = locomotionMode;
            PresentationLocomotionSpeedNormalized = locomotionSpeedNormalized;
            ModelPresenter?.SetLocomotion(locomotionMode, locomotionSpeedNormalized);
        }

        public void PlayAnimation(AnimationCue cue)
        {
            LastAnimationCue = cue;
            ModelPresenter?.PlayAnimation(cue);
        }

        private void CacheNodes()
        {
            VisualRoot ??= GetNode<Node3D>("VisualRoot");
            CollisionRoot ??= GetNode<Node3D>("CollisionRoot");
            AnchorsRoot ??= GetNode<Node3D>("Anchors");
            CameraPivot ??= GetNode<Node3D>("Anchors/CameraPivot");
            NameplatePivot ??= GetNode<Node3D>("Anchors/NameplatePivot");
            EffectsRoot ??= GetNode<Node3D>("Anchors/EffectsRoot");
        }

        private void ClearRuntimeChildren()
        {
            if (VisualInstance != null)
            {
                ModelPresenter?.Detach();
                ModelPresenter = null;
                VisualInstance.QueueFree();
                VisualInstance = null;
            }

            if (CollisionInstance != null)
            {
                CollisionInstance.QueueFree();
                CollisionInstance = null;
            }
        }

        private void ApplyAnchorTransforms(MobileDefinition definition)
        {
            CameraPivot.Position = ResolveAnchorPosition(CameraPivotNodeName, definition.CameraPivotOffset);
            NameplatePivot.Position = ResolveAnchorPosition(NameplatePivotNodeName, definition.NameplateOffset);
            EffectsRoot.Position = ResolveAnchorPosition(EffectsRootNodeName, definition.EffectsOffset);
        }

        private Vector3 ResolveAnchorPosition(string anchorName, Vector3 fallbackPosition)
        {
            if (TryFindVisualAnchor(anchorName, out Node3D? anchor))
            {
                return ToLocal(anchor!.GlobalPosition);
            }

            return fallbackPosition;
        }

        private bool TryFindVisualAnchor(string anchorName, out Node3D? anchor)
        {
            anchor = null;
            if (VisualInstance == null)
            {
                return false;
            }

            Node? directMatch = VisualInstance.GetNodeOrNull(anchorName);
            if (directMatch is Node3D directAnchor)
            {
                anchor = directAnchor;
                return true;
            }

            Node? nestedMatch = VisualInstance.GetNodeOrNull($"Anchors/{anchorName}");
            if (nestedMatch is Node3D nestedAnchor)
            {
                anchor = nestedAnchor;
                return true;
            }

            Node? recursiveMatch = VisualInstance.FindChild(anchorName, true, false);
            if (recursiveMatch is Node3D recursiveAnchor)
            {
                anchor = recursiveAnchor;
                return true;
            }

            return false;
        }

        private void SyncPresentationStateToPresenter()
        {
            ModelPresenter?.SetFacingYawDegrees(PresentationFacingYawDegrees);
            ModelPresenter?.SetLocomotion(PresentationLocomotionMode, PresentationLocomotionSpeedNormalized);
        }

        private static CharacterModelPresenter? FindModelPresenter(Node visualInstance)
        {
            if (visualInstance is CharacterModelPresenter directPresenter)
            {
                return directPresenter;
            }

            foreach (Node child in visualInstance.GetChildren())
            {
                CharacterModelPresenter? nestedPresenter = FindModelPresenter(child);
                if (nestedPresenter != null)
                {
                    return nestedPresenter;
                }
            }

            return null;
        }
    }
}
