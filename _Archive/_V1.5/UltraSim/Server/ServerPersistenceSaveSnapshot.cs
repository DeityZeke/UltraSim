using System;
using UltraSim.Server.Engines.Accounting;

namespace UltraSim.Server
{
    public sealed class ServerPersistenceSaveSnapshot : IServerRuntimeSaveSnapshot
    {
        public ServerPersistenceSaveSnapshot(
            AccountPersistenceSaveSnapshot accountSnapshot,
            WorldPersistenceSaveSnapshot worldSnapshot)
        {
            AccountSnapshot = accountSnapshot ?? throw new ArgumentNullException(nameof(accountSnapshot));
            WorldSnapshot = worldSnapshot ?? throw new ArgumentNullException(nameof(worldSnapshot));
        }

        public AccountPersistenceSaveSnapshot AccountSnapshot { get; }
        public WorldPersistenceSaveSnapshot WorldSnapshot { get; }
    }
}
