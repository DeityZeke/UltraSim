using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace UltraSim.ECS;

internal static class SnapshotParser
{
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static long EstimateKnownTemporaryBytes(ReadOnlySpan<byte> bytes, PersistenceLimits limits)
    {
        if (bytes.Length < SnapshotContainerBuilder.HeaderSize + 32 || bytes.Length > limits.MaximumArtifactBytes)
            throw new PersistenceValidationException("Snapshot size is outside configured bounds.");
        ReadOnlySpan<byte> header = bytes[..SnapshotContainerBuilder.HeaderSize];
        if (BinaryPrimitives.ReadUInt64LittleEndian(header) != SnapshotContainerBuilder.Magic)
            throw new PersistenceValidationException("Snapshot magic does not identify an ECS 3.4 container.");
        int entities = BinaryPrimitives.ReadInt32LittleEndian(header[56..]);
        int shapes = BinaryPrimitives.ReadInt32LittleEndian(header[60..]);
        int sections = BinaryPrimitives.ReadInt32LittleEndian(header[64..]);
        if (entities < 0 || entities > limits.MaximumEntities || shapes < 0 || shapes > limits.MaximumShapes ||
            sections <= 0 || sections > limits.MaximumSections)
            throw new PersistenceValidationException("Snapshot preflight counts exceed configured bounds.");
        try
        {
            // The parser owns an immutable artifact clone and may copy every
            // section payload once. A third artifact-sized allowance covers
            // parsed tables and strings; entity/shape/directory arrays are
            // accounted separately. Runtime decode expansion is known only
            // after catalog resolution and is added by ParsedSnapshot.Resolve.
            return checked((long)bytes.Length * 3L + (long)entities * 40L +
                (long)shapes * 128L + (long)sections * 64L);
        }
        catch (OverflowException exception)
        {
            throw new PersistenceValidationException($"Snapshot temporary-memory estimate overflowed: {exception.Message}");
        }
    }

