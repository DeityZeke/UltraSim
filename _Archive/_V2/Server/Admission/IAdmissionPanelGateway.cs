using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Server.Admission;

public interface IAdmissionPanelGateway
{
    bool TryOpenPanel(ConnectionId connectionId, UiBuiltPanel panel, string panelInstanceId);

    bool TryUpdatePanel(ConnectionId connectionId, UiModelUpdateDocument update);
}
