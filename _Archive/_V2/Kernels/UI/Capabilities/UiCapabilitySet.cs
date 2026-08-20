namespace UltraSim.UI;

public static class UiCapabilitySet
{
    public const UiCapabilityFlags AllKnown =
        UiCapabilityFlags.FixedLayoutV1 |
        UiCapabilityFlags.RichTooltipsV1 |
        UiCapabilityFlags.ContextMenusV1 |
        UiCapabilityFlags.RadialMenusV1 |
        UiCapabilityFlags.InventoryGridV1 |
        UiCapabilityFlags.ContainerCanvasV1 |
        UiCapabilityFlags.RuntimeStateBindingsV1 |
        UiCapabilityFlags.CatalogCacheV1;
}
