using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace UltraSim.ECS;

public readonly struct PersistenceLimits
{
    public PersistenceLimits(long maximumArtifactBytes, int maximumEntities, int maximumShapes,
        int maximumComponents, int maximumSections, long maximumComponentPayloadBytes, long maximumTemporaryBytes)
    {
        if (maximumArtifactBytes <= 0 || maximumArtifactBytes > Array.MaxLength || maximumEntities < 0 || maximumShapes <= 0 || maximumComponents <= 0 ||
            maximumSections <= 0 || maximumComponentPayloadBytes < 0 || maximumTemporaryBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumArtifactBytes), "Persistence limits must be positive and bounded.");
        if (maximumComponentPayloadBytes > maximumArtifactBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumComponentPayloadBytes), "Component payload cannot exceed the array-backed artifact bound.");
        MaximumArtifactBytes = maximumArtifactBytes;
        MaximumEntities = maximumEntities;
        MaximumShapes = maximumShapes;
        MaximumComponents = maximumComponents;
        MaximumSections = maximumSections;
        MaximumComponentPayloadBytes = maximumComponentPayloadBytes;
        MaximumTemporaryBytes = maximumTemporaryBytes;
    }
    public long MaximumArtifactBytes { get; }
    public int MaximumEntities { get; }
    public int MaximumShapes { get; }
    public int MaximumComponents { get; }
    public int MaximumSections { get; }
    public long MaximumComponentPayloadBytes { get; }
    public long MaximumTemporaryBytes { get; }
    public static PersistenceLimits ServerDefault => new(1_500_000_000, 20_000_000, 65_536,
        ComponentSchema.MaximumComponentCount, 1_000_000, 1_250_000_000, 2_000_000_000);
}

public readonly struct PersistenceRestoreOptions
{
    public PersistenceRestoreOptions(PersistenceLimits limits, ChunkLayoutOptions chunkLayout,
        int maximumDiagnostics = 256)
    {
        if (!chunkLayout.IsValid) throw new ArgumentException("Chunk layout is invalid.", nameof(chunkLayout));
        if (maximumDiagnostics is < 0 or > 4096) throw new ArgumentOutOfRangeException(nameof(maximumDiagnostics));
        Limits = limits;
        ChunkLayout = chunkLayout;
        MaximumDiagnostics = maximumDiagnostics;
    }
    public PersistenceLimits Limits { get; }
    public ChunkLayoutOptions ChunkLayout { get; }
    public int MaximumDiagnostics { get; }
}

public sealed class PersistenceRestoreShapeRequirement
{
    internal PersistenceRestoreShapeRequirement(ComponentId[] components, int entityCount, int maximumChunks)
    { Components = components; EntityCount = entityCount; MaximumChunks = maximumChunks; }
    public ReadOnlyMemory<ComponentId> Components { get; }
    public int EntityCount { get; }
    public int MaximumChunks { get; }
}

public sealed class PersistenceRestoreRequirements
{
    private readonly PersistenceRestoreShapeRequirement[] _shapes;
    internal PersistenceRestoreRequirements(ComponentSchema schema, PersistentWorldId worldId, ulong highWater,
        ulong snapshotSequence, int entityCount, PersistenceRestoreShapeRequirement[] shapes, int requiredArchetypes,
        long estimatedTemporaryBytes, long estimatedChunkBytes)
    {
        Schema = schema; PersistentWorldId = worldId; AllocatorHighWater = highWater; SnapshotSequence = snapshotSequence; EntityCount = entityCount;
        RequiredArchetypes = requiredArchetypes;
        _shapes = shapes; EstimatedTemporaryBytes = estimatedTemporaryBytes; EstimatedChunkPayloadBytes = estimatedChunkBytes;
    }
    public ComponentSchema Schema { get; }
    public PersistentWorldId PersistentWorldId { get; }
    public ulong AllocatorHighWater { get; }
    public ulong SnapshotSequence { get; }
    public int EntityCount { get; }
    public IReadOnlyList<PersistenceRestoreShapeRequirement> Shapes => _shapes;
    public int RequiredArchetypes { get; }
    public long EstimatedTemporaryBytes { get; }
    public long EstimatedChunkPayloadBytes { get; }
    public PersistenceIdentityOptions IdentityOptions => new(PersistentWorldId, AllocatorHighWater);
}

