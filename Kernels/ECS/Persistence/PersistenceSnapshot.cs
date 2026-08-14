using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace UltraSim.ECS;

/// <summary>One capture's nonzero sequence, timestamp, and bounded persistent-system payload policy.</summary>
public readonly struct PersistenceCaptureOptions
{
    public PersistenceCaptureOptions(ulong snapshotSequence, long captureUnixMilliseconds = 0,
        int maximumSystemStateBytes = 64 * 1024 * 1024)
    {
        if (snapshotSequence == 0) throw new ArgumentOutOfRangeException(nameof(snapshotSequence));
        if (maximumSystemStateBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumSystemStateBytes));
        SnapshotSequence = snapshotSequence;
        CaptureUnixMilliseconds = captureUnixMilliseconds;
        MaximumSystemStateBytes = maximumSystemStateBytes;
    }
    public ulong SnapshotSequence { get; }
    public long CaptureUnixMilliseconds { get; }
    public int MaximumSystemStateBytes { get; }
}

/// <summary>Owned immutable canonical snapshot bytes plus identity, count, and digest metadata.</summary>
public sealed class PersistenceSnapshotArtifact
{
    private readonly byte[] _bytes;
    internal PersistenceSnapshotArtifact(byte[] bytes, PersistentWorldId worldId, ulong sequence,
        int entityCount, int shapeCount, byte[] digest)
    {
        _bytes = bytes;
        WorldId = worldId;
        SnapshotSequence = sequence;
        EntityCount = entityCount;
        ShapeCount = shapeCount;
        Digest = Convert.ToHexString(digest);
    }
    public ReadOnlyMemory<byte> Bytes => _bytes;
    public PersistentWorldId WorldId { get; }
    public ulong SnapshotSequence { get; }
    public int EntityCount { get; }
    public int ShapeCount { get; }
    public int ByteCount => _bytes.Length;
    public string Digest { get; }
    public void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(_bytes);
    }
}

/// <summary>Owned canonical raw world state captured at one authoritative epoch.</summary>
/// <remarks>Complete encoding/checksums outside the world pause with <see cref="EcsPersistence.CompleteCapture"/>.</remarks>
public sealed class PersistenceWorldCapture : IDisposable
{
    private object? _model;
    internal PersistenceWorldCapture(object model, PersistenceCatalog catalog,
        PersistenceCaptureOptions options, long collectTicks)
    { _model = model; Catalog = catalog; Options = options; CollectTicks = collectTicks; }
    internal PersistenceCatalog Catalog { get; }
    internal PersistenceCaptureOptions Options { get; }
    internal long CollectTicks { get; }
    internal object TakeModel()
    {
        object model = _model ?? throw new InvalidOperationException("The world capture was already consumed or disposed.");
        _model = null;
        return model;
    }
    public void Dispose() => _model = null;
}

/// <summary>Phase timing and logical-size report for one completed world capture.</summary>
public readonly struct PersistenceCaptureReport
{
    internal PersistenceCaptureReport(long collectTicks, long encodeTicks, long checksumTicks,
        int entities, int shapes, int columns, int bytes)
    {
        CollectTicks = collectTicks;
        EncodeTicks = encodeTicks;
        ChecksumTicks = checksumTicks;
        EntityCount = entities;
        ShapeCount = shapes;
        ColumnCount = columns;
        ByteCount = bytes;
    }
    public long CollectTicks { get; }
    public long EncodeTicks { get; }
    public long ChecksumTicks { get; }
    public int EntityCount { get; }
    public int ShapeCount { get; }
    public int ColumnCount { get; }
    public int ByteCount { get; }
    public TimeSpan CollectTime => Stopwatch.GetElapsedTime(0, CollectTicks);
    public TimeSpan EncodeTime => Stopwatch.GetElapsedTime(0, EncodeTicks);
    public TimeSpan ChecksumTime => Stopwatch.GetElapsedTime(0, ChecksumTicks);
}

/// <summary>Owner-bound whole-world capture API with separable authority collection and detached encoding.</summary>
public static class EcsPersistence
{
    public static PersistenceSnapshotArtifact Capture(
        EcsWorld world,
        PersistenceCatalog catalog,
        PersistenceCaptureOptions options,
        out PersistenceCaptureReport report)
    {
        using PersistenceWorldCapture capture = BeginCapture(world, catalog, options);
        return CompleteCapture(capture, out report);
    }

