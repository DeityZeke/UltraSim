using System.Collections.Generic;
using Client.Godot.Controllers;
using Client.Godot.Prefabs.Mobiles;
using Godot;
using UltraSim.Client.Runtime;

namespace Client.Godot.World
{
    [GlobalClass]
    public partial class ClientCharacterWorld : Node3D
    {
        private const float DefaultRenderDeadband = 0.001f;

        [Export] public PackedScene? CharacterRootScene { get; set; }
        [Export] public MobileDefinition? DefaultMobileDefinition { get; set; }
        [Export] public NodePath? MobileCatalogNodePath { get; set; }
        [Export] public MobileDefinitionCatalog? MobileCatalog { get; set; }
        [Export] public global::Godot.Collections.Array<MobileCatalogEntry> MobileCatalogEntries { get; set; } = new();
        [Export] public NodePath? LocalPlayerControllerPath { get; set; }
        [Export] public ulong DefaultLocalControlledEntityId { get; set; } = 1;
        [Export] public bool SpawnPreviewActorOnReady { get; set; } = true;
        [Export] public Vector3 PreviewSpawnPosition { get; set; } = Vector3.Zero;

        private readonly Dictionary<ulong, CharacterBinding> _bindings = new();
        private LocalPlayerController? _localPlayerController;
        private MobileCatalogNode? _mobileCatalogNode;

        public ulong LocalControlledEntityId { get; private set; }

        public override void _Ready()
        {
            _localPlayerController = ResolveLocalPlayerController();
            _mobileCatalogNode = ResolveMobileCatalogNode();
            LocalControlledEntityId = SpawnPreviewActorOnReady ? DefaultLocalControlledEntityId : 0;

            if (SpawnPreviewActorOnReady)
            {
                ClientEntityViewId previewId = new(LocalControlledEntityId);
                ClientWorldPose previewPose = new(
                    PreviewSpawnPosition.X,
                    PreviewSpawnPosition.Y,
                    PreviewSpawnPosition.Z,
                    0.0f,
                    0.0f,
                    0.0f);
                SetActorPose(previewId, in previewPose);
            }
        }

        public override void _Process(double delta)
        {
            float deltaSeconds = (float)delta;
            if (deltaSeconds <= 0.0f)
                return;

            foreach ((ulong entityId, CharacterBinding binding) in _bindings)
            {
                if (!binding.HasRenderTarget)
                    continue;

                Vector3 currentPosition = binding.Character.GlobalPosition;
                float currentYawRadians = Mathf.DegToRad(binding.Character.PresentationFacingYawDegrees);
                Vector3 targetPosition = binding.TargetPosition;
                float targetYawRadians = binding.TargetYawRadians;

                float distance = currentPosition.DistanceTo(targetPosition);
                float yawDelta = Mathf.Abs(Mathf.AngleDifference(currentYawRadians, targetYawRadians));
                if (distance <= DefaultRenderDeadband && yawDelta <= 0.001f)
                    continue;

                ClientWorldPose nextPose;
                if (distance >= binding.RenderSnapDistance)
                {
                    nextPose = new ClientWorldPose(
                        targetPosition.X,
                        targetPosition.Y,
                        targetPosition.Z,
                        targetYawRadians,
                        0.0f,
                        0.0f);
                }
                else
                {
                    float alpha = 1.0f - Mathf.Exp(-binding.RenderSharpness * deltaSeconds);
                    Vector3 blendedPosition = currentPosition.Lerp(targetPosition, alpha);
                    float blendedYaw = Mathf.LerpAngle(currentYawRadians, targetYawRadians, alpha);
                    nextPose = new ClientWorldPose(
                        blendedPosition.X,
                        blendedPosition.Y,
                        blendedPosition.Z,
                        blendedYaw,
                        0.0f,
                        0.0f);
                }

                ApplyPose(entityId, binding, in nextPose);
            }
        }

