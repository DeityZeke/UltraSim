namespace UltraSim.Scripts.Packages.Admission;

public sealed record AdmissionFlowStepDescriptor(
    string OwnerKey,
    string InputEvent,
    string OutputEvent,
    string PanelProviderKey,
    int Priority = 0,
    bool BlocksGameplayInput = true);