    public static PersistenceWorldCapture BeginCapture(
        EcsWorld world,
        PersistenceCatalog catalog,
        PersistenceCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);
        if (options.SnapshotSequence == 0)
            throw new ArgumentException("Persistence capture options must have a nonzero snapshot sequence.", nameof(options));
        world.EnsurePersistenceOwnerRuntime();
        if (world.PersistenceStructuralCommands.Count != 0)
            throw new InvalidOperationException("Full-world capture requires an empty structural command arena.");
        if (catalog.Schema.Id != world.Schema.Id)
            throw new ArgumentException("The persistence catalog belongs to a different component schema.", nameof(catalog));
        if (!world.PersistenceCore.IsPersistenceEnabled)
            throw new InvalidOperationException("The world was not configured with persistence identity storage.");

        long collectStart = Stopwatch.GetTimestamp();
        PersistentSystemStateRecord[] systems = world.CapturePersistenceSystemState(options.MaximumSystemStateBytes);
        CaptureModel model = Collect(world.PersistenceCore, catalog, world.PersistenceSchedulerTick, systems);
        long collectTicks = Stopwatch.GetTimestamp() - collectStart;
        return new PersistenceWorldCapture(model, catalog, options, collectTicks);
    }

    /// <summary>Backend-neutral cold capture seam used to prove storage-backend parity.</summary>
    internal static PersistenceSnapshotArtifact CaptureBackend<TArena>(
        EcsWorld<TArena> world,
        PersistenceCatalog catalog,
        PersistenceCaptureOptions options,
        out PersistenceCaptureReport report)
        where TArena : class, IChunkArenaBackend
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);
        if (options.SnapshotSequence == 0)
            throw new ArgumentException("Persistence capture options must have a nonzero snapshot sequence.", nameof(options));
        if (!world.IsRunning)
            throw new InvalidOperationException("Backend persistence capture requires a running world.");
        if (catalog.Schema.Id != world.Schema.Id)
            throw new ArgumentException("The persistence catalog belongs to a different component schema.", nameof(catalog));
        if (!world.IsPersistenceEnabled)
            throw new InvalidOperationException("The world was not configured with persistence identity storage.");

        long collectStart = Stopwatch.GetTimestamp();
        CaptureModel model = Collect(world, catalog, schedulerTick: 0, []);
        long collectTicks = Stopwatch.GetTimestamp() - collectStart;
        using var capture = new PersistenceWorldCapture(model, catalog, options, collectTicks);
        return CompleteCapture(capture, out report);
    }

    public static PersistenceSnapshotArtifact CompleteCapture(
        PersistenceWorldCapture capture,
        out PersistenceCaptureReport report)
    {
        ArgumentNullException.ThrowIfNull(capture);
        CaptureModel model = (CaptureModel)capture.TakeModel();
        PersistenceCatalog catalog = capture.Catalog;
        PersistenceCaptureOptions options = capture.Options;
        long encodeStart = Stopwatch.GetTimestamp();
        var builder = new SnapshotContainerBuilder(
            model.WorldId,
            model.AllocatorHighWater,
            options,
            catalog.Schema.Id,
            model.Entities.Length,
            model.Shapes.Length);
        int columns = EncodeSections(catalog, model, builder);
        long encodeTicks = Stopwatch.GetTimestamp() - encodeStart;
        long checksumStart = Stopwatch.GetTimestamp();
        byte[] bytes = builder.Build(out byte[] digest);
        long checksumTicks = Stopwatch.GetTimestamp() - checksumStart;
        report = new PersistenceCaptureReport(
            capture.CollectTicks, encodeTicks, checksumTicks, model.Entities.Length, model.Shapes.Length, columns, bytes.Length);
        return new PersistenceSnapshotArtifact(bytes, model.WorldId, options.SnapshotSequence,
            model.Entities.Length, model.Shapes.Length, digest);
    }

    private static CaptureModel Collect<TArena>(
        EcsWorld<TArena> core,
        PersistenceCatalog catalog,
        ulong schedulerTick,
        PersistentSystemStateRecord[] systems)
        where TArena : class, IChunkArenaBackend
    {
        var runtimeShapes = new List<CapturedShape>();
        var shapeByArchetype = new CapturedShape?[core.ArchetypeCount + 1];
        for (int index = 0; index < core.ArchetypeCount; index++)
        {
            ArchetypeStorage<TArena> storage = core.GetArchetypeStorage(new ArchetypeId(unchecked((uint)index + 1)));
            int count = storage.Store.Count;
            if (count == 0) continue;
            var members = new CapturedMember[storage.Signature.Length];
            for (int memberIndex = 0; memberIndex < members.Length; memberIndex++)
            {
                ComponentId componentId = storage.Signature[memberIndex];
                ref readonly ComponentMetadata metadata = ref catalog.Schema.GetMetadata(componentId);
                if (metadata.PersistenceId == 0 || metadata.PersistenceMode == ComponentPersistenceMode.None)
                {
                    throw new PersistenceCompatibilityException(
                        $"Occupied shape contains component '{catalog.Schema.GetCanonicalKey(componentId)}' without a stable persistence identity.");
                }
                members[memberIndex] = new CapturedMember(componentId, metadata.PersistenceId);
            }
            Array.Sort(members, CapturedMemberComparer.Instance);
            var shape = new CapturedShape(storage.Id, count, members);
            runtimeShapes.Add(shape);
            shapeByArchetype[storage.Id.Value] = shape;
        }
        runtimeShapes.Sort(CapturedShapeComparer.Instance);
        for (int index = 0; index < runtimeShapes.Count; index++) runtimeShapes[index].CanonicalIndex = index;

        var entities = new CapturedEntity[core.AliveCount];
        int written = 0;
        for (int slot = 0; slot < core.EntityHighWatermark; slot++)
        {
            if (!core.TryGetEntity(unchecked((uint)slot), out Entity entity)) continue;
            if (!core.TryGetLocation(entity, out EntityLocation location))
                throw new PersistenceValidationException("A live entity has no storage location during capture.");
            CapturedShape shape = shapeByArchetype[location.ArchetypeId.Value] ??
                throw new PersistenceValidationException("A live entity resolved to an uncollected shape.");
            PersistentEntityId id = core.PersistenceIdentities.GetOrAssign(entity);
            entities[written++] = new CapturedEntity(
                id.Value, entity, location, shape, core.PersistenceIdentities.GetPrefabKeyHash(entity));
        }
        if (written != entities.Length) throw new PersistenceValidationException("Alive entity count changed during capture.");
        Array.Sort(entities, CapturedEntityComparer.Instance);
        var ordinalByIndex = new int[core.EntityCapacity.MaximumEntities];
        Array.Fill(ordinalByIndex, -1);
        var shapeOffsets = new int[runtimeShapes.Count + 1];
        for (int index = 0; index < entities.Length; index++)
        {
            if (index != 0 && entities[index - 1].PersistentId == entities[index].PersistentId)
                throw new PersistenceValidationException("Duplicate persistent entity identity during capture.");
            ordinalByIndex[entities[index].Entity.Index] = index;
            shapeOffsets[entities[index].Shape.CanonicalIndex + 1]++;
        }
        var generationByIndex = new uint[core.EntityCapacity.MaximumEntities];
        foreach (CapturedEntity entity in entities) generationByIndex[entity.Entity.Index] = entity.Entity.Generation;
        for (int i = 1; i < shapeOffsets.Length; i++) shapeOffsets[i] += shapeOffsets[i - 1];
        var shapeOrdinals = new uint[entities.Length];
        int[] cursors = (int[])shapeOffsets.Clone();
        for (int ordinal = 0; ordinal < entities.Length; ordinal++)
        {
            int shape = entities[ordinal].Shape.CanonicalIndex;
            shapeOrdinals[cursors[shape]++] = checked((uint)ordinal);
        }
        int maximumRuntimeSize = 0;
        foreach (ComponentMetadata metadata in catalog.Schema.Components) maximumRuntimeSize = Math.Max(maximumRuntimeSize, metadata.Size);
        byte[] scratch = new byte[maximumRuntimeSize];
        for (int shapeIndex = 0; shapeIndex < runtimeShapes.Count; shapeIndex++)
        {
            CapturedShape shape = runtimeShapes[shapeIndex];
            ArchetypeChunkStore<TArena> store = core.GetArchetypeStorage(shape.ArchetypeId).Store;
            int start = shapeOffsets[shapeIndex], count = shapeOffsets[shapeIndex + 1] - start;
            foreach (CapturedMember member in shape.Members)
            {
                ref readonly ComponentMetadata metadata = ref catalog.Schema.GetMetadata(member.ComponentId);
                int columnIndex = store.GetColumnIndex(member.ComponentId);
                if (metadata.PersistenceMode == ComponentPersistenceMode.Value)
                {
                    member.RuntimeBytes = new byte[checked(count * metadata.Size)];
                    Span<byte> value = scratch.AsSpan(0, metadata.Size);
                    for (int row = 0; row < count; row++)
                    {
                        uint ordinal = shapeOrdinals[start + row];
                        store.CopyBoundComponentBytes(entities[ordinal].Location.ChunkRow, columnIndex, value);
                        value.CopyTo(member.RuntimeBytes.AsSpan(row * metadata.Size, metadata.Size));
                    }
                }
                if (metadata.IsEnableable && (metadata.Flags & ComponentFlags.PersistEnableState) != 0)
                {
                    member.EnableBits = new byte[(count + 7) >> 3];
                    for (int row = 0; row < count; row++)
                    {
                        uint ordinal = shapeOrdinals[start + row];
                        if (store.IsEnabled(entities[ordinal].Location.ChunkRow, columnIndex))
                            member.EnableBits[row >> 3] |= checked((byte)(1 << (row & 7)));
                    }
                }
            }
        }
        var prefabKeys = new SortedDictionary<ulong, string>();
        foreach (CapturedEntity entity in entities)
        {
            if (entity.PrefabKeyHash == 0) continue;
            if (!core.PersistenceIdentities.TryGetPrefabKey(entity.PrefabKeyHash, out string key))
                throw new PersistenceValidationException("Prefab provenance hash has no stable key text.");
            if (prefabKeys.TryGetValue(entity.PrefabKeyHash, out string? existing))
            {
                if (!StringComparer.Ordinal.Equals(existing, key))
                    throw new PersistenceValidationException("Prefab stable-key hash collision during capture.");
            }
            else
            {
                prefabKeys.Add(entity.PrefabKeyHash, key);
            }
        }
        return new CaptureModel(
            core.Id,
            core.PersistenceIdentities.WorldId,
            core.PersistenceIdentities.AllocatorHighWater,
            runtimeShapes.ToArray(), entities, ordinalByIndex, generationByIndex, shapeOffsets, shapeOrdinals,
            prefabKeys, schedulerTick, systems);
    }

    private static int EncodeSections(PersistenceCatalog catalog, CaptureModel model, SnapshotContainerBuilder builder)
    {
        var used = new SortedDictionary<uint, CapturedMember>();
        foreach (CapturedShape shape in model.Shapes)
            foreach (CapturedMember member in shape.Members) used.TryAdd(member.PersistenceId, member);

        var schema = new LittleEndianBuffer();
        schema.WriteInt32(used.Count);
        foreach (KeyValuePair<uint, CapturedMember> pair in used)
        {
            ref readonly ComponentMetadata metadata = ref catalog.Schema.GetMetadata(pair.Value.ComponentId);
            IBoundPersistenceCodec? codec = metadata.PersistenceMode == ComponentPersistenceMode.Value
                ? catalog.GetCurrent(pair.Value.ComponentId)
                : null;
            schema.WriteUInt32(metadata.PersistenceId);
            schema.WriteUInt16(metadata.Version);
            schema.WriteByte((byte)metadata.PersistenceMode);
            schema.WriteByte((byte)metadata.Flags);
            schema.WriteInt32(codec?.EncodedSize ?? 0);
            schema.WriteUInt64(codec?.Fingerprint.Low ?? 0);
            schema.WriteUInt64(codec?.Fingerprint.High ?? 0);
            schema.WriteUInt64(catalog.Schema.GetStableKeyHash(pair.Value.ComponentId));
            schema.WriteUtf8(catalog.Schema.GetCanonicalKey(pair.Value.ComponentId));
        }
        builder.Add(SnapshotSectionKind.Schema, 0, schema.ToArray());

        var shapes = new LittleEndianBuffer();
        shapes.WriteInt32(model.Shapes.Length);
        foreach (CapturedShape shape in model.Shapes)
        {
            shapes.WriteInt32(shape.Members.Length);
            shapes.WriteInt32(shape.EntityCount);
            foreach (CapturedMember member in shape.Members) shapes.WriteUInt32(member.PersistenceId);
        }
        builder.Add(SnapshotSectionKind.Shapes, 0, shapes.ToArray());

        var prefabs = new LittleEndianBuffer();
        prefabs.WriteInt32(model.PrefabKeys.Count);
        foreach (KeyValuePair<ulong, string> prefab in model.PrefabKeys)
        {
            prefabs.WriteUInt64(prefab.Key);
            prefabs.WriteUtf8(prefab.Value);
        }
        builder.Add(SnapshotSectionKind.Prefabs, 0, prefabs.ToArray());

        var entities = new LittleEndianBuffer();
        entities.WriteInt32(model.Entities.Length);
        foreach (CapturedEntity entity in model.Entities)
        {
            entities.WriteUInt64(entity.PersistentId);
            entities.WriteInt32(entity.Shape.CanonicalIndex);
            entities.WriteUInt64(entity.PrefabKeyHash);
        }
        builder.Add(SnapshotSectionKind.Entities, 0, entities.ToArray());

        var rows = new LittleEndianBuffer();
        rows.WriteInt32(model.Shapes.Length);
        for (int shape = 0; shape < model.Shapes.Length; shape++)
        {
            int start = model.ShapeOffsets[shape], count = model.ShapeOffsets[shape + 1] - start;
            rows.WriteInt32(count);
            for (int i = 0; i < count; i++) rows.WriteUInt32(model.ShapeOrdinals[start + i]);
        }
        builder.Add(SnapshotSectionKind.ShapeRows, 0, rows.ToArray());

        var writeContext = new PersistenceWriteContext(model.RuntimeWorldId, model.OrdinalByEntityIndex, model.GenerationByEntityIndex);
        int columnOrdinal = 0;
        for (int shapeIndex = 0; shapeIndex < model.Shapes.Length; shapeIndex++)
        {
            CapturedShape shape = model.Shapes[shapeIndex];
            int start = model.ShapeOffsets[shapeIndex], count = model.ShapeOffsets[shapeIndex + 1] - start;
            foreach (CapturedMember member in shape.Members)
            {
                ref readonly ComponentMetadata metadata = ref catalog.Schema.GetMetadata(member.ComponentId);
                if (metadata.PersistenceMode == ComponentPersistenceMode.Value)
                {
                    IBoundPersistenceCodec codec = catalog.GetCurrent(member.ComponentId);
                    var column = new LittleEndianBuffer(checked(20 + count * codec.EncodedSize));
                    column.WriteInt32(shapeIndex);
                    column.WriteUInt32(member.PersistenceId);
                    column.WriteUInt16(metadata.Version);
                    column.WriteUInt16(0);
                    column.WriteInt32(codec.EncodedSize);
                    column.WriteInt32(count);
                    codec.EncodeBatch(member.RuntimeBytes, count,
                        column.Reserve(checked(count * codec.EncodedSize)), writeContext);
                    builder.Add(SnapshotSectionKind.Column, columnOrdinal++, column.ToArray());
                }
                if (metadata.IsEnableable && (metadata.Flags & ComponentFlags.PersistEnableState) != 0)
                {
                    int byteCount = member.EnableBits.Length;
                    var enable = new LittleEndianBuffer(16 + byteCount);
                    enable.WriteInt32(shapeIndex);
                    enable.WriteUInt32(member.PersistenceId);
                    enable.WriteInt32(count);
                    enable.WriteInt32(byteCount);
                    member.EnableBits.CopyTo(enable.Reserve(byteCount));
                    builder.Add(SnapshotSectionKind.Enable, columnOrdinal++, enable.ToArray());
                }
            }
        }

        PersistentSystemStateRecord[] systemStates = model.SystemStates;
        var systems = new LittleEndianBuffer();
        systems.WriteUInt64(model.SchedulerTick);
        systems.WriteInt32(systemStates.Length);
        foreach (PersistentSystemStateRecord state in systemStates)
        {
            systems.WriteUInt64(state.PersistenceId);
            systems.WriteUInt16(state.Version);
            systems.WriteUInt16(state.Required ? (ushort)1 : (ushort)0);
            systems.WriteInt32(state.Bytes.Length);
            state.Bytes.CopyTo(systems.Reserve(state.Bytes.Length));
        }
        builder.Add(SnapshotSectionKind.Systems, 0, systems.ToArray());
        return columnOrdinal;
    }

    private sealed class CapturedShape
    {
        internal CapturedShape(ArchetypeId archetypeId, int entityCount, CapturedMember[] members)
        { ArchetypeId = archetypeId; EntityCount = entityCount; Members = members; }
        internal ArchetypeId ArchetypeId { get; }
        internal int EntityCount { get; }
        internal CapturedMember[] Members { get; }
        internal int CanonicalIndex { get; set; }
    }
    private sealed class CapturedMember
    {
        internal CapturedMember(ComponentId componentId, uint persistenceId) { ComponentId = componentId; PersistenceId = persistenceId; }
        internal ComponentId ComponentId { get; }
        internal uint PersistenceId { get; }
        internal byte[] RuntimeBytes = [];
        internal byte[] EnableBits = [];
    }
    private readonly record struct CapturedEntity(ulong PersistentId, Entity Entity, EntityLocation Location,
        CapturedShape Shape, ulong PrefabKeyHash);
    private sealed record CaptureModel(WorldId RuntimeWorldId, PersistentWorldId WorldId, ulong AllocatorHighWater, CapturedShape[] Shapes,
        CapturedEntity[] Entities, int[] OrdinalByEntityIndex, uint[] GenerationByEntityIndex,
        int[] ShapeOffsets, uint[] ShapeOrdinals, SortedDictionary<ulong, string> PrefabKeys,
        ulong SchedulerTick, PersistentSystemStateRecord[] SystemStates);

    private sealed class CapturedMemberComparer : IComparer<CapturedMember>
    {
        internal static readonly CapturedMemberComparer Instance = new();
        public int Compare(CapturedMember? x, CapturedMember? y) => (x?.PersistenceId ?? 0).CompareTo(y?.PersistenceId ?? 0);
    }
    private sealed class CapturedShapeComparer : IComparer<CapturedShape>
    {
        internal static readonly CapturedShapeComparer Instance = new();
        public int Compare(CapturedShape? x, CapturedShape? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1; if (y is null) return 1;
            int count = Math.Min(x.Members.Length, y.Members.Length);
            for (int i = 0; i < count; i++)
            {
                int comparison = x.Members[i].PersistenceId.CompareTo(y.Members[i].PersistenceId);
                if (comparison != 0) return comparison;
            }
            return x.Members.Length.CompareTo(y.Members.Length);
        }
    }
    private sealed class CapturedEntityComparer : IComparer<CapturedEntity>
    {
        internal static readonly CapturedEntityComparer Instance = new();
        public int Compare(CapturedEntity x, CapturedEntity y) => x.PersistentId.CompareTo(y.PersistentId);
    }
}

