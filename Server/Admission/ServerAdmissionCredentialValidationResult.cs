namespace UltraSim.Server.Admission;

public readonly record struct ServerAdmissionCredentialValidationResult(bool Accepted, string Error)
{
    public static ServerAdmissionCredentialValidationResult Accept()
    {
        return new ServerAdmissionCredentialValidationResult(true, string.Empty);
    }

    public static ServerAdmissionCredentialValidationResult Reject(string error)
    {
        return new ServerAdmissionCredentialValidationResult(false, string.IsNullOrWhiteSpace(error) ? "Rejected." : error);
    }
}
