namespace UltraSim.Portal.Models;

public sealed class ForumPost
{
    public long Id { get; set; }
    public long ThreadId { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public ForumThread? Thread { get; set; }
}
