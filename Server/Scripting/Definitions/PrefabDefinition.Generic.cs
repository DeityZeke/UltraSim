namespace UltraSim.Server.Scripting;

public abstract class PrefabDefinition<TArchetype> : IUltraSimScriptPrefabDefinition
    where TArchetype : ArchetypeShape, new()
{
    public abstract string Key { get; }

    public PrefabDescriptor BuildDescriptor()
    {
        var archetypeShape = new TArchetype();
        ArchetypeDescriptor archetype = archetypeShape.BuildDescriptor();
        var builder = new PrefabBuilder(Key, archetype, typeof(TArchetype));
        Configure(builder);
        return builder.Build();
    }

    protected virtual void Configure(PrefabBuilder builder)
    {
    }
}
