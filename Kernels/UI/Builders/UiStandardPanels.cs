namespace UltraSim.UI;

public static class UiStandardPanels
{
    public const string MessageBoxPanelType = "standard.message_box";
    public const string ConfirmBoxPanelType = "standard.confirm_box";
    public const string ChoiceBoxPanelType = "standard.choice_box";
    public const string TextPromptPanelType = "standard.text_prompt";

    public const string TitleSlotId = "standard.title";
    public const string BodySlotId = "standard.body";
    public const string PromptValueSlotId = "standard.prompt.value";

    public const string MessageOkActionId = "standard.message.ok";
    public const string ConfirmAcceptActionId = "standard.confirm.accept";
    public const string ConfirmCancelActionId = "standard.confirm.cancel";
    public const string PromptAcceptActionId = "standard.prompt.accept";
    public const string PromptCancelActionId = "standard.prompt.cancel";

    public static UiBuiltPanel MessageBox(
        string serverIdentity,
        string title,
        string body,
        string buttonText = "OK",
        string panelType = MessageBoxPanelType,
        int schemaRevision = 1,
        string actionId = MessageOkActionId,
        UiPanelMetadata? metadata = null)
    {
        metadata ??= CreateModalMetadata(panelType);
        return UiPanelBuilder
            .Create(serverIdentity, panelType, schemaRevision, metadata)
            .Panel(panel =>
            {
                panel.Column(column =>
                {
                    column.DynamicText(TitleSlotId, "Title", title, maxStringLength: 160);
                    column.DynamicText(BodySlotId, "Body", body, maxStringLength: UiValidation.MaxDisplayTextLength);
                    column.Row(row => row.Button(actionId, buttonText, UiActionRole.Primary));
                });
            })
            .Build();
    }

    public static UiBuiltPanel ConfirmBox(
        string serverIdentity,
        string title,
        string body,
        string acceptText = "Accept",
        string cancelText = "Cancel",
        string panelType = ConfirmBoxPanelType,
        int schemaRevision = 1,
        string acceptActionId = ConfirmAcceptActionId,
        string cancelActionId = ConfirmCancelActionId,
        UiPanelMetadata? metadata = null)
    {
        metadata ??= CreateModalMetadata(panelType);
        return UiPanelBuilder
            .Create(serverIdentity, panelType, schemaRevision, metadata)
            .Panel(panel =>
            {
                panel.Column(column =>
                {
                    column.DynamicText(TitleSlotId, "Title", title, maxStringLength: 160);
                    column.DynamicText(BodySlotId, "Body", body, maxStringLength: UiValidation.MaxDisplayTextLength);
                    column.Row(row =>
                    {
                        row.Button(cancelActionId, cancelText, UiActionRole.Normal);
                        row.Button(acceptActionId, acceptText, UiActionRole.Primary);
                    });
                });
            })
            .Build();
    }

    public static UiBuiltPanel ChoiceBox(
        string serverIdentity,
        string title,
        string body,
        IReadOnlyList<UiStandardChoice> choices,
        string panelType = ChoiceBoxPanelType,
        int schemaRevision = 1,
        UiPanelMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count == 0)
        {
            throw new InvalidOperationException("ChoiceBox requires at least one choice.");
        }

        metadata ??= CreateModalMetadata(panelType);
        return UiPanelBuilder
            .Create(serverIdentity, panelType, schemaRevision, metadata)
            .Panel(panel =>
            {
                panel.Column(column =>
                {
                    column.DynamicText(TitleSlotId, "Title", title, maxStringLength: 160);
                    column.DynamicText(BodySlotId, "Body", body, maxStringLength: UiValidation.MaxDisplayTextLength);
                    column.Row(row =>
                    {
                        for (int i = 0; i < choices.Count; i++)
                        {
                            UiStandardChoice choice = choices[i] ?? throw new InvalidOperationException("ChoiceBox choices cannot contain null entries.");
                            row.Button(
                                choice.ActionId,
                                choice.Label,
                                choice.Role,
                                UiActionContextKind.Panel,
                                choice.RequiresConfirm,
                                choice.IconId,
                                childMenuKey: null,
                                choice.Availability,
                                choice.AvailabilityReason);
                        }
                    });
                });
            })
            .Build();
    }

    public static UiBuiltPanel TextPrompt(
        string serverIdentity,
        string title,
        string body,
        string placeholder,
        string value = "",
        string acceptText = "Accept",
        string cancelText = "Cancel",
        int maxStringLength = UiValidation.MaxDisplayTextLength,
        string panelType = TextPromptPanelType,
        int schemaRevision = 1,
        string acceptActionId = PromptAcceptActionId,
        string cancelActionId = PromptCancelActionId,
        UiPanelMetadata? metadata = null)
    {
        metadata ??= CreateModalMetadata(panelType);
        return UiPanelBuilder
            .Create(serverIdentity, panelType, schemaRevision, metadata)
            .Panel(panel =>
            {
                panel.Column(column =>
                {
                    column.DynamicText(TitleSlotId, "Title", title, maxStringLength: 160);
                    column.DynamicText(BodySlotId, "Body", body, maxStringLength: UiValidation.MaxDisplayTextLength);
                    column.TextInput(PromptValueSlotId, placeholder, value, maxStringLength);
                    column.Row(row =>
                    {
                        row.Button(cancelActionId, cancelText, UiActionRole.Normal);
                        row.Button(acceptActionId, acceptText, UiActionRole.Primary);
                    });
                });
            })
            .Build();
    }

    private static UiPanelMetadata CreateModalMetadata(string panelType)
    {
        return new UiPanelMetadata(
            panelType,
            UiSurfaceTarget.Modal,
            UiFocusMode.Modal,
            ExclusiveGroup: null,
            ZOrderClass: 10,
            UiOpenPolicy.New,
            UiBackPolicy.CloseAndFocusParent,
            UiTooltipPolicy.SuppressExternal,
            BlocksGameplayInput: true,
            PersistenceKey: null);
    }
}

public sealed record UiStandardChoice(
    string ActionId,
    string Label,
    UiActionRole Role = UiActionRole.Normal,
    bool RequiresConfirm = false,
    string? IconId = null,
    UiActionAvailability Availability = UiActionAvailability.Available,
    string? AvailabilityReason = null);
