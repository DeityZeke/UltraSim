using System;
using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Stores the base display name of a player character.
    /// Fits in a fixed inline buffer so the component stays unmanaged.
    ///
    /// Layout: up to 32 UTF-16 chars (64 bytes) + 1 byte length.
    /// Prefix/suffix expansion is deferred to a later phase.
    /// </summary>
    [PersistenceId(20001u)]
    public struct PlayerNameComponent : ISerializableComponent<PlayerNameComponent>
    {
        public const int MaxNameLength = 32;
        public const ushort SchemaVersion = 1;

        [InlineArray(MaxNameLength)]
        private struct NameBuffer { private char _; }

        private NameBuffer _chars;
        public byte NameLength;

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        public string GetName()
        {
            return new string(_chars[..NameLength]);
        }

        public static PlayerNameComponent FromString(ReadOnlySpan<char> name)
        {
            int len = Math.Min(name.Length, MaxNameLength);
            var c = new PlayerNameComponent { NameLength = (byte)len };
            name[..len].CopyTo(c._chars);
            return c;
        }

        // -------------------------------------------------------------------------
        // ISerializableComponent
        // -------------------------------------------------------------------------

        public static void Serialize(ref BitWriter writer, in PlayerNameComponent value)
        {
            writer.WriteByte(value.NameLength);
            int len = Math.Min((int)value.NameLength, MaxNameLength);
            ReadOnlySpan<char> chars = value._chars;
            for (int i = 0; i < len; i++)
                writer.WriteUInt16((ushort)chars[i]);
        }

        public static PlayerNameComponent Deserialize(ref BitReader reader, ushort version)
        {
            var c = new PlayerNameComponent();
            c.NameLength = reader.ReadByte();
            int len = Math.Min((int)c.NameLength, MaxNameLength);
            Span<char> chars = c._chars;
            for (int i = 0; i < len; i++)
                chars[i] = (char)reader.ReadUInt16();
            return c;
        }
    }
}
