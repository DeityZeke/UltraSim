using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public delegate bool ServerEcsMovementStateProjector(
    World world,
    Entity entity,
    uint networkEntityId,
    ushort lastProcessedInputSequence,
    ulong serverTick,
    out MovementStatePacket packet);
