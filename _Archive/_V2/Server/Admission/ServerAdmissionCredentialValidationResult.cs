namespace UltraSim.Server.Admission;

public readonly record struct ServerAdmissionCredentialValidationResult(
    bool Accepted,
    AdmissionCredentialFailureReason FailureReason,
    string Error,
    ulong AccountId = 0)
{
    public static ServerAdmissionCredentialValidationResult Accept()
    {
        return Accept(0);
    }

    public static ServerAdmissionCredentialValidationResult Accept(ulong accountId)
    {
        return new ServerAdmissionCredentialValidationResult(
            true,
            AdmissionCredentialFailureReason.None,
            string.Empty,
            accountId);
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
            string.IsNullOrWhiteSpace(error) ? "Rejected." : error,
            0);
    }
}
