namespace UltraSim.ECS;

public delegate void ReadOnlyQueryAction<T>(Entity entity, in T component)
    where T : unmanaged, IComponent<T>;

public delegate void ReadOnlyQueryAction<T1, T2>(Entity entity, in T1 component1, in T2 component2)
    where T1 : unmanaged, IComponent<T1>
    where T2 : unmanaged, IComponent<T2>;

public delegate void ReadOnlyQueryAction<T1, T2, T3>(Entity entity, in T1 component1, in T2 component2, in T3 component3)
    where T1 : unmanaged, IComponent<T1>
    where T2 : unmanaged, IComponent<T2>
    where T3 : unmanaged, IComponent<T3>;
