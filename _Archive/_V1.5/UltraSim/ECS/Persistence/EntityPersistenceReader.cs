using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UltraSim.ECS.Persistence
{
    public static class EntityPersistenceReader
    {
        private static readonly Dictionary<Type, ReadComponentSectionDelegate> SectionReaderCache = new();
        private static readonly Dictionary<Type, FixupComponentSectionDelegate?> SectionFixupCache = new();
        private static readonly object CacheLock = new();

        private delegate void ReadComponentSectionDelegate(
            BinaryDataReader reader,
            Archetype archetype,
            int startSlot,
            int entityCount,
            ComponentId componentId,
            ushort schemaVersion,
            int payloadByteLength);

        private delegate void FixupComponentSectionDelegate(
            Archetype archetype,
            int startSlot,
            int entityCount,
            ComponentId componentId,
            EntityRemapTable remapTable);

        public static EntityPersistenceLoadResult ReadCreatePhase(
            World world,
            SaveSetPaths paths,
            PersistenceTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(paths);
            ArgumentNullException.ThrowIfNull(registry);

            var result = new EntityPersistenceLoadResult();

            bool hasIdx = File.Exists(paths.EntitiesIdx);
            bool hasBin = File.Exists(paths.EntitiesBin);
            if (!hasIdx || !hasBin)
            {
                if (!hasIdx)
                    result.MissingFiles.Add(paths.EntitiesIdx);
                if (!hasBin)
                    result.MissingFiles.Add(paths.EntitiesBin);
                return result;
            }

            IndexBlock[] indexBlocks;
            using (var idxReader = new BinaryDataReader(paths.EntitiesIdx))
            {
                SaveFileHeaders.ReadAndValidateHeader(idxReader, SaveFileHeaders.EntityIdxMagic, SaveFileHeaders.EntityIdxVersion);
                int blockCount = idxReader.ReadInt32();
                if (blockCount < 0)
                    throw new InvalidDataException($"Invalid entity block count {blockCount} in '{paths.EntitiesIdx}'.");

                indexBlocks = new IndexBlock[blockCount];
                for (int i = 0; i < blockCount; i++)
                    indexBlocks[i] = ReadIndexBlock(idxReader, registry);
            }

            var loadBlocks = new EntityPersistenceLoadBlock[indexBlocks.Length];

            using var binStream = File.Open(paths.EntitiesBin, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var binReader = new BinaryDataReader(binStream, leaveOpen: true);
            SaveFileHeaders.ReadAndValidateHeader(binReader, SaveFileHeaders.EntityBinMagic, SaveFileHeaders.EntityBinVersion);

            for (int i = 0; i < indexBlocks.Length; i++)
            {
                IndexBlock indexBlock = indexBlocks[i];
                binStream.Position = indexBlock.BinOffset;

                var archetype = world.Archetypes.GetOrCreate(indexBlock.RuntimeComponentIds);
                var entities = new Entity[indexBlock.EntityCount];

                int allocated = world.EntityStore.AllocateBulk(entities);
                if (allocated != entities.Length)
                {
                    throw new InvalidOperationException(
                        $"EntityManager allocated {allocated} entities, expected {entities.Length}.");
                }

                int startSlot = archetype.AddEntitiesUninitialized(entities);
                world.EntityStore.SetLocationsBulkAssumeValid(entities, archetype, startSlot);

                for (int e = 0; e < entities.Length; e++)
                {
                    ulong oldPacked = binReader.ReadUInt64();
                    result.RemapTable.Add(oldPacked, entities[e]);
                }

                loadBlocks[i] = new EntityPersistenceLoadBlock
                {
                    Signature = indexBlock.Signature,
                    Descriptors = indexBlock.Descriptors,
                    Archetype = archetype,
                    Entities = entities,
                    StartSlot = startSlot,
                    BinOffset = indexBlock.BinOffset,
                    BinLength = indexBlock.BinLength,
                };

                result.LoadedEntityCount += entities.Length;
            }

            result.BlockCount = loadBlocks.Length;
            result.Blocks = loadBlocks;
            return result;
        }

        public static void Hydrate(EntityPersistenceLoadResult loadResult, SaveSetPaths paths)
        {
            ArgumentNullException.ThrowIfNull(loadResult);
            ArgumentNullException.ThrowIfNull(paths);

            if (loadResult.MissingFiles.Count > 0 || loadResult.Blocks.Length == 0)
                return;

            using var binStream = File.Open(paths.EntitiesBin, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var binReader = new BinaryDataReader(binStream, leaveOpen: true);
            SaveFileHeaders.ReadAndValidateHeader(binReader, SaveFileHeaders.EntityBinMagic, SaveFileHeaders.EntityBinVersion);

            for (int i = 0; i < loadResult.Blocks.Length; i++)
            {
                EntityPersistenceLoadBlock block = loadResult.Blocks[i];
                long payloadOffset = checked(block.BinOffset + (sizeof(ulong) * (long)block.Entities.Length));
                binStream.Position = payloadOffset;

                for (int c = 0; c < block.Descriptors.Length; c++)
                {
                    PersistenceComponentDescriptor descriptor = block.Descriptors[c];

                    uint storedPersistenceId = binReader.ReadUInt32();
                    ushort schemaVersion = binReader.ReadUInt16();
                    int payloadByteLength = binReader.ReadInt32();
                    int entityCount = binReader.ReadInt32();

                    if (storedPersistenceId != descriptor.PersistenceId)
                    {
                        throw new InvalidDataException(
                            $"Entity payload section persistence id mismatch: expected {descriptor.PersistenceId}, got {storedPersistenceId}.");
                    }

                    if (payloadByteLength < 0)
                        throw new InvalidDataException($"Invalid payload byte length {payloadByteLength} for persistence id {storedPersistenceId}.");

                    if (entityCount != block.Entities.Length)
                    {
                        throw new InvalidDataException(
                            $"Entity payload section count mismatch for persistence id {storedPersistenceId}: expected {block.Entities.Length}, got {entityCount}.");
                    }

                    long payloadStart = binStream.Position;
                    GetSectionReader(descriptor.ClrType)(
                        binReader,
                        block.Archetype,
                        block.StartSlot,
                        block.Entities.Length,
                        descriptor.RuntimeComponentId,
                        schemaVersion,
                        payloadByteLength);

                    int consumed = checked((int)(binStream.Position - payloadStart));
                    if (consumed != payloadByteLength)
                    {
                        throw new InvalidDataException(
                            $"Entity payload section length mismatch for persistence id {storedPersistenceId}: expected {payloadByteLength}, consumed {consumed}.");
                    }
                }
            }
        }

        public static void FixupEntityReferences(EntityPersistenceLoadResult loadResult)
        {
            ArgumentNullException.ThrowIfNull(loadResult);

            if (loadResult.Blocks.Length == 0)
                return;

            for (int i = 0; i < loadResult.Blocks.Length; i++)
            {
                EntityPersistenceLoadBlock block = loadResult.Blocks[i];
                for (int c = 0; c < block.Descriptors.Length; c++)
                {
                    PersistenceComponentDescriptor descriptor = block.Descriptors[c];
                    FixupComponentSectionDelegate? fixup = GetSectionFixup(descriptor.ClrType);
                    if (fixup is null)
                        continue;

                    fixup(
                        block.Archetype,
                        block.StartSlot,
                        block.Entities.Length,
                        descriptor.RuntimeComponentId,
                        loadResult.RemapTable);
                }
            }
        }

        private static IndexBlock ReadIndexBlock(BinaryDataReader reader, PersistenceTypeRegistry registry)
        {
            int componentCount = reader.ReadInt32();
            if (componentCount < 0)
                throw new InvalidDataException($"Invalid component count {componentCount} in entity block index.");

            var persistenceIds = new uint[componentCount];
            var descriptors = new PersistenceComponentDescriptor[componentCount];
            var runtimeComponentIds = new ComponentId[componentCount];

            for (int i = 0; i < componentCount; i++)
            {
                uint persistenceId = reader.ReadUInt32();
                persistenceIds[i] = persistenceId;
                descriptors[i] = ResolveDescriptor(persistenceId, registry);
                runtimeComponentIds[i] = descriptors[i].RuntimeComponentId;
            }

            int entityCount = reader.ReadInt32();
            long binOffset = reader.ReadInt64();
            int binLength = reader.ReadInt32();

            if (entityCount < 0)
                throw new InvalidDataException($"Invalid entity count {entityCount} in entity block index.");
            if (binOffset < SaveFileHeaders.HeaderBytes)
                throw new InvalidDataException($"Invalid entity block offset {binOffset} in entity block index.");
            if (binLength < 0)
                throw new InvalidDataException($"Invalid entity block length {binLength} in entity block index.");

            return new IndexBlock(
                new PersistenceSignature(persistenceIds),
                descriptors,
                runtimeComponentIds,
                entityCount,
                binOffset,
                binLength);
        }

        private static PersistenceComponentDescriptor ResolveDescriptor(uint persistenceId, PersistenceTypeRegistry registry)
        {
            if (!registry.TryGetById(persistenceId, out PersistenceTypeRecord record) ||
                record.Kind != PersistenceKind.Component)
            {
                throw new InvalidDataException(
                    $"Entity block references persistence id {persistenceId}, but no persistent component registration was found.");
            }

            if (!ComponentManager.TryGetId(record.ClrType, out ComponentId runtimeComponentId))
            {
                throw new InvalidOperationException(
                    $"Persistent component '{record.ClrType.FullName}' is registered in the persistence registry but not in ComponentManager.");
            }

            return new PersistenceComponentDescriptor(record.PersistenceId, runtimeComponentId, record.ClrType);
        }

        private static ReadComponentSectionDelegate GetSectionReader(Type componentType)
        {
            lock (CacheLock)
            {
                if (SectionReaderCache.TryGetValue(componentType, out ReadComponentSectionDelegate? reader))
                    return reader;

                MethodInfo method = typeof(EntityPersistenceReader)
                    .GetMethod(nameof(ReadComponentSectionTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(componentType);

                reader = (ReadComponentSectionDelegate)Delegate.CreateDelegate(typeof(ReadComponentSectionDelegate), method);
                SectionReaderCache[componentType] = reader;
                return reader;
            }
        }

        private static FixupComponentSectionDelegate? GetSectionFixup(Type componentType)
        {
            lock (CacheLock)
            {
                if (SectionFixupCache.TryGetValue(componentType, out FixupComponentSectionDelegate? fixup))
                    return fixup;

                if (!ImplementsEntityReferenceFixup(componentType))
                {
                    SectionFixupCache[componentType] = null;
                    return null;
                }

                MethodInfo method = typeof(EntityPersistenceReader)
                    .GetMethod(nameof(FixupComponentSectionTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(componentType);

                fixup = (FixupComponentSectionDelegate)Delegate.CreateDelegate(typeof(FixupComponentSectionDelegate), method);
                SectionFixupCache[componentType] = fixup;
                return fixup;
            }
        }

        private static void ReadComponentSectionTyped<T>(
            BinaryDataReader reader,
            Archetype archetype,
            int startSlot,
            int entityCount,
            ComponentId componentId,
            ushort schemaVersion,
            int payloadByteLength)
            where T : unmanaged, ISerializableComponent<T>
        {
            Span<T> destination = archetype.GetComponentSpan<T>(componentId).Slice(startSlot, entityCount);

            // Bulk-read the entire component section into one pooled buffer, then parse
            // in-memory. Replaces O(entityCount) BinaryDataReader dispatches with one
            // large stream read followed by cheap in-memory length-prefix parsing.
            byte[] sectionBuffer = ArrayPool<byte>.Shared.Rent(payloadByteLength);
            try
            {
                int bytesRead = reader.ReadBytes(sectionBuffer, 0, payloadByteLength);
                if (bytesRead != payloadByteLength)
                {
                    throw new EndOfStreamException(
                        $"Expected {payloadByteLength} section bytes for component id {componentId}, got {bytesRead}.");
                }

                int offset = 0;
                for (int i = 0; i < entityCount; i++)
                {
                    if (offset + sizeof(int) > payloadByteLength)
                    {
                        throw new InvalidDataException(
                            $"Component payload section truncated at entity {i} for component id {componentId}.");
                    }

                    int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(sectionBuffer.AsSpan(offset));
                    offset += sizeof(int);

                    if (payloadLength < 0 || offset + payloadLength > payloadByteLength)
                    {
                        throw new InvalidDataException(
                            $"Invalid component payload length {payloadLength} at entity {i} for component id {componentId}.");
                    }

                    var bitReader = new BitReader(sectionBuffer.AsSpan(offset, payloadLength));
                    destination[i] = T.Deserialize(ref bitReader, schemaVersion);
                    offset += payloadLength;
                }

                if (offset != payloadByteLength)
                {
                    throw new InvalidDataException(
                        $"Component payload section for component id {componentId} has {payloadByteLength - offset} unread bytes remaining.");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sectionBuffer, clearArray: false);
            }
        }

        private static void FixupComponentSectionTyped<T>(
            Archetype archetype,
            int startSlot,
            int entityCount,
            ComponentId componentId,
            EntityRemapTable remapTable)
            where T : unmanaged, ISerializableComponent<T>, IEntityReferenceFixup<T>
        {
            Span<T> span = archetype.GetComponentSpan<T>(componentId).Slice(startSlot, entityCount);
            for (int i = 0; i < span.Length; i++)
                T.FixupEntityReferences(ref span[i], remapTable);
        }

        private static bool ImplementsEntityReferenceFixup(Type type)
        {
            Type[] interfaces = type.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                Type iface = interfaces[i];
                if (!iface.IsGenericType)
                    continue;

                if (iface.GetGenericTypeDefinition() != typeof(IEntityReferenceFixup<>))
                    continue;

                Type[] args = iface.GetGenericArguments();
                return args.Length == 1 && args[0] == type;
            }

            return false;
        }

        private readonly record struct IndexBlock(
            PersistenceSignature Signature,
            PersistenceComponentDescriptor[] Descriptors,
            ComponentId[] RuntimeComponentIds,
            int EntityCount,
            long BinOffset,
            int BinLength);
    }
}
