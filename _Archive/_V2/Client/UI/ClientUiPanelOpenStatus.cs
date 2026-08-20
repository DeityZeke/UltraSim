namespace UltraSim.Client.UI;

public enum ClientUiPanelOpenStatus : byte
{
    ColdOpened = 1,
    HotOpened = 2,
    MissingSchema = 3,
    SupersededSchema = 4,
    MissingCatalog = 5,
    SupersededCatalog = 6,
    Rejected = 7,
}
