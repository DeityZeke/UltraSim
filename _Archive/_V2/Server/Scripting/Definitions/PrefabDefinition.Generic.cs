namespace UltraSim.Server.Scripting;

public abstract class PrefabDefinition<TArchetype> : IUltraSimScriptPrefabDefinition
    where TArchetype : ArchetypeShape, new()
{
    public virtual string Key => GetDefaultKey(GetType());

    public virtual string SourceName => GetType().FullName ?? GetDefaultKey(GetType());

    public PrefabDescriptor BuildDescriptor()
    {
        var archetypeShape = new TArchetype();
        ArchetypeDescriptor archetype = archetypeShape.BuildDescriptor();
        var builder = new PrefabBuilder(Key, SourceName, archetype, typeof(TArchetype));
        Configure(builder);
        return builder.Build();
    }

    protected virtual void Configure(PrefabBuilder builder)
    {
    }

    private static string GetDefaultKey(Type type)
    {
        string name = type.Name;
        int nestedSeparator = name.LastIndexOf('+');
        if (nestedSeparator >= 0 && nestedSeparator + 1 < name.Length)
            name = name[(nestedSeparator + 1)..];

        int genericSeparator = name.IndexOf('`');
        if (genericSeparator > 0)
            name = name[..genericSeparator];

        return name;
    }
}
