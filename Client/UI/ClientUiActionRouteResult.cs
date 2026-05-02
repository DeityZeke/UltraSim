using UltraSim.UI;

namespace UltraSim.Client.UI;

public sealed record ClientUiActionRouteResult(
    ClientUiActionRouteStatus Status,
    UiActionRequestDocument? Request,
    ClientUiPanelInstance? Panel,
    string Error)
{
    public bool ReadyToSend => Status == ClientUiActionRouteStatus.Validated;

    public static ClientUiActionRouteResult Validated(
        UiActionRequestDocument request,
        ClientUiPanelInstance panel)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(panel);
        return new ClientUiActionRouteResult(ClientUiActionRouteStatus.Validated, request, panel, string.Empty);
    }

    public static ClientUiActionRouteResult MissingPanel(string error)
    {
        return new ClientUiActionRouteResult(ClientUiActionRouteStatus.MissingPanel, null, null, error);
    }

    public static ClientUiActionRouteResult Rejected(string error)
    {
        return new ClientUiActionRouteResult(ClientUiActionRouteStatus.Rejected, null, null, error);
    }
}
