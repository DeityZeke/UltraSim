using UltraSim.ECS;

namespace UltraSim.Server.Network;

public readonly record struct ServerMovementBody(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY) : IComponent<ServerMovementBody>;
