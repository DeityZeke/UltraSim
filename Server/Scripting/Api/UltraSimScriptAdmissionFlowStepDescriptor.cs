namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptAdmissionFlowStepDescriptor(
    string OwnerKey,
    string StartsOnEvent,
    string CompletesWithEvent,
    string PanelProviderKey,
    int Priority = 0,
    bool BlocksGameplayInput = true,
    string? FailureStatusSlotId = null)
{
    public UltraSimScriptAdmissionFlowStepDescriptor NormalizeAndValidate()
    {
        ScriptDefinitionValidation.ValidateStableKey(OwnerKey, nameof(OwnerKey));
        ScriptDefinitionValidation.ValidateStableKey(StartsOnEvent, nameof(StartsOnEvent));
        ScriptDefinitionValidation.ValidateStableKey(CompletesWithEvent, nameof(CompletesWithEvent));
        ScriptDefinitionValidation.ValidateStableKey(PanelProviderKey, nameof(PanelProviderKey));
        if (!string.IsNullOrWhiteSpace(FailureStatusSlotId))
        {
            ScriptDefinitionValidation.ValidateStableKey(FailureStatusSlotId, nameof(FailureStatusSlotId));
        }

        return this with
        {
            OwnerKey = ScriptDefinitionValidation.NormalizeKey(OwnerKey),
            StartsOnEvent = ScriptDefinitionValidation.NormalizeKey(StartsOnEvent),
            CompletesWithEvent = ScriptDefinitionValidation.NormalizeKey(CompletesWithEvent),
            PanelProviderKey = ScriptDefinitionValidation.NormalizeKey(PanelProviderKey),
            FailureStatusSlotId = string.IsNullOrWhiteSpace(FailureStatusSlotId)
                ? null
                : ScriptDefinitionValidation.NormalizeKey(FailureStatusSlotId)
        };
    }
}
