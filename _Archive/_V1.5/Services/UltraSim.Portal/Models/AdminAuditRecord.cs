using System.ComponentModel.DataAnnotations;

namespace UltraSim.Portal.Models;

public sealed class AdminAuditRecord
{
    public int Id { get; set; }

    [Required]
    [StringLength(450)]
    public string ActorUserId { get; set; } = string.Empty;

    [StringLength(256)]
    public string? ActorEmail { get; set; }

    [Required]
    [StringLength(64)]
    public string ActorRole { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string ActionType { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string TargetType { get; set; } = string.Empty;

    [StringLength(256)]
    public string? TargetId { get; set; }

    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string Result { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Details { get; set; }

    [StringLength(45)]
    public string? ActorIpAddress { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}
