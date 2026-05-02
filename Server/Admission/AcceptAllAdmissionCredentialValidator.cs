using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Server.Admission;

public sealed class AcceptAllAdmissionCredentialValidator : IAdmissionCredentialValidator
{
    public static AcceptAllAdmissionCredentialValidator Instance { get; } = new();

    private AcceptAllAdmissionCredentialValidator()
    {
    }

    public ServerAdmissionCredentialValidationResult ValidateTrustedCredential(
        ConnectionId connectionId,
        UiActionRequestDocument request)
    {
        return connectionId.IsValid && request.ContextKind == UiActionContextKind.TrustedCredential
            ? ServerAdmissionCredentialValidationResult.Accept()
            : ServerAdmissionCredentialValidationResult.Reject("Trusted credential action is not valid for this connection.");
    }
}
