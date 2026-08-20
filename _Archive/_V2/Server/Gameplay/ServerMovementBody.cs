using UltraSim.ECS;

namespace UltraSim.Server.Gameplay;

public readonly record struct ServerMovementBody(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY) : IComponent<ServerMovementBody>;
