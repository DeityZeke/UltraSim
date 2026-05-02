namespace UltraSim.UI;

public static class UiStandardPanels
{
    public const string MessageBoxPanelType = "standard.message_box";
    public const string ConfirmBoxPanelType = "standard.confirm_box";
    public const string ChoiceBoxPanelType = "standard.choice_box";
    public const string TextPromptPanelType = "standard.text_prompt";
    public const string PagedListPickerPanelType = "standard.paged_list_picker";
    public const string LoginPanelType = "admission.login";

    public const string TitleSlotId = "standard.title";
    public const string BodySlotId = "standard.body";
    public const string PromptValueSlotId = "standard.prompt.value";
    public const string LoginUsernameSlotId = "login.username";
    public const string LoginPasswordSlotId = "login.password";
    public const string LoginRememberSlotId = "login.remember";
    public const string LoginStatusSlotId = "login.status";
    public const string PagedListRowsSlotId = "standard.list.rows";
    public const string PagedListRowLabelSlotId = "standard.list.row.label";
    public const string PagedListRowDescriptionSlotId = "standard.list.row.description";
    public const string PagedListCurrentPageSlotId = "standard.list.page.current";
    public const string PagedListTotalPagesSlotId = "standard.list.page.total";

    public const string MessageOkActionId = "standard.message.ok";
    public const string ConfirmAcceptActionId = "standard.confirm.accept";
    public const string ConfirmCancelActionId = "standard.confirm.cancel";
    public const string PromptAcceptActionId = "standard.prompt.accept";
    public const string PromptCancelActionId = "standard.prompt.cancel";
    public const string LoginSubmitActionId = "login.submit";
    public const string LoginCancelActionId = "login.cancel";
    public const string PagedListAcceptActionId = "standard.list.accept";
    public const string PagedListCancelActionId = "standard.list.cancel";
    public const string PagedListPreviousActionId = "standard.list.previous";
    public const string PagedListNextActionId = "standard.list.next";
    public const string PagedListRowSelectActionId = "standard.list.row.select";

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

    public static UiStandardPagedListPicker PagedListPicker(
        string serverIdentity,
        string title,
        string body,
        IReadOnlyList<UiStandardListRow> rows,
        string acceptText = "Select",
        string cancelText = "Cancel",
        bool includePagingActions = false,
        long currentPage = 1,
        long totalPages = 1,
        string previousText = "Previous",
        string nextText = "Next",
        string panelType = PagedListPickerPanelType,
        int schemaRevision = 1,
        string acceptActionId = PagedListAcceptActionId,
        string cancelActionId = PagedListCancelActionId,
        string previousActionId = PagedListPreviousActionId,
        string nextActionId = PagedListNextActionId,
        string rowSelectActionId = PagedListRowSelectActionId,
        UiPanelMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("PagedListPicker requires at least one row.");
        }

        for (int i = 0; i < rows.Count; i++)
        {
            _ = rows[i] ?? throw new InvalidOperationException("PagedListPicker rows cannot contain null entries.");
        }

        metadata ??= CreateModalMetadata(panelType);
        UiBuiltPanel panel = UiPanelBuilder
            .Create(serverIdentity, panelType, schemaRevision, metadata)
            .Panel(root =>
            {
                root.Column(column =>
                {
                    column.DynamicText(TitleSlotId, "Title", title, maxStringLength: 160);
                    column.DynamicText(BodySlotId, "Body", body, maxStringLength: UiValidation.MaxDisplayTextLength);
                    column.Repeater(PagedListRowsSlotId, "Rows", rowSelectActionId, acceptText, UiActionRole.Primary);
                    column.Row(row =>
                    {
                        if (includePagingActions)
                        {
                            row.Pager(
                                PagedListCurrentPageSlotId,
                                currentPage,
                                PagedListTotalPagesSlotId,
                                totalPages,
                                previousActionId,
                                nextActionId,
                                previousText,
                                nextText);
                        }

                        row.Button(cancelActionId, cancelText, UiActionRole.Normal);
                        row.Button(acceptActionId, acceptText, UiActionRole.Primary);
                    });
                });
            })
            .Build();
        return new UiStandardPagedListPicker(panel, rows.ToArray());
    }

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
                    column.DynamicText(TitleSlotId, "Title", title, maxStringLength: 160);
                    column.DynamicText(BodySlotId, "Body", body, maxStringLength: UiValidation.MaxDisplayTextLength);
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

public sealed record UiStandardChoice(
    string ActionId,
    string Label,
    UiActionRole Role = UiActionRole.Normal,
    bool RequiresConfirm = false,
    string? IconId = null,
    UiActionAvailability Availability = UiActionAvailability.Available,
    string? AvailabilityReason = null);

public sealed record UiStandardListRow(
    string RowKey,
    string Label,
    string Description = "")
{
    public UiRepeaterRowDelta ToAddDelta(string repeaterSlotId)
    {
        UiValidationResult rowKeyResult = UiValidation.ValidateStableId(RowKey, nameof(RowKey), UiValidation.MaxSlotIdLength);
        if (!rowKeyResult.IsValid)
        {
            throw new InvalidOperationException(rowKeyResult.Error);
        }

        var values = string.IsNullOrEmpty(Description)
            ? new[] { UiModelSlotValue.String(UiStandardPanels.PagedListRowLabelSlotId, Label) }
            : new[]
            {
                UiModelSlotValue.String(UiStandardPanels.PagedListRowLabelSlotId, Label),
                UiModelSlotValue.String(UiStandardPanels.PagedListRowDescriptionSlotId, Description)
            };
        return new UiRepeaterRowDelta(
            repeaterSlotId,
            RowKey,
            UiRepeaterRowUpdateKind.Add,
            values);
    }
}

public sealed record UiStandardPagedListPicker(
    UiBuiltPanel Panel,
    IReadOnlyList<UiStandardListRow> Rows)
{
    public UiModelUpdateDocument CreateInitialRowsUpdate(string panelInstanceId, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelInstanceId);
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Update sequence cannot be negative.");
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
            RowDeltas: Rows.Select(row => row.ToAddDelta(UiStandardPanels.PagedListRowsSlotId)).ToArray());
    }
}
