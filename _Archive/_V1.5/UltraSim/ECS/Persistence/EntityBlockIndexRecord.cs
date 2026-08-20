namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Index metadata for one persistence-signature block in Entities/entities.idx.
    /// The corresponding payload lives in Entities/entities.bin.
    /// </summary>
    public readonly record struct EntityBlockIndexRecord(
        PersistenceSignature Signature,
        int EntityCount,
        long BinOffset,
        int BinLength);
}
