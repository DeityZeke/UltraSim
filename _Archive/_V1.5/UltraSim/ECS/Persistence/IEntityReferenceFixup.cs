namespace UltraSim.ECS.Persistence
{
    public interface IEntityReferenceFixup<TSelf>
        where TSelf : unmanaged, IEntityReferenceFixup<TSelf>
    {
        static abstract void FixupEntityReferences(ref TSelf value, EntityRemapTable remapTable);
    }
}
