namespace UltraSim.Server.Accounting;

public sealed record CredentialValidationResult(
    CredentialValidationResultKind Kind,
    ServerAccountRecord? Account,
    string PublicMessage,
    TimeSpan? RetryAfter = null)
{
    public bool Succeeded => Kind == CredentialValidationResultKind.Success;

    public static CredentialValidationResult Success(ServerAccountRecord account)
    {
        return new CredentialValidationResult(
            CredentialValidationResultKind.Success,
            account,
            "Login accepted.");
    }

    public static CredentialValidationResult Reject(
        CredentialValidationResultKind kind,
        string publicMessage,
        TimeSpan? retryAfter = null)
    {
        return new CredentialValidationResult(kind, null, publicMessage, retryAfter);
    }
}
