using System.Buffers;
using System.Reflection;

namespace UltraSim.ECS.Persistence;

/// <summary>
/// Serializes in-memory entity persistence snapshots to and from a single binary stream.
/// </summary>
public static class BinaryEntityPersistenceSerializer
{
    private const uint SnapshotMagic = 0x31535045; // EPS1
    private const ushort SnapshotVersion = 1;

    private static readonly object s_cacheSync = new();
    private static readonly Dictionary<Type, WriteSectionDelegate> s_writeSectionByType = new();
    private static readonly Dictionary<Type, ReadSectionDelegate> s_readSectionByType = new();
    private static readonly Dictionary<Type, ushort> s_schemaVersionByType = new();

    private delegate void WriteSectionDelegate(BinaryDataWriter writer, Array values);
    private delegate Array ReadSectionDelegate(BinaryDataReader reader, int valueCount, ushort schemaVersion);

    public static void WriteSnapshot(BinaryDataWriter writer, EntityPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(snapshot);

        writer.WriteUInt32(SnapshotMagic);
        writer.WriteUInt16(SnapshotVersion);
        writer.WriteInt32(snapshot.GroupCount);

        for (int i = 0; i < snapshot.Groups.Count; i++)
        {
            EntityPersistenceGroup group = snapshot.Groups[i];
            WriteGroup(writer, group);
        }
    }

    public static EntityPersistenceSnapshot ReadSnapshot(
        BinaryDataReader reader,
        EcsSchema schema,
        PersistenceTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(registry);

        uint magic = reader.ReadUInt32();
        if (magic != SnapshotMagic)
            throw new InvalidDataException($"Unexpected persistence snapshot magic 0x{magic:X8}.");

        ushort version = reader.ReadUInt16();
        if (version != SnapshotVersion)
            throw new InvalidDataException($"Unsupported persistence snapshot version {version}.");

        int groupCount = reader.ReadInt32();
        if (groupCount < 0)
            throw new InvalidDataException($"Invalid persistence snapshot group count {groupCount}.");

        if (!registry.IsSealed)
            throw new InvalidOperationException("PersistenceTypeRegistry must be sealed before reading a binary snapshot.");

        var groups = new EntityPersistenceGroup[groupCount];
        for (int i = 0; i < groupCount; i++)
            groups[i] = ReadGroup(reader, schema, registry);

        return new EntityPersistenceSnapshot(groups);
    }

    private static void WriteGroup(BinaryDataWriter writer, EntityPersistenceGroup group)
    {
        ReadOnlySpan<uint> signatureIds = group.Signature.PersistenceIds;
        writer.WriteInt32(signatureIds.Length);
        for (int i = 0; i < signatureIds.Length; i++)
            writer.WriteUInt32(signatureIds[i]);

        writer.WriteInt32(group.EntityCount);
        for (int i = 0; i < group.SourceEntityPackedIds.Length; i++)
            writer.WriteUInt64(group.SourceEntityPackedIds[i]);

        writer.WriteInt32(group.Sections.Length);
        for (int i = 0; i < group.Sections.Length; i++)
            WriteSection(writer, group.Sections[i], group.EntityCount);
    }

    private static EntityPersistenceGroup ReadGroup(
        BinaryDataReader reader,
        EcsSchema schema,
        PersistenceTypeRegistry registry)
    {
        int componentCount = reader.ReadInt32();
        if (componentCount < 0)
            throw new InvalidDataException($"Invalid persistence group component count {componentCount}.");

        var signatureIds = new uint[componentCount];
        for (int i = 0; i < componentCount; i++)
            signatureIds[i] = reader.ReadUInt32();

        int entityCount = reader.ReadInt32();
        if (entityCount < 0)
            throw new InvalidDataException($"Invalid persistence group entity count {entityCount}.");

        var packedIds = new ulong[entityCount];
        for (int i = 0; i < entityCount; i++)
            packedIds[i] = reader.ReadUInt64();

        int sectionCount = reader.ReadInt32();
        if (sectionCount < 0)
            throw new InvalidDataException($"Invalid persistence group section count {sectionCount}.");

        var sections = new EntityPersistenceSection[sectionCount];
        for (int i = 0; i < sectionCount; i++)
            sections[i] = ReadSection(reader, schema, registry, entityCount);

        return new EntityPersistenceGroup(new PersistenceSignature(signatureIds), packedIds, sections);
    }

