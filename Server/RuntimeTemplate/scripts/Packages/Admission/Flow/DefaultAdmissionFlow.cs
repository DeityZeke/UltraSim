using System.Collections.Generic;
using UltraSim.Server.Scripting;

namespace UltraSim.Scripts.Packages.Admission;

public static class DefaultAdmissionFlow
{
    public static IReadOnlyList<UltraSimScriptAdmissionFlowStepDescriptor> Steps { get; } =
    [
        new UltraSimScriptAdmissionFlowStepDescriptor(
            "admission.default.login",
            AdmissionFlowEvents.ClientConnected,
            AdmissionFlowEvents.LoginVerified,
            AdmissionPanelProviderKeys.Login,
            Priority: 0),
        new UltraSimScriptAdmissionFlowStepDescriptor(
            "admission.default.character_select",
            AdmissionFlowEvents.LoginVerified,
            AdmissionFlowEvents.CharacterSelected,
            AdmissionPanelProviderKeys.CharacterSelect,
            Priority: 0),
        new UltraSimScriptAdmissionFlowStepDescriptor(
            "admission.default.character_create",
            AdmissionFlowEvents.CharacterCreateRequested,
            AdmissionFlowEvents.CharacterCreated,
            AdmissionPanelProviderKeys.CharacterCreate,
            Priority: 0)
    ];
}
