namespace UltraSim.Server.Accounting;

public interface ICredentialService
{
    CredentialValidationResult ValidateLogin(CredentialValidationRequest request);
}