internal enum SnapshotSectionKind : uint { Schema = 1, Shapes = 2, Entities = 3, ShapeRows = 4, Column = 5, Enable = 6, Systems = 7, Prefabs = 8 }

internal sealed class SnapshotContainerBuilder
{
    internal const int HeaderSize = 128;
    internal const int DirectoryEntrySize = 32;
    internal const ulong Magic = 0x3433534353455355UL; // USECSC34 little endian
    private readonly List<(SnapshotSectionKind Kind, int Ordinal, byte[] Bytes)> _sections = new();
    private readonly PersistentWorldId _world;
    private readonly ulong _highWater;
    private readonly PersistenceCaptureOptions _options;
    private readonly SchemaId _schema;
    private readonly int _entities;
    private readonly int _shapes;
    internal SnapshotContainerBuilder(PersistentWorldId world, ulong highWater, PersistenceCaptureOptions options,
        SchemaId schema, int entities, int shapes)
    { _world = world; _highWater = highWater; _options = options; _schema = schema; _entities = entities; _shapes = shapes; }
    internal void Add(SnapshotSectionKind kind, int ordinal, byte[] bytes) => _sections.Add((kind, ordinal, bytes));
    internal byte[] Build(out byte[] digest)
    {
        long payloadBytes = 0;
        foreach (var section in _sections) payloadBytes = checked(payloadBytes + Align8(section.Bytes.Length));
        long directoryOffset = checked(HeaderSize + payloadBytes);
        long digestOffset = checked(directoryOffset + (long)_sections.Count * DirectoryEntrySize);
        int total = checked((int)(digestOffset + 32));
        byte[] output = new byte[total];
        Span<byte> header = output.AsSpan(0, HeaderSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], HeaderSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header[16..], checked((ulong)total));
        BinaryPrimitives.WriteUInt64LittleEndian(header[24..], _options.SnapshotSequence);
        BinaryPrimitives.WriteUInt64LittleEndian(header[32..], _world.Value);
        BinaryPrimitives.WriteUInt64LittleEndian(header[40..], _highWater);
        BinaryPrimitives.WriteInt64LittleEndian(header[48..], _options.CaptureUnixMilliseconds);
        BinaryPrimitives.WriteInt32LittleEndian(header[56..], _entities);
        BinaryPrimitives.WriteInt32LittleEndian(header[60..], _shapes);
        BinaryPrimitives.WriteInt32LittleEndian(header[64..], _sections.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(header[72..], checked((ulong)directoryOffset));
        BinaryPrimitives.WriteUInt64LittleEndian(header[80..], checked((ulong)digestOffset));
        BinaryPrimitives.WriteUInt64LittleEndian(header[88..], _schema.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(header[96..], _schema.High);
        int payloadOffset = HeaderSize;
        int directory = checked((int)directoryOffset);
        for (int index = 0; index < _sections.Count; index++)
        {
            var section = _sections[index];
            section.Bytes.CopyTo(output, payloadOffset);
            Span<byte> entry = output.AsSpan(directory + index * DirectoryEntrySize, DirectoryEntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)section.Kind);
            BinaryPrimitives.WriteInt32LittleEndian(entry[4..], section.Ordinal);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], checked((ulong)payloadOffset));
            BinaryPrimitives.WriteUInt64LittleEndian(entry[16..], checked((ulong)section.Bytes.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[24..], PersistenceCrc32.Compute(section.Bytes));
            payloadOffset += Align8(section.Bytes.Length);
        }
        digest = SHA256.HashData(output.AsSpan(0, checked((int)digestOffset)));
        digest.CopyTo(output, checked((int)digestOffset));
        return output;
    }
    private static int Align8(int value) => checked((value + 7) & ~7);
}

