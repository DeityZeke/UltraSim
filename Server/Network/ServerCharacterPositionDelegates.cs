using UltraSim.ECS;
using UltraSim.Network;
using UltraSim.Server.Accounting;

namespace UltraSim.Server.Network;

public delegate bool ServerCharacterPositionProjector(
    World world,
    Entity entity,
    out CharacterPositionSnapshot position);

public delegate bool ServerCharacterPositionApplier(
    World world,
    Entity entity,
    CharacterPositionSnapshot position);

public delegate bool ServerSelectedCharacterSnapshotWriter(
    ConnectionId connectionId,
    ServerSelectedCharacterSession selectedCharacter,
    CharacterPositionSnapshot position,
    DateTimeOffset nowUtc);
