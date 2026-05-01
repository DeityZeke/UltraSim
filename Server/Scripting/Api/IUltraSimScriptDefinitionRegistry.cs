namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptDefinitionRegistry
{
    bool RegisterArchetype<TShape>()
        where TShape : ArchetypeShape, new();

    bool RegisterArchetype(ArchetypeDescriptor descriptor);

    bool RegisterPrefab<TPrefab>()
        where TPrefab : IUltraSimScriptPrefabDefinition, new();

    bool RegisterPrefab(PrefabDescriptor descriptor);
}
