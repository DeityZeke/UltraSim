using System;

namespace UltraSim.ECS.Persistence
{
    internal sealed class EntityPersistenceCapturedSnapshot
    {
        public static readonly EntityPersistenceCapturedSnapshot Empty =
            new(0, 0, Array.Empty<EntityPersistenceCapturedGroup>());

        public EntityPersistenceCapturedSnapshot(
            int blockCount,
            int persistentEntityCount,
            EntityPersistenceCapturedGroup[] groups)
        {
            BlockCount = blockCount;
            PersistentEntityCount = persistentEntityCount;
            Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        }

        public int BlockCount { get; }
        public int PersistentEntityCount { get; }
        public EntityPersistenceCapturedGroup[] Groups { get; }
    }

    internal sealed class EntityPersistenceCapturedGroup
    {
        public EntityPersistenceCapturedGroup(
            PersistenceSignature signature,
            ulong[] entityPackedIds,
            EntityPersistenceCapturedSection[] sections)
        {
            Signature = signature ?? throw new ArgumentNullException(nameof(signature));
            EntityPackedIds = entityPackedIds ?? throw new ArgumentNullException(nameof(entityPackedIds));
            Sections = sections ?? throw new ArgumentNullException(nameof(sections));
        }

        public PersistenceSignature Signature { get; }
        public ulong[] EntityPackedIds { get; }
        public EntityPersistenceCapturedSection[] Sections { get; }
        public int EntityCount => EntityPackedIds.Length;
    }

    internal sealed class EntityPersistenceCapturedSection
    {
        public EntityPersistenceCapturedSection(
            PersistenceComponentDescriptor descriptor,
            ushort schemaVersion,
            Array values)
        {
            Descriptor = descriptor;
            SchemaVersion = schemaVersion;
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public PersistenceComponentDescriptor Descriptor { get; }
        public ushort SchemaVersion { get; }
        public Array Values { get; }
    }
}
