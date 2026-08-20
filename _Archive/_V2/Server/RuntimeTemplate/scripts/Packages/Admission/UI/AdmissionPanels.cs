using System;
using System.Collections.Generic;
using System.Linq;
using UltraSim.UI;

namespace UltraSim.Scripts.Packages.Admission;

public static class AdmissionPanels
{
    public const string LoginPanelType = "admission.login";
    public const string CharacterSelectPanelType = "admission.character_select";
    public const string CharacterCreatePanelType = "admission.character_create";

    public const string LoginUsernameSlotId = "login.username";
    public const string LoginPasswordSlotId = "login.password";
    public const string LoginRememberSlotId = "login.remember";
    public const string LoginStatusSlotId = "login.status";
    public const string CharacterSelectRowsSlotId = "character_select.rows";
    public const string CharacterSelectRowNameSlotId = "character_select.row.name";
    public const string CharacterSelectRowSummarySlotId = "character_select.row.summary";
    public const string CharacterSelectStatusSlotId = "character_select.status";
    public const string CharacterCreateNameSlotId = "character_create.name";
    public const string CharacterCreateSexSlotId = "character_create.sex";
    public const string CharacterCreateStatusSlotId = "character_create.status";

    public const string LoginSubmitActionId = "login.submit";
    public const string LoginCancelActionId = "login.cancel";
    public const string CharacterSelectPlayActionId = "character_select.play";
    public const string CharacterSelectCreateActionId = "character_select.create";
    public const string CharacterSelectBackActionId = "character_select.back";
    public const string CharacterCreateSubmitActionId = "character_create.submit";
    public const string CharacterCreateCancelActionId = "character_create.cancel";

    public static UiBuiltPanel LoginPanel(
        string serverIdentity,
        string title = "UltraSim",
        string body = "Sign in to continue.",
        string username = "",
        bool rememberAccount = false,
        string status = "",
        string submitText = "Log In",
        string cancelText = "Back",
        string panelType = LoginPanelType,
        int schemaRevision = 1,
        string submitActionId = LoginSubmitActionId,
        string cancelActionId = LoginCancelActionId,
        UiPanelMetadata? metadata = null)
    {
        metadata ??= CreateSessionAdmissionMetadata(panelType);
        return UiPanelBuilder
            .Create(serverIdentity, panelType, schemaRevision, metadata)
            .Panel(panel =>
            {
                panel.Column(column =>
                {
                    column.DynamicText(UiStandardPanels.TitleSlotId, "Title", title, maxStringLength: 160);
                    column.DynamicText(UiStandardPanels.BodySlotId, "Body", body, maxStringLength: UiValidation.MaxDisplayTextLength);
                    column.TextInput(LoginUsernameSlotId, "Account", username, maxStringLength: 128);
                    column.PasswordInput(LoginPasswordSlotId, "Password", maxStringLength: 256);
                    column.Toggle(LoginRememberSlotId, "Remember Account", rememberAccount);
                    column.DynamicText(LoginStatusSlotId, "Status", status, maxStringLength: 256);
                    column.Row(row =>
                    {
                        row.Button(cancelActionId, cancelText, UiActionRole.Normal);
                        row.Button(submitActionId, submitText, UiActionRole.Primary, UiActionContextKind.TrustedCredential);
                    });
                });
            })
            .Build();
    }

    public static AdmissionCharacterSelectPanel CharacterSelectPanel(
        string serverIdentity,
        IReadOnlyList<AdmissionCharacterRow> characters,
        string title = "Select Character",
        string body = "Choose a character to enter the world.",
        string status = "",
        string playText = "Play",
        string createText = "Create",
        string backText = "Back",
        string panelType = CharacterSelectPanelType,
        int schemaRevision = 1,
        string playActionId = CharacterSelectPlayActionId,
        string createActionId = CharacterSelectCreateActionId,
        string backActionId = CharacterSelectBackActionId,
        UiPanelMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(characters);
        for (int i = 0; i < characters.Count; i++)
        {
            _ = characters[i] ?? throw new InvalidOperationException("CharacterSelectPanel rows cannot contain null entries.");
        }

        metadata ??= CreateSessionAdmissionMetadata(panelType);
        UiBuiltPanel panel = UiPanelBuilder
            .Create(serverIdentity, panelType, schemaRevision, metadata)
            .Panel(root =>
            {
                root.Column(column =>
                {
                    column.DynamicText(UiStandardPanels.TitleSlotId, "Title", title, maxStringLength: 160);
                    column.DynamicText(UiStandardPanels.BodySlotId, "Body", body, maxStringLength: UiValidation.MaxDisplayTextLength);
                    column.DynamicText(CharacterSelectStatusSlotId, "Status", status, maxStringLength: 256);
                    column.Repeater(CharacterSelectRowsSlotId, "Characters", playActionId, playText, UiActionRole.Primary);
                    column.Row(row =>
                    {
                        row.Button(backActionId, backText, UiActionRole.Normal);
                        row.Button(createActionId, createText, UiActionRole.Primary);
                    });
                });
            })
            .Build();
        return new AdmissionCharacterSelectPanel(panel, characters.ToArray());
    }

