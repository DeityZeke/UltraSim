namespace UltraSim.UI;

public sealed record UiSchemaValidationLimits(
    int MaxNodeCount,
    int MaxTreeDepth,
    int MaxChildrenPerNode,
    int MaxActions,
    int MaxStateBindings,
    int MaxTextLength)
{
    public static UiSchemaValidationLimits Default { get; } = new(
        MaxNodeCount: 512,
        MaxTreeDepth: 32,
        MaxChildrenPerNode: 256,
        MaxActions: 256,
        MaxStateBindings: 256,
        MaxTextLength: UiValidation.MaxDisplayTextLength);

    public UiValidationResult Validate()
    {
        if (MaxNodeCount <= 0 ||
            MaxTreeDepth <= 0 ||
            MaxChildrenPerNode < 0 ||
            MaxActions < 0 ||
            MaxStateBindings < 0 ||
            MaxTextLength <= 0)
        {
            return UiValidationResult.Invalid("Schema validation limits must be positive, except action/binding/child limits which may be zero.");
        }

        return UiValidationResult.Valid;
    }
}
