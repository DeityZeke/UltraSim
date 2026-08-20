using System;
using UltraSim.Shared.Gameplay;

namespace UltraSim.Client.Runtime
{
    public interface IPresentationSink
    {
        void SetActorDefinition(ClientEntityViewId entityId, ClientActorDefinitionId definitionId);

        void SetActorPose(ClientEntityViewId entityId, in ClientWorldPose pose);

        void PlayAnimation(ClientEntityViewId entityId, AnimationCue cue);

        void SpawnVfx(VfxCue cue);

        void PlaySfx(SfxCue cue);

        void ShowChatBubble(ClientEntityViewId entityId, string text, float ttlSeconds);

        void UpdateNameplate(ClientEntityViewId entityId, NameplateState state);

        void DespawnActor(ClientEntityViewId entityId);
    }

    public readonly record struct ClientEntityViewId(ulong Value);

    public readonly record struct ClientActorDefinitionId(ushort Value)
    {
        public bool IsEmpty => Value == 0;
        public override string ToString() => $"0x{Value:X4}";
    }

    public static class ClientActorDefinitionIds
    {
        public static readonly ClientActorDefinitionId Player = new(SharedActorDefinitionIds.Player);
        public static readonly ClientActorDefinitionId PlayerMale = new(SharedActorDefinitionIds.PlayerMale);
        public static readonly ClientActorDefinitionId PlayerFemale = new(SharedActorDefinitionIds.PlayerFemale);

        public static ClientActorDefinitionId FromSharedId(ushort sharedId) => new(sharedId);
    }

    public readonly record struct ClientWorldPose(
        float X,
        float Y,
        float Z,
        float YawRadians,
        float PitchRadians,
        float RollRadians);

    public readonly record struct AnimationCue(
        string AnimationId,
        float Speed = 1f,
        float BlendSeconds = 0f,
        bool Loop = false);

    public readonly record struct VfxCue(
        string VfxId,
        ClientEntityViewId? TargetEntityId = null,
        ClientWorldPose? WorldPose = null,
        float DurationSeconds = 0f);

    public readonly record struct SfxCue(
        string SfxId,
        ClientEntityViewId? TargetEntityId = null,
        ClientWorldPose? WorldPose = null,
        float Volume = 1f);

    public readonly record struct NameplateState(
        string PrimaryText,
        string? SecondaryText = null,
        bool Visible = true);
}
