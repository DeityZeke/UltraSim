namespace UltraSim.ECS
{
    public interface IPrefab<TSelf>
        where TSelf : unmanaged, IPrefab<TSelf>
    {
        ArchetypeSignature Signature { get; }
        void ApplyTo(Archetype archetype, int slot);
    }
}