        public void SetActorPose(ClientEntityViewId entityId, in ClientWorldPose pose)
        {
            CharacterBinding binding = GetOrCreateBinding(entityId);
            ApplyPose(entityId.Value, binding, in pose);
            binding.TargetPosition = binding.Character.GlobalPosition;
            binding.TargetYawRadians = pose.YawRadians;
            binding.RenderSharpness = 0.0f;
            binding.RenderSnapDistance = 0.0f;
            binding.HasRenderTarget = false;
        }

        public void SetActorDefinition(ClientEntityViewId entityId, ClientActorDefinitionId definitionId)
        {
            CharacterBinding binding = GetOrCreateBinding(entityId);
            binding.RequestedDefinitionId = definitionId;
            ApplyResolvedDefinition(binding);
        }

        public void SetActorRenderTargetPose(
            ClientEntityViewId entityId,
            in ClientWorldPose pose,
            float sharpness,
            float snapDistance)
        {
            CharacterBinding binding = GetOrCreateBinding(entityId);
            if (!binding.HasLastPose)
            {
                ApplyPose(entityId.Value, binding, in pose);
            }

            binding.TargetPosition = new Vector3(pose.X, pose.Y, pose.Z);
            binding.TargetYawRadians = pose.YawRadians;
            binding.RenderSharpness = sharpness;
            binding.RenderSnapDistance = snapDistance;
            binding.HasRenderTarget = true;
        }

        public void SetLocalControlledEntityId(ulong entityId)
        {
            if (LocalControlledEntityId == entityId)
            {
                return;
            }

            LocalControlledEntityId = entityId;

            if (_localPlayerController == null)
                return;

            if (entityId == 0)
            {
                _localPlayerController.SetControlledCharacter(null);
                return;
            }

            if (_bindings.TryGetValue(entityId, out CharacterBinding? binding) && binding.HasLastPose)
                _localPlayerController.SetControlledCharacter(binding.Character);
            else
                _localPlayerController.SetControlledCharacter(null);
        }

        public void ResetLocalControlledEntityId()
        {
            LocalControlledEntityId = 0;
            _localPlayerController?.SetControlledCharacter(null);
        }

        public void SetLocalPlayerController(LocalPlayerController? controller)
        {
            _localPlayerController = controller;

            if (_localPlayerController == null)
            {
                return;
            }

            if (LocalControlledEntityId == 0)
            {
                _localPlayerController.SetControlledCharacter(null);
                return;
            }

            if (_bindings.TryGetValue(LocalControlledEntityId, out CharacterBinding? binding) && binding.HasLastPose)
            {
                _localPlayerController.SetControlledCharacter(binding.Character);
            }
            else
            {
                _localPlayerController.SetControlledCharacter(null);
            }
        }

        public void DespawnActor(ClientEntityViewId entityId)
        {
            if (!_bindings.Remove(entityId.Value, out CharacterBinding? binding))
            {
                return;
            }

            if (_localPlayerController != null && _localPlayerController.ControlledCharacter == binding.Character)
            {
                _localPlayerController.SetControlledCharacter(null);
            }

            binding.Character.QueueFree();
        }

        public void ClearAllActors()
        {
            foreach (CharacterBinding binding in _bindings.Values)
            {
                binding.Character.QueueFree();
            }

            _bindings.Clear();
            ResetLocalControlledEntityId();
        }

        public void PlayAnimation(ClientEntityViewId entityId, AnimationCue cue)
        {
            CharacterBinding binding = GetOrCreateBinding(entityId);
            binding.Character.PlayAnimation(cue);
        }

        public CharacterRoot? TryGetCharacter(ClientEntityViewId entityId) =>
            _bindings.TryGetValue(entityId.Value, out CharacterBinding? binding) ? binding.Character : null;

