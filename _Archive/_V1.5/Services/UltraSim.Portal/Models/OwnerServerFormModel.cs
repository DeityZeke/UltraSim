using System.ComponentModel.DataAnnotations;

namespace UltraSim.Portal.Models;

public sealed class OwnerServerFormModel
{
    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required]
    [StringLength(255)]
    public string TargetHost { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int TargetPort { get; set; } = 42042;

    [HttpOrHttpsUrl]
    [StringLength(512)]
    public string? WebsiteUrl { get; set; }

    [HttpOrHttpsUrl]
    [StringLength(512)]
    public string? DiscordUrl { get; set; }

    [StringLength(512)]
    public string? OwnershipToken { get; set; }
}
