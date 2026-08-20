namespace UltraSim.ECS
{
    public interface ISerializableComponent<TSelf> : IComponent<TSelf>
        where TSelf : unmanaged, ISerializableComponent<TSelf>
    {
        static abstract void Serialize(ref BitWriter writer, in TSelf value);
        static abstract TSelf Deserialize(ref BitReader reader, ushort version);
    }
}
