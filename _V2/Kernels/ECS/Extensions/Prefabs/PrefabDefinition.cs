using System.Reflection;

namespace UltraSim.ECS;

/// <summary>
/// Immutable runtime prefab definition used by resolver-backed spawn paths.
/// </summary>
public readonly record struct PrefabDefinition
{
    public PrefabDefinition(
        ArchetypeSignature signature,
        PrefabApply apply,
        PrefabApply? applyPerInstance = null,
        PrefabApplyResolved? applyResolved = null,
        ComponentId[]? resolvedComponentIds = null)
    {
        Signature = signature ?? throw new ArgumentNullException(nameof(signature));
        Apply = apply ?? throw new ArgumentNullException(nameof(apply));

        if (applyResolved is null != resolvedComponentIds is null)
            throw new ArgumentException("Resolved apply and resolved component ids must either both be supplied or both be omitted.");

        ApplyPerInstance = applyPerInstance;
        ApplyResolved = applyResolved;
        ResolvedComponentIds = resolvedComponentIds;
    }

    public ArchetypeSignature Signature { get; }
    public PrefabApply Apply { get; }
    public PrefabApply? ApplyPerInstance { get; }
    public PrefabApplyResolved? ApplyResolved { get; }
    public ComponentId[]? ResolvedComponentIds { get; }

    public bool HasResolvedApply => ApplyResolved is not null && ResolvedComponentIds is { Length: > 0 };

    internal void ApplyTo(Archetype archetype, int slot)
    {
        if (HasResolvedApply)
        {
            ComponentId[] resolvedIds = ResolvedComponentIds!;
            var columns = new int[resolvedIds.Length];
            for (int i = 0; i < resolvedIds.Length; i++)
                columns[i] = archetype.GetColumnIndex(resolvedIds[i]);

            ApplyResolved!(archetype, slot, columns);
            return;
        }

        Apply(archetype, slot);
        ApplyPerInstance?.Invoke(archetype, slot);
    }

    public static PrefabDefinition Create(ArchetypeSignature signature, PrefabApply apply)
        => new(signature, apply);

    public static PrefabDefinition CreateTemplate(ArchetypeSignature signature, PrefabApply applyTemplate, PrefabApply? applyPerInstance = null)
        => new(signature, applyTemplate, applyPerInstance);

    public static PrefabDefinition CreateResolved(
        ArchetypeSignature signature,
        ReadOnlySpan<ComponentId> resolvedComponentIds,
        PrefabApplyResolved applyResolved)
    {
        if (resolvedComponentIds.Length == 0)
            throw new ArgumentException("Resolved component list cannot be empty.", nameof(resolvedComponentIds));

        return new(signature, static (_, _) => { }, null, applyResolved, resolvedComponentIds.ToArray());
    }

    public static PrefabDefinition From<TPrefab>()
        where TPrefab : unmanaged, IPrefab<TPrefab>
        => PrefabTypeCache<TPrefab>.Definition;

    private delegate void TypedPrefabApply<TPrefab>(in TPrefab prefab, Archetype archetype, int slot)
        where TPrefab : unmanaged, IPrefab<TPrefab>;

    private delegate void TypedPrefabApplyResolved<TPrefab>(in TPrefab prefab, Archetype archetype, int slot, ReadOnlySpan<int> columns)
        where TPrefab : unmanaged, IPrefab<TPrefab>;

    internal static class PrefabTypeCache<TPrefab>
        where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        private static readonly TypedPrefabApply<TPrefab> s_apply = static (in TPrefab prefab, Archetype archetype, int slot) => prefab.ApplyTo(archetype, slot);
        private static readonly bool s_isTemplate;
        private static readonly bool s_isResolved;
        private static readonly bool s_hasPerInstanceData;
        private static readonly PrefabApply? s_applyTemplateDefault;
        private static readonly PrefabApply? s_applyPerInstanceDefault;
        private static readonly PrefabApplyResolved? s_applyResolvedDefault;
        private static readonly TypedPrefabApply<TPrefab>? s_applyTemplate;
        private static readonly TypedPrefabApply<TPrefab>? s_applyPerInstance;
        private static readonly TypedPrefabApplyResolved<TPrefab>? s_applyResolved;
        private static readonly ComponentId[]? s_resolvedComponentIds;

        static PrefabTypeCache()
        {
            Type prefabType = typeof(TPrefab);

            if (ImplementsGeneric(prefabType, typeof(ITemplatePrefab<>)))
            {
                Type adapterType = typeof(TemplatePrefabAdapter<>).MakeGenericType(prefabType);
                s_applyTemplate = CreateTypedApply(adapterType, "ApplyTemplate");
                s_applyPerInstance = CreateTypedApply(adapterType, "ApplyPerInstance");
                s_applyTemplateDefault = CreateApply(adapterType, "ApplyTemplateDefault");
                s_applyPerInstanceDefault = CreateApply(adapterType, "ApplyPerInstanceDefault");
                s_hasPerInstanceData = (bool)(adapterType.GetProperty("HasPerInstanceData", BindingFlags.Public | BindingFlags.Static)!.GetValue(null) ?? false);
                s_isTemplate = true;
            }

            if (ImplementsGeneric(prefabType, typeof(IResolvedPrefab<>)))
            {
                Type adapterType = typeof(ResolvedPrefabAdapter<>).MakeGenericType(prefabType);
                s_applyResolved = CreateTypedResolvedApply(adapterType, "ApplyResolved");
                s_applyResolvedDefault = CreateResolvedApply(adapterType, "ApplyResolvedDefault");
                s_resolvedComponentIds = (ComponentId[])(adapterType.GetProperty("ResolvedComponentIds", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)
                    ?? throw new InvalidOperationException($"Resolved component ids were missing for {prefabType.FullName}."));
                s_isResolved = true;
            }

            if (s_isResolved)
            {
                Definition = new PrefabDefinition(
                    default(TPrefab).Signature,
                    static (_, _) => { },
                    null,
                    s_applyResolvedDefault!,
                    s_resolvedComponentIds);
                return;
            }

            if (s_isTemplate)
            {
                Definition = new PrefabDefinition(
                    default(TPrefab).Signature,
                    s_applyTemplateDefault!,
                    s_hasPerInstanceData ? s_applyPerInstanceDefault : null);
                return;
            }

            Definition = new PrefabDefinition(
                default(TPrefab).Signature,
                static (Archetype archetype, int slot) => default(TPrefab).ApplyTo(archetype, slot));
        }

        public static PrefabDefinition Definition { get; }

        public static void Apply(in TPrefab prefab, Archetype archetype, int slot)
        {
            if (s_isResolved)
            {
                int[] columns = ResolveColumns(archetype, s_resolvedComponentIds!);
                s_applyResolved!(in prefab, archetype, slot, columns);
                return;
            }

            if (s_isTemplate)
            {
                s_applyTemplate!(in prefab, archetype, slot);
                if (s_hasPerInstanceData)
                    s_applyPerInstance!(in prefab, archetype, slot);
                return;
            }

            s_apply(in prefab, archetype, slot);
        }

        private static PrefabApply CreateApply(Type adapterType, string methodName)
        {
            MethodInfo method = adapterType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(adapterType.FullName, methodName);
            return (PrefabApply)Delegate.CreateDelegate(typeof(PrefabApply), method);
        }

        private static PrefabApplyResolved CreateResolvedApply(Type adapterType, string methodName)
        {
            MethodInfo method = adapterType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(adapterType.FullName, methodName);
            return (PrefabApplyResolved)Delegate.CreateDelegate(typeof(PrefabApplyResolved), method);
        }

        private static TypedPrefabApply<TPrefab> CreateTypedApply(Type adapterType, string methodName)
        {
            MethodInfo method = adapterType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(adapterType.FullName, methodName);
            return (TypedPrefabApply<TPrefab>)Delegate.CreateDelegate(typeof(TypedPrefabApply<TPrefab>), method);
        }

        private static TypedPrefabApplyResolved<TPrefab> CreateTypedResolvedApply(Type adapterType, string methodName)
        {
            MethodInfo method = adapterType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(adapterType.FullName, methodName);
            return (TypedPrefabApplyResolved<TPrefab>)Delegate.CreateDelegate(typeof(TypedPrefabApplyResolved<TPrefab>), method);
        }

        private static bool ImplementsGeneric(Type type, Type genericTypeDefinition)
        {
            Type[] interfaces = type.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                Type current = interfaces[i];
                if (!current.IsGenericType || current.GetGenericTypeDefinition() != genericTypeDefinition)
                    continue;

                Type[] args = current.GetGenericArguments();
                if (args.Length == 1 && args[0] == type)
                    return true;
            }

            return false;
        }

        private static int[] ResolveColumns(Archetype archetype, ComponentId[] resolvedComponentIds)
        {
            var columns = new int[resolvedComponentIds.Length];
            for (int i = 0; i < resolvedComponentIds.Length; i++)
                columns[i] = archetype.GetColumnIndex(resolvedComponentIds[i]);

            return columns;
        }
    }

    public static class TemplatePrefabAdapter<TPrefab>
        where TPrefab : unmanaged, ITemplatePrefab<TPrefab>
    {
        public static bool HasPerInstanceData => default(TPrefab).HasPerInstanceData;

        public static void ApplyTemplateDefault(Archetype archetype, int slot)
            => default(TPrefab).ApplyTemplateTo(archetype, slot);

        public static void ApplyPerInstanceDefault(Archetype archetype, int slot)
            => default(TPrefab).ApplyInstanceTo(archetype, slot);

        public static void ApplyTemplate(in TPrefab prefab, Archetype archetype, int slot)
            => prefab.ApplyTemplateTo(archetype, slot);

        public static void ApplyPerInstance(in TPrefab prefab, Archetype archetype, int slot)
            => prefab.ApplyInstanceTo(archetype, slot);
    }

    public static class ResolvedPrefabAdapter<TPrefab>
        where TPrefab : unmanaged, IResolvedPrefab<TPrefab>
    {
        public static ComponentId[] ResolvedComponentIds => TPrefab.ResolvedComponentIds;

        public static void ApplyResolvedDefault(Archetype archetype, int slot, ReadOnlySpan<int> columns)
            => default(TPrefab).ApplyResolvedTo(archetype, slot, columns);

        public static void ApplyResolved(in TPrefab prefab, Archetype archetype, int slot, ReadOnlySpan<int> columns)
            => prefab.ApplyResolvedTo(archetype, slot, columns);
    }
}
