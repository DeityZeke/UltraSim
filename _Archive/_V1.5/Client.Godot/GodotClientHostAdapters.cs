using UltraSim.Client.Runtime;
using UltraSim.UI.Contracts;
using Godot;

namespace Client.Godot
{
    public sealed class GodotClientClock : IClientClock
    {
        private float _frameDeltaSeconds;

        public void SetFrameDeltaSeconds(float deltaSeconds)
        {
            _frameDeltaSeconds = deltaSeconds < 0f ? 0f : deltaSeconds;
        }

        public long GetMonotonicTicksUsec() => unchecked((long)Time.GetTicksUsec());

        public float GetFrameDeltaSeconds() => _frameDeltaSeconds;
    }

    public sealed class GodotClientLogger : IClientLogger
    {
        private readonly string _prefix;

        public GodotClientLogger(string prefix = "[Client]")
        {
            _prefix = string.IsNullOrWhiteSpace(prefix) ? "[Client]" : prefix;
        }

        public void Info(string message) => GD.Print($"{_prefix} {message}");

        public void Warn(string message) => GD.PushWarning($"{_prefix} {message}");

        public void Error(string message) => GD.PushError($"{_prefix} {message}");
    }

    // Placeholder adapter used while the semantic presentation/UI command path is wired into the current host.
    public sealed class GodotNoopPresentationSink : IPresentationSink, IUiCommandSink
    {
        public void SetActorDefinition(ClientEntityViewId entityId, ClientActorDefinitionId definitionId) { }

        public void SetActorPose(ClientEntityViewId entityId, in ClientWorldPose pose) { }

        public void PlayAnimation(ClientEntityViewId entityId, AnimationCue cue) { }

        public void SpawnVfx(VfxCue cue) { }

        public void PlaySfx(SfxCue cue) { }

        public void ShowChatBubble(ClientEntityViewId entityId, string text, float ttlSeconds) { }

        public void UpdateNameplate(ClientEntityViewId entityId, NameplateState state) { }

        public void DespawnActor(ClientEntityViewId entityId) { }

        public void OpenPanel(UiPanelOpenCommand command) { }

        public void UpdatePanel(UiPanelUpdateCommand command) { }

        public void ClosePanel(UiPanelCloseCommand command) { }
    }
}
