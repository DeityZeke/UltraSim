namespace UltraSim.ECS;

internal delegate void ComponentColumnCopier(IComponentColumn source, IComponentColumn destination, int sourceSlot, int destinationSlot);

internal static class ComponentColumnCopierCache<T>
    where T : unmanaged, IComponent<T>
{
    public static readonly ComponentColumnCopier Copy = static (source, destination, sourceSlot, destinationSlot) =>
        ((ComponentColumn<T>)destination).CopyFromTyped((ComponentColumn<T>)source, sourceSlot, destinationSlot);
}
