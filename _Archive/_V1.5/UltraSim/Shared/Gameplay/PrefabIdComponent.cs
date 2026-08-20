using UltraSim.ECS;
using UltraSim.ECS.Persistence;

namespace UltraSim.Shared.Gameplay
{
    [PersistenceId(21002u)]
    public struct PrefabIdComponent : ISerializableComponent<PrefabIdComponent>
    {
        public ushort PrefabId;

        public static void Serialize(ref BitWriter writer, in PrefabIdComponent value)
        {
            writer.WriteUInt16(value.PrefabId);
        }

        public static PrefabIdComponent Deserialize(ref BitReader reader, ushort version)
        {
            return new PrefabIdComponent
            {
                PrefabId = reader.ReadUInt16(),
            };
        }
    }
}
