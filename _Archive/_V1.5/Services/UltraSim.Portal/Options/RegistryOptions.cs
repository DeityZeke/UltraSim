namespace UltraSim.Portal.Options;

public sealed class RegistryOptions
{
    public const string SectionName = "Registry";

    public string BaseUrl { get; set; } = "http://127.0.0.1:5180";
    public string? PortalApiToken { get; set; }
    public string? AdminApiToken { get; set; }
    public int TimeoutSeconds { get; set; } = 5;
}
