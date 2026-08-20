using System;
using System.Collections.Generic;
using System.IO;

namespace UltraSim.ECS.Persistence
{
    public static class PersistenceTypeDatabaseSerializer
    {
        public static int Write(string filePath, PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            ReadOnlySpan<PersistenceTypeRecord> records = registry.GetAllSorted();

            using var writer = new BinaryDataWriter(filePath);
            return Write(writer, records);
        }

        public static int Write(Stream stream, PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(registry);

            ReadOnlySpan<PersistenceTypeRecord> records = registry.GetAllSorted();
            using var writer = new BinaryDataWriter(stream, leaveOpen: true);
            return Write(writer, records);
        }

        private static int Write(BinaryDataWriter writer, ReadOnlySpan<PersistenceTypeRecord> records)
        {
            SaveFileHeaders.WriteHeader(writer, SaveFileHeaders.TypeDbMagic, SaveFileHeaders.TypeDbVersion);
            writer.WriteInt32(records.Length);

            for (int i = 0; i < records.Length; i++)
            {
                ref readonly var record = ref records[i];
                writer.WriteUInt32(record.PersistenceId);
                writer.WriteByte((byte)record.Kind);
                writer.WriteString(record.ClrType.FullName ?? record.ClrType.Name);
            }

            return records.Length;
        }

        public static PersistenceTypeRecord[] ReadAndValidate(string filePath, PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            using var reader = new BinaryDataReader(filePath);
            SaveFileHeaders.ReadAndValidateHeader(reader, SaveFileHeaders.TypeDbMagic, SaveFileHeaders.TypeDbVersion);

            int count = reader.ReadInt32();
            if (count < 0)
                throw new InvalidDataException("Type database record count cannot be negative.");

            var records = new PersistenceTypeRecord[count];

            for (int i = 0; i < count; i++)
            {
                uint persistenceId = reader.ReadUInt32();
                byte rawKind = reader.ReadByte();
                string storedTypeName = reader.ReadString();

                if (!Enum.IsDefined(typeof(PersistenceKind), rawKind))
                    throw new InvalidDataException($"Unknown persistence kind value {rawKind} in types.tdb.");

                var kind = (PersistenceKind)rawKind;

                if (!registry.TryGetById(persistenceId, out PersistenceTypeRecord record))
                    throw new InvalidDataException($"Unknown persistence id {persistenceId} found in types.tdb.");

                string runtimeTypeName = record.ClrType.FullName ?? record.ClrType.Name;
                if (!string.Equals(runtimeTypeName, storedTypeName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Persistence type name mismatch for id {persistenceId}: expected '{runtimeTypeName}', got '{storedTypeName}'.");
                }

                if (record.Kind != kind)
                {
                    throw new InvalidDataException(
                        $"Persistence kind mismatch for id {persistenceId}: expected '{record.Kind}', got '{kind}'.");
                }

                records[i] = record;
            }

            ReadOnlySpan<PersistenceTypeRecord> expected = registry.GetAllSorted();
            if (records.Length != expected.Length)
            {
                throw new InvalidDataException(
                    $"Type database count mismatch: expected {expected.Length}, got {records.Length}.");
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (!EqualityComparer<PersistenceTypeRecord>.Default.Equals(expected[i], records[i]))
                {
                    throw new InvalidDataException(
                        $"Type database record mismatch at index {i}: expected id {expected[i].PersistenceId}, got {records[i].PersistenceId}.");
                }
            }

            return records;
        }
    }
}
