using System.Collections.Generic;

namespace UltraSim.ECS.Persistence
{
    public sealed class EntityPersistenceLoadResult
    {
        public int BlockCount { get; internal set; }
        public int LoadedEntityCount { get; internal set; }
        public EntityRemapTable RemapTable { get; } = new EntityRemapTable();
        public List<string> MissingFiles { get; } = new List<string>();

        internal EntityPersistenceLoadBlock[] Blocks { get; set; } = System.Array.Empty<EntityPersistenceLoadBlock>();
    }
}
