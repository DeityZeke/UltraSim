using Godot;
using UltraSim.Client.Runtime;
using UltraSim.Shared.Gameplay;

namespace Client.Godot
{
    public sealed class PreviewClientSessionDriver : IClientSessionDriver
    {
        private readonly ulong _previewActorId;
        private readonly Vector3 _previewSpawnPosition;
        private GodotClientSession? _session;

        public PreviewClientSessionDriver(ulong previewActorId, Vector3 previewSpawnPosition)
        {
            _previewActorId = previewActorId;
            _previewSpawnPosition = previewSpawnPosition;
        }

        public void Initialize(GodotClientSession session)
        {
            _session = session;
            _session.QueueUpdateNameplate(
                new ClientEntityViewId(_previewActorId),
                new NameplateState("Preview", Visible: true));
            _session.QueueSetActorDefinition(
                new ClientEntityViewId(_previewActorId),
                ClientActorDefinitionIds.FromSharedId(SharedActorDefinitionIds.Player));
            _session.QueueSetActorPose(
                new ClientEntityViewId(_previewActorId),
                new ClientWorldPose(
                    _previewSpawnPosition.X,
                    _previewSpawnPosition.Y,
                    _previewSpawnPosition.Z,
                    0.0f,
                    0.0f,
                    0.0f));
        }

        public void Tick(float deltaSeconds)
        {
        }

        public void Shutdown()
        {
            if (_session != null)
            {
                _session.QueueDespawnActor(new ClientEntityViewId(_previewActorId));
            }

            _session = null;
        }
    }
}
