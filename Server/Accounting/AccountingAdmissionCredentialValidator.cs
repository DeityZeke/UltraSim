using System.Text;
using UltraSim.Network;
using UltraSim.Server.Admission;
using UltraSim.UI;

namespace UltraSim.Server.Accounting;

public sealed class AccountingAdmissionCredentialValidator : IAdmissionCredentialValidator
{
    private readonly IAccountAuthenticationProvider _provider;
    private readonly Func<ConnectionId, string> _remoteAddressResolver;
    private readonly Func<ConnectionId, string> _sessionKeyResolver;
    private readonly Func<DateTimeOffset> _clock;

    public AccountingAdmissionCredentialValidator(
        IAccountAuthenticationProvider provider,
        Func<ConnectionId, string>? remoteAddressResolver = null,
        Func<ConnectionId, string>? sessionKeyResolver = null,
        Func<DateTimeOffset>? clock = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _remoteAddressResolver = remoteAddressResolver ?? (static connectionId => $"connection:{connectionId.Value}");
        _sessionKeyResolver = sessionKeyResolver ?? (static connectionId => $"connection:{connectionId.Value}");
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    public ServerAdmissionCredentialValidationResult ValidateTrustedCredential(
        ConnectionId connectionId,
        UiActionRequestDocument request)
    {
        return ServerAdmissionCredentialValidationResult.Reject(
            "Trusted credential values must use the dedicated login credential packet.");
    }

    public ServerAdmissionCredentialValidationResult ValidateTrustedCredential(
        ConnectionId connectionId,
        AdmissionCredentialRequest request)
    {
        if (!connectionId.IsValid)
            return ServerAdmissionCredentialValidationResult.Reject("Credential packet requires a valid connection.");

        if (string.IsNullOrWhiteSpace(request.Username))
            return ServerAdmissionCredentialValidationResult.Reject("Username is required.");

        if (!TryMapMaterialKind(request.MaterialKind, out CredentialMaterialKind materialKind))
            return ServerAdmissionCredentialValidationResult.Reject("Unsupported credential material kind.");

        string secret = DecodeMaterial(request.MaterialBytes.Span);
        if (string.IsNullOrWhiteSpace(secret))
            return ServerAdmissionCredentialValidationResult.Reject("Credential material is required.");

        var validationRequest = new CredentialValidationRequest(
            request.Username,
            secret,
            _remoteAddressResolver(connectionId),
            _sessionKeyResolver(connectionId),
            _clock(),
            materialKind);
        CredentialValidationResult result = _provider.ValidateLogin(validationRequest);
        return result.Succeeded
            ? ServerAdmissionCredentialValidationResult.Accept()
            : ServerAdmissionCredentialValidationResult.Reject(result.PublicMessage);
    }

    private static bool TryMapMaterialKind(
        LoginCredentialMaterialKind source,
        out CredentialMaterialKind destination)
    {
        switch (source)
        {
            case LoginCredentialMaterialKind.PlainPassword:
                destination = CredentialMaterialKind.PlainPassword;
                return true;
            case LoginCredentialMaterialKind.PortalSessionToken:
                destination = CredentialMaterialKind.PortalSessionToken;
                return true;
            default:
                destination = default;
                return false;
        }
    }

    private static string DecodeMaterial(ReadOnlySpan<byte> material)
    {
        if (material.IsEmpty)
            return string.Empty;

        return Encoding.UTF8.GetString(material);
    }
}
