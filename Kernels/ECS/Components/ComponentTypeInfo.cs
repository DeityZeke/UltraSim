namespace UltraSim.ECS;

public sealed class ComponentTypeInfo
{
    internal ComponentTypeInfo(ComponentId id, Type clrType, Func<int, IComponentColumn> columnFactory)
    {
        Id = id;
        ClrType = clrType;
        ColumnFactory = columnFactory;
    }

    public ComponentId Id { get; }
    public Type ClrType { get; }

    internal Func<int, IComponentColumn> ColumnFactory { get; }
}
