namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Root manifest metadata for a save set.
    /// This is the authoritative entry point used to validate that the save set is complete
    /// enough to attempt a load.
    /// </summary>
    public readonly record struct SaveSetManifest(
        long SavedAtUtcTicks,
        int TypeRecordCount,
        int SystemFileCount,
        int EntityBlockCount,
        int PersistentEntityCount);
}
