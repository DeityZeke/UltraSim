using System;
using System.Collections.Generic;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Registry of named account store backends.
    ///
    /// Built-in profiles are registered automatically.
    /// Third-party or custom stores register via Register() at application startup.
    ///
    /// Usage:
    ///   AccountStoreRegistry.Register(new AccountStoreProfile(
    ///       "MySqlite", "SQLite-backed store",
    ///       (dir, backups) => new SqliteAccountStore(dir, backups)));
    ///
    ///   // ServerAccountOptions:
    ///   new ServerAccountOptions(StorageProfile: "MySqlite", BackupCount: 3)
    /// </summary>
    public static class AccountStoreRegistry
    {
        public const string EncryptedBinary = "EncryptedBinary";
        public const string Json = "Json";

        private static readonly Dictionary<string, AccountStoreProfile> _profiles =
            new Dictionary<string, AccountStoreProfile>(StringComparer.OrdinalIgnoreCase);

        static AccountStoreRegistry()
        {
            Register(new AccountStoreProfile(
                EncryptedBinary,
                "AES-256-GCM encrypted binary (recommended default). Key stored in server.key.",
                (dataDir, backups) => new BinaryAccountStore(dataDir, backups)));

            Register(new AccountStoreProfile(
                Json,
                "Plaintext JSON — passwords hashed, usernames visible. Debug/inspection use only.",
                (dataDir, backups) => new JsonAccountStore(dataDir, backups)));
        }

        /// <summary>
        /// Registers a named store profile. Overwrites any existing entry with the same name.
        /// Call during application startup before any ServerAccountSystem is constructed.
        /// </summary>
        public static void Register(AccountStoreProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            _profiles[profile.Name] = profile;
        }

        /// <summary>
        /// Retrieves a registered profile by name (case-insensitive).
        /// Returns false if the name is not registered.
        /// </summary>
        public static bool TryGet(string name, out AccountStoreProfile profile)
            => _profiles.TryGetValue(name, out profile!);

        /// <summary>
        /// Creates a store from a registered profile name, pointed at dataDirectory,
        /// with the given backup slot count.
        /// Throws InvalidOperationException if the name is not registered.
        /// </summary>
        public static IAccountStore Create(string profileName, string dataDirectory, int backupCount)
        {
            if (!TryGet(profileName, out var profile))
                throw new InvalidOperationException(
                    $"No account store profile named '{profileName}' is registered. " +
                    $"Call AccountStoreRegistry.Register() before constructing ServerAccountSystem.");

            return profile.Create(dataDirectory, backupCount);
        }

        /// <summary>All currently registered profiles, for tooling/discovery.</summary>
        public static IEnumerable<AccountStoreProfile> All => _profiles.Values;
    }
}
