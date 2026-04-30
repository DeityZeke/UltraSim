namespace UltraSim.Client.UI;

public enum ClientUiModelUpdateApplyStatus : byte
{
    Applied = 1,
    MissingPanel = 2,
    Rejected = 3,
    IgnoredStale = 4,
    SchemaSuperseded = 5,
    CatalogSuperseded = 6,
}
