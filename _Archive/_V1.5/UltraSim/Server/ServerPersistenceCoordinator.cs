using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace UltraSim.Server
{
    public sealed class ServerPersistenceCoordinator
    {
        private readonly ServerAccountPersistenceCoordinator _accountCoordinator;
        private readonly WorldPersistenceCoordinator _worldCoordinator;

        public ServerPersistenceCoordinator(
            ServerAccountPersistenceCoordinator accountCoordinator,
            WorldPersistenceCoordinator worldCoordinator)
        {
            _accountCoordinator = accountCoordinator ?? throw new ArgumentNullException(nameof(accountCoordinator));
            _worldCoordinator = worldCoordinator ?? throw new ArgumentNullException(nameof(worldCoordinator));
        }

        public void Load(ServerRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            _accountCoordinator.Load(runtime);
            _worldCoordinator.Load(runtime);
        }

        public void Save(ServerRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            ServerPersistenceSaveSnapshot snapshot = CaptureSnapshot(runtime);
            WriteSnapshot(snapshot);
        }

        public ServerPersistenceSaveSnapshot CaptureSnapshot(ServerRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            return new ServerPersistenceSaveSnapshot(
                _accountCoordinator.CaptureSnapshot(runtime),
                _worldCoordinator.CaptureSnapshot(runtime));
        }

        public void WriteSnapshot(ServerPersistenceSaveSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            Task accountTask = Task.Run(() => _accountCoordinator.WriteSnapshot(snapshot.AccountSnapshot));
            Task worldTask = Task.Run(() => _worldCoordinator.WriteSnapshot(snapshot.WorldSnapshot));

            try
            {
                Task.WaitAll(accountTask, worldTask);
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
            {
                ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
                throw;
            }
        }
    }
}
