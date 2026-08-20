using System.Collections.Generic;
using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Engines.Networking
{
    public sealed class ServerSessionEntityBindings
    {
        private readonly Dictionary<long, Entity> _entityBySession = new Dictionary<long, Entity>();
        private readonly Dictionary<Entity, long> _sessionByEntity = new Dictionary<Entity, long>();
        private readonly Dictionary<long, Entity> _entityByOpaqueIdentity = new Dictionary<long, Entity>();

        public int SessionBindingCount => _entityBySession.Count;

        public int OpaqueIdentityBindingCount => _entityByOpaqueIdentity.Count;

        public void Clear()
        {
            _entityBySession.Clear();
            _sessionByEntity.Clear();
            _entityByOpaqueIdentity.Clear();
        }

        public void BindSessionEntity(long sessionId, Entity entity)
        {
            if (_entityBySession.TryGetValue(sessionId, out Entity previousEntity))
                _sessionByEntity.Remove(previousEntity);

            if (_sessionByEntity.TryGetValue(entity, out long previousSessionId))
                _entityBySession.Remove(previousSessionId);

            _entityBySession[sessionId] = entity;
            _sessionByEntity[entity] = sessionId;
        }

        public bool UnbindSessionEntity(long sessionId)
        {
            if (!_entityBySession.Remove(sessionId, out Entity entity))
                return false;

            _sessionByEntity.Remove(entity);
            return true;
        }

        public bool UnbindEntity(Entity entity)
        {
            if (!_sessionByEntity.Remove(entity, out long sessionId))
                return false;

            _entityBySession.Remove(sessionId);
            return true;
        }

        public bool TryGetEntityForSession(long sessionId, out Entity entity) =>
            _entityBySession.TryGetValue(sessionId, out entity);

        public bool TryGetSessionForEntity(Entity entity, out long sessionId) =>
            _sessionByEntity.TryGetValue(entity, out sessionId);

        public bool TryGetEntityForConnection(
            NetworkSessionManager sessions,
            int connectionId,
            out Entity entity)
        {
            if (!sessions.TryGetSessionIdByConnection(connectionId, out long sessionId))
            {
                entity = default;
                return false;
            }

            return _entityBySession.TryGetValue(sessionId, out entity);
        }

        public void BindOpaqueIdentityEntity(long opaqueGameIdentityKey, Entity entity)
        {
            if (opaqueGameIdentityKey == 0)
                return;

            _entityByOpaqueIdentity[opaqueGameIdentityKey] = entity;
        }

        public bool TryGetEntityForOpaqueIdentity(long opaqueGameIdentityKey, out Entity entity)
        {
            if (opaqueGameIdentityKey == 0)
            {
                entity = default;
                return false;
            }

            return _entityByOpaqueIdentity.TryGetValue(opaqueGameIdentityKey, out entity);
        }

        public bool UnbindOpaqueIdentity(long opaqueGameIdentityKey)
        {
            if (opaqueGameIdentityKey == 0)
                return false;

            return _entityByOpaqueIdentity.Remove(opaqueGameIdentityKey);
        }
    }
}
