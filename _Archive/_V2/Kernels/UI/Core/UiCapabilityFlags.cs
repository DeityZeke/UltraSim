namespace UltraSim.UI;

[Flags]
public enum UiCapabilityFlags : ulong
{
    None = 0,
    FixedLayoutV1 = 1UL << 0,
    RichTooltipsV1 = 1UL << 1,
    ContextMenusV1 = 1UL << 2,
    RadialMenusV1 = 1UL << 3,
    InventoryGridV1 = 1UL << 4,
    ContainerCanvasV1 = 1UL << 5,
    RuntimeStateBindingsV1 = 1UL << 6,
    CatalogCacheV1 = 1UL << 7,
}
