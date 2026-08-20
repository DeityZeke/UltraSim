using System;
using UltraSim.Server.Scripting;
using UltraSim.UI;

namespace UltraSim.Scripts.Packages.Admission;

public sealed class AdmissionModule : IUltraSimScriptModule
{
    private const string ServerIdentity = "runtime/admission";

    public void Configure(IUltraSimScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        RegisterPanelProvider(
            context,
            AdmissionPanelProviderKeys.Login,
            () => AdmissionPanels.LoginPanel(ServerIdentity));
        RegisterPanelProvider(
            context,
            AdmissionPanelProviderKeys.CharacterSelect,
            () => AdmissionPanels.CharacterSelectPanel(ServerIdentity, []).Panel);
        RegisterPanelProvider(
            context,
            AdmissionPanelProviderKeys.CharacterCreate,
            () => AdmissionPanels.CharacterCreatePanel(ServerIdentity));
        foreach (UltraSimScriptAdmissionFlowStepDescriptor step in DefaultAdmissionFlow.Steps)
        {
            context.Admission.RegisterStep(step);
        }

        UltraSimRuntimeContentGroupOperationResult group = context.ContentGroups.Register(
            new UltraSimRuntimeContentGroupDescriptor(
                "packages/admission",
                UltraSimRuntimeContentGroupKind.ScriptPackage,
                "Default Admission",
                "Login, character selection, and character creation flow.",
                "scripts/Packages/Admission",
                "1"));
        if (!group.Accepted &&
            group.Status != UltraSimRuntimeContentGroupOperationStatus.DuplicateKey)
        {
            throw new InvalidOperationException(group.Error);
        }

        context.Logger.Info("Default admission package configured.");
    }

    public void Initialize(IUltraSimScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Logger.Info($"Default admission flow ready ({DefaultAdmissionFlow.Steps.Count} steps).");
    }

    private static void RegisterPanelProvider(
        IUltraSimScriptContext context,
        string key,
        Func<UiBuiltPanel> provider)
    {
        if (!context.UiPanels.RegisterPanelProvider(key, provider))
        {
            throw new InvalidOperationException($"Admission panel provider '{key}' is already registered.");
        }
    }
}
