using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services.Forums;

namespace UltraSim.Portal.Pages.Forums;

public sealed class EditPostModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ForumAuditWriter _forumAuditWriter;

    public EditPostModel(
        ApplicationDbContext db,
        ForumAuditWriter forumAuditWriter)
    {
        _db = db;
        _forumAuditWriter = forumAuditWriter;
    }

    public EditPostModel(
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
    public bool CanModerate { get; private set; }
    public bool RequiresModerationReason { get; private set; }

    public async Task<IActionResult> OnGetAsync(string sectionSlug, long threadId, long postId, CancellationToken cancellationToken)
    {
        PostLoadResult? result = await LoadAsync(sectionSlug, threadId, postId, cancellationToken);
        if (result == null)
            return NotFound();

        if (!result.CanEdit)
            return Forbid();

        Input = new InputModel
        {
            Title = result.IsOpeningPost ? result.Thread.Title : string.Empty,
            Content = result.Post.Content,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string sectionSlug, long threadId, long postId, CancellationToken cancellationToken)
    {
        PostLoadResult? result = await LoadAsync(sectionSlug, threadId, postId, cancellationToken);
        if (result == null)
            return NotFound();

        if (!result.CanEdit)
            return Forbid();

        ValidateInput(result.IsOpeningPost, result.RequiresModerationReason);
        if (!ModelState.IsValid)
            return Page();

        ForumThread? thread = await _db.ForumThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        ForumPost? post = await _db.ForumPosts.FirstOrDefaultAsync(x => x.Id == postId && x.ThreadId == threadId, cancellationToken);
        if (thread == null || post == null)
            return NotFound();

        bool titleChanged = false;
        if (result.IsOpeningPost)
        {
            string normalizedTitle = Input.Title.Trim();
            if (!string.Equals(thread.Title, normalizedTitle, StringComparison.Ordinal))
            {
                thread.Title = normalizedTitle;
                titleChanged = true;
            }
        }

        string normalizedContent = Input.Content.Trim();
        if (!string.Equals(post.Content, normalizedContent, StringComparison.Ordinal))
            post.Content = normalizedContent;

        post.UpdatedUtc = DateTime.UtcNow;
        thread.UpdatedUtc = post.UpdatedUtc;
        await _db.SaveChangesAsync(cancellationToken);

        if (result.RequiresModerationReason)
        {
            await _forumAuditWriter.WriteModerationAuditAsync(
                User,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                actionType: titleChanged ? "forum.thread.edit" : "forum.post.edit",
                targetType: titleChanged ? "ForumThread" : "ForumPost",
                targetId: titleChanged ? thread.Id.ToString() : post.Id.ToString(),
                reason: Input.ModerationReason!.Trim(),
                details: titleChanged ? thread.Title : $"Thread {thread.Id} / Post {post.Id}",
                cancellationToken);
        }

        return RedirectToPage("/Forums/Thread", new { sectionSlug, threadId, pageNumber = PageNumber });
    }

    private void ValidateInput(bool isOpeningPost, bool requiresModerationReason)
    {
        if (isOpeningPost && string.IsNullOrWhiteSpace(Input.Title))
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Title)}", "Title is required for the opening post.");

        if (requiresModerationReason && string.IsNullOrWhiteSpace(Input.ModerationReason))
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ModerationReason)}", "A moderation reason is required when editing another user's post.");
    }

    private async Task<PostLoadResult?> LoadAsync(string sectionSlug, long threadId, long postId, CancellationToken cancellationToken)
    {
        string? actorUserId = ForumAccessEvaluator.GetUserId(User);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return null;

        CanModerate = ForumAccessEvaluator.CanModerate(User);

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
        bool canEditOwnPost = string.Equals(post.AuthorUserId, actorUserId, StringComparison.Ordinal) && !post.IsHidden;
        bool canEdit = canEditOwnPost || CanModerate;
        bool requiresModerationReason = CanModerate && !string.Equals(post.AuthorUserId, actorUserId, StringComparison.Ordinal);

        RequiresModerationReason = requiresModerationReason;
        Section = new ForumSectionView(section.Slug, section.Title);
        ThreadEntry = new ForumThreadView(thread.Id, thread.Title, isOpeningPost);
        PostEntry = new ForumPostView(post.Id, post.AuthorUserId, post.IsHidden);

        return new PostLoadResult(thread, post, canEdit, isOpeningPost, requiresModerationReason);
    }

    public sealed class InputModel
    {
        [StringLength(200, MinimumLength = 4)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(20000, MinimumLength = 2)]
        public string Content { get; set; } = string.Empty;

        [StringLength(500)]
        public string? ModerationReason { get; set; }
    }

    public sealed record ForumSectionView(string Slug, string Title);
    public sealed record ForumThreadView(long Id, string Title, bool IsOpeningPost);
    public sealed record ForumPostView(long Id, string AuthorUserId, bool IsHidden);

    private sealed record PostLoadResult(
        ForumThread Thread,
        ForumPost Post,
        bool CanEdit,
        bool IsOpeningPost,
        bool RequiresModerationReason);
}