    public static UiBuiltPanel CharacterCreatePanel(
        string serverIdentity,
        string title = "Create Character",
        string body = "Choose a name and presentation.",
        string name = "",
        string selectedSex = "male",
        string status = "",
        string createText = "Create",
        string cancelText = "Back",
        string panelType = CharacterCreatePanelType,
        int schemaRevision = 1,
        string createActionId = CharacterCreateSubmitActionId,
        string cancelActionId = CharacterCreateCancelActionId,
        UiPanelMetadata? metadata = null)
    {
        UiSelectOption[] sexOptions =
        [
            new UiSelectOption("male", "Male"),
            new UiSelectOption("female", "Female")
        ];
        selectedSex = string.IsNullOrWhiteSpace(selectedSex) ? "male" : selectedSex;
        metadata ??= CreateSessionAdmissionMetadata(panelType);
        return UiPanelBuilder
            .Create(serverIdentity, panelType, schemaRevision, metadata)
            .Panel(root =>
            {
                root.Column(column =>
                {
                    column.DynamicText(UiStandardPanels.TitleSlotId, "Title", title, maxStringLength: 160);
                    column.DynamicText(UiStandardPanels.BodySlotId, "Body", body, maxStringLength: UiValidation.MaxDisplayTextLength);
                    column.TextInput(CharacterCreateNameSlotId, "Name", name, maxStringLength: 64);
                    column.Select(CharacterCreateSexSlotId, "Sex", selectedSex, sexOptions);
                    column.DynamicText(CharacterCreateStatusSlotId, "Status", status, maxStringLength: 256);
                    column.Row(row =>
                    {
                        row.Button(cancelActionId, cancelText, UiActionRole.Normal);
                        row.Button(createActionId, createText, UiActionRole.Primary);
                    });
                });
            })
            .Build();
    }

    private static UiPanelMetadata CreateSessionAdmissionMetadata(string panelType)
    {
        return new UiPanelMetadata(
            panelType,
            UiSurfaceTarget.Exclusive,
            UiFocusMode.Exclusive,
            ExclusiveGroup: "SessionAdmission",
            ZOrderClass: 100,
            UiOpenPolicy.ReplaceCurrent,
            UiBackPolicy.SendAction,
            UiTooltipPolicy.SuppressExternal,
            BlocksGameplayInput: true,
            PersistenceKey: null);
    }
}

public sealed record AdmissionCharacterRow(
    string CharacterId,
    string Name,
    string Summary = "")
{
    public UiRepeaterRowDelta ToAddDelta(string repeaterSlotId)
    {
        UiValidationResult rowKeyResult = UiValidation.ValidateStableId(CharacterId, nameof(CharacterId), UiValidation.MaxSlotIdLength);
        if (!rowKeyResult.IsValid)
        {
            throw new InvalidOperationException(rowKeyResult.Error);
        }

        var values = string.IsNullOrEmpty(Summary)
            ? new[] { UiModelSlotValue.String(AdmissionPanels.CharacterSelectRowNameSlotId, Name) }
            : new[]
            {
                UiModelSlotValue.String(AdmissionPanels.CharacterSelectRowNameSlotId, Name),
                UiModelSlotValue.String(AdmissionPanels.CharacterSelectRowSummarySlotId, Summary)
            };
        return new UiRepeaterRowDelta(
            repeaterSlotId,
            CharacterId,
            UiRepeaterRowUpdateKind.Add,
            values);
    }
}

public sealed record AdmissionCharacterSelectPanel(
    UiBuiltPanel Panel,
    IReadOnlyList<AdmissionCharacterRow> Characters)
{
    public UiModelUpdateDocument CreateInitialRowsUpdate(string panelInstanceId, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelInstanceId);
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Update sequence cannot be negative.");
        }

        if (Characters.Count == 0)
        {
            throw new InvalidOperationException("Character select has no rows to send.");
        }

        return new UiModelUpdateDocument(
            panelInstanceId.Trim(),
            Panel.Schema.Identity,
            Panel.Schema.CatalogIdentity,
            sequence,
            UiModelUpdateKind.RepeaterRows,
            ScopeId: null,
            SlotValues: [],
            ClearedSlotIds: [],
            RowDeltas: Characters.Select(row => row.ToAddDelta(AdmissionPanels.CharacterSelectRowsSlotId)).ToArray());
    }
}
