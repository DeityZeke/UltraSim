namespace UltraSim.Server.Admission;

public readonly record struct ServerAdmissionCredentialValidationResult(
    bool Accepted,
    AdmissionCredentialFailureReason FailureReason,
    string Error)
{
    public static ServerAdmissionCredentialValidationResult Accept()
    {
        return new ServerAdmissionCredentialValidationResult(
            true,
            AdmissionCredentialFailureReason.None,
            string.Empty);
    }

    public static ServerAdmissionCredentialValidationResult Reject(string error)
    {
        return Reject(AdmissionCredentialFailureReason.Rejected, error);
    }

    public static ServerAdmissionCredentialValidationResult Reject(
        AdmissionCredentialFailureReason reason,
        string error)
    {
        if (reason == AdmissionCredentialFailureReason.None)
            reason = AdmissionCredentialFailureReason.Rejected;

        return new ServerAdmissionCredentialValidationResult(
            false,
            reason,
            string.IsNullOrWhiteSpace(error) ? "Rejected." : error);
    }
}
