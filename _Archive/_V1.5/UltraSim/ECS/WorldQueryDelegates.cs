namespace UltraSim.ECS
{
    public delegate void QueryAction<T>(Entity entity, ref T component)
        where T : unmanaged, IComponent<T>;

    public delegate void QueryAction<T1, T2>(Entity entity, ref T1 component1, ref T2 component2)
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>;

    public delegate void QueryAction<T1, T2, T3>(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3)
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
        where T3 : unmanaged, IComponent<T3>;
}
