using System;
using Client.Godot.World;
using UltraSim.Client.Runtime;

namespace Client.Godot
{
    public sealed class GodotSemanticPresentationSink : IPresentationSink
    {
        private readonly Action<ClientEntityViewId, ClientActorDefinitionId>? _setActorDefinition;
        private readonly Action<ClientEntityViewId, ClientWorldPose>? _setActorPose;
        private readonly Action<ClientEntityViewId, AnimationCue>? _playAnimation;
        private readonly Action<ClientEntityViewId, string, float>? _showChatBubble;
        private readonly Action<ClientEntityViewId, NameplateState>? _updateNameplate;
        private readonly Action<ClientEntityViewId>? _despawnActor;
        private readonly IClientLogger? _logger;
        private readonly IContentResolver? _contentResolver;

        public GodotSemanticPresentationSink(
            ClientCharacterWorld world,
            IClientLogger? logger = null,
            IContentResolver? contentResolver = null)
            : this(
                setActorDefinition: world.SetActorDefinition,
                setActorPose: (entityId, pose) => world.SetActorPose(entityId, in pose),
                playAnimation: world.PlayAnimation,
                despawnActor: world.DespawnActor,
                logger: logger,
                contentResolver: contentResolver)
        {
        }

        public GodotSemanticPresentationSink(
            Action<ClientEntityViewId, ClientActorDefinitionId>? setActorDefinition = null,
            Action<ClientEntityViewId, ClientWorldPose>? setActorPose = null,
            Action<ClientEntityViewId, AnimationCue>? playAnimation = null,
            Action<ClientEntityViewId, string, float>? showChatBubble = null,
            Action<ClientEntityViewId, NameplateState>? updateNameplate = null,
            Action<ClientEntityViewId>? despawnActor = null,
            IClientLogger? logger = null,
            IContentResolver? contentResolver = null)
        {
            _setActorDefinition = setActorDefinition;
            _setActorPose = setActorPose;
            _playAnimation = playAnimation;
            _showChatBubble = showChatBubble;
            _updateNameplate = updateNameplate;
            _despawnActor = despawnActor;
            _logger = logger;
            _contentResolver = contentResolver;
        }

        public void SetActorDefinition(ClientEntityViewId entityId, ClientActorDefinitionId definitionId) =>
            _setActorDefinition?.Invoke(entityId, definitionId);

        public void SetActorPose(ClientEntityViewId entityId, in ClientWorldPose pose) =>
            _setActorPose?.Invoke(entityId, pose);

        public void PlayAnimation(ClientEntityViewId entityId, AnimationCue cue)
        {
            LogAssetCue("anim", ClientAssetKind.Animation, cue.AnimationId, entityId);
            _playAnimation?.Invoke(entityId, cue);
        }

        public void SpawnVfx(VfxCue cue)
        {
            LogAssetCue("vfx", ClientAssetKind.Vfx, cue.VfxId, cue.TargetEntityId);
            // Actual VFX instancing will be wired later.
        }

        public void PlaySfx(SfxCue cue)
        {
            LogAssetCue("sfx", ClientAssetKind.Sfx, cue.SfxId, cue.TargetEntityId);
            // Actual audio playback will be wired later.
        }

        public void ShowChatBubble(ClientEntityViewId entityId, string text, float ttlSeconds) =>
            _showChatBubble?.Invoke(entityId, text, ttlSeconds);

        public void UpdateNameplate(ClientEntityViewId entityId, NameplateState state) =>
            _updateNameplate?.Invoke(entityId, state);

        public void DespawnActor(ClientEntityViewId entityId) =>
            _despawnActor?.Invoke(entityId);

        private void LogAssetCue(string label, ClientAssetKind kind, string logicalId, ClientEntityViewId? entityId)
        {
            if (_logger == null)
                return;

            if (_contentResolver != null &&
                _contentResolver.TryResolve(kind, logicalId, out ClientResolvedAssetRef asset) &&
                !asset.IsEmpty)
            {
                _logger.Info($"{label} cue entity={entityId?.Value ?? 0} id={logicalId} asset={asset.Value}");
            }
            else
            {
                _logger.Info($"{label} cue entity={entityId?.Value ?? 0} id={logicalId} (unresolved)");
            }
        }
    }
}
