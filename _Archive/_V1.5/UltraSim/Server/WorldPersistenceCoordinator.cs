using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;

namespace UltraSim.Server
{
    public sealed class WorldPersistenceCoordinator
    {
        private readonly WorldPersistenceOptions _options;
        private readonly WorldPersistenceSaveSnapshotPool _snapshotPool = new();

        public WorldPersistenceCoordinator(
            WorldPersistenceOptions? options = null,
            PersistenceTypeRegistry? registry = null)
        {
            _options = options ?? new WorldPersistenceOptions();
            Registry = registry ?? new PersistenceTypeRegistry();
        }

        public PersistenceTypeRegistry Registry { get; }
        public WorldPersistenceSaveTiming LastSaveTiming { get; private set; }
        public WorldPersistenceLoadTiming LastLoadTiming { get; private set; }

        public void RegisterComponent<T>() where T : unmanaged, IComponent<T>
            => Registry.Register<T>(PersistenceKind.Component);

        public void RegisterSystem<T>() where T : SystemBase
            => Registry.Register<T>(PersistenceKind.System);

        public SaveSetManifest Save(ServerRuntime runtime)
        {
            WorldPersistenceSaveSnapshot snapshot = CaptureSnapshot(runtime);
            return WriteSnapshot(snapshot);
        }

        public WorldPersistenceSaveSnapshot CaptureSnapshot(ServerRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);

            SealRegistryIfNeeded();

            SaveSetPaths livePaths = ResolvePaths(runtime);
            World world = runtime.PlayerWorld.World;

            var phaseStopwatch = Stopwatch.StartNew();
            world.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
            phaseStopwatch.Stop();
            TimeSpan structuralFlushDuration = phaseStopwatch.Elapsed;

            phaseStopwatch.Restart();
            byte[] typeDatabaseBytes;
            int typeRecordCount;
            using (var stream = new MemoryStream())
            {
                typeRecordCount = PersistenceTypeDatabaseSerializer.Write(stream, Registry);
                typeDatabaseBytes = stream.ToArray();
            }
            phaseStopwatch.Stop();
            TimeSpan typeDatabaseSerializeDuration = phaseStopwatch.Elapsed;

            WorldPersistenceSystemFileSnapshot[] systemFiles;
            EntityPersistenceCapturedSnapshot entitySnapshot;
            TimeSpan systemsSerializeDuration;
            TimeSpan entitiesSerializeDuration;

            if (_options.ParallelMode == WorldSaveParallelMode.SystemsParallel)
            {
                // Run entity and system capture concurrently; they serialize to independent buffers.
                var systemsStopwatch = new Stopwatch();
                var entitiesStopwatch = new Stopwatch();

                WorldPersistenceSystemFileSnapshot[] capturedSystemFiles = Array.Empty<WorldPersistenceSystemFileSnapshot>();
                EntityPersistenceCapturedSnapshot? capturedEntitySnapshot = null;

                Task systemsTask = Task.Run(() =>
                {
                    systemsStopwatch.Start();
                    SystemPersistenceFilePayload[] payloads = SystemPersistenceSerializer.CaptureAll(world, Registry);
                    capturedSystemFiles = new WorldPersistenceSystemFileSnapshot[payloads.Length];
                    for (int i = 0; i < payloads.Length; i++)
                        capturedSystemFiles[i] = new WorldPersistenceSystemFileSnapshot(payloads[i].FileStem, payloads[i].Payload);
                    systemsStopwatch.Stop();
                });
                Task entitiesTask = Task.Run(() =>
                {
                    entitiesStopwatch.Start();
                    capturedEntitySnapshot = EntityPersistenceWriter.Capture(world, Registry);
                    entitiesStopwatch.Stop();
                });

                try
                {
                    Task.WaitAll(systemsTask, entitiesTask);
                }
                catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
                    throw;
                }

