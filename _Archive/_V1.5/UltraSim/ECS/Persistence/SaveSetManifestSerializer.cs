using System;
using System.IO;

namespace UltraSim.ECS.Persistence
{
    public static class SaveSetManifestSerializer
    {
        public static void Write(string filePath, in SaveSetManifest manifest)
        {
            using var writer = new BinaryDataWriter(filePath);
            Write(writer, manifest);
        }

        public static void Write(Stream stream, in SaveSetManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var writer = new BinaryDataWriter(stream, leaveOpen: true);
            Write(writer, manifest);
        }

        private static void Write(BinaryDataWriter writer, in SaveSetManifest manifest)
        {
            SaveFileHeaders.WriteHeader(writer, SaveFileHeaders.MetaMagic, SaveFileHeaders.MetaVersion);
            writer.WriteInt64(manifest.SavedAtUtcTicks);
            writer.WriteInt32(manifest.TypeRecordCount);
            writer.WriteInt32(manifest.SystemFileCount);
            writer.WriteInt32(manifest.EntityBlockCount);
            writer.WriteInt32(manifest.PersistentEntityCount);
        }

        public static SaveSetManifest Read(string filePath)
        {
            using var reader = new BinaryDataReader(filePath);
            SaveFileHeaders.ReadAndValidateHeader(reader, SaveFileHeaders.MetaMagic, SaveFileHeaders.MetaVersion);

            long savedAtUtcTicks = reader.ReadInt64();
            int typeRecordCount = reader.ReadInt32();
            int systemFileCount = reader.ReadInt32();
            int entityBlockCount = reader.ReadInt32();
            int persistentEntityCount = reader.ReadInt32();

            if (typeRecordCount < 0)
                throw new InvalidDataException("Manifest type record count cannot be negative.");
            if (systemFileCount < 0)
                throw new InvalidDataException("Manifest system file count cannot be negative.");
            if (entityBlockCount < 0)
                throw new InvalidDataException("Manifest entity block count cannot be negative.");
            if (persistentEntityCount < 0)
                throw new InvalidDataException("Manifest persistent entity count cannot be negative.");

            return new SaveSetManifest(
                savedAtUtcTicks,
                typeRecordCount,
                systemFileCount,
                entityBlockCount,
                persistentEntityCount);
        }
    }
}
