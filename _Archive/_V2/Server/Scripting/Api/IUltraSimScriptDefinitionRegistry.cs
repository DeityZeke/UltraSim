namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptDefinitionRegistry
{
    bool RegisterArchetype<TShape>()
        where TShape : ArchetypeShape, new();

    bool RegisterArchetype(ArchetypeDescriptor descriptor);

    bool RegisterArchetypeForContentGroup<TShape>(string contentGroupKey)
        where TShape : ArchetypeShape, new();

    bool RegisterArchetypeForContentGroup(string contentGroupKey, ArchetypeDescriptor descriptor);

    bool UnregisterArchetype(string key);

    bool RegisterPrefab<TPrefab>()
        where TPrefab : IUltraSimScriptPrefabDefinition, new();

    bool RegisterPrefab(PrefabDescriptor descriptor);

    bool RegisterPrefabForContentGroup<TPrefab>(string contentGroupKey)
        where TPrefab : IUltraSimScriptPrefabDefinition, new();

    bool RegisterPrefabForContentGroup(string contentGroupKey, PrefabDescriptor descriptor);

    bool UnregisterPrefab(string key);
}
