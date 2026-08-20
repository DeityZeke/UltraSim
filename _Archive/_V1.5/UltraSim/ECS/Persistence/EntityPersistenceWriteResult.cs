namespace UltraSim.ECS.Persistence
{
    public sealed class EntityPersistenceWriteResult
    {
        public int BlockCount { get; internal set; }
        public int PersistentEntityCount { get; internal set; }
        public EntityBlockIndexRecord[] Blocks { get; internal set; } = System.Array.Empty<EntityBlockIndexRecord>();
    }
}
