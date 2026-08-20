using UltraSim.Network;

namespace UltraSim.Client.Engines.Networking
{
    public readonly struct EntityFloat3Correction
    {
        public EntityFloat3Correction(NetworkEntityId entityId, float x, float y, float z, float yawRadians, ushort actorDefinitionId, int ownerConnectionId)
        {
            EntityId = entityId;
            X = x;
            Y = y;
            Z = z;
            YawRadians = yawRadians;
            ActorDefinitionId = actorDefinitionId;
            OwnerConnectionId = ownerConnectionId;
        }

        public NetworkEntityId EntityId { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float YawRadians { get; }
        public ushort ActorDefinitionId { get; }
        public int OwnerConnectionId { get; }
    }
}
