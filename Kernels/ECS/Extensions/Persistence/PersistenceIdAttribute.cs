namespace UltraSim.ECS.Persistence;

/// <summary>
/// Declares a stable persistence identity for a component or system type.
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PersistenceIdAttribute : Attribute
{
    public PersistenceIdAttribute(uint id)
    {
        Id = id;
    }

    public uint Id { get; }
}
