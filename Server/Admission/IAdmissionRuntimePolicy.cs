namespace UltraSim.Server.Admission;

public interface IAdmissionRuntimePolicy
{
    void OnStepOpened(AdmissionStepOpenedContext context);

    AdmissionActionRouteResult EvaluateActionRoute(AdmissionActionRouteContext context);
}
