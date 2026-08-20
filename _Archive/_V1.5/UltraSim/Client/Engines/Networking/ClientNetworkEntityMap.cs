using System.Collections.Generic;
using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Client.Engines.Networking
{
    public sealed class ClientNetworkEntityMap
    {
        private readonly Dictionary<ulong, Entity> _entityByNetwork = new Dictionary<ulong, Entity>();
        private readonly Dictionary<Entity, NetworkEntityId> _networkByEntity = new Dictionary<Entity, NetworkEntityId>();

        public int Count => _entityByNetwork.Count;

        public void Clear()
        {
            _entityByNetwork.Clear();
            _networkByEntity.Clear();
        }

        public void Bind(NetworkEntityId networkEntityId, Entity localEntity)
        {
            if (_entityByNetwork.TryGetValue(networkEntityId.Value, out Entity previousLocalEntity))
                _networkByEntity.Remove(previousLocalEntity);

            if (_networkByEntity.TryGetValue(localEntity, out NetworkEntityId previousNetworkEntityId))
                _entityByNetwork.Remove(previousNetworkEntityId.Value);

            _entityByNetwork[networkEntityId.Value] = localEntity;
            _networkByEntity[localEntity] = networkEntityId;
        }

        public bool Unbind(NetworkEntityId networkEntityId)
        {
            if (!_entityByNetwork.Remove(networkEntityId.Value, out Entity entity))
                return false;

            _networkByEntity.Remove(entity);
            return true;
        }

        public bool TryGetEntity(NetworkEntityId networkEntityId, out Entity entity) =>
            _entityByNetwork.TryGetValue(networkEntityId.Value, out entity);

        public bool TryGetNetworkEntityId(Entity entity, out NetworkEntityId networkEntityId) =>
            _networkByEntity.TryGetValue(entity, out networkEntityId);
    }
}
