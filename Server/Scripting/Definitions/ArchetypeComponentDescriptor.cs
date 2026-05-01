namespace UltraSim.Server.Scripting;

public sealed record ArchetypeComponentDescriptor(Type ComponentType)
{
    public string ComponentName => ComponentType.FullName ?? ComponentType.Name;
}
