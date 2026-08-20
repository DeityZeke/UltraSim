namespace UltraSim.ECS
{
    public interface IComponent<TSelf>
        where TSelf : unmanaged, IComponent<TSelf>
    {
    }
}
