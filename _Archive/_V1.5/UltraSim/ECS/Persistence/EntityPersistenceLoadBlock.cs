namespace UltraSim.ECS.Persistence
{
    internal sealed class EntityPersistenceLoadBlock
    {
        public required PersistenceSignature Signature { get; init; }
        public required PersistenceComponentDescriptor[] Descriptors { get; init; }
        public required Archetype Archetype { get; init; }
        public required Entity[] Entities { get; init; }
        public required int StartSlot { get; init; }
        public required long BinOffset { get; init; }
        public required int BinLength { get; init; }
    }
}
