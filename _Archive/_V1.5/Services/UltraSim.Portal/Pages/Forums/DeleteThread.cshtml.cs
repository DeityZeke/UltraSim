using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services.Forums;

namespace UltraSim.Portal.Pages.Forums;

public sealed class DeleteThreadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ForumAuditWriter _forumAuditWriter;

    public DeleteThreadModel(
        ApplicationDbContext db,
        ForumAuditWriter forumAuditWriter)
    {
        _db = db;
        _forumAuditWriter = forumAuditWriter;
    }

    public DeleteThreadModel(
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
    public int PostCount { get; private set; }
    public bool RequiresModerationReason { get; private set; }
    public bool CanDelete { get; private set; }

    public async Task<IActionResult> OnGetAsync(string sectionSlug, long threadId, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(sectionSlug, threadId, cancellationToken))
            return NotFound();

        return CanDelete ? Page() : Forbid();
    }

    public async Task<IActionResult> OnPostAsync(string sectionSlug, long threadId, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(sectionSlug, threadId, cancellationToken))
            return NotFound();

        if (!CanDelete)
            return Forbid();

        if (RequiresModerationReason && string.IsNullOrWhiteSpace(Input.Reason))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Reason)}", "A moderation reason is required.");
            return Page();
        }

        ForumThread? thread = await _db.ForumThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (thread == null)
            return NotFound();

        string threadTitle = thread.Title;
        _db.ForumThreads.Remove(thread);
        await _db.SaveChangesAsync(cancellationToken);

        if (RequiresModerationReason)
        {
            await _forumAuditWriter.WriteModerationAuditAsync(
                User,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                actionType: "forum.thread.delete",
                targetType: "ForumThread",
                targetId: threadId.ToString(),
                reason: Input.Reason!.Trim(),
                details: $"Deleted thread '{threadTitle}'.",
                cancellationToken);
        }

        return RedirectToPage("/Forums/Section", new { sectionSlug = Section!.Slug });
    }

    private async Task<bool> LoadAsync(string sectionSlug, long threadId, CancellationToken cancellationToken)
    {
        string? actorUserId = ForumAccessEvaluator.GetUserId(User);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return false;

        ForumThread? thread = await _db.ForumThreads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (thread == null)
            return false;

        ForumSection? section = await _db.ForumSections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == thread.SectionId && x.Slug == sectionSlug, cancellationToken);
        if (section == null)
            return false;

        PostCount = await _db.ForumPosts
            .AsNoTracking()
            .CountAsync(x => x.ThreadId == threadId, cancellationToken);

        bool canModerate = ForumAccessEvaluator.CanModerate(User);
        bool isOwnThread = string.Equals(thread.AuthorUserId, actorUserId, StringComparison.Ordinal);
        CanDelete = canModerate || (isOwnThread && PostCount <= 1 && !thread.IsHidden);
        RequiresModerationReason = canModerate && !isOwnThread;

        Section = new ForumSectionView(section.Slug, section.Title);
        ThreadEntry = new ForumThreadView(thread.Id, thread.Title, thread.IsHidden);
        return true;
    }

    public sealed class InputModel
    {
        [StringLength(500)]
        public string? Reason { get; set; }
    }

    public sealed record ForumSectionView(string Slug, string Title);
    public sealed record ForumThreadView(long Id, string Title, bool IsHidden);
}
