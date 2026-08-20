using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Services.Forums;

public sealed class ForumReplyService
{
    private readonly ApplicationDbContext _db;

    public ForumReplyService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ForumReplyResult> AddReplyAsync(
        long threadId,
        string content,
        ClaimsPrincipal user,
        bool canModerate,
        CancellationToken cancellationToken = default)
    {
        string? userId = ForumAccessEvaluator.GetUserId(user);
        if (string.IsNullOrWhiteSpace(userId))
            return ForumReplyResult.MissingUser;

        ForumThread? thread = await _db.ForumThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (thread == null)
            return ForumReplyResult.ThreadNotFound;

        DateTime nowUtc = DateTime.UtcNow;
        _db.ForumPosts.Add(new ForumPost
        {
            ThreadId = threadId,
            AuthorUserId = userId,
            Content = content,
            IsHidden = false,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        });

        thread.LastPostUserId = userId;
        thread.LastPostedUtc = nowUtc;
        thread.UpdatedUtc = nowUtc;

        await _db.SaveChangesAsync(cancellationToken);

        int visiblePostCount = await _db.ForumPosts
            .AsNoTracking()
            .CountAsync(x => x.ThreadId == threadId && (canModerate || !x.IsHidden), cancellationToken);
        int replyPage = Math.Max(1, (int)Math.Ceiling(visiblePostCount / (double)ForumThreadQueryService.PostsPerPage));
        return new ForumReplyResult(ForumReplyStatus.Success, replyPage);
    }
}

public enum ForumReplyStatus
{
    Success,
    MissingUser,
    ThreadNotFound,
}

public readonly record struct ForumReplyResult(ForumReplyStatus Status, int ReplyPage)
{
    public static ForumReplyResult MissingUser => new(ForumReplyStatus.MissingUser, 0);
    public static ForumReplyResult ThreadNotFound => new(ForumReplyStatus.ThreadNotFound, 0);
}
