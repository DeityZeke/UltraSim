using UltraSim.Network;
using UltraSim.Server.Scripting;
using UltraSim.UI;

namespace UltraSim.Server.Admission;

public sealed record AdmissionActionRouteContext(
    ConnectionId ConnectionId,
    SessionAdmissionState State,
    UiActionRequestDocument Request,
    UltraSimScriptAdmissionActionRouteDescriptor Route);
