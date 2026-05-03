using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public sealed record ServerSelectedCharacterSession(
    ConnectionId ConnectionId,
    ulong AccountId,
    ulong CharacterId,
    string CharacterName,
    string PrefabKey,
    Entity Entity,
    bool UsedPrefabEntity);