    internal static ParsedSnapshot Parse(byte[] bytes, PersistenceRestoreOptions options)
    {
        PersistenceLimits limits = options.Limits;
        if (bytes.Length < SnapshotContainerBuilder.HeaderSize + 32 || bytes.Length > limits.MaximumArtifactBytes)
            throw new PersistenceValidationException("Snapshot size is outside configured bounds.");
        ReadOnlySpan<byte> header = bytes.AsSpan(0, SnapshotContainerBuilder.HeaderSize);
        if (BinaryPrimitives.ReadUInt64LittleEndian(header) != SnapshotContainerBuilder.Magic)
            throw new PersistenceValidationException("Snapshot magic does not identify an ECS 3.4 container.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) != SnapshotContainerBuilder.HeaderSize)
            throw new PersistenceValidationException("Unsupported snapshot format/header version.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(header[68..]) != 0 ||
            header[104..].ContainsAnyExcept((byte)0))
            throw new PersistenceValidationException("Snapshot header reserved bytes are not canonical zero.");
        ulong declaredLength = BinaryPrimitives.ReadUInt64LittleEndian(header[16..]);
        if (declaredLength != unchecked((ulong)bytes.Length))
            throw new PersistenceValidationException("Snapshot total length is inconsistent.");
        ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(header[24..]);
        ulong worldId = BinaryPrimitives.ReadUInt64LittleEndian(header[32..]);
        ulong highWater = BinaryPrimitives.ReadUInt64LittleEndian(header[40..]);
        var schemaHint = new SchemaId(BinaryPrimitives.ReadUInt64LittleEndian(header[88..]),
            BinaryPrimitives.ReadUInt64LittleEndian(header[96..]));
        int entityCount = BinaryPrimitives.ReadInt32LittleEndian(header[56..]);
        int shapeCount = BinaryPrimitives.ReadInt32LittleEndian(header[60..]);
        int sectionCount = BinaryPrimitives.ReadInt32LittleEndian(header[64..]);
        ulong directoryOffset64 = BinaryPrimitives.ReadUInt64LittleEndian(header[72..]);
        ulong digestOffset64 = BinaryPrimitives.ReadUInt64LittleEndian(header[80..]);
        if (sequence == 0 || worldId == 0 || entityCount < 0 || entityCount > limits.MaximumEntities ||
            shapeCount < 0 || shapeCount > limits.MaximumShapes || sectionCount <= 0 || sectionCount > limits.MaximumSections)
            throw new PersistenceValidationException("Snapshot header contains invalid or over-limit counts/identities.");
        int directoryOffset = ToBoundedInt(directoryOffset64, bytes.Length, "directory offset");
        int digestOffset = ToBoundedInt(digestOffset64, bytes.Length, "digest offset");
        int directoryBytes = CheckedProduct(sectionCount, SnapshotContainerBuilder.DirectoryEntrySize, bytes.Length, "directory size");
        if (directoryOffset < SnapshotContainerBuilder.HeaderSize ||
            checked(directoryOffset + directoryBytes) != digestOffset || checked(digestOffset + 32) != bytes.Length)
            throw new PersistenceValidationException("Snapshot directory/digest boundaries are invalid.");
        byte[] computed = SHA256.HashData(bytes.AsSpan(0, digestOffset));
        if (!CryptographicOperations.FixedTimeEquals(computed, bytes.AsSpan(digestOffset, 32)))
            throw new PersistenceValidationException("Snapshot global digest mismatch.");

        var entries = new SectionEntry[sectionCount];
        var unique = new HashSet<(SnapshotSectionKind, int)>();
        for (int index = 0; index < sectionCount; index++)
        {
            ReadOnlySpan<byte> entry = bytes.AsSpan(directoryOffset + index * SnapshotContainerBuilder.DirectoryEntrySize,
                SnapshotContainerBuilder.DirectoryEntrySize);
            SnapshotSectionKind kind = (SnapshotSectionKind)BinaryPrimitives.ReadUInt32LittleEndian(entry);
            int ordinal = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            int offset = ToBoundedInt(BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]), directoryOffset, "section offset");
            int length = ToBoundedInt(BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]), directoryOffset, "section length");
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(entry[24..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry[28..]) != 0)
                throw new PersistenceValidationException("Snapshot directory reserved bytes are not canonical zero.");
            if (!Enum.IsDefined(kind) || ordinal < 0 || offset < SnapshotContainerBuilder.HeaderSize ||
                length < 0 || checked(offset + length) > directoryOffset || !unique.Add((kind, ordinal)))
                throw new PersistenceValidationException("Snapshot section directory is invalid or duplicated.");
            if (PersistenceCrc32.Compute(bytes.AsSpan(offset, length)) != crc)
                throw new PersistenceValidationException($"Snapshot section checksum mismatch for {kind}:{ordinal}.");
            entries[index] = new SectionEntry(kind, ordinal, offset, length);
        }
        if (entries.Length < 6 || entries[0].Kind != SnapshotSectionKind.Schema || entries[0].Ordinal != 0 ||
            entries[1].Kind != SnapshotSectionKind.Shapes || entries[1].Ordinal != 0 ||
            entries[2].Kind != SnapshotSectionKind.Prefabs || entries[2].Ordinal != 0 ||
            entries[3].Kind != SnapshotSectionKind.Entities || entries[3].Ordinal != 0 ||
            entries[4].Kind != SnapshotSectionKind.ShapeRows || entries[4].Ordinal != 0 ||
            entries[^1].Kind != SnapshotSectionKind.Systems || entries[^1].Ordinal != 0)
            throw new PersistenceValidationException("Snapshot section directory order is not canonical.");
        int columnOrdinal = 0;
        for (int index = 5; index < entries.Length - 1; index++)
        {
            if (entries[index].Kind is not (SnapshotSectionKind.Column or SnapshotSectionKind.Enable) ||
                entries[index].Ordinal != columnOrdinal++)
                throw new PersistenceValidationException("Snapshot column/enable ordinals are not contiguous and canonical.");
        }
        int end = SnapshotContainerBuilder.HeaderSize;
        foreach (SectionEntry entry in entries)
        {
            if (entry.Offset != end)
                throw new PersistenceValidationException("Snapshot section offsets are not in canonical physical order.");
            end = checked(entry.Offset + entry.Length);
            int aligned = Align8(end);
            if (bytes.AsSpan(end, aligned - end).ContainsAnyExcept((byte)0))
                throw new PersistenceValidationException("Snapshot section padding is not canonical zero.");
            end = aligned;
        }
        if (end != directoryOffset)
            throw new PersistenceValidationException("Snapshot payload does not end at the canonical directory boundary.");

        SectionEntry schemaEntry = RequireSingle(entries, SnapshotSectionKind.Schema);
        SectionEntry shapesEntry = RequireSingle(entries, SnapshotSectionKind.Shapes);
        SectionEntry entitiesEntry = RequireSingle(entries, SnapshotSectionKind.Entities);
        SectionEntry rowsEntry = RequireSingle(entries, SnapshotSectionKind.ShapeRows);
        SectionEntry systemsEntry = RequireSingle(entries, SnapshotSectionKind.Systems);
        SectionEntry prefabsEntry = RequireSingle(entries, SnapshotSectionKind.Prefabs);
        SavedSchema[] schemas = ParseSchemas(bytes, schemaEntry, limits);
        ResolvedShape[] shapes = ParseShapes(bytes, shapesEntry, shapeCount, entityCount, limits);
        var schemaIds = new HashSet<uint>(schemas.Select(static schema => schema.PersistenceId));
        foreach (ResolvedShape shape in shapes)
            foreach (uint id in shape.SavedIds)
                if (!schemaIds.Contains(id))
                    throw new PersistenceValidationException("Saved shape refers to a component absent from the schema directory.");
        SavedEntity[] entities = ParseEntities(bytes, entitiesEntry, entityCount, shapeCount, highWater);
        uint[][] ordinals = ParseRows(bytes, rowsEntry, shapes, entityCount);
        ResolvedColumn[] columns = ParseColumns(bytes, entries, shapes, schemas, limits);
        ResolvedEnable[] enables = ParseEnables(bytes, entries, shapes, schemas);
        Dictionary<ulong, string> prefabs = ParsePrefabs(bytes, prefabsEntry, limits);
        foreach (SavedEntity entity in entities)
            if (entity.PrefabKeyHash != 0 && !prefabs.ContainsKey(entity.PrefabKeyHash))
                throw new PersistenceValidationException("Entity prefab provenance is absent from the prefab-key directory.");
        ParseSystems(bytes, systemsEntry, limits, out ulong schedulerTick, out ParsedSystemState[] systems);

        return new ParsedSnapshot
        {
            WorldId = new PersistentWorldId(worldId), AllocatorHighWater = highWater, Sequence = sequence, SchemaHint = schemaHint,
            Schemas = schemas, Shapes = shapes, Entities = entities, ShapeOrdinals = ordinals,
            Columns = columns, Enables = enables, Systems = systems, Prefabs = prefabs, SchedulerTick = schedulerTick,
            Diagnostics = new List<string>(options.MaximumDiagnostics),
        };
    }

    private static Dictionary<ulong, string> ParsePrefabs(byte[] bytes, SectionEntry entry, PersistenceLimits limits)
    {
        var reader = new CheckedSectionReader(bytes, entry);
        int count = reader.ReadCount("prefab key count", limits.MaximumEntities);
        var result = new Dictionary<ulong, string>(count);
        ulong previous = 0;
        for (int index = 0; index < count; index++)
        {
            ulong hash = reader.ReadUInt64("prefab key hash");
            string key = reader.ReadUtf8("prefab key", PrefabKey.MaximumLength);
            if (hash == 0 || hash <= previous || StableHash64.Compute(key) != hash || !result.TryAdd(hash, key))
                throw new PersistenceValidationException("Prefab provenance directory is not canonical.");
            _ = new PrefabKey(key);
            previous = hash;
        }
        reader.RequireEnd();
        return result;
    }

    private static void ParseSystems(byte[] bytes, SectionEntry entry, PersistenceLimits limits,
        out ulong schedulerTick, out ParsedSystemState[] systems)
    {
        var reader = new CheckedSectionReader(bytes, entry);
        schedulerTick = reader.ReadUInt64("scheduler tick");
        int count = reader.ReadCount("system state count", limits.MaximumSections);
        systems = new ParsedSystemState[count];
        ulong previous = 0;
        long payload = 0;
        for (int index = 0; index < count; index++)
        {
            ulong id = reader.ReadUInt64("system persistence ID");
            ushort version = reader.ReadUInt16("system state version");
            ushort flags = reader.ReadUInt16("system state flags");
            int bytesCount = reader.ReadNonNegative("system state bytes", reader.Remaining);
            payload = checked(payload + bytesCount);
            if (id == 0 || id <= previous || version == 0 || (flags & ~1) != 0 ||
                payload > limits.MaximumComponentPayloadBytes)
                throw new PersistenceValidationException("System state directory is not canonical or exceeds configured bounds.");
            systems[index] = new ParsedSystemState(id, version, (flags & 1) != 0,
                reader.ReadBytes(bytesCount, "system state payload"));
            previous = id;
        }
        reader.RequireEnd();
    }

    private static SavedSchema[] ParseSchemas(byte[] bytes, SectionEntry entry, PersistenceLimits limits)
    {
        var reader = new CheckedSectionReader(bytes, entry);
        int count = reader.ReadCount("component schema count", limits.MaximumComponents);
        var schemas = new SavedSchema[count];
        uint previous = 0;
        for (int i = 0; i < count; i++)
        {
            uint id = reader.ReadUInt32("component persistence ID");
            ushort version = reader.ReadUInt16("component version");
            ComponentPersistenceMode mode = (ComponentPersistenceMode)reader.ReadByte("component persistence mode");
            ComponentFlags flags = (ComponentFlags)reader.ReadByte("component flags");
            int encoded = reader.ReadNonNegative("component encoded width", int.MaxValue);
            ulong low = reader.ReadUInt64("component fingerprint low");
            ulong high = reader.ReadUInt64("component fingerprint high");
            ulong keyHash = reader.ReadUInt64("component stable-key hash");
            // Component canonical keys use the container's ushort UTF-8 length
            // field. PrefabKey's intentionally smaller authoring bound is a
            // different contract and must not make a capturable schema
            // unrestorable.
            string key = reader.ReadUtf8("component canonical key", ushort.MaxValue);
            if (id == 0 || id <= previous || version == 0 || !Enum.IsDefined(mode) || mode is ComponentPersistenceMode.Auto or ComponentPersistenceMode.None)
                throw new PersistenceValidationException("Component schema directory is not canonical.");
            const ComponentFlags knownFlags = ComponentFlags.Tag | ComponentFlags.Enableable |
                ComponentFlags.Persistent | ComponentFlags.PersistEnableState;
            if ((flags & ~knownFlags) != 0 ||
                ((flags & ComponentFlags.Persistent) != 0) != (mode == ComponentPersistenceMode.Value) ||
                (flags & ComponentFlags.PersistEnableState) != 0 && (flags & ComponentFlags.Enableable) == 0 ||
                keyHash == 0 || StableHash64.Compute(key) != keyHash)
                throw new PersistenceValidationException("Component schema flags/key identity are inconsistent.");
            if ((mode == ComponentPersistenceMode.Value && encoded <= 0) ||
                (mode != ComponentPersistenceMode.Value && encoded != 0) ||
                (mode != ComponentPersistenceMode.Value && (low | high) != 0))
                throw new PersistenceValidationException("Component schema payload metadata is inconsistent.");
            PersistenceSchemaFingerprint fingerprint = (low | high) == 0 ? default : new PersistenceSchemaFingerprint(low, high);
            schemas[i] = new SavedSchema(id, version, mode, flags, encoded, fingerprint, keyHash, key);
            previous = id;
        }
        reader.RequireEnd();
        return schemas;
    }

    private static ResolvedShape[] ParseShapes(byte[] bytes, SectionEntry entry, int expectedCount, int totalEntities, PersistenceLimits limits)
    {
        var reader = new CheckedSectionReader(bytes, entry);
        if (reader.ReadCount("shape count", limits.MaximumShapes) != expectedCount)
            throw new PersistenceValidationException("Shape count disagrees with the snapshot header.");
        var shapes = new ResolvedShape[expectedCount];
        long population = 0;
        uint[]? previous = null;
        for (int i = 0; i < shapes.Length; i++)
        {
            int componentCount = reader.ReadCount("shape component count", limits.MaximumComponents);
            int entityCount = reader.ReadCount("shape entity count", totalEntities);
            if (componentCount == 0 || entityCount == 0) throw new PersistenceValidationException("Saved shapes must be occupied and nonempty.");
            var ids = new uint[componentCount];
            for (int c = 0; c < ids.Length; c++)
            {
                ids[c] = reader.ReadUInt32("shape component ID");
                if (ids[c] == 0 || (c != 0 && ids[c] <= ids[c - 1]))
                    throw new PersistenceValidationException("Saved shape membership is not canonical.");
            }
            if (previous is not null && CompareLex(previous, ids) >= 0)
                throw new PersistenceValidationException("Saved shapes are not in canonical lexicographic order.");
            shapes[i] = new ResolvedShape { SavedIds = ids, EntityCount = entityCount };
            previous = ids;
            population = checked(population + entityCount);
        }
        if (population != totalEntities) throw new PersistenceValidationException("Saved shape populations do not sum to the entity count.");
        reader.RequireEnd();
        return shapes;
    }

    private static SavedEntity[] ParseEntities(byte[] bytes, SectionEntry entry, int expectedCount, int shapeCount, ulong highWater)
    {
        var reader = new CheckedSectionReader(bytes, entry);
        if (reader.ReadCount("entity count", expectedCount) != expectedCount)
            throw new PersistenceValidationException("Entity table count disagrees with the snapshot header.");
        var entities = new SavedEntity[expectedCount];
        ulong previous = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            ulong id = reader.ReadUInt64("persistent entity ID");
            int shape = reader.ReadNonNegative("entity shape index", shapeCount - 1);
            ulong prefab = reader.ReadUInt64("prefab provenance hash");
            if (id == 0 || id <= previous || id > highWater)
                throw new PersistenceValidationException("Persistent entity IDs are not canonical or exceed allocator high-water.");
            entities[i] = new SavedEntity(id, shape, prefab);
            previous = id;
        }
        reader.RequireEnd();
        return entities;
    }

    private static uint[][] ParseRows(byte[] bytes, SectionEntry entry, ResolvedShape[] shapes, int entityCount)
    {
        var reader = new CheckedSectionReader(bytes, entry);
        if (reader.ReadCount("shape row-list count", shapes.Length) != shapes.Length)
            throw new PersistenceValidationException("Shape-row directory count mismatch.");
        var rows = new uint[shapes.Length][];
        var seen = new byte[entityCount];
        for (int shape = 0; shape < shapes.Length; shape++)
        {
            int count = reader.ReadCount("shape row count", entityCount);
            if (count != shapes[shape].EntityCount) throw new PersistenceValidationException("Shape row count differs from shape population.");
            rows[shape] = new uint[count];
            uint previous = 0;
            for (int row = 0; row < count; row++)
            {
                uint ordinal = reader.ReadUInt32("save ordinal");
                if (ordinal >= unchecked((uint)entityCount) || seen[ordinal] != 0 || (row != 0 && ordinal <= previous))
                    throw new PersistenceValidationException("Shape save ordinals are duplicated, unordered, or out of range.");
                seen[ordinal] = 1; rows[shape][row] = ordinal; previous = ordinal;
            }
        }
        if (seen.Any(static value => value == 0)) throw new PersistenceValidationException("Shape row lists do not cover every entity exactly once.");
        reader.RequireEnd();
        return rows;
    }

    private static ResolvedColumn[] ParseColumns(byte[] bytes, SectionEntry[] entries, ResolvedShape[] shapes,
        SavedSchema[] schemas, PersistenceLimits limits)
    {
        Dictionary<uint, SavedSchema> schema = schemas.ToDictionary(static item => item.PersistenceId);
        SectionEntry[] selected = entries.Where(static entry => entry.Kind == SnapshotSectionKind.Column)
            .OrderBy(static entry => entry.Ordinal).ToArray();
        var result = new ResolvedColumn[selected.Length];
        long payloadTotal = 0;
        var keys = new HashSet<(int, uint)>();
        for (int i = 0; i < selected.Length; i++)
        {
            var reader = new CheckedSectionReader(bytes, selected[i]);
            int shape = reader.ReadNonNegative("column shape index", shapes.Length - 1);
            uint id = reader.ReadUInt32("column persistence ID");
            ushort version = reader.ReadUInt16("column version");
            _ = reader.ReadUInt16("column reserved");
            int width = reader.ReadNonNegative("column encoded width", int.MaxValue);
            int rows = reader.ReadCount("column row count", limits.MaximumEntities);
            if (!schema.TryGetValue(id, out SavedSchema? component) || component.Mode != ComponentPersistenceMode.Value ||
                component.Version != version || component.EncodedSize != width || rows != shapes[shape].EntityCount ||
                Array.BinarySearch(shapes[shape].SavedIds, id) < 0 || !keys.Add((shape, id)))
                throw new PersistenceValidationException("Component column header is inconsistent with schema/shape metadata.");
            int payloadBytes = CheckedProduct(width, rows, reader.Remaining, "component column payload");
            payloadTotal = checked(payloadTotal + payloadBytes);
            if (payloadTotal > limits.MaximumComponentPayloadBytes)
                throw new PersistenceValidationException("Component payload exceeds its configured bound.");
            result[i] = new ResolvedColumn { ShapeIndex = shape, SavedPersistenceId = id, SavedVersion = version,
                EncodedSize = width, RowCount = rows, Payload = reader.ReadBytes(payloadBytes, "component payload") };
            reader.RequireEnd();
        }
        foreach ((int shapeIndex, ResolvedShape shape) in shapes.Select(static (shape, index) => (index, shape)))
            foreach (uint id in shape.SavedIds)
                if (schema[id].Mode == ComponentPersistenceMode.Value && !keys.Contains((shapeIndex, id)))
                    throw new PersistenceValidationException("A value-persisted shape member has no column section.");
        return result;
    }

    private static ResolvedEnable[] ParseEnables(byte[] bytes, SectionEntry[] entries, ResolvedShape[] shapes, SavedSchema[] schemas)
    {
        Dictionary<uint, SavedSchema> schema = schemas.ToDictionary(static item => item.PersistenceId);
        SectionEntry[] selected = entries.Where(static entry => entry.Kind == SnapshotSectionKind.Enable)
            .OrderBy(static entry => entry.Ordinal).ToArray();
        var result = new ResolvedEnable[selected.Length];
        var keys = new HashSet<(int, uint)>();
        for (int i = 0; i < selected.Length; i++)
        {
            var reader = new CheckedSectionReader(bytes, selected[i]);
            int shape = reader.ReadNonNegative("enable shape index", shapes.Length - 1);
            uint id = reader.ReadUInt32("enable persistence ID");
            int rows = reader.ReadCount("enable row count", shapes[shape].EntityCount);
            int byteCount = reader.ReadNonNegative("enable byte count", reader.Remaining);
            if (!schema.TryGetValue(id, out SavedSchema? component) ||
                (component.Flags & (ComponentFlags.Enableable | ComponentFlags.PersistEnableState)) !=
                    (ComponentFlags.Enableable | ComponentFlags.PersistEnableState) ||
                rows != shapes[shape].EntityCount || byteCount != ((rows + 7) >> 3) ||
                Array.BinarySearch(shapes[shape].SavedIds, id) < 0 || !keys.Add((shape, id)))
                throw new PersistenceValidationException("Enable-state section is inconsistent with schema/shape metadata.");
            byte[] bits = reader.ReadBytes(byteCount, "enable bitplane");
            if (rows % 8 != 0 && byteCount != 0 && (bits[^1] & ~((1 << (rows & 7)) - 1)) != 0)
                throw new PersistenceValidationException("Enable bitplane contains nonzero padding bits.");
            result[i] = new ResolvedEnable { ShapeIndex = shape, SavedPersistenceId = id, RowCount = rows, Bits = bits };
            reader.RequireEnd();
        }
        foreach ((int shapeIndex, ResolvedShape shape) in shapes.Select(static (shape, index) => (index, shape)))
            foreach (uint id in shape.SavedIds)
                if ((schema[id].Flags & (ComponentFlags.Enableable | ComponentFlags.PersistEnableState)) ==
                    (ComponentFlags.Enableable | ComponentFlags.PersistEnableState) && !keys.Contains((shapeIndex, id)))
                    throw new PersistenceValidationException("A durable enableable shape member has no enable bitplane.");
        return result;
    }

    private static SectionEntry RequireSingle(SectionEntry[] entries, SnapshotSectionKind kind)
    {
        SectionEntry[] matches = entries.Where(entry => entry.Kind == kind).ToArray();
        if (matches.Length != 1 || matches[0].Ordinal != 0)
            throw new PersistenceValidationException($"Snapshot requires exactly one {kind}:0 section.");
        return matches[0];
    }
    private static int ToBoundedInt(ulong value, int maximum, string field)
    {
        if (value > unchecked((ulong)maximum) || value > int.MaxValue) throw new PersistenceValidationException($"Invalid {field}.");
        return checked((int)value);
    }
    private static int CheckedProduct(int left, int right, int maximum, string field)
    {
        long value = (long)left * right;
        if (left < 0 || right < 0 || value > maximum || value > int.MaxValue) throw new PersistenceValidationException($"Invalid {field}.");
        return checked((int)value);
    }
    private static int Align8(int value) => checked((value + 7) & ~7);
    private static int CompareLex(uint[] left, uint[] right)
    {
        int count = Math.Min(left.Length, right.Length);
        for (int i = 0; i < count; i++) { int c = left[i].CompareTo(right[i]); if (c != 0) return c; }
        return left.Length.CompareTo(right.Length);
    }
}

