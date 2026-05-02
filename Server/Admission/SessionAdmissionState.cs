using UltraSim.Network;
using UltraSim.Server.Scripting;
using UltraSim.UI;

namespace UltraSim.Server.Admission;

public sealed record SessionAdmissionState(
    ConnectionId ConnectionId,
    UltraSimScriptAdmissionFlowStepDescriptor Step,
    string PanelInstanceId,
    UiSchemaIdentity SchemaIdentity,
    UiCatalogIdentity? CatalogIdentity,
    string WaitingForEvent,
    IReadOnlySet<string> TrustedCredentialActionIds);
