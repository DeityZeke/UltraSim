using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Server.Admission;

public interface IAdmissionCredentialValidator
{
    ServerAdmissionCredentialValidationResult ValidateTrustedCredential(
        ConnectionId connectionId,
        UiActionRequestDocument request);
}
