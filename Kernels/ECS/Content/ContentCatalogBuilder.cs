using System.Reflection;

namespace UltraSim.ECS;

/// <summary>Cold explicit compiler input for class-shaped and direct-DOD prefab definitions.</summary>
/// <remarks>
/// Object Scripts are registered explicitly; this builder never scans ambient
/// AppDomain assemblies. One attribute lookup and one authoring-object construction
/// occur during registration. Neither survives in the compiled catalog.
/// </remarks>
public sealed class ContentCatalogBuilder
{
    private readonly ComponentSchema _schema;
    private readonly List<ContentDefinitionState> _definitions = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private bool _compiled;

    public ContentCatalogBuilder(ComponentSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _schema = schema;
    }

    public int DefinitionCount => _definitions.Count;
    public bool IsCompiled => _compiled;

    /// <summary>Begins one direct low-level ECS prefab definition.</summary>
    public PrefabBuilder Define(PrefabKey key)
    {
        EnsureMutable();
        ContentDefinitionState state = AddDefinition(key);
        return new PrefabBuilder(state);
    }

    /// <summary>Executes one explicitly selected Object Script during cold compilation input.</summary>
    public void Register<TObjectScript>()
        where TObjectScript : BaseClass, new()
    {
        EnsureMutable();
        Type type = typeof(TObjectScript);
        if (type.IsAbstract)
            throw new ArgumentException($"Abstract authoring type {type} cannot be registered as a concrete prefab.");
        ObjectScriptAttribute attribute = type.GetCustomAttribute<ObjectScriptAttribute>(inherit: false)
            ?? throw new ArgumentException($"Concrete authoring type {type} requires ObjectScriptAttribute.");
        var key = new PrefabKey(attribute.Key);
        if (_keys.Contains(key.Value))
            throw new InvalidOperationException($"Duplicate prefab key '{key.Value}'.");
        var state = new ContentDefinitionState(_schema, key);
        var authoringObject = new TObjectScript();
        authoringObject.Build(state);
        _keys.Add(key.Value);
        _definitions.Add(state);
    }

    /// <summary>Compiles, binds, and freezes this content set before world Seal.</summary>
    public CompiledPrefabCatalog Compile(EcsWorld world, ContentCompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.CompileContent(this, options);
    }

