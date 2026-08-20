using System.Collections.Generic;

namespace UltraSim.Server
{
    public interface IWorldPersistenceRecoveryInteraction
    {
        WorldPersistenceRecoveryMode? ResolveMissingOptionalSections(WorldPersistenceRecoveryRequest request);
    }

    public sealed record WorldPersistenceRecoveryRequest(
        string RootPath,
        IReadOnlyList<string> MissingSystemFiles,
        IReadOnlyList<string> MissingEntityFiles,
        WorldPersistenceRecoveryMode ConfiguredMode);
}
