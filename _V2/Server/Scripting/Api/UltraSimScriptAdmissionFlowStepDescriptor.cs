using UltraSim.UI;

namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptAdmissionFlowStepDescriptor(
    string OwnerKey,
    string StartsOnEvent,
    string CompletesWithEvent,
    string PanelProviderKey,
    int Priority = 0,
    bool BlocksGameplayInput = true,
    string? FailureStatusSlotId = null,
    IReadOnlyList<UltraSimScriptAdmissionActionRouteDescriptor>? ActionRoutes = null)
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

        UltraSimScriptAdmissionActionRouteDescriptor[] routes = ActionRoutes is null
            ? []
            : ActionRoutes
                .Select(static route => (route ?? throw new ArgumentException("Admission action route cannot be null.", nameof(ActionRoutes))).NormalizeAndValidate())
                .ToArray();
        for (int i = 0; i < routes.Length; i++)
        {
            for (int j = i + 1; j < routes.Length; j++)
            {
                if (string.Equals(routes[i].ActionId, routes[j].ActionId, StringComparison.Ordinal) &&
                    routes[i].ContextKind == routes[j].ContextKind)
                {
                    throw new ArgumentException(
                        $"Admission step '{OwnerKey}' declares duplicate action route '{routes[i].ActionId}' for context '{routes[i].ContextKind}'.",
                        nameof(ActionRoutes));
                }
            }
        }

        return this with
        {
            OwnerKey = ScriptDefinitionValidation.NormalizeKey(OwnerKey),
            StartsOnEvent = ScriptDefinitionValidation.NormalizeKey(StartsOnEvent),
            CompletesWithEvent = ScriptDefinitionValidation.NormalizeKey(CompletesWithEvent),
            PanelProviderKey = ScriptDefinitionValidation.NormalizeKey(PanelProviderKey),
            FailureStatusSlotId = string.IsNullOrWhiteSpace(FailureStatusSlotId)
                ? null
                : ScriptDefinitionValidation.NormalizeKey(FailureStatusSlotId),
            ActionRoutes = routes
        };
    }

    public bool TryGetActionRoute(
        string actionId,
        UiActionContextKind contextKind,
        out UltraSimScriptAdmissionActionRouteDescriptor route)
    {
        route = default!;
        if (string.IsNullOrWhiteSpace(actionId) || ActionRoutes is null)
            return false;

        string normalized = ScriptDefinitionValidation.NormalizeKey(actionId);
        for (int i = 0; i < ActionRoutes.Count; i++)
        {
            UltraSimScriptAdmissionActionRouteDescriptor candidate = ActionRoutes[i];
            if (candidate.ContextKind == contextKind &&
                string.Equals(candidate.ActionId, normalized, StringComparison.Ordinal))
            {
                route = candidate;
                return true;
            }
        }

        return false;
    }
}
