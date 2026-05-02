namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptAdmissionFlowStepDescriptor(
    string OwnerKey,
    string StartsOnEvent,
    string CompletesWithEvent,
    string PanelProviderKey,
    int Priority = 0,
    bool BlocksGameplayInput = true)
{
    public UltraSimScriptAdmissionFlowStepDescriptor NormalizeAndValidate()
    {
        ScriptDefinitionValidation.ValidateStableKey(OwnerKey, nameof(OwnerKey));
        ScriptDefinitionValidation.ValidateStableKey(StartsOnEvent, nameof(StartsOnEvent));
        ScriptDefinitionValidation.ValidateStableKey(CompletesWithEvent, nameof(CompletesWithEvent));
        ScriptDefinitionValidation.ValidateStableKey(PanelProviderKey, nameof(PanelProviderKey));

        return this with
        {
            OwnerKey = ScriptDefinitionValidation.NormalizeKey(OwnerKey),
            StartsOnEvent = ScriptDefinitionValidation.NormalizeKey(StartsOnEvent),
            CompletesWithEvent = ScriptDefinitionValidation.NormalizeKey(CompletesWithEvent),
            PanelProviderKey = ScriptDefinitionValidation.NormalizeKey(PanelProviderKey)
        };
    }
}
