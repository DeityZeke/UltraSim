namespace UltraSim.Server
{
    /// <summary>
    /// Controls whether the two independent branches of a world save
    /// (entity serialization and system serialization) run serially or in parallel.
    /// </summary>
    public enum WorldSaveParallelMode
    {
        /// <summary>
        /// Systems and entities are written sequentially on the calling thread.
        /// Default and always-safe option.
        /// </summary>
        Serial,

        /// <summary>
        /// Systems and entities are written concurrently on separate <see cref="System.Threading.Tasks.Task"/>s.
        /// Both branches write to independent staging files; the manifest/commit step
        /// remains serial after both branches complete.
        /// </summary>
        SystemsParallel,
    }
}
