using UltraSim.UI;

namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptAdmissionActionRouteDescriptor(
    string ActionId,
    string CompletesWithEvent,
    UiActionContextKind ContextKind = UiActionContextKind.Panel)
{
    public UltraSimScriptAdmissionActionRouteDescriptor NormalizeAndValidate()
    {
        ScriptDefinitionValidation.ValidateStableKey(ActionId, nameof(ActionId));
        ScriptDefinitionValidation.ValidateStableKey(CompletesWithEvent, nameof(CompletesWithEvent));
        if (!UiValidation.IsDefined(ContextKind))
        {
            throw new ArgumentException("Admission action route context kind is invalid.", nameof(ContextKind));
        }

        return this with
        {
            ActionId = ScriptDefinitionValidation.NormalizeKey(ActionId),
            CompletesWithEvent = ScriptDefinitionValidation.NormalizeKey(CompletesWithEvent)
        };
    }
}
