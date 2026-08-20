using UltraSim.ECS;
using UltraSim.ECS.Persistence;

namespace UltraSim.Shared.Gameplay
{
    [PersistenceId(21001u)]
    public readonly struct PlayerMarker : ISerializableComponent<PlayerMarker>
    {
        public static void Serialize(ref BitWriter writer, in PlayerMarker value)
        {
        }

        public static PlayerMarker Deserialize(ref BitReader reader, ushort version) => default;
    }
}
