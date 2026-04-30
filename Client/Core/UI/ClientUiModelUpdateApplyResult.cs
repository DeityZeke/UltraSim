using UltraSim.UI;

namespace UltraSim.Client.UI;

public sealed record ClientUiModelUpdateApplyResult(
    ClientUiModelUpdateApplyStatus Status,
    ClientUiPanelInstance? Panel,
    UiModelUpdateDocument? Update,
    string Error)
{
    public bool Applied => Status == ClientUiModelUpdateApplyStatus.Applied;

    public static ClientUiModelUpdateApplyResult AppliedUpdate(
        ClientUiPanelInstance panel,
        UiModelUpdateDocument update)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(update);
        return new ClientUiModelUpdateApplyResult(ClientUiModelUpdateApplyStatus.Applied, panel, update, string.Empty);
    }

    public static ClientUiModelUpdateApplyResult MissingPanel(string error)
    {
        return new ClientUiModelUpdateApplyResult(ClientUiModelUpdateApplyStatus.MissingPanel, null, null, error);
    }

    public static ClientUiModelUpdateApplyResult Rejected(string error)
    {
        return new ClientUiModelUpdateApplyResult(ClientUiModelUpdateApplyStatus.Rejected, null, null, error);
    }

    public static ClientUiModelUpdateApplyResult IgnoredStale(
        ClientUiPanelInstance panel,
        UiModelUpdateDocument update)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(update);
        return new ClientUiModelUpdateApplyResult(ClientUiModelUpdateApplyStatus.IgnoredStale, panel, update, string.Empty);
    }

    public static ClientUiModelUpdateApplyResult SchemaSuperseded(
        ClientUiPanelInstance panel,
        UiModelUpdateDocument update)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(update);
        return new ClientUiModelUpdateApplyResult(ClientUiModelUpdateApplyStatus.SchemaSuperseded, panel, update, string.Empty);
    }

    public static ClientUiModelUpdateApplyResult CatalogSuperseded(
        ClientUiPanelInstance panel,
        UiModelUpdateDocument update)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(update);
        return new ClientUiModelUpdateApplyResult(ClientUiModelUpdateApplyStatus.CatalogSuperseded, panel, update, string.Empty);
    }
}
