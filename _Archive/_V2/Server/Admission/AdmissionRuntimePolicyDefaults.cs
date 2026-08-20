namespace UltraSim.Server.Admission;

public sealed class AdmissionRuntimePolicyDefaults : IAdmissionRuntimePolicy
{
    public static AdmissionRuntimePolicyDefaults Instance { get; } = new();

    private AdmissionRuntimePolicyDefaults()
    {
    }

    public void OnStepOpened(AdmissionStepOpenedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public AdmissionActionRouteResult EvaluateActionRoute(AdmissionActionRouteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return AdmissionActionRouteResult.Complete(context.Route.CompletesWithEvent);
    }
}
