using System;
using System.IO;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Canonical path helpers for a save-set rooted at a given directory.
    ///
    /// Save-set layout:
    /// <code>
    /// {Root}/
    ///   meta.bin
    ///   types.tdb
    ///   Entities/
    ///     entities.idx
    ///     entities.bin
    ///   Systems/
    ///     {SystemName}.bin
    /// </code>
    /// </summary>
    public sealed class SaveSetPaths
    {
        private static readonly char[] InvalidSystemFileNameChars = Path.GetInvalidFileNameChars();

        public string Root        { get; }
        public string EntitiesDir { get; }
        public string SystemsDir  { get; }

        public string MetaBin     => Path.Combine(Root, "meta.bin");
        public string TypesDb     => Path.Combine(Root, "types.tdb");
        public string EntitiesIdx => Path.Combine(EntitiesDir, "entities.idx");
        public string EntitiesBin => Path.Combine(EntitiesDir, "entities.bin");

        public SaveSetPaths(string root)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            Root        = root;
            EntitiesDir = Path.Combine(root, "Entities");
            SystemsDir  = Path.Combine(root, "Systems");
        }

        /// <summary>Returns the canonical path for a named system's payload file.</summary>
        public string SystemFile(string systemName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
            if (systemName.IndexOfAny(InvalidSystemFileNameChars) >= 0 ||
                systemName.Contains(Path.DirectorySeparatorChar) ||
                systemName.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new ArgumentException(
                    $"System file name '{systemName}' contains invalid path characters.",
                    nameof(systemName));
            }

            return Path.Combine(SystemsDir, systemName + ".bin");
        }

        /// <summary>
        /// Creates the <see cref="EntitiesDir"/> and <see cref="SystemsDir"/> subdirectories
        /// under <see cref="Root"/> if they do not already exist.
        /// </summary>
        public void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(EntitiesDir);
            Directory.CreateDirectory(SystemsDir);
        }
    }
}
