using UltraSim.ECS;
using UltraSim.ECS.Persistence;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Binds a live player entity to its durable character record.
    /// CharacterId is the authoritative rebind key; live session/connection attachment is tracked elsewhere.
    /// </summary>
    [PersistenceId(20002u)]
    public struct PlayerCharacterComponent : ISerializableComponent<PlayerCharacterComponent>
    {
        public const ushort SchemaVersion = 1;

        /// <summary>Durable character identity from ServerAccountRegistry; survives disconnect and restart.</summary>
        public ulong CharacterId;

        /// <summary>Character slot index within the owning account (0-based).</summary>
        public byte SlotIndex;

        // -------------------------------------------------------------------------
        // ISerializableComponent
        // -------------------------------------------------------------------------

        public static void Serialize(ref BitWriter writer, in PlayerCharacterComponent value)
        {
            writer.WriteUInt64(value.CharacterId);
            writer.WriteByte(value.SlotIndex);
        }

        public static PlayerCharacterComponent Deserialize(ref BitReader reader, ushort version)
        {
            return new PlayerCharacterComponent
            {
                CharacterId = reader.ReadUInt64(),
                SlotIndex = reader.ReadByte(),
            };
        }
    }
}
