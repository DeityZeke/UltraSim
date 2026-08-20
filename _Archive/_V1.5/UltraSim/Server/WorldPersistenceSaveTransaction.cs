using System;
using System.Collections.Generic;
using System.IO;
using UltraSim.ECS.Persistence;

namespace UltraSim.Server
{
    internal sealed class WorldPersistenceSaveTransaction : IDisposable
    {
        private readonly string _root;
        private readonly string _stagingRoot;
        private bool _committed;

        public WorldPersistenceSaveTransaction(SaveSetPaths livePaths)
        {
            ArgumentNullException.ThrowIfNull(livePaths);

            _root = livePaths.Root;
            _stagingRoot = Path.Combine(_root, $".staging-{Guid.NewGuid():N}");
            StagingPaths = new SaveSetPaths(_stagingRoot);
            StagingPaths.EnsureDirectoriesExist();
        }

        public SaveSetPaths StagingPaths { get; }

        public void Commit()
        {
            if (_committed)
                return;

            SaveSetPaths livePaths = new SaveSetPaths(_root);
            livePaths.EnsureDirectoriesExist();

            PromoteNonManifestFiles(livePaths);
            PromoteFile(StagingPaths.MetaBin, livePaths.MetaBin);

            _committed = true;
            CleanupStagingRoot();
        }

        public void Dispose()
        {
            if (_committed)
                return;

            CleanupStagingRoot();
        }

        private void PromoteNonManifestFiles(SaveSetPaths livePaths)
        {
            PromoteFile(StagingPaths.TypesDb, livePaths.TypesDb);
            PromoteFile(StagingPaths.EntitiesIdx, livePaths.EntitiesIdx);
            PromoteFile(StagingPaths.EntitiesBin, livePaths.EntitiesBin);

            foreach (string stagedSystemFile in Directory.EnumerateFiles(StagingPaths.SystemsDir, "*.bin"))
            {
                string liveSystemFile = Path.Combine(livePaths.SystemsDir, Path.GetFileName(stagedSystemFile));
                PromoteFile(stagedSystemFile, liveSystemFile);
            }
        }

        private static void PromoteFile(string sourcePath, string destinationPath)
        {
            string? destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir))
                Directory.CreateDirectory(destinationDir);

            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Expected staged persistence artifact was not found.", sourcePath);

            if (File.Exists(destinationPath))
            {
                string backupPath = destinationPath + ".bak";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);

                File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors: true);
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                return;
            }

            File.Move(sourcePath, destinationPath);
        }

        private void CleanupStagingRoot()
        {
            if (!Directory.Exists(_stagingRoot))
                return;

            Directory.Delete(_stagingRoot, recursive: true);
        }
    }
}
