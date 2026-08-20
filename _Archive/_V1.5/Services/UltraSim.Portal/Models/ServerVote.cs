using System.ComponentModel.DataAnnotations;

namespace UltraSim.Portal.Models;

public sealed class ServerVote
{
    public int Id { get; set; }

    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string ServerId { get; set; } = string.Empty;

    public DateTimeOffset VotedUtc { get; set; }

    public DateTimeOffset LastChangedUtc { get; set; }
}
