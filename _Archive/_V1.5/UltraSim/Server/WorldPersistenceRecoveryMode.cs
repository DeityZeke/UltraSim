namespace UltraSim.Server
{
    public enum WorldPersistenceRecoveryMode
    {
        Abort = 0,
        ContinueWithoutEntities = 1,
        ContinueWithoutMissingSystems = 2,
        ContinueWithoutAllMissingOptionalSections = 3,
    }
}
