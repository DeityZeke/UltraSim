using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UltraSim.ECS.Persistence
{
    public static class EntityPersistenceWriter
    {
        private static readonly Dictionary<Type, WriteComponentSectionDelegate> SectionWriterCache = new();
        private static readonly Dictionary<Type, CaptureComponentSectionDelegate> SectionCaptureCache = new();
        private static readonly Dictionary<Type, WriteCapturedComponentSectionDelegate> CapturedSectionWriterCache = new();
        private static readonly Dictionary<Type, ushort> SchemaVersionCache = new();
        private static readonly object CacheLock = new();

        private delegate void WriteComponentSectionDelegate(
            BinaryDataWriter writer,
            IReadOnlyList<PersistenceArchetypeView> archetypes,
            ComponentId componentId);
        private delegate Array CaptureComponentSectionDelegate(
            IReadOnlyList<PersistenceArchetypeView> archetypes,
            ComponentId componentId,
            int entityCount);
        private delegate void WriteCapturedComponentSectionDelegate(
            BinaryDataWriter writer,
            Array values);

        public static EntityPersistenceWriteResult Write(
            World world,
            SaveSetPaths paths,
            PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(world);

            var worldView = new WorldPersistenceView(world);
            return WriteArchetypes(worldView.GetPersistentArchetypes(), paths, registry);
        }

        internal static EntityPersistenceCapturedSnapshot Capture(
            World world,
            PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(world);

            var worldView = new WorldPersistenceView(world);
            return CaptureArchetypes(worldView.GetPersistentArchetypes(), registry);
        }

        internal static EntityPersistenceWriteResult WriteArchetypes(
            IReadOnlyList<PersistenceArchetypeView> archetypes,
            SaveSetPaths paths,
            PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(archetypes);
            ArgumentNullException.ThrowIfNull(paths);
            ArgumentNullException.ThrowIfNull(registry);

            paths.EnsureDirectoriesExist();

            using var idxWriter = new BinaryDataWriter(paths.EntitiesIdx);
            using var binWriter = new BinaryDataWriter(paths.EntitiesBin);
            return WriteArchetypes(archetypes, idxWriter, binWriter, registry);
        }

        internal static EntityPersistenceWriteResult WriteArchetypes(
            IReadOnlyList<PersistenceArchetypeView> archetypes,
            Stream idxStream,
            Stream binStream,
            PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(archetypes);
            ArgumentNullException.ThrowIfNull(idxStream);
            ArgumentNullException.ThrowIfNull(binStream);
            ArgumentNullException.ThrowIfNull(registry);

            using var idxWriter = new BinaryDataWriter(idxStream, leaveOpen: true);
            using var binWriter = new BinaryDataWriter(binStream, leaveOpen: true);
            return WriteArchetypes(archetypes, idxWriter, binWriter, registry);
        }

        private static EntityPersistenceWriteResult WriteArchetypes(
            IReadOnlyList<PersistenceArchetypeView> archetypes,
            BinaryDataWriter idxWriter,
            BinaryDataWriter binWriter,
            PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(archetypes);
            ArgumentNullException.ThrowIfNull(idxWriter);
            ArgumentNullException.ThrowIfNull(binWriter);
            ArgumentNullException.ThrowIfNull(registry);

            var groups = BuildGroups(archetypes, registry);

            groups.Sort(static (a, b) => CompareSignatures(a.Signature, b.Signature));
            for (int i = 0; i < groups.Count; i++)
                groups[i].Archetypes.Sort(CompareArchetypeViews);

            SaveFileHeaders.WriteHeader(idxWriter, SaveFileHeaders.EntityIdxMagic, SaveFileHeaders.EntityIdxVersion);
            SaveFileHeaders.WriteHeader(binWriter, SaveFileHeaders.EntityBinMagic, SaveFileHeaders.EntityBinVersion);

            idxWriter.WriteInt32(groups.Count);

            var records = new EntityBlockIndexRecord[groups.Count];
            int totalEntities = 0;

            for (int i = 0; i < groups.Count; i++)
            {
                Group group = groups[i];
                int entityCount = CountEntities(group.Archetypes);
                totalEntities += entityCount;

                long blockOffset = binWriter.Position;
                WriteBlockPayload(binWriter, group);
                int blockLength = checked((int)(binWriter.Position - blockOffset));

                WriteIndexRecord(idxWriter, group.Signature, entityCount, blockOffset, blockLength);
                records[i] = new EntityBlockIndexRecord(group.Signature, entityCount, blockOffset, blockLength);
            }

            return new EntityPersistenceWriteResult
            {
                BlockCount = groups.Count,
                PersistentEntityCount = totalEntities,
                Blocks = records,
            };
        }

        internal static EntityPersistenceCapturedSnapshot CaptureArchetypes(
            IReadOnlyList<PersistenceArchetypeView> archetypes,
            PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(archetypes);
            ArgumentNullException.ThrowIfNull(registry);

            var groups = BuildGroups(archetypes, registry);

            groups.Sort(static (a, b) => CompareSignatures(a.Signature, b.Signature));
            for (int i = 0; i < groups.Count; i++)
                groups[i].Archetypes.Sort(CompareArchetypeViews);

            var capturedGroups = new EntityPersistenceCapturedGroup[groups.Count];
            int totalEntities = 0;

            for (int i = 0; i < groups.Count; i++)
            {
                Group group = groups[i];
                int entityCount = CountEntities(group.Archetypes);
                totalEntities += entityCount;

                ulong[] entityPackedIds = CaptureEntityPackedIds(group.Archetypes, entityCount);
                var sections = new EntityPersistenceCapturedSection[group.Descriptors.Length];

                for (int j = 0; j < group.Descriptors.Length; j++)
                {
                    PersistenceComponentDescriptor descriptor = group.Descriptors[j];
                    ushort schemaVersion = GetSchemaVersion(descriptor.ClrType);
                    Array values = GetSectionCapture(descriptor.ClrType)(group.Archetypes, descriptor.RuntimeComponentId, entityCount);
                    sections[j] = new EntityPersistenceCapturedSection(descriptor, schemaVersion, values);
                }

                capturedGroups[i] = new EntityPersistenceCapturedGroup(group.Signature, entityPackedIds, sections);
            }

            return new EntityPersistenceCapturedSnapshot(groups.Count, totalEntities, capturedGroups);
        }

        internal static EntityPersistenceWriteResult WriteSnapshot(
            EntityPersistenceCapturedSnapshot snapshot,
            SaveSetPaths paths)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(paths);

            paths.EnsureDirectoriesExist();

            using var idxWriter = new BinaryDataWriter(paths.EntitiesIdx);
            using var binWriter = new BinaryDataWriter(paths.EntitiesBin);
            return WriteSnapshot(snapshot, idxWriter, binWriter);
        }

        internal static EntityPersistenceWriteResult WriteSnapshot(
            EntityPersistenceCapturedSnapshot snapshot,
            Stream idxStream,
            Stream binStream)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(idxStream);
            ArgumentNullException.ThrowIfNull(binStream);

            using var idxWriter = new BinaryDataWriter(idxStream, leaveOpen: true);
            using var binWriter = new BinaryDataWriter(binStream, leaveOpen: true);
            return WriteSnapshot(snapshot, idxWriter, binWriter);
        }

        private static EntityPersistenceWriteResult WriteSnapshot(
            EntityPersistenceCapturedSnapshot snapshot,
            BinaryDataWriter idxWriter,
            BinaryDataWriter binWriter)
        {
            SaveFileHeaders.WriteHeader(idxWriter, SaveFileHeaders.EntityIdxMagic, SaveFileHeaders.EntityIdxVersion);
            SaveFileHeaders.WriteHeader(binWriter, SaveFileHeaders.EntityBinMagic, SaveFileHeaders.EntityBinVersion);

            idxWriter.WriteInt32(snapshot.Groups.Length);

            var records = new EntityBlockIndexRecord[snapshot.Groups.Length];

            for (int i = 0; i < snapshot.Groups.Length; i++)
            {
                EntityPersistenceCapturedGroup group = snapshot.Groups[i];
                long blockOffset = binWriter.Position;
                WriteCapturedBlockPayload(binWriter, group);
                int blockLength = checked((int)(binWriter.Position - blockOffset));

                WriteIndexRecord(idxWriter, group.Signature, group.EntityCount, blockOffset, blockLength);
                records[i] = new EntityBlockIndexRecord(group.Signature, group.EntityCount, blockOffset, blockLength);
            }

            return new EntityPersistenceWriteResult
            {
                BlockCount = snapshot.BlockCount,
                PersistentEntityCount = snapshot.PersistentEntityCount,
                Blocks = records,
            };
        }

        private static List<Group> BuildGroups(
            IReadOnlyList<PersistenceArchetypeView> archetypes,
            PersistenceTypeRegistry registry)
        {
            var groups = new Dictionary<PersistenceSignature, Group>();

            for (int i = 0; i < archetypes.Count; i++)
            {
                PersistenceArchetypeView archetypeView = archetypes[i];
                if (!PersistenceSignatureBuilder.TryBuild(archetypeView, registry, out PersistenceSignature? signature, out PersistenceComponentDescriptor[] descriptors))
                    continue;

                if (!groups.TryGetValue(signature!, out Group? group))
                {
                    group = new Group(signature!, descriptors);
                    groups.Add(signature!, group);
                }

                group.Archetypes.Add(archetypeView);
            }

            return new List<Group>(groups.Values);
        }

        private static void WriteIndexRecord(
            BinaryDataWriter writer,
            PersistenceSignature signature,
            int entityCount,
            long binOffset,
            int binLength)
        {
            writer.WriteInt32(signature.ComponentCount);
            ReadOnlySpan<uint> persistenceIds = signature.PersistenceIds;
            for (int i = 0; i < persistenceIds.Length; i++)
                writer.WriteUInt32(persistenceIds[i]);

            writer.WriteInt32(entityCount);
            writer.WriteInt64(binOffset);
            writer.WriteInt32(binLength);
        }

        private static void WriteBlockPayload(BinaryDataWriter writer, Group group)
        {
            IReadOnlyList<PersistenceArchetypeView> archetypes = group.Archetypes;

            for (int i = 0; i < archetypes.Count; i++)
            {
                ReadOnlySpan<Entity> entities = archetypes[i].Entities;
                for (int e = 0; e < entities.Length; e++)
                    writer.WriteUInt64(entities[e].Packed);
            }

            for (int i = 0; i < group.Descriptors.Length; i++)
            {
                PersistenceComponentDescriptor descriptor = group.Descriptors[i];
                ushort schemaVersion = GetSchemaVersion(descriptor.ClrType);

                using var sectionStream = new MemoryStream();
                using (var sectionWriter = new BinaryDataWriter(sectionStream, leaveOpen: true))
                {
                    GetSectionWriter(descriptor.ClrType)(sectionWriter, archetypes, descriptor.RuntimeComponentId);
                    sectionWriter.Flush();
                }

                int payloadByteLength = checked((int)sectionStream.Length);
                writer.WriteUInt32(descriptor.PersistenceId);
                writer.WriteUInt16(schemaVersion);
                writer.WriteInt32(payloadByteLength);
                writer.WriteInt32(CountEntities(archetypes));
                writer.WriteBytes(sectionStream.GetBuffer().AsSpan(0, payloadByteLength));
            }
        }

        private static void WriteCapturedBlockPayload(BinaryDataWriter writer, EntityPersistenceCapturedGroup group)
        {
            for (int i = 0; i < group.EntityPackedIds.Length; i++)
                writer.WriteUInt64(group.EntityPackedIds[i]);

            for (int i = 0; i < group.Sections.Length; i++)
            {
                EntityPersistenceCapturedSection section = group.Sections[i];

                using var sectionStream = new MemoryStream();
                using (var sectionWriter = new BinaryDataWriter(sectionStream, leaveOpen: true))
                {
                    GetCapturedSectionWriter(section.Descriptor.ClrType)(sectionWriter, section.Values);
                    sectionWriter.Flush();
                }

                int payloadByteLength = checked((int)sectionStream.Length);
                writer.WriteUInt32(section.Descriptor.PersistenceId);
                writer.WriteUInt16(section.SchemaVersion);
                writer.WriteInt32(payloadByteLength);
                writer.WriteInt32(group.EntityCount);
                writer.WriteBytes(sectionStream.GetBuffer().AsSpan(0, payloadByteLength));
            }
        }

        private static WriteComponentSectionDelegate GetSectionWriter(Type componentType)
        {
            lock (CacheLock)
            {
                if (SectionWriterCache.TryGetValue(componentType, out WriteComponentSectionDelegate? writer))
                    return writer;

                MethodInfo method = typeof(EntityPersistenceWriter)
                    .GetMethod(nameof(WriteComponentSectionTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(componentType);

                writer = (WriteComponentSectionDelegate)Delegate.CreateDelegate(typeof(WriteComponentSectionDelegate), method);
                SectionWriterCache[componentType] = writer;
                return writer;
            }
        }

        private static CaptureComponentSectionDelegate GetSectionCapture(Type componentType)
        {
            lock (CacheLock)
            {
                if (SectionCaptureCache.TryGetValue(componentType, out CaptureComponentSectionDelegate? capture))
                    return capture;

                MethodInfo method = typeof(EntityPersistenceWriter)
                    .GetMethod(nameof(CaptureComponentSectionTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(componentType);

                capture = (CaptureComponentSectionDelegate)Delegate.CreateDelegate(typeof(CaptureComponentSectionDelegate), method);
                SectionCaptureCache[componentType] = capture;
                return capture;
            }
        }

        private static WriteCapturedComponentSectionDelegate GetCapturedSectionWriter(Type componentType)
        {
            lock (CacheLock)
            {
                if (CapturedSectionWriterCache.TryGetValue(componentType, out WriteCapturedComponentSectionDelegate? writer))
                    return writer;

                MethodInfo method = typeof(EntityPersistenceWriter)
                    .GetMethod(nameof(WriteCapturedComponentSectionTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(componentType);

                writer = (WriteCapturedComponentSectionDelegate)Delegate.CreateDelegate(typeof(WriteCapturedComponentSectionDelegate), method);
                CapturedSectionWriterCache[componentType] = writer;
                return writer;
            }
        }

        private static ushort GetSchemaVersion(Type componentType)
        {
            lock (CacheLock)
            {
                if (SchemaVersionCache.TryGetValue(componentType, out ushort version))
                    return version;

                FieldInfo? field = componentType.GetField(
                    "SchemaVersion",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                if (field is not null &&
                    field.FieldType == typeof(ushort) &&
                    field.IsLiteral &&
                    !field.IsInitOnly)
                {
                    version = (ushort)field.GetRawConstantValue()!;
                }
                else
                {
                    version = 1;
                }

                SchemaVersionCache[componentType] = version;
                return version;
            }
        }

        private static void WriteComponentSectionTyped<T>(
            BinaryDataWriter writer,
            IReadOnlyList<PersistenceArchetypeView> archetypes,
            ComponentId componentId)
            where T : unmanaged, ISerializableComponent<T>
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(64);

            try
            {
                for (int i = 0; i < archetypes.Count; i++)
                {
                    ReadOnlySpan<T> span = archetypes[i].Archetype.GetComponentSpan<T>(componentId);
                    WriteSerializedValues(writer, span, ref buffer);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }
        }

        private static Array CaptureComponentSectionTyped<T>(
            IReadOnlyList<PersistenceArchetypeView> archetypes,
            ComponentId componentId,
            int entityCount)
            where T : unmanaged, ISerializableComponent<T>
        {
            var values = new T[entityCount];
            int offset = 0;

            for (int i = 0; i < archetypes.Count; i++)
            {
                ReadOnlySpan<T> span = archetypes[i].Archetype.GetComponentSpan<T>(componentId);
                span.CopyTo(values.AsSpan(offset));
                offset += span.Length;
            }

            return values;
        }

        private static void WriteCapturedComponentSectionTyped<T>(BinaryDataWriter writer, Array values)
            where T : unmanaged, ISerializableComponent<T>
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(64);

            try
            {
                WriteSerializedValues<T>(writer, (T[])values, ref buffer);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }
        }

        private static void WriteSerializedValues<T>(BinaryDataWriter writer, ReadOnlySpan<T> values, ref byte[] buffer)
            where T : unmanaged, ISerializableComponent<T>
        {
            for (int i = 0; i < values.Length; i++)
            {
                int written = SerializeValue(values[i], ref buffer);
                writer.WriteInt32(written);
                writer.WriteBytes(buffer.AsSpan(0, written));
            }
        }

        private static int SerializeValue<T>(in T value, ref byte[] buffer)
            where T : unmanaged, ISerializableComponent<T>
        {
            while (true)
            {
                try
                {
                    var bitWriter = new BitWriter(buffer);
                    T.Serialize(ref bitWriter, in value);
                    return bitWriter.Position;
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("BitWriter overflow:", StringComparison.Ordinal))
                {
                    byte[] larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
                    buffer = larger;
                }
            }
        }

        private static int CountEntities(IReadOnlyList<PersistenceArchetypeView> archetypes)
        {
            int count = 0;
            for (int i = 0; i < archetypes.Count; i++)
                count += archetypes[i].EntityCount;
            return count;
        }

        private static ulong[] CaptureEntityPackedIds(IReadOnlyList<PersistenceArchetypeView> archetypes, int entityCount)
        {
            var packedIds = new ulong[entityCount];
            int offset = 0;

            for (int i = 0; i < archetypes.Count; i++)
            {
                ReadOnlySpan<Entity> entities = archetypes[i].Entities;
                for (int j = 0; j < entities.Length; j++)
                    packedIds[offset++] = entities[j].Packed;
            }

            return packedIds;
        }

        private static int CompareSignatures(PersistenceSignature left, PersistenceSignature right)
        {
            ReadOnlySpan<uint> leftIds = left.PersistenceIds;
            ReadOnlySpan<uint> rightIds = right.PersistenceIds;
            int shared = Math.Min(leftIds.Length, rightIds.Length);

            for (int i = 0; i < shared; i++)
            {
                int cmp = leftIds[i].CompareTo(rightIds[i]);
                if (cmp != 0)
                    return cmp;
            }

            return leftIds.Length.CompareTo(rightIds.Length);
        }

        private static int CompareArchetypeViews(PersistenceArchetypeView left, PersistenceArchetypeView right)
        {
            ReadOnlySpan<ComponentId> leftIds = left.ComponentIds;
            ReadOnlySpan<ComponentId> rightIds = right.ComponentIds;
            int shared = Math.Min(leftIds.Length, rightIds.Length);

            for (int i = 0; i < shared; i++)
            {
                int cmp = leftIds[i].Value.CompareTo(rightIds[i].Value);
                if (cmp != 0)
                    return cmp;
            }

            return leftIds.Length.CompareTo(rightIds.Length);
        }

        private sealed class Group
        {
            public Group(PersistenceSignature signature, PersistenceComponentDescriptor[] descriptors)
            {
                Signature = signature;
                Descriptors = descriptors;
            }

            public PersistenceSignature Signature { get; }
            public PersistenceComponentDescriptor[] Descriptors { get; }
            public List<PersistenceArchetypeView> Archetypes { get; } = new();
        }
    }
}
