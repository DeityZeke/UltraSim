using System;
using UltraSim.ECS.Persistence;

namespace UltraSim.Server
{
    public sealed class WorldPersistenceSaveSnapshot : IServerRuntimeSaveSnapshot
    {
        public string RootPath { get; private set; } = string.Empty;
        public SaveSetManifest Manifest { get; private set; }
        public byte[] TypeDatabaseBytes { get; private set; } = Array.Empty<byte>();
        public byte[] ManifestBytes { get; private set; } = Array.Empty<byte>();
        public WorldPersistenceSystemFileSnapshot[] SystemFiles { get; private set; } = Array.Empty<WorldPersistenceSystemFileSnapshot>();
        public TimeSpan StructuralFlushDuration { get; private set; }
        public TimeSpan TypeDatabaseSerializeDuration { get; private set; }
        public TimeSpan SystemsSerializeDuration { get; private set; }
        public TimeSpan EntitiesSerializeDuration { get; private set; }
        public TimeSpan ManifestSerializeDuration { get; private set; }
        internal EntityPersistenceCapturedSnapshot EntitySnapshot { get; private set; } = EntityPersistenceCapturedSnapshot.Empty;

        internal void Populate(
            string rootPath,
            SaveSetManifest manifest,
            byte[] typeDatabaseBytes,
            EntityPersistenceCapturedSnapshot entitySnapshot,
            byte[] manifestBytes,
            WorldPersistenceSystemFileSnapshot[] systemFiles,
            TimeSpan structuralFlushDuration,
            TimeSpan typeDatabaseSerializeDuration,
            TimeSpan systemsSerializeDuration,
            TimeSpan entitiesSerializeDuration,
            TimeSpan manifestSerializeDuration)
        {
            RootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
            Manifest = manifest;
            TypeDatabaseBytes = typeDatabaseBytes ?? throw new ArgumentNullException(nameof(typeDatabaseBytes));
            EntitySnapshot = entitySnapshot ?? throw new ArgumentNullException(nameof(entitySnapshot));
            ManifestBytes = manifestBytes ?? throw new ArgumentNullException(nameof(manifestBytes));
            SystemFiles = systemFiles ?? throw new ArgumentNullException(nameof(systemFiles));
            StructuralFlushDuration = structuralFlushDuration;
            TypeDatabaseSerializeDuration = typeDatabaseSerializeDuration;
            SystemsSerializeDuration = systemsSerializeDuration;
            EntitiesSerializeDuration = entitiesSerializeDuration;
            ManifestSerializeDuration = manifestSerializeDuration;
        }

        internal void Reset()
        {
            RootPath = string.Empty;
            Manifest = default;
            TypeDatabaseBytes = Array.Empty<byte>();
            EntitySnapshot = EntityPersistenceCapturedSnapshot.Empty;
            ManifestBytes = Array.Empty<byte>();
            SystemFiles = Array.Empty<WorldPersistenceSystemFileSnapshot>();
            StructuralFlushDuration = TimeSpan.Zero;
            TypeDatabaseSerializeDuration = TimeSpan.Zero;
            SystemsSerializeDuration = TimeSpan.Zero;
            EntitiesSerializeDuration = TimeSpan.Zero;
            ManifestSerializeDuration = TimeSpan.Zero;
        }
    }

    public readonly record struct WorldPersistenceSystemFileSnapshot(string FileStem, byte[] Payload);
}
