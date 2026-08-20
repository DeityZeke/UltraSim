namespace UltraSim.ECS;

public delegate void ComponentUpdater<T>(ref T component)
    where T : unmanaged, IComponent<T>;
