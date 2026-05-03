using UltraSim.Network;
using UltraSim.Server.Accounting;
using UltraSim.Server.Admission;
using UltraSim.UI;

namespace UltraSim.Server.Network;

public sealed class AccountingAdmissionRuntimePolicy : IAdmissionRuntimePolicy
{
    public const string CharacterSelectPanelType = "admission.character_select";
    public const string CharacterCreatePanelType = "admission.character_create";
    public const string CharacterSelectRowsSlotId = "character_select.rows";
    public const string CharacterSelectRowNameSlotId = "character_select.row.name";
    public const string CharacterSelectRowSummarySlotId = "character_select.row.summary";
    public const string CharacterSelectStatusSlotId = "character_select.status";
    public const string CharacterCreateNameSlotId = "character_create.name";
    public const string CharacterCreateTemplateSlotId = "character_create.template";
    public const string CharacterCreateStatusSlotId = "character_create.status";
    public const string CharacterCreateSubmitActionId = "character_create.submit";

    private readonly AccountCharacterService _characters;
    private readonly Func<DateTimeOffset> _clock;

    public AccountingAdmissionRuntimePolicy(
        AccountCharacterService characters,
        Func<DateTimeOffset>? clock = null)
    {
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    public void OnStepOpened(AdmissionStepOpenedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.State.AccountId == 0 ||
            !string.Equals(context.Panel.Schema.Identity.PanelType, CharacterSelectPanelType, StringComparison.Ordinal))
        {
            return;
        }

        IReadOnlyList<ServerCharacterRecord> characters = _characters.GetCharactersForAccount(context.State.AccountId);
        if (characters.Count == 0)
        {
            PublishSlotPatch(
                context.Panels,
                context.ConnectionId,
                context.State,
                context.NextModelUpdateSequence(),
                CharacterSelectStatusSlotId,
                "No characters yet. Create one to begin.");
            return;
        }

        UiRepeaterRowDelta[] rows = new UiRepeaterRowDelta[characters.Count];
        for (int i = 0; i < characters.Count; i++)
        {
            ServerCharacterRecord character = characters[i];
            rows[i] = new UiRepeaterRowDelta(
                CharacterSelectRowsSlotId,
                character.CharacterId.ToString(),
                UiRepeaterRowUpdateKind.Add,
                [
                    UiModelSlotValue.String(CharacterSelectRowNameSlotId, character.Name),
                    UiModelSlotValue.String(CharacterSelectRowSummarySlotId, BuildCharacterSummary(character))
                ]);
        }

        var update = new UiModelUpdateDocument(
            context.State.PanelInstanceId,
            context.State.SchemaIdentity,
            context.State.CatalogIdentity,
            context.NextModelUpdateSequence(),
            UiModelUpdateKind.RepeaterRows,
            ScopeId: null,
            SlotValues: [],
            ClearedSlotIds: [],
            RowDeltas: rows);
        context.Panels.TryUpdatePanel(context.ConnectionId, update);
    }

    public AdmissionActionRouteResult EvaluateActionRoute(AdmissionActionRouteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(context.State.SchemaIdentity.PanelType, CharacterCreatePanelType, StringComparison.Ordinal) ||
            !string.Equals(context.Request.ActionId, CharacterCreateSubmitActionId, StringComparison.Ordinal))
        {
            return AdmissionActionRouteResult.Complete(context.Route.CompletesWithEvent);
        }

        if (context.State.AccountId == 0)
        {
            return AdmissionActionRouteResult.StayOnStep(CharacterCreateStatusSlotId, "Login session expired.");
        }

        string name = GetStringField(context.Request, CharacterCreateNameSlotId);
        string prefabKey = GetStringField(context.Request, CharacterCreateTemplateSlotId);
        CharacterCreateResult result = _characters.TryCreateCharacter(
            context.State.AccountId,
            name,
            prefabKey,
            _clock());
        return result.Succeeded
            ? AdmissionActionRouteResult.Complete(context.Route.CompletesWithEvent)
            : AdmissionActionRouteResult.StayOnStep(CharacterCreateStatusSlotId, result.Error);
    }

    private static string GetStringField(UiActionRequestDocument request, string slotId)
    {
        for (int i = 0; i < request.FieldValues.Count; i++)
        {
            UiModelSlotValue value = request.FieldValues[i];
            if (string.Equals(value.SlotId, slotId, StringComparison.Ordinal))
                return value.StringValue ?? string.Empty;
        }

        return string.Empty;
    }

    private static void PublishSlotPatch(
        IAdmissionPanelGateway panels,
        ConnectionId connectionId,
        SessionAdmissionState state,
        long sequence,
        string slotId,
        string value)
    {
        var update = new UiModelUpdateDocument(
            state.PanelInstanceId,
            state.SchemaIdentity,
            state.CatalogIdentity,
            sequence,
            UiModelUpdateKind.SlotPatch,
            ScopeId: null,
            SlotValues: [UiModelSlotValue.String(slotId, value)],
            ClearedSlotIds: [],
            RowDeltas: []);
        panels.TryUpdatePanel(connectionId, update);
    }

    private static string BuildCharacterSummary(ServerCharacterRecord character)
    {
        return $"Slot {character.SlotIndex + 1} - {character.PrefabKey}";
    }
}
