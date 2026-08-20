namespace UltraSim.UI;

public enum UiModelUpdateKind : byte
{
    FullModel = 1,
    SlotPatch = 2,
    ScopedPatch = 3,
    RepeaterRows = 4,
    SchemaSuperseded = 5,
    CatalogSuperseded = 6,
}
