using UltraSim.ECS;

namespace UltraSim.Server.Network;

public sealed record ServerEntityInspectionResult(
    ServerEntityInspectionStatus Status,
    EntityInspectionSnapshot? Snapshot,
    ServerAccessEvaluationResult? AccessDecision,
    string Message)
{
    public bool Succeeded => Status == ServerEntityInspectionStatus.Success;

    public static ServerEntityInspectionResult Success(
        EntityInspectionSnapshot snapshot,
        ServerAccessEvaluationResult? accessDecision = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ServerEntityInspectionResult(
            ServerEntityInspectionStatus.Success,
            snapshot,
            accessDecision,
            string.Empty);
    }

    public static ServerEntityInspectionResult EntityNotFound(ServerAccessEvaluationResult? accessDecision = null)
    {
        return new ServerEntityInspectionResult(
            ServerEntityInspectionStatus.EntityNotFound,
            null,
            accessDecision,
            "Entity not found.");
    }

    public static ServerEntityInspectionResult PermissionDenied(ServerAccessEvaluationResult accessDecision)
    {
        ArgumentNullException.ThrowIfNull(accessDecision);
        return new ServerEntityInspectionResult(
            ServerEntityInspectionStatus.PermissionDenied,
            null,
            accessDecision,
            "Permission denied.");
    }
}
