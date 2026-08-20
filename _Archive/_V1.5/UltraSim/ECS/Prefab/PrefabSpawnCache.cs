using System;
using System.Reflection;

namespace UltraSim.ECS
{
    internal static class PrefabSpawnCache<TPrefab> where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        private static readonly ArchetypeSignature Signature = default(TPrefab).Signature;
        private static readonly PrefabApply Apply = static (archetype, slot) => default(TPrefab).ApplyTo(archetype, slot);

        private static readonly bool IsTemplate;
        private static readonly PrefabApply? ApplyTemplate;
        private static readonly PrefabApply? ApplyPerInstance;
        private static readonly bool HasPerInstanceData;

        static PrefabSpawnCache()
        {
            if (!ImplementsTemplatePrefab(typeof(TPrefab)))
                return;

            var adapterType = typeof(TemplatePrefabAdapter<>).MakeGenericType(typeof(TPrefab));
            ApplyTemplate = CreatePrefabApply(adapterType, "ApplyTemplate");
            ApplyPerInstance = CreatePrefabApply(adapterType, "ApplyPerInstance");
            HasPerInstanceData = (bool)(adapterType.GetProperty("HasPerInstanceData", BindingFlags.Public | BindingFlags.Static)!.GetValue(null) ?? false);
            IsTemplate = true;
        }

        internal static void Enqueue(World world)
        {
            if (IsTemplate)
            {
                world.EnqueueSpawnTemplate(
                    Signature,
                    ApplyTemplate!,
                    HasPerInstanceData ? ApplyPerInstance : null,
                    HasPerInstanceData);
                return;
            }

            world.EnqueueSpawn(Signature, Apply);
        }

        internal static void Enqueue(World world, int count)
        {
            if (count <= 0)
                return;

            if (IsTemplate)
            {
                world.EnqueueSpawnTemplate(
                    Signature,
                    ApplyTemplate!,
                    HasPerInstanceData ? ApplyPerInstance : null,
                    HasPerInstanceData,
                    count);
                return;
            }

            world.EnqueueSpawn(Signature, Apply, count);
        }

        private static PrefabApply CreatePrefabApply(Type adapterType, string methodName)
        {
            var method = adapterType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(adapterType.FullName, methodName);

            return (PrefabApply)Delegate.CreateDelegate(typeof(PrefabApply), method);
        }

        private static bool ImplementsTemplatePrefab(Type prefabType)
        {
            var interfaces = prefabType.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                var current = interfaces[i];
                if (!current.IsGenericType)
                    continue;

                if (current.GetGenericTypeDefinition() != typeof(ITemplatePrefab<>))
                    continue;

                var args = current.GetGenericArguments();
                if (args.Length == 1 && args[0] == prefabType)
                    return true;
            }

            return false;
        }
    }

    internal static class TemplatePrefabSpawnCache<TPrefab> where TPrefab : unmanaged, ITemplatePrefab<TPrefab>
    {
        private static readonly ArchetypeSignature Signature = default(TPrefab).Signature;
        private static readonly PrefabApply ApplyTemplate = static (archetype, slot) => default(TPrefab).ApplyTemplateTo(archetype, slot);
        private static readonly PrefabApply ApplyPerInstance = static (archetype, slot) => default(TPrefab).ApplyInstanceTo(archetype, slot);
        private static readonly bool HasPerInstanceData = default(TPrefab).HasPerInstanceData;

        internal static void Enqueue(World world)
        {
            world.EnqueueSpawnTemplate(
                Signature,
                ApplyTemplate,
                HasPerInstanceData ? ApplyPerInstance : null,
                HasPerInstanceData);
        }

        internal static void Enqueue(World world, int count)
        {
            if (count <= 0)
                return;

            world.EnqueueSpawnTemplate(
                Signature,
                ApplyTemplate,
                HasPerInstanceData ? ApplyPerInstance : null,
                HasPerInstanceData,
                count);
        }
    }

    internal static class TemplatePrefabAdapter<TPrefab> where TPrefab : unmanaged, ITemplatePrefab<TPrefab>
    {
        public static bool HasPerInstanceData => default(TPrefab).HasPerInstanceData;

        public static void ApplyTemplate(Archetype archetype, int slot)
            => default(TPrefab).ApplyTemplateTo(archetype, slot);

        public static void ApplyPerInstance(Archetype archetype, int slot)
            => default(TPrefab).ApplyInstanceTo(archetype, slot);
    }
}
