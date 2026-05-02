using UltraSim.Network;

namespace UltraSim.Server.Admission;

public sealed record AdmissionCredentialRequest(
    string PanelInstanceId,
    string ActionId,
    string Username,
    LoginCredentialMaterialKind MaterialKind,
    ReadOnlyMemory<byte> MaterialBytes);
