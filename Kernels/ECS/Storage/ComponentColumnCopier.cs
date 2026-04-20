namespace UltraSim.ECS;

internal delegate void ComponentColumnCopier(IComponentColumn source, IComponentColumn destination, int sourceSlot, int destinationSlot);

internal static class ComponentColumnCopierCache<T>
    where T : unmanaged, IComponent<T>
{
    public static readonly ComponentColumnCopier Copy = static (source, destination, sourceSlot, destinationSlot) =>
    {
        if (source is not ComponentColumn<T> typedSource)
            throw new InvalidCastException($"Source column is not a {typeof(ComponentColumn<T>).FullName}.");

        if (destination is not ComponentColumn<T> typedDestination)
            throw new InvalidCastException($"Destination column is not a {typeof(ComponentColumn<T>).FullName}.");

        typedDestination.CopyFromTyped(typedSource, sourceSlot, destinationSlot);
    };
}
