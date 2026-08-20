using System.Collections.Generic;
using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Engines.Networking
{
    public sealed class ServerNetworkEntityMap
    {
        private readonly Dictionary<Entity, NetworkEntityId> _networkByEntity = new Dictionary<Entity, NetworkEntityId>();
        private readonly Dictionary<ulong, Entity> _entityByNetwork = new Dictionary<ulong, Entity>();
        private ulong _nextNetworkEntityId = 1;

        public int Count => _networkByEntity.Count;

        public void Clear()
        {
            _networkByEntity.Clear();
            _entityByNetwork.Clear();
            _nextNetworkEntityId = 1;
        }

        public bool TryGetNetworkEntityId(Entity entity, out NetworkEntityId networkEntityId) =>
            _networkByEntity.TryGetValue(entity, out networkEntityId);

        public bool TryGetEntity(NetworkEntityId networkEntityId, out Entity entity) =>
            _entityByNetwork.TryGetValue(networkEntityId.Value, out entity);

        public NetworkEntityId GetOrAssignNetworkEntityId(Entity entity)
        {
            if (_networkByEntity.TryGetValue(entity, out NetworkEntityId existing))
                return existing;

            var created = new NetworkEntityId(_nextNetworkEntityId++);
            _networkByEntity.Add(entity, created);
            _entityByNetwork.Add(created.Value, entity);
            return created;
        }

        public bool Remove(Entity entity)
        {
            if (!_networkByEntity.Remove(entity, out NetworkEntityId networkEntityId))
                return false;

            _entityByNetwork.Remove(networkEntityId.Value);
            return true;
        }
    }
}