internal readonly record struct SectionEntry(SnapshotSectionKind Kind, int Ordinal, int Offset, int Length);

internal ref struct CheckedSectionReader
{
    private readonly ReadOnlySpan<byte> _bytes;
    private int _offset;
    internal CheckedSectionReader(byte[] container, SectionEntry entry) { _bytes = container.AsSpan(entry.Offset, entry.Length); _offset = 0; }
    internal int Remaining => _bytes.Length - _offset;
    internal bool AtEnd => _offset == _bytes.Length;
    internal byte ReadByte(string field) { Require(1, field); return _bytes[_offset++]; }
    internal ushort ReadUInt16(string field) { Require(2, field); ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes[_offset..]); _offset += 2; return value; }
    internal uint ReadUInt32(string field) { Require(4, field); uint value = BinaryPrimitives.ReadUInt32LittleEndian(_bytes[_offset..]); _offset += 4; return value; }
    internal int ReadInt32(string field) { Require(4, field); int value = BinaryPrimitives.ReadInt32LittleEndian(_bytes[_offset..]); _offset += 4; return value; }
    internal ulong ReadUInt64(string field) { Require(8, field); ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_bytes[_offset..]); _offset += 8; return value; }
    internal int ReadCount(string field, int maximum) { int value = ReadInt32(field); if (value < 0 || value > maximum) throw new PersistenceValidationException($"{field} is outside configured bounds."); return value; }
    internal int ReadNonNegative(string field, int maximum) => ReadCount(field, maximum);
    internal byte[] ReadBytes(int count, string field) { Require(count, field); byte[] value = _bytes.Slice(_offset, count).ToArray(); _offset += count; return value; }
    internal string ReadUtf8(string field, int maximumBytes)
    {
        int count = ReadUInt16(field + " length"); if (count > maximumBytes) throw new PersistenceValidationException($"{field} exceeds its bound.");
        Require(count, field); try { string value = SnapshotParser.StrictUtf8.GetString(_bytes.Slice(_offset, count)); _offset += count; return value; }
        catch (DecoderFallbackException ex) { throw new PersistenceValidationException($"{field} is not valid UTF-8: {ex.Message}"); }
    }
    internal void RequireEnd() { if (!AtEnd) throw new PersistenceValidationException("Snapshot section contains trailing bytes."); }
    private void Require(int count, string field)
    {
        if (count < 0 || count > Remaining) throw new PersistenceValidationException($"Snapshot ended while reading {field}.");
    }
}
