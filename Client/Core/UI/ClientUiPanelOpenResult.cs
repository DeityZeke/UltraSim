using UltraSim.UI;

namespace UltraSim.Client.UI;

public sealed record ClientUiPanelOpenResult(
    ClientUiPanelOpenStatus Status,
    ClientUiPanelInstance? Panel,
    string Error,
    UiSchemaIdentity? SupersededSchemaIdentity = null,
    UiCatalogIdentity? SupersededCatalogIdentity = null)
{
    public bool Opened => Status is ClientUiPanelOpenStatus.ColdOpened or ClientUiPanelOpenStatus.HotOpened;

    public static ClientUiPanelOpenResult ColdOpened(ClientUiPanelInstance panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return new ClientUiPanelOpenResult(ClientUiPanelOpenStatus.ColdOpened, panel, string.Empty);
    }

    public static ClientUiPanelOpenResult HotOpened(ClientUiPanelInstance panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return new ClientUiPanelOpenResult(ClientUiPanelOpenStatus.HotOpened, panel, string.Empty);
    }

    public static ClientUiPanelOpenResult MissingSchema(string error)
    {
        return new ClientUiPanelOpenResult(ClientUiPanelOpenStatus.MissingSchema, null, error);
    }

    public static ClientUiPanelOpenResult SupersededSchema(UiSchemaIdentity supersededByIdentity)
    {
        return new ClientUiPanelOpenResult(
            ClientUiPanelOpenStatus.SupersededSchema,
            null,
            "Cached schema identity has been superseded.",
            supersededByIdentity);
    }

    public static ClientUiPanelOpenResult MissingCatalog(string error)
    {
        return new ClientUiPanelOpenResult(ClientUiPanelOpenStatus.MissingCatalog, null, error);
    }

    public static ClientUiPanelOpenResult SupersededCatalog(UiCatalogIdentity supersededByIdentity)
    {
        return new ClientUiPanelOpenResult(
            ClientUiPanelOpenStatus.SupersededCatalog,
            null,
            "Cached catalog identity has been superseded.",
            SupersededCatalogIdentity: supersededByIdentity);
    }

    public static ClientUiPanelOpenResult Rejected(string error)
    {
        return new ClientUiPanelOpenResult(ClientUiPanelOpenStatus.Rejected, null, error);
    }
}
