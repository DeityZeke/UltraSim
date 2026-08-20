using System;
using System.Collections.Generic;
using UltraSim.UI.Contracts;

namespace UltraSim.Client.Runtime
{
    public sealed class ClientPresentationCommandBuffer : IPresentationSink, IUiCommandSink
    {
        private readonly List<PresentationCommand> _presentationCommands;
        private readonly List<UiCommand> _uiCommands;

        public ClientPresentationCommandBuffer(int presentationCapacity = 64, int uiCapacity = 16)
        {
            _presentationCommands = new List<PresentationCommand>(Math.Max(1, presentationCapacity));
            _uiCommands = new List<UiCommand>(Math.Max(1, uiCapacity));
        }

        public IReadOnlyList<PresentationCommand> PresentationCommands => _presentationCommands;

        public IReadOnlyList<UiCommand> UiCommands => _uiCommands;

        public void Clear()
        {
            _presentationCommands.Clear();
            _uiCommands.Clear();
        }

        public void SetActorDefinition(ClientEntityViewId entityId, ClientActorDefinitionId definitionId) =>
            _presentationCommands.Add(PresentationCommand.ForSetActorDefinition(entityId, definitionId));

        public void SetActorPose(ClientEntityViewId entityId, in ClientWorldPose pose) =>
            _presentationCommands.Add(PresentationCommand.ForSetActorPose(entityId, pose));

        public void PlayAnimation(ClientEntityViewId entityId, AnimationCue cue) =>
            _presentationCommands.Add(PresentationCommand.ForPlayAnimation(entityId, cue));

        public void SpawnVfx(VfxCue cue) =>
            _presentationCommands.Add(PresentationCommand.ForSpawnVfx(cue));

        public void PlaySfx(SfxCue cue) =>
            _presentationCommands.Add(PresentationCommand.ForPlaySfx(cue));

        public void ShowChatBubble(ClientEntityViewId entityId, string text, float ttlSeconds) =>
            _presentationCommands.Add(PresentationCommand.ForChatBubble(entityId, text, ttlSeconds));

        public void UpdateNameplate(ClientEntityViewId entityId, NameplateState state) =>
            _presentationCommands.Add(PresentationCommand.ForNameplate(entityId, state));

        public void DespawnActor(ClientEntityViewId entityId) =>
            _presentationCommands.Add(PresentationCommand.ForDespawnActor(entityId));

        public void OpenPanel(UiPanelOpenCommand command) => _uiCommands.Add(UiCommand.ForOpen(command));

        public void UpdatePanel(UiPanelUpdateCommand command) => _uiCommands.Add(UiCommand.ForUpdate(command));

        public void ClosePanel(UiPanelCloseCommand command) => _uiCommands.Add(UiCommand.ForClose(command));
    }

    public enum PresentationCommandKind : byte
    {
        Unknown = 0,
        SetActorDefinition = 1,
        SetActorPose = 2,
        PlayAnimation = 3,
        SpawnVfx = 4,
        PlaySfx = 5,
        ShowChatBubble = 6,
        UpdateNameplate = 7,
        DespawnActor = 8,
    }

    public readonly record struct PresentationCommand(
        PresentationCommandKind Kind,
        ClientEntityViewId EntityId,
        ClientWorldPose Pose,
        AnimationCue? Animation,
        VfxCue? Vfx,
        SfxCue? Sfx,
        NameplateState? Nameplate,
        ClientActorDefinitionId? DefinitionId,
        string? ChatText,
        float ChatTtlSeconds)
    {
        public static PresentationCommand ForSetActorDefinition(ClientEntityViewId entityId, ClientActorDefinitionId definitionId) =>
            new(PresentationCommandKind.SetActorDefinition, entityId, default, null, null, null, null, definitionId, null, 0f);

        public static PresentationCommand ForSetActorPose(ClientEntityViewId entityId, in ClientWorldPose pose) =>
            new(PresentationCommandKind.SetActorPose, entityId, pose, null, null, null, null, null, null, 0f);

        public static PresentationCommand ForPlayAnimation(ClientEntityViewId entityId, AnimationCue cue) =>
            new(PresentationCommandKind.PlayAnimation, entityId, default, cue, null, null, null, null, null, 0f);

        public static PresentationCommand ForSpawnVfx(VfxCue cue) =>
            new(PresentationCommandKind.SpawnVfx, cue.TargetEntityId ?? default, default, null, cue, null, null, null, null, 0f);

        public static PresentationCommand ForPlaySfx(SfxCue cue) =>
            new(PresentationCommandKind.PlaySfx, cue.TargetEntityId ?? default, default, null, null, cue, null, null, null, 0f);

        public static PresentationCommand ForChatBubble(ClientEntityViewId entityId, string text, float ttlSeconds) =>
            new(PresentationCommandKind.ShowChatBubble, entityId, default, null, null, null, null, null, text ?? string.Empty, ttlSeconds);

        public static PresentationCommand ForNameplate(ClientEntityViewId entityId, NameplateState state) =>
            new(PresentationCommandKind.UpdateNameplate, entityId, default, null, null, null, state, null, null, 0f);

        public static PresentationCommand ForDespawnActor(ClientEntityViewId entityId) =>
            new(PresentationCommandKind.DespawnActor, entityId, default, null, null, null, null, null, null, 0f);
    }

    public enum UiCommandKind : byte
    {
        Unknown = 0,
        Open = 1,
        Update = 2,
        Close = 3,
    }

    public readonly record struct UiCommand(
        UiCommandKind Kind,
        UiPanelOpenCommand? Open,
        UiPanelUpdateCommand? Update,
        UiPanelCloseCommand? Close)
    {
        public static UiCommand ForOpen(UiPanelOpenCommand command) =>
            new(UiCommandKind.Open, command, null, null);

        public static UiCommand ForUpdate(UiPanelUpdateCommand command) =>
            new(UiCommandKind.Update, null, command, null);

        public static UiCommand ForClose(UiPanelCloseCommand command) =>
            new(UiCommandKind.Close, null, null, command);
    }
}
