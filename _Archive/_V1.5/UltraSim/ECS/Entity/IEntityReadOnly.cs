namespace UltraSim.ECS
{
    public interface IEntityReadOnly
    {
        int AliveCount { get; }
        int Capacity { get; }
        bool IsValid(Entity entity);
        bool IsAlive(Entity entity);
    }
}
