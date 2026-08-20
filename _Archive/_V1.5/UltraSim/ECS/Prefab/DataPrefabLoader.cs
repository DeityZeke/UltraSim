using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace UltraSim.ECS
{
    public sealed class DataPrefabDefinition
    {
        public string Key { get; set; } = string.Empty;
        public DataPrefabComponentDefinition[] Components { get; set; } = Array.Empty<DataPrefabComponentDefinition>();
    }

    public sealed class DataPrefabComponentDefinition
    {
        public string Type { get; set; } = string.Empty;
        public JsonElement Value { get; set; }
    }

    public sealed class DataPrefabDocument
    {
        public DataPrefabDefinition[] Prefabs { get; set; } = Array.Empty<DataPrefabDefinition>();
    }

    public static class DataPrefabLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true
        };

        private static readonly MethodInfo CreateResolvedWriterMethod = typeof(DataPrefabLoader)
            .GetMethod(nameof(CreateResolvedWriterGeneric), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("DataPrefabLoader.CreateResolvedWriterGeneric method not found.");

        public static PrefabId LoadOrReload(DataPrefabDefinition definition, Func<string, Type?>? typeResolver = null)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Key))
                throw new ArgumentException("Prefab key is required.", nameof(definition));
            if (definition.Components == null || definition.Components.Length == 0)
                throw new ArgumentException("At least one component is required.", nameof(definition));

            var writers = new IDataResolvedPrefabWriter[definition.Components.Length];
            var componentIds = new ComponentId[definition.Components.Length];

            for (int i = 0; i < definition.Components.Length; i++)
            {
                var componentDef = definition.Components[i];
                if (string.IsNullOrWhiteSpace(componentDef.Type))
                    throw new ArgumentException($"Component type is required at index {i}.", nameof(definition));

                var componentType = ResolveType(componentDef.Type, typeResolver)
                    ?? throw new InvalidOperationException($"Component type could not be resolved: {componentDef.Type}");

                var componentId = ComponentManager.GetId(componentType);
                componentIds[i] = componentId;
                writers[i] = CreateWriter(componentType, componentDef.Value, componentId);
            }

            var signature = new ArchetypeSignature(componentIds);
            var writerArray = writers;
            PrefabApplyResolved applyResolved = (archetype, slot, columns) =>
            {
                for (int i = 0; i < writerArray.Length; i++)
                    writerArray[i].ApplyResolved(archetype, slot, columns[i]);
            };

            return PrefabRegistry.ReloadResolved(definition.Key, signature, componentIds, applyResolved);
        }

        public static PrefabId LoadOrReloadJson(string json, Func<string, Type?>? typeResolver = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Json payload is required.", nameof(json));

            var definition = JsonSerializer.Deserialize<DataPrefabDefinition>(json, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize prefab definition.");
            return LoadOrReload(definition, typeResolver);
        }

        public static int LoadOrReloadManyJson(string json, Func<string, Type?>? typeResolver = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Json payload is required.", nameof(json));

            using var document = JsonDocument.Parse(json);
            int loaded = 0;

            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in root.EnumerateArray())
                {
                    var def = node.Deserialize<DataPrefabDefinition>(JsonOptions)
                        ?? throw new InvalidOperationException("Failed to deserialize prefab definition.");
                    LoadOrReload(def, typeResolver);
                    loaded++;
                }

                return loaded;
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("prefabs", out var prefabsNode))
            {
                if (prefabsNode.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("prefabs must be an array.");

                foreach (var node in prefabsNode.EnumerateArray())
                {
                    var def = node.Deserialize<DataPrefabDefinition>(JsonOptions)
                        ?? throw new InvalidOperationException("Failed to deserialize prefab definition.");
                    LoadOrReload(def, typeResolver);
                    loaded++;
                }

                return loaded;
            }

            LoadOrReload(root.Deserialize<DataPrefabDefinition>(JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize prefab definition."), typeResolver);
            return 1;
        }

        private static Type? ResolveType(string typeName, Func<string, Type?>? typeResolver)
            => typeResolver?.Invoke(typeName) ?? Type.GetType(typeName, throwOnError: false);

        private static IDataResolvedPrefabWriter CreateWriter(Type type, JsonElement valueElement, ComponentId componentId)
        {
            object value = JsonSerializer.Deserialize(valueElement.GetRawText(), type, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize value for component type {type.FullName}.");

            try
            {
                return (IDataResolvedPrefabWriter)CreateResolvedWriterMethod
                    .MakeGenericMethod(type)
                    .Invoke(null, new[] { value, (object)componentId })!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Component type {type.FullName} must satisfy unmanaged + IComponent<TSelf>.",
                    ex);
            }
        }

        private static IDataResolvedPrefabWriter CreateResolvedWriterGeneric<T>(object boxedValue, ComponentId componentId)
            where T : unmanaged, IComponent<T>
        {
            return new DataResolvedPrefabWriter<T>((T)boxedValue, componentId);
        }

        private interface IDataResolvedPrefabWriter
        {
            void ApplyResolved(Archetype archetype, int slot, int columnIndex);
        }

        private sealed class DataResolvedPrefabWriter<T> : IDataResolvedPrefabWriter where T : unmanaged, IComponent<T>
        {
            private readonly T _value;
            private readonly ComponentId _componentId;

            public DataResolvedPrefabWriter(T value, ComponentId componentId)
            {
                _value = value;
                _componentId = componentId;
            }

            public void ApplyResolved(Archetype archetype, int slot, int columnIndex)
            {
                if (columnIndex >= 0)
                {
                    archetype.GetComponentSpanAt<T>(columnIndex)[slot] = _value;
                    return;
                }

                // Safety fallback if resolved columns are unavailable.
                archetype.GetComponentSpan<T>(_componentId)[slot] = _value;
            }
        }
    }
}
