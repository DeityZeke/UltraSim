using System.Collections.Generic;
using UltraSim.ECS.Persistence;

namespace UltraSim.Server
{
    public sealed class WorldPersistenceLoadSummary
    {
        public string RootPath { get; internal set; } = string.Empty;
        public bool SaveSetPresent { get; internal set; }
        public SaveSetManifest? Manifest { get; internal set; }
        public int LoadedSystemCount { get; internal set; }
        public int LoadedEntityCount { get; internal set; }
        public List<string> MissingOptionalFiles { get; } = new();
    }
}
