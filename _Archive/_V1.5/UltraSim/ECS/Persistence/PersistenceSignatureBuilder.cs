using System;
using System.Collections.Generic;

namespace UltraSim.ECS.Persistence
{
    public static class PersistenceSignatureBuilder
    {
        public static bool TryBuild(
            PersistenceArchetypeView archetypeView,
            PersistenceTypeRegistry registry,
            out PersistenceSignature? signature,
            out PersistenceComponentDescriptor[] descriptors)
        {
            ArgumentNullException.ThrowIfNull(archetypeView);
            ArgumentNullException.ThrowIfNull(registry);

            ReadOnlySpan<ComponentId> componentIds = archetypeView.ComponentIds;
            var persistentDescriptors = new List<PersistenceComponentDescriptor>(componentIds.Length);

            for (int i = 0; i < componentIds.Length; i++)
            {
                ComponentId componentId = componentIds[i];
                Type componentType = ComponentManager.GetType(componentId);

                if (!IsSerializableComponentType(componentType))
                    continue;

                if (!registry.TryGetByType(componentType, out PersistenceTypeRecord record) ||
                    record.Kind != PersistenceKind.Component)
                {
                    throw new InvalidOperationException(
                        $"Serializable component '{componentType.FullName}' is present on a persistent archetype but is not registered as a persistent component.");
                }

                persistentDescriptors.Add(new PersistenceComponentDescriptor(
                    record.PersistenceId,
                    componentId,
                    componentType));
            }

            if (persistentDescriptors.Count == 0)
            {
                signature = null;
                descriptors = Array.Empty<PersistenceComponentDescriptor>();
                return false;
            }

            persistentDescriptors.Sort(static (a, b) => a.PersistenceId.CompareTo(b.PersistenceId));
            descriptors = persistentDescriptors.ToArray();

            var ids = new uint[descriptors.Length];
            for (int i = 0; i < descriptors.Length; i++)
                ids[i] = descriptors[i].PersistenceId;

            signature = new PersistenceSignature(ids);
            return true;
        }

        internal static bool IsSerializableComponentType(Type type)
        {
            var interfaces = type.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                Type iface = interfaces[i];
                if (!iface.IsGenericType)
                    continue;

                if (iface.GetGenericTypeDefinition() != typeof(ISerializableComponent<>))
                    continue;

                Type[] args = iface.GetGenericArguments();
                return args.Length == 1 && args[0] == type;
            }

            return false;
        }
    }
}