    internal CompiledPrefabCatalog CompileCore<TArena>(
        EcsWorld<TArena> world,
        ContentCompilationOptions options)
        where TArena : class, IChunkArenaBackend
    {
        EnsureMutable();
        if (!options.IsValid)
            throw new ArgumentException("Content compilation options are invalid or default initialized.", nameof(options));
        if (!ReferenceEquals(_schema, world.Schema))
            throw new ArgumentException("The content builder uses a different component schema than the target world.");
        if (_definitions.Count == 0)
            throw new InvalidOperationException("A compiled content catalog requires at least one prefab definition.");

        ContentDefinitionState[] ordered = _definitions
            .OrderBy(static definition => definition.Key.Value, StringComparer.Ordinal)
            .ToArray();
        var resolved = new ResolvedDefinition[ordered.Length];
        var uniqueSignatures = new List<ComponentId[]>();
        var signatureBuckets = new Dictionary<ulong, List<int>>();
        int descriptorCount = 0;
        int defaultByteCount = 0;
        for (int index = 0; index < ordered.Length; index++)
        {
            ContentDefinitionState definition = ordered[index];
            ComponentId[] signature = definition.Components.Keys.Order().ToArray();
            if (signature.Length == 0)
                throw new InvalidOperationException($"Prefab {definition.Key} has an empty component signature.");
            ulong signatureHash = ArchetypeSignatureHash.Compute(signature);
            int signatureIndex = FindOrAddSignature(
                signature,
                signatureHash,
                uniqueSignatures,
                signatureBuckets);
            PendingPrefabComponent[] defaults = definition.Components.Values
                .Where(static component => component.DefaultBytes is not null)
                .OrderBy(static component => component.Id)
                .ToArray();
            descriptorCount = checked(descriptorCount + defaults.Length);
            for (int defaultIndex = 0; defaultIndex < defaults.Length; defaultIndex++)
                defaultByteCount = checked(defaultByteCount + defaults[defaultIndex].DefaultBytes!.Length);
            resolved[index] = new ResolvedDefinition(definition, signatureIndex, signatureHash, defaults);
        }

        int newSignatureCount = 0;
        for (int index = 0; index < uniqueSignatures.Count; index++)
        {
            ComponentId[] signature = uniqueSignatures[index];
            if (!world.TryGetArchetypeId(signature, out _))
            {
                newSignatureCount++;
                var columns = new ComponentColumnLayout[signature.Length];
                _ = ChunkLayoutPlanner.Plan(_schema, signature, options.Layout, columns);
            }
        }
        if (newSignatureCount > world.MaximumArchetypes - world.ArchetypeCount)
            throw new InvalidOperationException("Compiled prefab signatures exceed the world's remaining archetype capacity.");

        var archetypes = new ArchetypeId[uniqueSignatures.Count];
        for (int index = 0; index < uniqueSignatures.Count; index++)
        {
            ComponentId[] signature = uniqueSignatures[index];
            if (!world.TryGetArchetypeId(signature, out ArchetypeId archetypeId))
            {
                archetypeId = world.RegisterArchetype(
                    signature,
                    options.Layout,
                    options.MaximumChunksPerNewArchetype);
            }
            archetypes[index] = archetypeId;
        }

        var entries = new CompiledPrefabEntry[resolved.Length];
        var descriptors = new PrefabDefaultDescriptor[descriptorCount];
        var defaultBytes = new byte[defaultByteCount];
        var keys = new string[resolved.Length];
        var displayNames = new string?[resolved.Length];
        int descriptorCursor = 0;
        int byteCursor = 0;
        ulong catalogHash = StableHash64.OffsetBasis;
        for (int index = 0; index < resolved.Length; index++)
        {
            ref readonly ResolvedDefinition definition = ref resolved[index];
            ArchetypeId archetypeId = archetypes[definition.SignatureIndex];
            ArchetypeStorage<TArena> storage = world.GetArchetypeStorage(archetypeId);
            int descriptorStart = descriptorCursor;
            int prefabBytes = 0;
            for (int defaultIndex = 0; defaultIndex < definition.Defaults.Length; defaultIndex++)
            {
                PendingPrefabComponent component = definition.Defaults[defaultIndex];
                byte[] value = component.DefaultBytes!;
                int columnIndex = storage.Store.GetColumnIndex(component.Id);
                if (columnIndex < 0 || columnIndex > ushort.MaxValue)
                    throw new InvalidOperationException("Compiled prefab default did not bind to a physical data column.");
                ref readonly ComponentMetadata metadata = ref _schema.GetMetadata(component.Id);
                if (metadata.IsTag || metadata.Size != value.Length)
                    throw new InvalidOperationException("Compiled prefab default bytes do not match schema metadata.");
                value.CopyTo(defaultBytes.AsSpan(byteCursor));
                descriptors[descriptorCursor++] = new PrefabDefaultDescriptor(
                    component.Id,
                    checked((ushort)columnIndex),
                    byteCursor,
                    value.Length);
                byteCursor += value.Length;
                prefabBytes += value.Length;
            }

            uint prefabId = checked((uint)index + 1);
            entries[index] = new CompiledPrefabEntry(
                prefabId,
                archetypeId,
                descriptorStart,
                definition.Defaults.Length,
                prefabBytes,
                index,
                definition.SignatureHash);
            keys[index] = definition.State.Key.Value;
            displayNames[index] = definition.State.DisplayName;
            catalogHash = HashString(catalogHash, keys[index]);
            catalogHash = Hash(catalogHash, definition.SignatureHash);
            for (int componentIndex = 0; componentIndex < definition.Defaults.Length; componentIndex++)
            {
                PendingPrefabComponent component = definition.Defaults[componentIndex];
                catalogHash = Hash(catalogHash, component.Id.Value);
                catalogHash = HashBytes(catalogHash, component.DefaultBytes!);
            }
            if (displayNames[index] is { } display)
                catalogHash = HashString(catalogHash, display);
        }

        var catalog = new CompiledPrefabCatalog(
            world.Id,
            entries,
            descriptors,
            defaultBytes,
            keys,
            displayNames,
            uniqueSignatures.Count,
            newSignatureCount,
            catalogHash);
        world.BindContentCatalog(catalog);
        for (int index = 0; index < ordered.Length; index++)
            ordered[index].Close();
        _compiled = true;
        _definitions.Clear();
        _keys.Clear();
        return catalog;
    }

    private ContentDefinitionState AddDefinition(PrefabKey key)
    {
        if (!key.IsValid)
            throw new ArgumentException("Prefab key is invalid or default initialized.", nameof(key));
        if (!_keys.Add(key.Value))
            throw new InvalidOperationException($"Duplicate prefab key '{key.Value}'.");
        var state = new ContentDefinitionState(_schema, key);
        _definitions.Add(state);
        return state;
    }

    private static int FindOrAddSignature(
        ComponentId[] signature,
        ulong signatureHash,
        List<ComponentId[]> signatures,
        Dictionary<ulong, List<int>> buckets)
    {
        if (buckets.TryGetValue(signatureHash, out List<int>? bucket))
        {
            for (int index = 0; index < bucket.Count; index++)
            {
                int candidate = bucket[index];
                if (signature.AsSpan().SequenceEqual(signatures[candidate]))
                    return candidate;
            }
        }
        else
        {
            bucket = [];
            buckets.Add(signatureHash, bucket);
        }
        int added = signatures.Count;
        signatures.Add(signature);
        bucket.Add(added);
        return added;
    }

    private static ulong HashString(ulong hash, string value)
    {
        for (int index = 0; index < value.Length; index++)
            hash = unchecked((hash ^ value[index]) * StableHash64.Prime);
        return unchecked((hash ^ 0xFFUL) * StableHash64.Prime);
    }

    private static ulong HashBytes(ulong hash, ReadOnlySpan<byte> value)
    {
        for (int index = 0; index < value.Length; index++)
            hash = unchecked((hash ^ value[index]) * StableHash64.Prime);
        return hash;
    }

    private static ulong Hash(ulong hash, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
            hash = unchecked((hash ^ (byte)(value >> shift)) * StableHash64.Prime);
        return hash;
    }

    private void EnsureMutable()
    {
        if (_compiled)
            throw new InvalidOperationException("The content catalog builder is sealed after successful compilation.");
    }

    private readonly record struct ResolvedDefinition(
        ContentDefinitionState State,
        int SignatureIndex,
        ulong SignatureHash,
        PendingPrefabComponent[] Defaults);
}
