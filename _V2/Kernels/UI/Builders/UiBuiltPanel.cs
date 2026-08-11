namespace UltraSim.UI;

public sealed record UiBuiltPanel(UiSchemaDocument Schema, UiModelDocument Model)
{
    public UiPanelOpenDocument ToColdOpen(string panelInstanceId)
    {
        return new UiPanelOpenDocument(panelInstanceId, Schema.Identity, Schema.CatalogIdentity, Model, Schema);
    }

    public UiPanelOpenDocument ToHotOpen(string panelInstanceId)
    {
        return new UiPanelOpenDocument(panelInstanceId, Schema.Identity, Schema.CatalogIdentity, Model);
    }
}
