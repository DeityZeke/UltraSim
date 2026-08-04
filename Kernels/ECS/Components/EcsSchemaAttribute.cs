namespace UltraSim.ECS;

/// <summary>
/// Requests a generated, closed component schema for the annotated static partial class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EcsSchemaAttribute : Attribute
{
    public EcsSchemaAttribute(params Type[] componentTypes)
    {
        ComponentTypes = componentTypes;
    }

    public IReadOnlyList<Type> ComponentTypes { get; }
}