        private CharacterBinding GetOrCreateBinding(ClientEntityViewId entityId)
        {
            if (_bindings.TryGetValue(entityId.Value, out CharacterBinding? existing))
            {
                return existing;
            }

            if (CharacterRootScene == null)
            {
                throw new System.InvalidOperationException("ClientCharacterWorld requires a CharacterRootScene.");
            }

            CharacterRoot instance = CharacterRootScene.Instantiate<CharacterRoot>();
            AddChild(instance);

            CharacterBinding created = new(instance);
            ApplyResolvedDefinition(created);
            _bindings[entityId.Value] = created;

            return created;
        }

        private LocalPlayerController? ResolveLocalPlayerController()
        {
            if (LocalPlayerControllerPath == null || LocalPlayerControllerPath.IsEmpty)
            {
                return null;
            }

            return GetNodeOrNull<LocalPlayerController>(LocalPlayerControllerPath);
        }

        private void ApplyPose(ulong entityId, CharacterBinding binding, in ClientWorldPose pose)
        {
            Vector3 newPosition = new(pose.X, pose.Y, pose.Z);
            binding.Character.GlobalPosition = newPosition;
            binding.Character.SetPresentationFacingYawDegrees(Mathf.RadToDeg(pose.YawRadians));

            CharacterLocomotionMode locomotionMode = CharacterLocomotionMode.Idle;
            float locomotionSpeedNormalized = 0.0f;
            if (binding.HasLastPose)
            {
                Vector3 delta = newPosition - binding.LastPosition;
                float distance = delta.Length();
                if (distance > 0.001f)
                {
                    locomotionMode = CharacterLocomotionMode.Walk;
                    locomotionSpeedNormalized = Mathf.Clamp(distance * 4.0f, 0.0f, 1.0f);
                }
            }

            binding.Character.SetPresentationLocomotion(locomotionMode, locomotionSpeedNormalized);
            binding.LastPosition = newPosition;
            binding.HasLastPose = true;

            if (_localPlayerController != null &&
                entityId == LocalControlledEntityId &&
                binding.Character != _localPlayerController.ControlledCharacter)
            {
                _localPlayerController.SetControlledCharacter(binding.Character);
            }
        }

        private void ApplyResolvedDefinition(CharacterBinding binding)
        {
            MobileDefinition? resolved = ResolveDefinition(binding.RequestedDefinitionId);
            if (resolved == null)
                return;

            if (ReferenceEquals(binding.AppliedDefinition, resolved))
                return;

            binding.Character.ApplyDefinition(resolved);
            binding.AppliedDefinition = resolved;
        }

        private MobileDefinition? ResolveDefinition(ClientActorDefinitionId definitionId)
        {
            MobileDefinition? resolved = _mobileCatalogNode?.Resolve(definitionId) ?? MobileCatalog?.Resolve(definitionId);
            if (resolved != null)
                return resolved;

            if (!definitionId.IsEmpty)
            {
                for (int i = 0; i < MobileCatalogEntries.Count; i++)
                {
                    MobileCatalogEntry? entry = MobileCatalogEntries[i];
                    if (entry?.Definition != null && entry.ClientDefinitionId.Value == definitionId.Value)
                        return entry.Definition;
                }
            }

            return DefaultMobileDefinition;
        }

        private MobileCatalogNode? ResolveMobileCatalogNode()
        {
            if (MobileCatalogNodePath == null || MobileCatalogNodePath.IsEmpty)
                return null;

            return GetNodeOrNull<MobileCatalogNode>(MobileCatalogNodePath);
        }

        private sealed class CharacterBinding
        {
            public CharacterBinding(CharacterRoot character)
            {
                Character = character;
            }

            public CharacterRoot Character { get; }
            public ClientActorDefinitionId RequestedDefinitionId { get; set; }
            public MobileDefinition? AppliedDefinition { get; set; }
            public Vector3 LastPosition { get; set; }
            public bool HasLastPose { get; set; }
            public Vector3 TargetPosition { get; set; }
            public float TargetYawRadians { get; set; }
            public float RenderSharpness { get; set; }
            public float RenderSnapDistance { get; set; }
            public bool HasRenderTarget { get; set; }
        }
    }
}
