using UltraSim.ECS;
using UltraSim.Network;
using UltraSim.Server.Accounting;

namespace UltraSim.Server.Network;

public readonly record struct ServerMovementSessionSpawn(
    Entity Entity,
    uint NetworkEntityId);

public delegate ServerMovementSessionSpawn ServerMovementSessionSpawner(
    World world,
    ConnectionId connectionId);

public delegate void ServerMovementSessionDespawner(
    World world,
    ConnectionId connectionId,
    Entity entity);

public delegate void ServerAccountSessionEnder(
    ConnectionId connectionId,
    ulong accountId);

public delegate bool ServerAccountAccessMetadataProvider(
    ulong accountId,
    out ServerAccountRecord account);