public readonly struct PersistenceRestoreReport
{
    internal PersistenceRestoreReport(long parse, long preflight, long construct, long allocate, long hydrate,
        long validate, int entities, int shapes, int columns, int migrated, int dropped)
    { ParseTicks = parse; PreflightTicks = preflight; ConstructTicks = construct; AllocateTicks = allocate;
      HydrateTicks = hydrate; ValidateTicks = validate; EntityCount = entities; ShapeCount = shapes;
      ColumnCount = columns; MigratedComponentCount = migrated; DroppedComponentCount = dropped; }
    public long ParseTicks { get; }
    public long PreflightTicks { get; }
    public long ConstructTicks { get; }
    public long AllocateTicks { get; }
    public long HydrateTicks { get; }
    public long ValidateTicks { get; }
    public int EntityCount { get; }
    public int ShapeCount { get; }
    public int ColumnCount { get; }
    public int MigratedComponentCount { get; }
    public int DroppedComponentCount { get; }
}

public sealed class PersistenceRestorePlan
{
    private readonly ParsedSnapshot _snapshot;
    private readonly PersistenceCatalog _catalog;
    private readonly PersistenceRestoreOptions _options;
    private readonly long _parseTicks;
    private readonly long _preflightTicks;
    private bool _consumed;
    internal PersistenceRestorePlan(ParsedSnapshot snapshot, PersistenceCatalog catalog, PersistenceRestoreOptions options,
        PersistenceRestoreRequirements requirements, long parseTicks, long preflightTicks)
    { _snapshot = snapshot; _catalog = catalog; _options = options; Requirements = requirements;
      _parseTicks = parseTicks; _preflightTicks = preflightTicks; }
    public PersistenceRestoreRequirements Requirements { get; }
    public IReadOnlyList<string> Diagnostics => _snapshot.Diagnostics;

