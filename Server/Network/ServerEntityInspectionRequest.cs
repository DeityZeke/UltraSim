using UltraSim.ECS;

namespace UltraSim.Server.Network;

public sealed record ServerEntityInspectionRequest(
    Entity Entity,
    ServerAccessEvaluationRequest? AccessRequest = null);
