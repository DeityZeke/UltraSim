using System;
using System.Diagnostics;
using System.IO;
using UltraSim.Server.Engines.Accounting;

namespace UltraSim.Server
{
    public sealed class ServerAccountPersistenceCoordinator
    {
        private readonly IAccountStore _store;

        public ServerAccountPersistenceCoordinator(
            ServerAccountOptions? options = null,
            string? serverDataRoot = null)
        {
            Options = options ?? ServerAccountOptions.Default;
            string dataPath = Path.Combine(
                serverDataRoot ?? AppContext.BaseDirectory,
                Options.AccountDataPath);
            _store = AccountStoreRegistry.Create(Options.StorageProfile, dataPath, Options.BackupCount);
        }

        public ServerAccountOptions Options { get; }
        public AccountPersistenceTiming LastLoadTiming { get; private set; }
        public AccountPersistenceTiming LastSaveTiming { get; private set; }

        public void Load(ServerRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            var stopwatch = Stopwatch.StartNew();
            runtime.Accounts.LoadFromStore(_store);
            stopwatch.Stop();
            LastLoadTiming = new AccountPersistenceTiming(stopwatch.Elapsed);
        }

        public void Save(ServerRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            AccountPersistenceSaveSnapshot snapshot = CaptureSnapshot(runtime);
            WriteSnapshot(snapshot);
        }

        public AccountPersistenceSaveSnapshot CaptureSnapshot(ServerRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            return runtime.Accounts.CaptureSnapshot();
        }

        public void WriteSnapshot(AccountPersistenceSaveSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var stopwatch = Stopwatch.StartNew();
            _store.WriteAsync(snapshot.Accounts, snapshot.Characters, snapshot.IpBans).GetAwaiter().GetResult();
            stopwatch.Stop();
            LastSaveTiming = new AccountPersistenceTiming(stopwatch.Elapsed);
        }
    }
}
