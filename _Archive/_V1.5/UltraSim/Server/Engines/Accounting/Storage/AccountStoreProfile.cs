using System;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Descriptor for a named account store backend.
    /// Register custom backends via AccountStoreRegistry.Register().
    ///
    /// The factory receives (dataDirectory, backupCount) so each backend can
    /// implement its own rotation strategy using the server-owner's preference.
    /// </summary>
    public sealed class AccountStoreProfile
    {
        public string Name { get; }
        public string Description { get; }

        private readonly Func<string, int, IAccountStore> _factory;

        /// <param name="factory">
        /// (dataDirectory, backupCount) → IAccountStore.
        /// backupCount is the value from ServerAccountOptions.BackupCount:
        ///   -1 = infinite (never delete oldest), 0 = disabled, N = keep N slots.
        /// </param>
        public AccountStoreProfile(string name, string description, Func<string, int, IAccountStore> factory)
        {
            Name = name;
            Description = description;
            _factory = factory;
        }

        /// <summary>Creates a store instance for the given directory and backup count.</summary>
        public IAccountStore Create(string dataDirectory, int backupCount)
            => _factory(dataDirectory, backupCount);
    }
}