    public EcsWorld Restore(Func<PersistenceRestoreRequirements, EcsWorld> stagingWorldFactory,
        out PersistenceRestoreReport report)
    {
        ArgumentNullException.ThrowIfNull(stagingWorldFactory);
        if (_consumed) throw new InvalidOperationException("A persistence restore plan is single-use.");
        _consumed = true;
        long constructStart = Stopwatch.GetTimestamp();
        EcsWorld? world = null;
        try
        {
            world = stagingWorldFactory(Requirements) ?? throw new PersistenceValidationException("The staging factory returned null.");
            if (world.State != EcsWorldState.Created)
                throw new PersistenceValidationException("The staging factory must return a newly created, unsealed world.");
            if (world.Schema.Id != _catalog.Schema.Id)
                throw new PersistenceCompatibilityException("The staging world uses a different current schema.");
            if (world.Options.EntityCapacity.MaximumEntities < Requirements.EntityCount ||
                !world.Options.Persistence.IsEnabled ||
                world.Options.Persistence.WorldId != Requirements.PersistentWorldId)
                throw new PersistenceValidationException("The staging world does not satisfy entity or persistent-identity requirements.");
            var handles = new ArchetypeHandle[_snapshot.Shapes.Length];
            for (int index = 0; index < _snapshot.Shapes.Length; index++)
            {
                ResolvedShape shape = _snapshot.Shapes[index];
                if (!world.TryGetArchetype(shape.CurrentComponents, out handles[index]))
                {
                    handles[index] = world.RegisterArchetype(shape.CurrentComponents, _options.ChunkLayout,
                        Requirements.Shapes[index].MaximumChunks);
                }
            }
            world.Seal();
            world.Initialize();
            foreach (KeyValuePair<ulong, string> prefab in _snapshot.Prefabs)
                world.PersistenceCore.PersistenceIdentities.RegisterRestoredPrefabKey(prefab.Key, prefab.Value);
            long constructTicks = Stopwatch.GetTimestamp() - constructStart;

            long allocateStart = Stopwatch.GetTimestamp();
            var entities = new Entity[_snapshot.Entities.Length];
            for (int ordinal = 0; ordinal < entities.Length; ordinal++)
            {
                SavedEntity saved = _snapshot.Entities[ordinal];
                if (!world.PersistenceCore.TryCreatePersistenceRestore(handles[saved.ShapeIndex].Id,
                        new PersistentEntityId(saved.PersistentId), saved.PrefabKeyHash, out entities[ordinal]))
                    throw new PersistenceValidationException("A preflighted staging row rejected restore allocation.");
            }
            world.PersistenceCore.PersistenceIdentities.SetAllocatorHighWater(_snapshot.AllocatorHighWater);
            long allocateTicks = Stopwatch.GetTimestamp() - allocateStart;

            long hydrateStart = Stopwatch.GetTimestamp();
            var readContext = new PersistenceReadContext(entities);
            foreach (ResolvedColumn column in _snapshot.Columns)
            {
                if (column.Dropped || column.ShapeMigrated) continue;
                ref readonly ComponentMetadata metadata = ref world.Schema.GetMetadata(column.ComponentId);
                byte[] runtimeValues = new byte[checked(column.RowCount * metadata.Size)];
                column.Reader!.DecodeBatch(column.Payload, column.RowCount, runtimeValues, readContext);
                for (int row = 0; row < column.RowCount; row++)
                {
                    uint ordinal = _snapshot.ShapeOrdinals[column.ShapeIndex][row];
                    world.PersistenceCore.RestorePersistenceComponent(entities[ordinal], column.ComponentId,
                        runtimeValues.AsSpan(row * metadata.Size, metadata.Size));
                }
            }
            foreach (ResolvedEnable enable in _snapshot.Enables)
            {
                if (enable.Dropped || enable.ShapeMigrated) continue;
                for (int row = 0; row < enable.RowCount; row++)
                {
                    uint ordinal = _snapshot.ShapeOrdinals[enable.ShapeIndex][row];
                    bool state = (enable.Bits[row >> 3] & (1 << (row & 7))) != 0;
                    world.PersistenceCore.RestorePersistenceEnabled(entities[ordinal], enable.ComponentId, state);
                }
            }
            for (int shapeIndex = 0; shapeIndex < _snapshot.Shapes.Length; shapeIndex++)
            {
                ResolvedShape shape = _snapshot.Shapes[shapeIndex];
                Entity[] shapeEntities = new Entity[_snapshot.ShapeOrdinals[shapeIndex].Length];
                for (int row = 0; row < shapeEntities.Length; row++)
                    shapeEntities[row] = entities[_snapshot.ShapeOrdinals[shapeIndex][row]];
                if (shape.Migration is { } migration)
                {
                    PersistenceSavedColumn[] savedColumns = _snapshot.Columns
                        .Where(column => column.ShapeIndex == shapeIndex)
                        .Select(static column => new PersistenceSavedColumn(column.SavedPersistenceId,
                            column.SavedVersion, column.EncodedSize, column.Payload))
                        .ToArray();
                    PersistenceSavedEnablePlane[] savedEnables = _snapshot.Enables
                        .Where(enable => enable.ShapeIndex == shapeIndex)
                        .Select(static enable => new PersistenceSavedEnablePlane(
                            enable.SavedPersistenceId, enable.RowCount, enable.Bits))
                        .ToArray();
                    migration.Migrator.Migrate(new PersistenceShapeMigrationContext(
                        world, shapeEntities, savedColumns, savedEnables));
                }
                foreach (ComponentId componentId in shape.CurrentComponents)
                {
                    if (world.Schema.GetMetadata(componentId).PersistenceMode == ComponentPersistenceMode.Derived)
                    {
                        if (!_catalog.TryGetDerived(componentId, out IPersistenceDerivedReconstructor reconstructor))
                            throw new PersistenceCompatibilityException($"Derived component '{world.Schema.GetCanonicalKey(componentId)}' has no reconstructor.");
                        reconstructor.Reconstruct(new PersistenceReconstructionContext(world, shapeEntities));
                    }
                }
            }
            world.RestorePersistenceSystemState(_snapshot.SchedulerTick, _snapshot.Systems);
            long hydrateTicks = Stopwatch.GetTimestamp() - hydrateStart;

            long validateStart = Stopwatch.GetTimestamp();
            if (world.AliveCount != entities.Length)
                throw new PersistenceValidationException("Staging population differs from the validated snapshot.");
            for (int ordinal = 0; ordinal < entities.Length; ordinal++)
            {
                PersistentEntityId id = new(_snapshot.Entities[ordinal].PersistentId);
                if (!world.TryResolvePersistentEntity(id, out Entity resolved) || resolved != entities[ordinal])
                    throw new PersistenceValidationException("Restored durable identity failed round-trip validation.");
            }
            if (world.CurrentChangeVersion.IsValid)
                throw new PersistenceValidationException("Restore incorrectly published component changes.");
            world.BeginRun();
            long validateTicks = Stopwatch.GetTimestamp() - validateStart;
            report = new PersistenceRestoreReport(_parseTicks, _preflightTicks, constructTicks, allocateTicks,
                hydrateTicks, validateTicks, entities.Length, _snapshot.Shapes.Length, _snapshot.Columns.Length,
                _snapshot.MigratedComponents, _snapshot.DroppedComponents);
            return world;
        }
        catch
        {
            world?.Dispose();
            throw;
        }
    }
}

