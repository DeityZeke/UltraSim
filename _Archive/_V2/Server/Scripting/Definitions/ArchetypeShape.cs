namespace UltraSim.Server.Scripting;

public abstract class ArchetypeShape
{
    public abstract string Key { get; }

    public ArchetypeDescriptor BuildDescriptor()
    {
        var builder = new ArchetypeBuilder(Key);
        Configure(builder);
        return builder.Build();
    }

    protected virtual void Configure(ArchetypeBuilder builder)
    {
    }
}
