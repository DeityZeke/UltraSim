using System;

namespace UltraSim.Server
{
    internal sealed class WorldPersistenceSaveSnapshotPool
    {
        private readonly object _gate = new();
        private readonly WorldPersistenceSaveSnapshot _bufferA = new();
        private readonly WorldPersistenceSaveSnapshot _bufferB = new();
        private bool _bufferAInUse;
        private bool _bufferBInUse;

        public WorldPersistenceSaveSnapshot Rent()
        {
            lock (_gate)
            {
                if (!_bufferAInUse)
                {
                    _bufferAInUse = true;
                    return _bufferA;
                }

                if (!_bufferBInUse)
                {
                    _bufferBInUse = true;
                    return _bufferB;
                }
            }

            return new WorldPersistenceSaveSnapshot();
        }

        public void Release(WorldPersistenceSaveSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            snapshot.Reset();

            lock (_gate)
            {
                if (ReferenceEquals(snapshot, _bufferA))
                {
                    _bufferAInUse = false;
                    return;
                }

                if (ReferenceEquals(snapshot, _bufferB))
                {
                    _bufferBInUse = false;
                }
            }
        }
    }
}
