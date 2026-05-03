using System.Collections.Generic;
using UltraSim.Server.Scripting;
using UltraSim.UI;

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
            Priority: 0,
            FailureStatusSlotId: AdmissionPanels.LoginStatusSlotId),
        new UltraSimScriptAdmissionFlowStepDescriptor(
            "admission.default.character_select",
            AdmissionFlowEvents.LoginVerified,
            AdmissionFlowEvents.CharacterSelected,
            AdmissionPanelProviderKeys.CharacterSelect,
            Priority: 0,
            ActionRoutes:
            [
                new UltraSimScriptAdmissionActionRouteDescriptor(
                    AdmissionPanels.CharacterSelectPlayActionId,
                    AdmissionFlowEvents.EnterWorld,
                    UiActionContextKind.Row),
                new UltraSimScriptAdmissionActionRouteDescriptor(
                    AdmissionPanels.CharacterSelectCreateActionId,
                    AdmissionFlowEvents.CharacterCreateRequested)
            ]),
        new UltraSimScriptAdmissionFlowStepDescriptor(
            "admission.default.character_create",
            AdmissionFlowEvents.CharacterCreateRequested,
            AdmissionFlowEvents.CharacterCreated,
            AdmissionPanelProviderKeys.CharacterCreate,
            Priority: 0,
            ActionRoutes:
            [
                new UltraSimScriptAdmissionActionRouteDescriptor(
                    AdmissionPanels.CharacterCreateSubmitActionId,
                    AdmissionFlowEvents.EnterWorld),
                new UltraSimScriptAdmissionActionRouteDescriptor(
                    AdmissionPanels.CharacterCreateCancelActionId,
                    AdmissionFlowEvents.LoginVerified)
            ])
    ];
}
