namespace UltraSim.Server.Accounting;

public interface IAccountAuthenticationProvider
{
    string ProviderKey { get; }

    AccountAuthenticationMode Mode { get; }

    CredentialValidationResult ValidateLogin(CredentialValidationRequest request);
}
