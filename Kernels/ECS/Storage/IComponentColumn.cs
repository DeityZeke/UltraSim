namespace UltraSim.ECS;

internal interface IComponentColumn
{
    int Capacity { get; }
    void EnsureCapacity(int capacity);
}