    private static void WriteSection(BinaryDataWriter writer, EntityPersistenceSection section, int entityCount)
    {
        writer.WriteUInt32(section.Descriptor.PersistenceId);
        writer.WriteUInt16(GetSchemaVersion(section.Descriptor.ClrType));
        writer.WriteInt32(entityCount);
        GetWriteSection(section.Descriptor.ClrType)(writer, section.Values);
    }

    private static EntityPersistenceSection ReadSection(
        BinaryDataReader reader,
        EcsSchema schema,
        PersistenceTypeRegistry registry,
        int expectedEntityCount)
    {
        uint persistenceId = reader.ReadUInt32();
        ushort schemaVersion = reader.ReadUInt16();
        int entityCount = reader.ReadInt32();

        if (entityCount != expectedEntityCount)
            throw new InvalidDataException(
                $"Persistence section entity count mismatch: expected {expectedEntityCount}, got {entityCount}.");

        if (!registry.TryGetById(persistenceId, out PersistenceTypeRecord typeRecord) ||
            typeRecord.Kind != PersistenceKind.Component)
        {
            throw new InvalidDataException(
                $"Persistence section references component id {persistenceId}, but no persistent component registration was found.");
        }

        ComponentId runtimeComponentId = schema.GetComponentId(typeRecord.ClrType);
        Array values = GetReadSection(typeRecord.ClrType)(reader, entityCount, schemaVersion);
        var descriptor = new PersistenceComponentDescriptor(typeRecord.PersistenceId, runtimeComponentId, typeRecord.ClrType);
        return new EntityPersistenceSection(descriptor, values);
    }

    private static WriteSectionDelegate GetWriteSection(Type componentType)
    {
        lock (s_cacheSync)
        {
            if (s_writeSectionByType.TryGetValue(componentType, out WriteSectionDelegate? writer))
                return writer;

            MethodInfo method = typeof(BinaryEntityPersistenceSerializer)
                .GetMethod(nameof(WriteSectionTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(componentType);

            writer = (WriteSectionDelegate)Delegate.CreateDelegate(typeof(WriteSectionDelegate), method);
            s_writeSectionByType.Add(componentType, writer);
            return writer;
        }
    }

    private static ReadSectionDelegate GetReadSection(Type componentType)
    {
        lock (s_cacheSync)
        {
            if (s_readSectionByType.TryGetValue(componentType, out ReadSectionDelegate? reader))
                return reader;

            MethodInfo method = typeof(BinaryEntityPersistenceSerializer)
                .GetMethod(nameof(ReadSectionTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(componentType);

            reader = (ReadSectionDelegate)Delegate.CreateDelegate(typeof(ReadSectionDelegate), method);
            s_readSectionByType.Add(componentType, reader);
            return reader;
        }
    }

    private static ushort GetSchemaVersion(Type componentType)
    {
        lock (s_cacheSync)
        {
            if (s_schemaVersionByType.TryGetValue(componentType, out ushort version))
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

            s_schemaVersionByType.Add(componentType, version);
            return version;
        }
    }

    private static void WriteSectionTyped<T>(BinaryDataWriter writer, Array values)
        where T : unmanaged, ISerializableComponent<T>
    {
        if (values is not T[] typed)
            throw new InvalidCastException($"Expected {typeof(T).Name}[], got {values.GetType().Name}.");

        byte[] buffer = ArrayPool<byte>.Shared.Rent(64);
        try
        {
            for (int i = 0; i < typed.Length; i++)
            {
                int written = SerializeValue(typed[i], ref buffer);
                writer.WriteInt32(written);
                writer.WriteBytes(buffer, 0, written);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    private static Array ReadSectionTyped<T>(BinaryDataReader reader, int valueCount, ushort schemaVersion)
        where T : unmanaged, ISerializableComponent<T>
    {
        var values = new T[valueCount];
        for (int i = 0; i < valueCount; i++)
        {
            int payloadLength = reader.ReadInt32();
            if (payloadLength < 0)
                throw new InvalidDataException($"Invalid serialized payload length {payloadLength} for {typeof(T).FullName}.");

            byte[] payload = ArrayPool<byte>.Shared.Rent(payloadLength);
            try
            {
                int bytesRead = reader.ReadBytes(payload, 0, payloadLength);
                if (bytesRead != payloadLength)
                    throw new EndOfStreamException($"Expected {payloadLength} payload bytes for {typeof(T).FullName}, got {bytesRead}.");

                var bitReader = new BitReader(payload.AsSpan(0, payloadLength));
                values[i] = T.Deserialize(ref bitReader, schemaVersion);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payload, clearArray: false);
            }
        }

        return values;
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
}
