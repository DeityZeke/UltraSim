namespace UltraSim.Portal.Options;

public sealed class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>
    /// When true, users must confirm their email address before they can sign in.
    /// Enable this for any public-facing deployment to reduce account abuse.
    /// </summary>
    public bool RequireConfirmedAccount { get; set; } = false;
}
