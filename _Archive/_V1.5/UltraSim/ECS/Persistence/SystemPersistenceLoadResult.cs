using System.Collections.Generic;

namespace UltraSim.ECS.Persistence
{
    public sealed class SystemPersistenceLoadResult
    {
        public int LoadedSystemCount { get; internal set; }
        public List<string> MissingFiles { get; } = new List<string>();
    }
}