public static partial class EcsPersistenceRestore
{
    public static PersistenceRestorePlan Preflight(ReadOnlyMemory<byte> artifact, PersistenceCatalog catalog,
        PersistenceRestoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        long parseStart = Stopwatch.GetTimestamp();
        long knownTemporaryBytes = SnapshotParser.EstimateKnownTemporaryBytes(artifact.Span, options.Limits);
        if (knownTemporaryBytes > options.Limits.MaximumTemporaryBytes)
            throw new PersistenceValidationException("Restore preflight memory estimate exceeds the configured temporary-memory limit.");
        byte[] immutable = artifact.ToArray();
        ParsedSnapshot parsed = SnapshotParser.Parse(immutable, options);
        parsed.KnownTemporaryBytes = knownTemporaryBytes;
        long parseTicks = Stopwatch.GetTimestamp() - parseStart;
        long preflightStart = Stopwatch.GetTimestamp();
        PersistenceRestoreRequirements requirements = parsed.Resolve(catalog, options);
        long preflightTicks = Stopwatch.GetTimestamp() - preflightStart;
        return new PersistenceRestorePlan(parsed, catalog, options, requirements, parseTicks, preflightTicks);
    }
}

internal sealed class ParsedSnapshot
{
    internal required PersistentWorldId WorldId;
    internal SchemaId SchemaHint;
    internal required ulong AllocatorHighWater;
    internal required ulong Sequence;
    internal required SavedSchema[] Schemas;
    internal required ResolvedShape[] Shapes;
    internal required SavedEntity[] Entities;
    internal required uint[][] ShapeOrdinals;
    internal required ResolvedColumn[] Columns;
    internal required ResolvedEnable[] Enables;
    internal required ParsedSystemState[] Systems;
    internal required Dictionary<ulong, string> Prefabs;
    internal ulong SchedulerTick;
    internal required List<string> Diagnostics;
    internal int MigratedComponents;
    internal int DroppedComponents;
    internal long KnownTemporaryBytes;

