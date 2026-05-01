namespace UltraSim.UI;

public static class UiStandardPanels
{
    public const string MessageBoxPanelType = "standard.message_box";
    public const string ConfirmBoxPanelType = "standard.confirm_box";

    public const string TitleSlotId = "standard.title";
    public const string BodySlotId = "standard.body";

    public const string MessageOkActionId = "standard.message.ok";
    public const string ConfirmAcceptActionId = "standard.confirm.accept";
    public const string ConfirmCancelActionId = "standard.confirm.cancel";

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
