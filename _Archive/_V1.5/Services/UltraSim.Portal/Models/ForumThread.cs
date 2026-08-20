namespace UltraSim.Portal.Models;

public sealed class ForumThread
{
    public long Id { get; set; }
    public int SectionId { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;
    public string? LastPostUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public bool IsLocked { get; set; }
    public bool IsHidden { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastPostedUtc { get; set; } = DateTime.UtcNow;
    public ForumSection? Section { get; set; }
    public ICollection<ForumPost> Posts { get; set; } = [];
}