    internal PersistenceRestoreRequirements Resolve(PersistenceCatalog catalog, PersistenceRestoreOptions options)
    {
        if (SchemaHint != catalog.Schema.Id && Diagnostics.Count < options.MaximumDiagnostics)
            Diagnostics.Add($"Snapshot schema hint {SchemaHint} differs from current schema {catalog.Schema.Id}; compatibility was resolved from stable persistence identities.");
        var schemaById = Schemas.ToDictionary(static schema => schema.PersistenceId);
        long chunkBytes = 0;
        int requiredArchetypes = 0;
        var requirements = new PersistenceRestoreShapeRequirement[Shapes.Length];
        for (int shapeIndex = 0; shapeIndex < Shapes.Length; shapeIndex++)
        {
            ResolvedShape shape = Shapes[shapeIndex];
            if (catalog.TryGetShapeMigration(shape.SavedIds, out ShapeMigrationBinding migration))
            {
                shape.Migration = migration;
                shape.CurrentComponents = (ComponentId[])migration.CurrentComponents.Clone();
                continue;
            }
            var current = new List<ComponentId>(shape.SavedIds.Length);
            foreach (uint savedId in shape.SavedIds)
            {
                if (!catalog.TryResolveSavedId(savedId, out ComponentId id, out bool drop))
                {
                    if (drop) { DroppedComponents++; continue; }
                    throw new PersistenceCompatibilityException($"Saved shape component persistence ID {savedId} is unknown and not explicitly dropped.");
                }
                if (current.Contains(id)) throw new PersistenceCompatibilityException("Aliases collapse a saved shape to duplicate current components.");
                current.Add(id);
            }
            current.Sort(static (left, right) => left.Value.CompareTo(right.Value));
            if (current.Count == 0)
                throw new PersistenceCompatibilityException("A saved shape cannot resolve to an empty current archetype.");
            shape.CurrentComponents = current.ToArray();
        }

        // Multiple historical shapes may legitimately converge on one current
        // archetype through aliases or explicit migrations. Capacity is a fact
        // of the final store, not of each saved group independently.
        var assigned = new bool[Shapes.Length];
        for (int shapeIndex = 0; shapeIndex < Shapes.Length; shapeIndex++)
        {
            if (assigned[shapeIndex]) continue;
            requiredArchetypes++;
            ComponentId[] signature = Shapes[shapeIndex].CurrentComponents;
            long population = 0;
            for (int candidate = shapeIndex; candidate < Shapes.Length; candidate++)
            {
                if (!signature.AsSpan().SequenceEqual(Shapes[candidate].CurrentComponents)) continue;
                assigned[candidate] = true;
                population = checked(population + Shapes[candidate].EntityCount);
            }
            if (population > int.MaxValue) throw new PersistenceValidationException("Resolved archetype population exceeds supported bounds.");
            Span<ComponentColumnLayout> columns = new ComponentColumnLayout[signature.Length];
            ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(catalog.Schema, signature, options.ChunkLayout, columns);
            int chunks = checked(((int)population + layout.RowCapacity - 1) / layout.RowCapacity);
            chunkBytes = checked(chunkBytes + (long)chunks * options.ChunkLayout.ChunkSizeBytes);
            for (int candidate = shapeIndex; candidate < Shapes.Length; candidate++)
                if (signature.AsSpan().SequenceEqual(Shapes[candidate].CurrentComponents))
                    requirements[candidate] = new PersistenceRestoreShapeRequirement(
                        Shapes[candidate].CurrentComponents, Shapes[candidate].EntityCount, Math.Max(1, chunks));
        }

        foreach (ResolvedColumn column in Columns)
        {
            if (Shapes[column.ShapeIndex].Migration is not null)
            {
                column.ShapeMigrated = true;
                continue;
            }
            if (!schemaById.TryGetValue(column.SavedPersistenceId, out SavedSchema? schema))
                throw new PersistenceValidationException("A component column has no schema directory entry.");
            if (!catalog.TryResolveSavedId(column.SavedPersistenceId, out ComponentId id, out bool drop))
            {
                if (drop) { column.Dropped = true; continue; }
                throw new PersistenceCompatibilityException($"Column persistence ID {column.SavedPersistenceId} is unknown.");
            }
            ref readonly ComponentMetadata metadata = ref catalog.Schema.GetMetadata(id);
            if (metadata.PersistenceMode != ComponentPersistenceMode.Value)
                throw new PersistenceCompatibilityException("A saved value column now resolves to a non-value persistence policy without a migration.");
            if (!catalog.TryGetReader(metadata.PersistenceId, column.SavedVersion, out IBoundPersistenceCodec reader) ||
                reader.EncodedSize != column.EncodedSize || !reader.Fingerprint.Equals(schema.Fingerprint))
                throw new PersistenceCompatibilityException($"No direct compatible reader exists for persistence ID {column.SavedPersistenceId} version {column.SavedVersion}.");
            column.ComponentId = id;
            column.Reader = reader;
            if (column.SavedVersion != metadata.Version) MigratedComponents++;
        }
        foreach (ResolvedEnable enable in Enables)
        {
            if (Shapes[enable.ShapeIndex].Migration is not null)
            {
                enable.ShapeMigrated = true;
                continue;
            }
            if (!catalog.TryResolveSavedId(enable.SavedPersistenceId, out ComponentId id, out bool drop))
            {
                if (drop) { enable.Dropped = true; continue; }
                throw new PersistenceCompatibilityException("An enable-state section resolves to an unknown component.");
            }
            ref readonly ComponentMetadata metadata = ref catalog.Schema.GetMetadata(id);
            if (!metadata.IsEnableable || (metadata.Flags & ComponentFlags.PersistEnableState) == 0)
                throw new PersistenceCompatibilityException("Saved durable enable state has no compatible current destination.");
            enable.ComponentId = id;
        }

        long restoreScratch = checked((long)Entities.Length * (sizeof(uint) + 12 + sizeof(ulong)) +
            (long)Entities.Length * sizeof(uint) + (long)Shapes.Length * 64);
        long temporary = Math.Max(KnownTemporaryBytes, restoreScratch);
        if (temporary > options.Limits.MaximumTemporaryBytes)
            throw new PersistenceValidationException("Restore scratch estimate exceeds the configured temporary-memory limit.");
        return new PersistenceRestoreRequirements(catalog.Schema, WorldId, AllocatorHighWater, Sequence, Entities.Length,
            requirements, requiredArchetypes, temporary, chunkBytes);
    }
}

internal sealed record SavedSchema(uint PersistenceId, ushort Version, ComponentPersistenceMode Mode,
    ComponentFlags Flags, int EncodedSize, PersistenceSchemaFingerprint Fingerprint, ulong StableKeyHash, string CanonicalKey);
internal sealed class ResolvedShape
{
    internal required uint[] SavedIds;
    internal required int EntityCount;
    internal ComponentId[] CurrentComponents = [];
    internal ShapeMigrationBinding? Migration;
}
internal readonly record struct SavedEntity(ulong PersistentId, int ShapeIndex, ulong PrefabKeyHash);
internal sealed class ResolvedColumn
{
    internal int ShapeIndex; internal uint SavedPersistenceId; internal ushort SavedVersion;
    internal int EncodedSize; internal int RowCount; internal required byte[] Payload;
    internal ComponentId ComponentId; internal IBoundPersistenceCodec? Reader; internal bool Dropped;
    internal bool ShapeMigrated;
}
internal sealed class ResolvedEnable
{
    internal int ShapeIndex; internal uint SavedPersistenceId; internal int RowCount; internal required byte[] Bits;
    internal ComponentId ComponentId; internal bool Dropped;
    internal bool ShapeMigrated;
}
