namespace UltraSim.Inventory;

public sealed record InventoryMutationResult(
    string CorrelationId,
    long ClientSequence,
    InventoryMutationKind MutationKind,
    InventoryMutationResultKind ResultKind,
    InventoryMutationFailureReason FailureReason,
    string? Message,
    IReadOnlyList<string> AffectedContainerIds)
{
    public InventoryValidationResult Validate()
    {
        InventoryValidationResult correlationResult = InventoryValidation.ValidateStableId(
            CorrelationId,
            nameof(CorrelationId),
            InventoryValidation.MaxStableIdLength);
        if (!correlationResult.IsValid)
        {
            return correlationResult;
        }

        if (ClientSequence < -1)
        {
            return InventoryValidationResult.Invalid("Inventory mutation result sequence cannot be less than -1.");
        }

        if (!InventoryValidation.IsDefined(MutationKind))
        {
            return InventoryValidationResult.Invalid("Inventory mutation result kind references an invalid mutation kind.");
        }

        if (!InventoryValidation.IsDefined(ResultKind))
        {
            return InventoryValidationResult.Invalid("Inventory mutation result kind is invalid.");
        }

        if (!InventoryValidation.IsDefined(FailureReason))
        {
            return InventoryValidationResult.Invalid("Inventory mutation failure reason is invalid.");
        }

        if (ResultKind == InventoryMutationResultKind.Accepted && FailureReason != InventoryMutationFailureReason.None)
        {
            return InventoryValidationResult.Invalid("Accepted inventory mutation results cannot carry a failure reason.");
        }

        if (ResultKind == InventoryMutationResultKind.Rejected && FailureReason == InventoryMutationFailureReason.None)
        {
            return InventoryValidationResult.Invalid("Rejected inventory mutation results require a failure reason.");
        }

        if (!string.IsNullOrEmpty(Message) && Message.Length > 512)
        {
            return InventoryValidationResult.Invalid("Inventory mutation result message cannot exceed 512 characters.");
        }

        for (int i = 0; i < AffectedContainerIds.Count; i++)
        {
            string containerId = AffectedContainerIds[i];
            InventoryValidationResult containerResult = InventoryValidation.ValidateStableId(containerId, nameof(AffectedContainerIds));
            if (!containerResult.IsValid)
            {
                return containerResult;
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(AffectedContainerIds[j], containerId, StringComparison.Ordinal))
                {
                    return InventoryValidationResult.Invalid($"Inventory mutation result contains duplicate affected container '{containerId}'.");
                }
            }
        }

        return InventoryValidationResult.Valid;
    }
}
