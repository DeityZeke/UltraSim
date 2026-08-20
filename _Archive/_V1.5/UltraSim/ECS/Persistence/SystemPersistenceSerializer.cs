using System;
using System.Collections.Generic;
using System.IO;

namespace UltraSim.ECS.Persistence
{
    public static class SystemPersistenceSerializer
    {
        public static int WriteAll(World world, SaveSetPaths paths, PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(paths);
            ArgumentNullException.ThrowIfNull(registry);

            paths.EnsureDirectoriesExist();

            ReadOnlySpan<SystemBase> systems = world.Systems.GetSystemsSpan();
            int written = 0;

            for (int i = 0; i < systems.Length; i++)
            {
                SystemBase system = systems[i];
                if (!registry.TryGetByType(system.GetType(), out PersistenceTypeRecord record) ||
                    record.Kind != PersistenceKind.System)
                {
                    continue;
                }

                using var writer = new BinaryDataWriter(paths.SystemFile(GetSystemFileStem(record)));
                SaveFileHeaders.WriteHeader(writer, SaveFileHeaders.SystemMagic, SaveFileHeaders.SystemVersion);
                writer.WriteUInt32(record.PersistenceId);
                writer.WriteString(record.ClrType.FullName ?? record.ClrType.Name);
                system.Serialize(writer);
                written++;
            }

            return written;
        }

        public static SystemPersistenceFilePayload[] CaptureAll(World world, PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(registry);

            ReadOnlySpan<SystemBase> systems = world.Systems.GetSystemsSpan();
            var payloads = new List<SystemPersistenceFilePayload>();

            for (int i = 0; i < systems.Length; i++)
            {
                SystemBase system = systems[i];
                if (!registry.TryGetByType(system.GetType(), out PersistenceTypeRecord record) ||
                    record.Kind != PersistenceKind.System)
                {
                    continue;
                }

                using var stream = new MemoryStream();
                using (var writer = new BinaryDataWriter(stream, leaveOpen: true))
                {
                    SaveFileHeaders.WriteHeader(writer, SaveFileHeaders.SystemMagic, SaveFileHeaders.SystemVersion);
                    writer.WriteUInt32(record.PersistenceId);
                    writer.WriteString(record.ClrType.FullName ?? record.ClrType.Name);
                    system.Serialize(writer);
                    writer.Flush();
                }

                payloads.Add(new SystemPersistenceFilePayload(
                    GetSystemFileStem(record),
                    stream.ToArray()));
            }

            return payloads.ToArray();
        }

        public static SystemPersistenceLoadResult ReadAll(World world, SaveSetPaths paths, PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(paths);
            ArgumentNullException.ThrowIfNull(registry);

            ReadOnlySpan<SystemBase> systems = world.Systems.GetSystemsSpan();
            var result = new SystemPersistenceLoadResult();

            for (int i = 0; i < systems.Length; i++)
            {
                SystemBase system = systems[i];
                if (!registry.TryGetByType(system.GetType(), out PersistenceTypeRecord record) ||
                    record.Kind != PersistenceKind.System)
                {
                    continue;
                }

                string path = paths.SystemFile(GetSystemFileStem(record));
                if (!File.Exists(path))
                {
                    result.MissingFiles.Add(path);
                    continue;
                }

                using var reader = new BinaryDataReader(path);
                SaveFileHeaders.ReadAndValidateHeader(reader, SaveFileHeaders.SystemMagic, SaveFileHeaders.SystemVersion);

                uint storedPersistenceId = reader.ReadUInt32();
                string storedTypeName = reader.ReadString();

                if (storedPersistenceId != record.PersistenceId)
                {
                    throw new InvalidDataException(
                        $"System persistence id mismatch for '{record.ClrType.FullName}': expected {record.PersistenceId}, got {storedPersistenceId}.");
                }

                string runtimeTypeName = record.ClrType.FullName ?? record.ClrType.Name;
                if (!string.Equals(runtimeTypeName, storedTypeName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"System type name mismatch for persistence id {record.PersistenceId}: expected '{runtimeTypeName}', got '{storedTypeName}'.");
                }

                system.Deserialize(reader);
                result.LoadedSystemCount++;
            }

            return result;
        }

        internal static string GetSystemFileStem(PersistenceTypeRecord record)
            => record.PersistenceId.ToString("D10");
    }

    public readonly record struct SystemPersistenceFilePayload(string FileStem, byte[] Payload);
}
