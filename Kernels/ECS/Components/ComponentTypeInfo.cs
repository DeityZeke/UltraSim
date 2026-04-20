namespace UltraSim.ECS;

public sealed class ComponentTypeInfo
{
    internal ComponentTypeInfo(
        ComponentId id,
        Type clrType,
        Func<int, IComponentColumn> columnFactory,
        ComponentColumnCopier columnCopier)
    {
        Id = id;
        ClrType = clrType;
        ColumnFactory = columnFactory;
        ColumnCopier = columnCopier;
    }

    public ComponentId Id { get; }
    public Type ClrType { get; }

    internal Func<int, IComponentColumn> ColumnFactory { get; }
    internal ComponentColumnCopier ColumnCopier { get; }
}
