using System.IO;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Magic bytes and format version constants for each save-set file type.
    /// All files use little-endian encoding throughout.
    ///
    /// Standard file header layout (all files): [4 bytes] magic | [2 bytes] formatVersion
    /// </summary>
    public static class SaveFileHeaders
    {
        // /Saves/meta.bin — root save-set manifest
        public const uint   MetaMagic        = 0x454D5355u; // 'USME' as little-endian uint
        public const ushort MetaVersion      = 1;

        // /Saves/types.tdb — persistence type database (shared by entities and systems)
        public const uint   TypeDbMagic      = 0x544D5355u; // 'USMT'
        public const ushort TypeDbVersion    = 1;

        // /Saves/Entities/entities.idx — block index for entity payload blobs
        public const uint   EntityIdxMagic   = 0x494D5355u; // 'USMI'
        public const ushort EntityIdxVersion = 1;

        // /Saves/Entities/entities.bin — raw entity component payload blob
        public const uint   EntityBinMagic   = 0x424D5355u; // 'USMB'
        public const ushort EntityBinVersion = 1;

        // /Saves/Systems/<Name>.bin — per-system payload
        public const uint   SystemMagic      = 0x534D5355u; // 'USMS'
        public const ushort SystemVersion    = 1;

        /// <summary>Byte size of the standard header written by <see cref="WriteHeader"/>.</summary>
        public const int HeaderBytes = 6; // 4 (magic) + 2 (version)

        /// <summary>Writes the standard 6-byte header for a save-set file.</summary>
        public static void WriteHeader(BinaryWriter writer, uint magic, ushort version)
        {
            writer.Write(magic);
            writer.Write(version);
        }

        public static void WriteHeader(IDataWriter writer, uint magic, ushort version)
        {
            writer.WriteUInt32(magic);
            writer.WriteUInt16(version);
        }

        /// <summary>
        /// Reads and validates the 6-byte header from a save-set file.
        /// Throws <see cref="InvalidDataException"/> if magic or version do not match.
        /// </summary>
        public static void ReadAndValidateHeader(BinaryReader reader, uint expectedMagic, ushort expectedVersion)
        {
            uint   magic   = reader.ReadUInt32();
            ushort version = reader.ReadUInt16();

            if (magic != expectedMagic)
                throw new InvalidDataException(
                    $"Save file magic mismatch: expected 0x{expectedMagic:X8}, got 0x{magic:X8}.");

            if (version != expectedVersion)
                throw new InvalidDataException(
                    $"Save file version mismatch: expected {expectedVersion}, got {version}.");
        }

        public static void ReadAndValidateHeader(IDataReader reader, uint expectedMagic, ushort expectedVersion)
        {
            uint magic = reader.ReadUInt32();
            ushort version = reader.ReadUInt16();

            if (magic != expectedMagic)
                throw new InvalidDataException(
                    $"Save file magic mismatch: expected 0x{expectedMagic:X8}, got 0x{magic:X8}.");

            if (version != expectedVersion)
                throw new InvalidDataException(
                    $"Save file version mismatch: expected {expectedVersion}, got {version}.");
        }
    }
}
