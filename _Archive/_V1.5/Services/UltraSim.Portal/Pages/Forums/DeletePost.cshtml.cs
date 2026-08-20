using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services.Forums;

namespace UltraSim.Portal.Pages.Forums;

public sealed class DeletePostModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ForumAuditWriter _forumAuditWriter;

    public DeletePostModel(
        ApplicationDbContext db,
        ForumAuditWriter forumAuditWriter)
    {
        _db = db;
        _forumAuditWriter = forumAuditWriter;
    }

    public DeletePostModel(
        ApplicationDbContext db,
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager,
        UltraSim.Portal.Services.AdminAuditService adminAuditService)
        : this(db, new ForumAuditWriter(userManager, adminAuditService))
    {
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public ForumSectionView? Section { get; private set; }
    public ForumThreadView? ThreadEntry { get; private set; }
    public ForumPostView? PostEntry { get; private set; }
    public bool RequiresModerationReason { get; private set; }

    public async Task<IActionResult> OnGetAsync(string sectionSlug, long threadId, long postId, CancellationToken cancellationToken)
    {
        DeleteLoadResult? result = await LoadAsync(sectionSlug, threadId, postId, cancellationToken);
        if (result == null)
            return NotFound();

        if (!result.CanDelete)
            return Forbid();

        if (result.IsOpeningPost)
            return RedirectToPage("/Forums/DeleteThread", new { sectionSlug, threadId, pageNumber = PageNumber });

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string sectionSlug, long threadId, long postId, CancellationToken cancellationToken)
    {
        DeleteLoadResult? result = await LoadAsync(sectionSlug, threadId, postId, cancellationToken);
        if (result == null)
            return NotFound();

        if (!result.CanDelete)
            return Forbid();

        if (result.IsOpeningPost)
            return RedirectToPage("/Forums/DeleteThread", new { sectionSlug, threadId, pageNumber = PageNumber });

        if (RequiresModerationReason && string.IsNullOrWhiteSpace(Input.Reason))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Reason)}", "A moderation reason is required.");
            return Page();
        }

        ForumPost? post = await _db.ForumPosts.FirstOrDefaultAsync(x => x.Id == postId && x.ThreadId == threadId, cancellationToken);
        ForumThread? thread = await _db.ForumThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (post == null || thread == null)
            return NotFound();

        _db.ForumPosts.Remove(post);
        await _db.SaveChangesAsync(cancellationToken);

        ForumPost? latestRemainingPost = await _db.ForumPosts
            .AsNoTracking()
            .Where(x => x.ThreadId == threadId)
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestRemainingPost != null)
        {
            thread.LastPostUserId = latestRemainingPost.AuthorUserId;
            thread.LastPostedUtc = latestRemainingPost.CreatedUtc;
        }
        else
        {
            thread.LastPostUserId = thread.AuthorUserId;
            thread.LastPostedUtc = thread.CreatedUtc;
        }

        thread.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        if (RequiresModerationReason)
        {
            await _forumAuditWriter.WriteModerationAuditAsync(
                User,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                actionType: "forum.post.delete",
                targetType: "ForumPost",
                targetId: postId.ToString(),
                reason: Input.Reason!.Trim(),
                details: $"Deleted post {postId} from thread '{thread.Title}'.",
                cancellationToken);
        }

        return RedirectToPage("/Forums/Thread", new { sectionSlug, threadId, pageNumber = PageNumber });
    }

    private async Task<DeleteLoadResult?> LoadAsync(string sectionSlug, long threadId, long postId, CancellationToken cancellationToken)
    {
        string? actorUserId = ForumAccessEvaluator.GetUserId(User);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return null;

        ForumThread? thread = await _db.ForumThreads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (thread == null)
            return null;

        ForumSection? section = await _db.ForumSections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == thread.SectionId && x.Slug == sectionSlug, cancellationToken);
        if (section == null)
            return null;

        ForumPost? post = await _db.ForumPosts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == postId && x.ThreadId == threadId, cancellationToken);
        if (post == null)
            return null;

        long? firstPostId = await _db.ForumPosts
            .AsNoTracking()
            .Where(x => x.ThreadId == threadId)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        bool isOpeningPost = firstPostId == post.Id;
        bool canModerate = ForumAccessEvaluator.CanModerate(User);
        bool canDelete = canModerate || (
            string.Equals(post.AuthorUserId, actorUserId, StringComparison.Ordinal)
            && !isOpeningPost
            && !post.IsHidden);
        bool requiresReason = canModerate && !string.Equals(post.AuthorUserId, actorUserId, StringComparison.Ordinal);

        RequiresModerationReason = requiresReason;
        Section = new ForumSectionView(section.Slug, section.Title);
        ThreadEntry = new ForumThreadView(thread.Id, thread.Title);
        PostEntry = new ForumPostView(post.Id, post.Content, post.IsHidden);

        return new DeleteLoadResult(canDelete, isOpeningPost);
    }

    public sealed class InputModel
    {
        [StringLength(500)]
        public string? Reason { get; set; }
    }

    public sealed record ForumSectionView(string Slug, string Title);
    public sealed record ForumThreadView(long Id, string Title);
    public sealed record ForumPostView(long Id, string Content, bool IsHidden);
    private sealed record DeleteLoadResult(bool CanDelete, bool IsOpeningPost);
}
