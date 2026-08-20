namespace UltraSim.UI;

public sealed record UiActionResultDocument(
    string PanelInstanceId,
    UiSchemaIdentity SchemaIdentity,
    long ClientSequence,
    string ActionId,
    UiActionResultKind Kind,
    UiActionFailureReason FailureReason,
    string? Message,
    IReadOnlyList<UiModelUpdateDocument> ModelUpdates)
{
    public UiValidationResult ValidateAgainst(UiSchemaCompatibilityContext context)
    {
        UiValidationResult panelResult = UiValidation.ValidateStableId(
            PanelInstanceId,
            nameof(PanelInstanceId),
            UiValidation.MaxIdentityLength);
        if (!panelResult.IsValid)
        {
            return panelResult;
        }

        UiValidationResult schemaResult = context.ValidateSchemaIdentity(SchemaIdentity);
        if (!schemaResult.IsValid)
        {
            return schemaResult;
        }

        if (ClientSequence < 0)
        {
            return UiValidationResult.Invalid("Action result sequence cannot be negative.");
        }

        UiValidationResult actionResult = UiValidation.ValidateStableId(ActionId, nameof(ActionId), UiValidation.MaxActionIdLength);
        if (!actionResult.IsValid)
        {
            return actionResult;
        }

        if (!context.TryGetAction(ActionId, out _))
        {
            return UiValidationResult.Invalid($"Action result references unknown action '{ActionId}'.");
        }

        if (!UiValidation.IsDefined(Kind))
        {
            return UiValidationResult.Invalid("Action result kind is invalid.");
        }

        if (!UiValidation.IsDefined(FailureReason))
        {
            return UiValidationResult.Invalid("Action failure reason is invalid.");
        }

        if (Kind == UiActionResultKind.Accepted && FailureReason != UiActionFailureReason.None)
        {
            return UiValidationResult.Invalid("Accepted action results cannot carry a failure reason.");
        }

        if (Kind == UiActionResultKind.Rejected && FailureReason == UiActionFailureReason.None)
        {
            return UiValidationResult.Invalid("Rejected action results require a failure reason.");
        }

        if (!string.IsNullOrEmpty(Message) && Message.Length > UiValidation.MaxDisplayTextLength)
        {
            return UiValidationResult.Invalid($"Action result message cannot exceed {UiValidation.MaxDisplayTextLength} characters.");
        }

        for (int i = 0; i < ModelUpdates.Count; i++)
        {
            UiModelUpdateDocument update = ModelUpdates[i];
            UiValidationResult updateResult = update.ValidateAgainst(context);
            if (!updateResult.IsValid)
            {
                return updateResult;
            }
        }

        return UiValidationResult.Valid;
    }
}
