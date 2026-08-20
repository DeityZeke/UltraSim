namespace UltraSim.Server
{
    public sealed record WorldPersistenceOptions(
        string? SaveDirectory = null,
        WorldPersistenceRecoveryMode RecoveryMode = WorldPersistenceRecoveryMode.Abort,
        IWorldPersistenceRecoveryInteraction? RecoveryInteraction = null,
        WorldSaveParallelMode ParallelMode = WorldSaveParallelMode.Serial);
}
