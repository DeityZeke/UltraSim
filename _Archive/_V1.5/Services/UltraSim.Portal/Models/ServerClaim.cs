using System.ComponentModel.DataAnnotations;

namespace UltraSim.Portal.Models;

public sealed class ServerClaim
{
    public int Id { get; set; }

    [Required]
    public string OwnerUserId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required]
    [StringLength(255)]
    public string TargetHost { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int TargetPort { get; set; }

    [StringLength(512)]
    public string? WebsiteUrl { get; set; }

    [StringLength(512)]
    public string? DiscordUrl { get; set; }

    [StringLength(200)]
    public string? RequestedServerId { get; set; }

    [StringLength(512)]
    public string? OwnershipToken { get; set; }

    public ServerClaimStatus Status { get; set; } = ServerClaimStatus.Draft;

    [StringLength(1000)]
    public string? LastError { get; set; }

    public DateTimeOffset SubmittedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