                systemFiles = capturedSystemFiles;
                entitySnapshot = capturedEntitySnapshot!;
                systemsSerializeDuration = systemsStopwatch.Elapsed;
                entitiesSerializeDuration = entitiesStopwatch.Elapsed;
            }
            else
            {
                phaseStopwatch.Restart();
                SystemPersistenceFilePayload[] payloads = SystemPersistenceSerializer.CaptureAll(world, Registry);
                phaseStopwatch.Stop();
                systemsSerializeDuration = phaseStopwatch.Elapsed;
                systemFiles = new WorldPersistenceSystemFileSnapshot[payloads.Length];
                for (int i = 0; i < payloads.Length; i++)
                    systemFiles[i] = new WorldPersistenceSystemFileSnapshot(payloads[i].FileStem, payloads[i].Payload);

                phaseStopwatch.Restart();
                entitySnapshot = EntityPersistenceWriter.Capture(world, Registry);
                phaseStopwatch.Stop();
                entitiesSerializeDuration = phaseStopwatch.Elapsed;
            }

            var manifest = new SaveSetManifest(
                SavedAtUtcTicks: DateTime.UtcNow.Ticks,
                TypeRecordCount: typeRecordCount,
                SystemFileCount: systemFiles.Length,
                EntityBlockCount: entitySnapshot.BlockCount,
                PersistentEntityCount: entitySnapshot.PersistentEntityCount);

            phaseStopwatch.Restart();
            byte[] manifestBytes;
            using (var stream = new MemoryStream())
            {
                SaveSetManifestSerializer.Write(stream, manifest);
                manifestBytes = stream.ToArray();
            }
            phaseStopwatch.Stop();
            TimeSpan manifestSerializeDuration = phaseStopwatch.Elapsed;

