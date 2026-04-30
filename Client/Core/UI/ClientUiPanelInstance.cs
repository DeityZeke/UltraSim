using UltraSim.UI;

namespace UltraSim.Client.UI;

public sealed record ClientUiPanelInstance(
    string PanelInstanceId,
    UiSchemaIdentity SchemaIdentity,
    UiCatalogIdentity? CatalogIdentity,
    UiPanelMetadata Metadata,
    UiModelDocument Model,
    long OpenSequence);
