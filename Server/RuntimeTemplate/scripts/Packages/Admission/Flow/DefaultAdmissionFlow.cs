using System.Collections.Generic;

namespace UltraSim.Scripts.Packages.Admission;

public static class DefaultAdmissionFlow
{
    public static IReadOnlyList<AdmissionFlowStepDescriptor> Steps { get; } =
    [
        new AdmissionFlowStepDescriptor(
            "admission.default.login",
            AdmissionFlowEvents.ClientConnected,
            AdmissionFlowEvents.LoginVerified,
            AdmissionPanelProviderKeys.Login,
            Priority: 0),
        new AdmissionFlowStepDescriptor(
            "admission.default.character_select",
            AdmissionFlowEvents.LoginVerified,
            AdmissionFlowEvents.CharacterSelected,
            AdmissionPanelProviderKeys.CharacterSelect,
            Priority: 0),
        new AdmissionFlowStepDescriptor(
            "admission.default.character_create",
            AdmissionFlowEvents.CharacterCreateRequested,
            AdmissionFlowEvents.CharacterCreated,
            AdmissionPanelProviderKeys.CharacterCreate,
            Priority: 0)
    ];
}
