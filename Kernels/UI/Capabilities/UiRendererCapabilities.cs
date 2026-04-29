namespace UltraSim.UI;

public sealed record UiRendererCapabilities(
    int ProtocolVersion,
    UiCapabilityFlags CapabilityFlags,
    int MaxSchemaBytes,
    int MaxModelBytes,
    int MaxNodeCount,
    int MaxTreeDepth,
    int MaxRepeaterRows,
    int MaxTableColumns)
{
    public UiValidationResult Validate()
    {
        if (ProtocolVersion <= 0)
        {
            return UiValidationResult.Invalid("Protocol version must be positive.");
        }

        if ((CapabilityFlags & ~AllKnownFlags) != 0)
        {
            return UiValidationResult.Invalid("Capability flags contain unknown values.");
        }

        if (MaxSchemaBytes <= 0 ||
            MaxModelBytes <= 0 ||
            MaxNodeCount <= 0 ||
            MaxTreeDepth <= 0 ||
            MaxRepeaterRows < 0 ||
            MaxTableColumns < 0)
        {
            return UiValidationResult.Invalid("Renderer limits must be positive, except row/column limits which may be zero.");
        }

        return UiValidationResult.Valid;
    }

    private const UiCapabilityFlags AllKnownFlags =
        UiCapabilityFlags.FixedLayoutV1 |
        UiCapabilityFlags.RichTooltipsV1 |
        UiCapabilityFlags.ContextMenusV1 |
        UiCapabilityFlags.RadialMenusV1 |
        UiCapabilityFlags.InventoryGridV1 |
        UiCapabilityFlags.ContainerCanvasV1 |
        UiCapabilityFlags.RuntimeStateBindingsV1 |
        UiCapabilityFlags.CatalogCacheV1;
}
