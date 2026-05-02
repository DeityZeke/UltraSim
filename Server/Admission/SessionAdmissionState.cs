using UltraSim.Network;
using UltraSim.Server.Scripting;

namespace UltraSim.Server.Admission;

public sealed record SessionAdmissionState(
    ConnectionId ConnectionId,
    UltraSimScriptAdmissionFlowStepDescriptor Step,
    string PanelInstanceId,
    string WaitingForEvent,
    IReadOnlySet<string> TrustedCredentialActionIds);
