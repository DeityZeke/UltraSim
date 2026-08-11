namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptAdmissionRegistry
{
    bool RegisterStep(UltraSimScriptAdmissionFlowStepDescriptor descriptor);
}
