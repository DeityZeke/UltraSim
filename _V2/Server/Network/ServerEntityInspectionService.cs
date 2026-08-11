using UltraSim.ECS;

namespace UltraSim.Server.Network;

public sealed class ServerEntityInspectionService
{
    private readonly World _world;
    private readonly ServerAccessRuleRegistry _accessRules;

    public ServerEntityInspectionService(World world, ServerAccessRuleRegistry? accessRules = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _accessRules = accessRules ?? new ServerAccessRuleRegistry();
    }

    public ServerEntityInspectionResult Inspect(ServerEntityInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ServerAccessEvaluationResult? accessDecision = null;
        if (request.AccessRequest is not null)
        {
            accessDecision = _accessRules.Evaluate(request.AccessRequest);
            if (!accessDecision.IsAllowed)
                return ServerEntityInspectionResult.PermissionDenied(accessDecision);
        }

        return _world.TryInspectEntity(request.Entity, out EntityInspectionSnapshot snapshot)
            ? ServerEntityInspectionResult.Success(snapshot, accessDecision)
            : ServerEntityInspectionResult.EntityNotFound(accessDecision);
    }
}