internal sealed class LittleEndianBuffer
{
    private readonly ArrayBufferWriter<byte> _writer;
    internal LittleEndianBuffer(int capacity = 256) => _writer = new ArrayBufferWriter<byte>(Math.Max(1, capacity));
    internal Span<byte> Reserve(int count) { Span<byte> span = _writer.GetSpan(count)[..count]; span.Clear(); _writer.Advance(count); return span; }
    internal void WriteByte(byte value) => Reserve(1)[0] = value;
    internal void WriteUInt16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);
    internal void WriteUInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
    internal void WriteInt32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
    internal void WriteUInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);
    internal void WriteUtf8(string value)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        if (count > ushort.MaxValue) throw new PersistenceValidationException("A diagnostic key is too long.");
        WriteUInt16(checked((ushort)count));
        Encoding.UTF8.GetBytes(value, Reserve(count));
    }
    internal byte[] ToArray() => _writer.WrittenSpan.ToArray();
}

internal static class PersistenceCrc32
{
    private static readonly uint[] Table = CreateTable();
    internal static uint Compute(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xffffffffU;
        foreach (byte value in bytes) crc = (crc >> 8) ^ Table[(crc ^ value) & 0xff];
        return ~crc;
    }
    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ (0xedb88320U & unchecked((uint)-(int)(value & 1)));
            table[index] = value;
        }
        return table;
    }
}