            WorldPersistenceSaveSnapshot snapshot = _snapshotPool.Rent();
            snapshot.Populate(
                livePaths.Root,
                manifest,
                typeDatabaseBytes,
                entitySnapshot,
                manifestBytes,
                systemFiles,
                structuralFlushDuration,
                typeDatabaseSerializeDuration,
                systemsSerializeDuration,
                entitiesSerializeDuration,
                manifestSerializeDuration);
            return snapshot;
        }

        public SaveSetManifest WriteSnapshot(WorldPersistenceSaveSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var totalStopwatch = Stopwatch.StartNew();
            try
            {
                var typeDatabaseDuration = snapshot.TypeDatabaseSerializeDuration;
                var systemsDuration = snapshot.SystemsSerializeDuration;
                var entitiesDuration = snapshot.EntitiesSerializeDuration;
                var manifestDuration = snapshot.ManifestSerializeDuration;

                var phaseStopwatch = new Stopwatch();
                var livePaths = new SaveSetPaths(snapshot.RootPath);
                livePaths.EnsureDirectoriesExist();

                using var transaction = new WorldPersistenceSaveTransaction(livePaths);
                SaveSetPaths stagingPaths = transaction.StagingPaths;

                phaseStopwatch.Restart();
                File.WriteAllBytes(stagingPaths.TypesDb, snapshot.TypeDatabaseBytes);
                phaseStopwatch.Stop();
                typeDatabaseDuration += phaseStopwatch.Elapsed;

                phaseStopwatch.Restart();
                for (int i = 0; i < snapshot.SystemFiles.Length; i++)
                {
                    WorldPersistenceSystemFileSnapshot systemFile = snapshot.SystemFiles[i];
                    File.WriteAllBytes(stagingPaths.SystemFile(systemFile.FileStem), systemFile.Payload);
                }
                phaseStopwatch.Stop();
                systemsDuration += phaseStopwatch.Elapsed;

                phaseStopwatch.Restart();
                EntityPersistenceWriter.WriteSnapshot(snapshot.EntitySnapshot, stagingPaths);
                phaseStopwatch.Stop();
                entitiesDuration += phaseStopwatch.Elapsed;

                phaseStopwatch.Restart();
                File.WriteAllBytes(stagingPaths.MetaBin, snapshot.ManifestBytes);
                phaseStopwatch.Stop();
                manifestDuration += phaseStopwatch.Elapsed;

                phaseStopwatch.Restart();
                transaction.Commit();
                phaseStopwatch.Stop();
                TimeSpan commitDuration = phaseStopwatch.Elapsed;

                totalStopwatch.Stop();
                LastSaveTiming = new WorldPersistenceSaveTiming(
                    totalStopwatch.Elapsed,
                    snapshot.StructuralFlushDuration,
                    typeDatabaseDuration,
                    systemsDuration,
                    entitiesDuration,
                    manifestDuration,
                    commitDuration,
                    snapshot.Manifest.TypeRecordCount,
                    snapshot.Manifest.SystemFileCount,
                    snapshot.Manifest.EntityBlockCount,
                    snapshot.Manifest.PersistentEntityCount);
                return snapshot.Manifest;
            }
            finally
            {
                _snapshotPool.Release(snapshot);
            }
        }

        public WorldPersistenceLoadSummary Load(ServerRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            var totalStopwatch = Stopwatch.StartNew();
            var phaseStopwatch = new Stopwatch();

            SealRegistryIfNeeded();

            SaveSetPaths paths = ResolvePaths(runtime);
            var summary = new WorldPersistenceLoadSummary
            {
                RootPath = paths.Root,
            };

            bool hasMeta = File.Exists(paths.MetaBin);
            bool hasTypes = File.Exists(paths.TypesDb);
            bool hasOptionalArtifacts = HasAnyOptionalArtifacts(paths);

            if (!hasMeta && !hasTypes && !hasOptionalArtifacts)
            {
                totalStopwatch.Stop();
                LastLoadTiming = new WorldPersistenceLoadTiming(
                    totalStopwatch.Elapsed,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    0,
                    0,
                    0);
                return summary;
            }

            if (!hasMeta || !hasTypes)
                throw new InvalidDataException("Save set is incomplete: both meta.bin and types.tdb are required to load world persistence.");

            summary.SaveSetPresent = true;
            phaseStopwatch.Restart();
            SaveSetManifest manifest = SaveSetManifestSerializer.Read(paths.MetaBin);
            phaseStopwatch.Stop();
            TimeSpan manifestReadDuration = phaseStopwatch.Elapsed;
            summary.Manifest = manifest;

            phaseStopwatch.Restart();
            PersistenceTypeRecord[] typeRecords = PersistenceTypeDatabaseSerializer.ReadAndValidate(paths.TypesDb, Registry);
            phaseStopwatch.Stop();
            TimeSpan typeValidationDuration = phaseStopwatch.Elapsed;
            if (typeRecords.Length != manifest.TypeRecordCount)
            {
                throw new InvalidDataException(
                    $"Save manifest type count mismatch: expected {manifest.TypeRecordCount}, got {typeRecords.Length}.");
            }

            World world = runtime.PlayerWorld.World;
            phaseStopwatch.Restart();
            SystemPersistenceLoadResult systemResult = SystemPersistenceSerializer.ReadAll(world, paths, Registry);
            phaseStopwatch.Stop();
            TimeSpan systemsLoadDuration = phaseStopwatch.Elapsed;

            phaseStopwatch.Restart();
            EntityPersistenceLoadResult entityResult = EntityPersistenceReader.ReadCreatePhase(world, paths, Registry);
            phaseStopwatch.Stop();
            TimeSpan entityCreateDuration = phaseStopwatch.Elapsed;

            if (systemResult.LoadedSystemCount + systemResult.MissingFiles.Count != manifest.SystemFileCount)
            {
                throw new InvalidDataException(
                    $"Save manifest system file count mismatch: expected {manifest.SystemFileCount}, got {systemResult.LoadedSystemCount + systemResult.MissingFiles.Count}.");
            }

            HandleMissingOptionalFiles(systemResult.MissingFiles, entityResult.MissingFiles);

            for (int i = 0; i < systemResult.MissingFiles.Count; i++)
                summary.MissingOptionalFiles.Add(systemResult.MissingFiles[i]);
            for (int i = 0; i < entityResult.MissingFiles.Count; i++)
                summary.MissingOptionalFiles.Add(entityResult.MissingFiles[i]);

            summary.LoadedSystemCount = systemResult.LoadedSystemCount;
            TimeSpan hydrateDuration = TimeSpan.Zero;
            TimeSpan fixupDuration = TimeSpan.Zero;

            if (entityResult.MissingFiles.Count == 0)
            {
                if (entityResult.BlockCount != manifest.EntityBlockCount)
                {
                    throw new InvalidDataException(
                        $"Save manifest entity block count mismatch: expected {manifest.EntityBlockCount}, got {entityResult.BlockCount}.");
                }

                if (entityResult.LoadedEntityCount != manifest.PersistentEntityCount)
                {
                    throw new InvalidDataException(
                            $"Save manifest persistent entity count mismatch: expected {manifest.PersistentEntityCount}, got {entityResult.LoadedEntityCount}.");
                }

                phaseStopwatch.Restart();
                EntityPersistenceReader.Hydrate(entityResult, paths);
                phaseStopwatch.Stop();
                hydrateDuration = phaseStopwatch.Elapsed;

                phaseStopwatch.Restart();
                EntityPersistenceReader.FixupEntityReferences(entityResult);
                phaseStopwatch.Stop();
                fixupDuration = phaseStopwatch.Elapsed;
                summary.LoadedEntityCount = entityResult.LoadedEntityCount;
            }

            totalStopwatch.Stop();
            LastLoadTiming = new WorldPersistenceLoadTiming(
                totalStopwatch.Elapsed,
                manifestReadDuration,
                typeValidationDuration,
                systemsLoadDuration,
                entityCreateDuration,
                hydrateDuration,
                fixupDuration,
                summary.LoadedSystemCount,
                summary.LoadedEntityCount,
                summary.MissingOptionalFiles.Count);
            return summary;
        }

        private void HandleMissingOptionalFiles(
            System.Collections.Generic.IReadOnlyList<string> missingSystemFiles,
            System.Collections.Generic.IReadOnlyList<string> missingEntityFiles)
        {
            bool hasMissingSystems = missingSystemFiles.Count > 0;
            bool hasMissingEntities = missingEntityFiles.Count > 0;

            if (!hasMissingSystems && !hasMissingEntities)
                return;

            WorldPersistenceRecoveryMode effectiveMode = _options.RecoveryInteraction?
                .ResolveMissingOptionalSections(new WorldPersistenceRecoveryRequest(
                    RootPath: ResolveRootPath(),
                    MissingSystemFiles: missingSystemFiles,
                    MissingEntityFiles: missingEntityFiles,
                    ConfiguredMode: _options.RecoveryMode))
                ?? _options.RecoveryMode;

            bool allowMissingSystems = effectiveMode == WorldPersistenceRecoveryMode.ContinueWithoutMissingSystems ||
                                       effectiveMode == WorldPersistenceRecoveryMode.ContinueWithoutAllMissingOptionalSections;
            bool allowMissingEntities = effectiveMode == WorldPersistenceRecoveryMode.ContinueWithoutEntities ||
                                        effectiveMode == WorldPersistenceRecoveryMode.ContinueWithoutAllMissingOptionalSections;

            if ((hasMissingSystems && !allowMissingSystems) ||
                (hasMissingEntities && !allowMissingEntities))
            {
                string[] missing = missingSystemFiles.Concat(missingEntityFiles).ToArray();
                throw new InvalidDataException(
                    $"Save set is incomplete: missing optional persistence sections. Missing: {string.Join(", ", missing)}.");
            }
        }

        private SaveSetPaths ResolvePaths(ServerRuntime runtime)
        {
            return new SaveSetPaths(ResolveRootPath(runtime.Options.SaveDirectory));
        }

        private string ResolveRootPath(string? runtimeSaveDirectory = null)
        {
            string root = _options.SaveDirectory
                ?? runtimeSaveDirectory
                ?? Path.Combine(AppContext.BaseDirectory, "Saves");

            if (!Path.IsPathRooted(root))
                root = Path.GetFullPath(root, AppContext.BaseDirectory);

            return root;
        }

        private static bool HasAnyOptionalArtifacts(SaveSetPaths paths)
        {
            if (File.Exists(paths.EntitiesIdx) || File.Exists(paths.EntitiesBin))
                return true;

            return Directory.Exists(paths.SystemsDir) &&
                   Directory.EnumerateFiles(paths.SystemsDir, "*.bin").Any();
        }

        private void SealRegistryIfNeeded()
        {
            if (!Registry.IsSealed)
                Registry.Seal();
        }
    }
}
