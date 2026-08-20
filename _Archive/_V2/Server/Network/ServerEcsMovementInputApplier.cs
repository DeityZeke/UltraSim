using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public delegate void ServerEcsMovementInputApplier(
    World world,
    Entity entity,
    in MovementInputPacket input,
    float deltaSeconds);
