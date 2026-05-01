using UltraSim.ECS;

namespace UltraSim.Server.Scripting;

public static class ScriptPrefabCatalogBuilder
{
    public static ScriptPrefabCatalogBuildResult Build(
        IEnumerable<ArchetypeDescriptor> archetypes,
        IEnumerable<PrefabDescriptor> prefabs,
        EcsSchemaBuilder schemaBuilder,
        ScriptPrefabCatalogBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(archetypes);
        ArgumentNullException.ThrowIfNull(prefabs);
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        options ??= new ScriptPrefabCatalogBuildOptions();

        var componentIdsByType = new Dictionary<Type, ComponentId>();
        var archetypesByKey = new Dictionary<string, ArchetypeDescriptor>(StringComparer.Ordinal);
        var signaturesByKey = new Dictionary<string, ArchetypeSignature>(StringComparer.Ordinal);

        foreach (ArchetypeDescriptor archetype in archetypes)
        {
            ScriptDefinitionValidation.ValidateStableKey(archetype.Key, nameof(archetype.Key));
            if (!archetypesByKey.TryAdd(archetype.Key, archetype))
                throw new InvalidOperationException($"Duplicate archetype key '{archetype.Key}'.");

            var componentIds = new ComponentId[archetype.Components.Count];
            for (int i = 0; i < archetype.Components.Count; i++)
            {
                Type componentType = archetype.Components[i].ComponentType;
                ScriptDefinitionValidation.ValidateComponentType(componentType);
                if (!componentIdsByType.TryGetValue(componentType, out ComponentId componentId))
                {
                    componentId = schemaBuilder.RegisterComponent(componentType);
                    componentIdsByType.Add(componentType, componentId);
                }

                componentIds[i] = componentId;
            }

            signaturesByKey.Add(archetype.Key, new ArchetypeSignature(componentIds));
        }

        var catalog = new PrefabCatalog();
        foreach (PrefabDescriptor prefab in prefabs)
        {
            ScriptDefinitionValidation.ValidateStableKey(prefab.Key, nameof(prefab.Key));
            ScriptDefinitionValidation.ValidateStableKey(prefab.ArchetypeKey, nameof(prefab.ArchetypeKey));
            if (!archetypesByKey.TryGetValue(prefab.ArchetypeKey, out ArchetypeDescriptor? archetype))
            {
                throw new InvalidOperationException(
                    $"Prefab '{prefab.Key}' references unregistered archetype '{prefab.ArchetypeKey}'.");
            }

            ArchetypeSignature signature = signaturesByKey[prefab.ArchetypeKey];
            ScriptPrefabComponentAssignment[] assignments = BuildAssignments(prefab, archetype, componentIdsByType);
            catalog.Add(prefab.Key, CreateEcsPrefabDefinition(prefab.Key, signature, assignments, options.Random));
        }

        return new ScriptPrefabCatalogBuildResult(catalog, signaturesByKey);
    }

    private static ScriptPrefabComponentAssignment[] BuildAssignments(
        PrefabDescriptor prefab,
        ArchetypeDescriptor archetype,
        IReadOnlyDictionary<Type, ComponentId> componentIdsByType)
    {
        var assignments = new ScriptPrefabComponentAssignment[prefab.ComponentValues.Count];
        for (int i = 0; i < prefab.ComponentValues.Count; i++)
        {
            PrefabComponentValue value = prefab.ComponentValues[i];
            Type componentType = value.ComponentType;
            if (!archetype.ContainsComponent(componentType))
            {
                throw new InvalidOperationException(
                    $"Prefab '{prefab.Key}' cannot set component '{componentType.FullName}' because archetype '{archetype.Key}' does not include it.");
            }

            if (!componentIdsByType.TryGetValue(componentType, out ComponentId componentId))
                throw new InvalidOperationException($"Component '{componentType.FullName}' was not registered in the ECS schema builder.");

            assignments[i] = new ScriptPrefabComponentAssignment(
                componentId,
                value,
                ScriptPrefabComponentWriter.Create(componentType));
        }

        return assignments;
    }

    private static UltraSim.ECS.PrefabDefinition CreateEcsPrefabDefinition(
        string prefabKey,
        ArchetypeSignature signature,
        ScriptPrefabComponentAssignment[] assignments,
        Random random)
    {
        long spawnSequence = 0;
        return UltraSim.ECS.PrefabDefinition.Create(
            signature,
            (Archetype archetype, int slot) =>
            {
                long sequence = Interlocked.Increment(ref spawnSequence);
                lock (random)
                {
                    var context = new PrefabRollContext(random, sequence, prefabKey);
                    for (int i = 0; i < assignments.Length; i++)
                        assignments[i].Apply(archetype, slot, context);
                }
            });
    }

    private readonly struct ScriptPrefabComponentAssignment
    {
        private readonly ComponentId _componentId;
        private readonly PrefabComponentValue _value;
        private readonly ScriptPrefabComponentWrite _write;

        public ScriptPrefabComponentAssignment(
            ComponentId componentId,
            PrefabComponentValue value,
            ScriptPrefabComponentWrite write)
        {
            _componentId = componentId;
            _value = value ?? throw new ArgumentNullException(nameof(value));
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        public void Apply(Archetype archetype, int slot, PrefabRollContext context)
        {
            object value = _value.CreateValue(context);
            _write(archetype, _componentId, slot, value);
        }
    }
}
