namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Classifies a type registered in <see cref="PersistenceTypeRegistry"/>.
    /// Used in the type database (<c>types.tdb</c>) to partition component and system entries.
    /// </summary>
    public enum PersistenceKind : byte
    {
        Component = 0,
        System    = 1,
    }
}
