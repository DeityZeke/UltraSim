using Client.Godot.World;
using Client.Godot.Controllers;
using Client.Godot.UI;
using UltraSim.Client.Engines.Networking;
using UltraSim.Client.Runtime;
using UltraSim.UI.Contracts;

namespace Client.Godot
{
    public sealed class GodotClientSession
    {
        public GodotClientSession(
            ClientCharacterWorld characterWorld,
            WorldCanvasManager? worldCanvasManager = null,
            MenuCanvasManager? menuCanvasManager = null)
        {
            CharacterWorld = characterWorld ?? throw new System.ArgumentNullException(nameof(characterWorld));
            WorldCanvasManager = worldCanvasManager;
            MenuCanvasManager = menuCanvasManager;
            Clock = new GodotClientClock();
            Logger = new GodotClientLogger();
            ContentResolver = new GodotContentResolver();
            MenuCanvasManager?.ConfigureServerDrivenUi(ContentResolver);
            System.Action<ClientEntityViewId, string, float>? showChatBubble =
                worldCanvasManager != null ? new System.Action<ClientEntityViewId, string, float>(worldCanvasManager.ShowChatBubble) : null;
            System.Action<ClientEntityViewId, NameplateState>? updateNameplate =
                worldCanvasManager != null ? new System.Action<ClientEntityViewId, NameplateState>(worldCanvasManager.UpdateNameplate) : null;
            PresentationSink = new GodotSemanticPresentationSink(
                setActorDefinition: CharacterWorld.SetActorDefinition,
                setActorPose: (entityId, pose) => CharacterWorld.SetActorPose(entityId, in pose),
                playAnimation: CharacterWorld.PlayAnimation,
                showChatBubble: showChatBubble,
                updateNameplate: updateNameplate,
                despawnActor: DespawnActor,
                logger: Logger,
                contentResolver: ContentResolver);
            PresentationBuffer = new ClientPresentationCommandBuffer();
            PresentationDispatcher = new ClientPresentationCommandDispatcher(PresentationSink, menuCanvasManager as IUiCommandSink);
        }

        public ClientCharacterWorld CharacterWorld { get; }
        public WorldCanvasManager? WorldCanvasManager { get; }
        public MenuCanvasManager? MenuCanvasManager { get; }
        public GodotClientClock Clock { get; }
        public GodotClientLogger Logger { get; }
        public GodotContentResolver ContentResolver { get; }
        public GodotSemanticPresentationSink PresentationSink { get; }
        public ClientPresentationCommandBuffer PresentationBuffer { get; }
        public ClientPresentationCommandDispatcher PresentationDispatcher { get; }
        public LocalPlayerController? LocalPlayerController { get; set; }

        /// <summary>
        /// Called by the network runtime when the server sends a SessionEnterGame packet.
        /// Wired by GodotClientRuntimeRoot to its NotifyEnterGame method.
        /// </summary>
        public System.Action<string>? OnEnterGame { get; set; }

        /// <summary>
        /// Called by the network runtime when the server sends a LoginResponsePacket (login failure).
        /// Provides the new challenge nonce for the next retry attempt.
        /// Wired by NetworkClientSessionDriver via MenuCanvasManager.
        /// </summary>
        public System.Action<LoginResponsePayload>? OnLoginResponse { get; set; }

        public void Tick(float deltaSeconds)
        {
            Clock.SetFrameDeltaSeconds(deltaSeconds);
            PresentationDispatcher.Drain(PresentationBuffer);
        }

        public void QueueSetActorDefinition(ClientEntityViewId entityId, ClientActorDefinitionId definitionId) =>
            PresentationBuffer.SetActorDefinition(entityId, definitionId);

        public void QueueSetActorPose(ClientEntityViewId entityId, in ClientWorldPose pose) =>
            PresentationBuffer.SetActorPose(entityId, in pose);

        public void QueuePlayAnimation(ClientEntityViewId entityId, AnimationCue cue) =>
            PresentationBuffer.PlayAnimation(entityId, cue);

        public void QueueShowChatBubble(ClientEntityViewId entityId, string text, float ttlSeconds) =>
            PresentationBuffer.ShowChatBubble(entityId, text, ttlSeconds);

        public void QueueUpdateNameplate(ClientEntityViewId entityId, NameplateState state) =>
            PresentationBuffer.UpdateNameplate(entityId, state);

        public void QueueDespawnActor(ClientEntityViewId entityId) =>
            PresentationBuffer.DespawnActor(entityId);

        private void DespawnActor(ClientEntityViewId entityId)
        {
            CharacterWorld.DespawnActor(entityId);
            WorldCanvasManager?.ClearEntity(entityId);
        }
    }
}
