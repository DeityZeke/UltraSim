using UltraSim.ECS;
using UltraSim.ECS.Persistence;

namespace UltraSim.Shared.Gameplay
{
    [PersistenceId(21003u)]
    public struct PlayerState : ISerializableComponent<PlayerState>
    {
        /// <summary>
        /// Transient live transport attachment for input ownership and replication filtering.
        /// This is never serialized and must not be treated as durable player identity.
        /// </summary>
        public int OwnerConnectionId;
        public ushort ActorDefinitionId;
        public float X;
        public float Y;
        public float Z;
        public float YawRadians;
        public short MoveX;
        public short MoveZ;

        public static void Serialize(ref BitWriter writer, in PlayerState value)
        {
            writer.WriteUInt16(value.ActorDefinitionId);
            writer.WriteSingle(value.X);
            writer.WriteSingle(value.Y);
            writer.WriteSingle(value.Z);
            writer.WriteSingle(value.YawRadians);
        }

        public static PlayerState Deserialize(ref BitReader reader, ushort version)
        {
            return new PlayerState
            {
                OwnerConnectionId = 0,
                ActorDefinitionId = reader.ReadUInt16(),
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle(),
                YawRadians = reader.ReadSingle(),
                MoveX = 0,
                MoveZ = 0,
            };
        }
    }
}
