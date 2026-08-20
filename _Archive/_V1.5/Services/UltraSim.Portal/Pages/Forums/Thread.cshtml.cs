using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;
using UltraSim.Portal.Services.Forums;

namespace UltraSim.Portal.Pages.Forums;

public sealed class ThreadModel : PageModel
{
    private readonly ForumThreadQueryService _forumThreadQueryService;
    private readonly ForumReplyService _forumReplyService;
    private readonly ForumModerationService _forumModerationService;

    public ThreadModel(
        ForumThreadQueryService forumThreadQueryService,
        ForumReplyService forumReplyService,
        ForumModerationService forumModerationService)
    {
        _forumThreadQueryService = forumThreadQueryService;
        _forumReplyService = forumReplyService;
        _forumModerationService = forumModerationService;
    }

    public ThreadModel(
        ApplicationDbContext db,
        ForumIdentityService forumIdentityService,
        UserManager<ApplicationUser> userManager,
        AdminAuditService adminAuditService)
        : this(
            new ForumThreadQueryService(db, forumIdentityService),
            new ForumReplyService(db),
            new ForumModerationService(db, userManager, adminAuditService))
    {
    }

    [BindProperty]
    public ReplyInputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public ForumSectionView? Section { get; private set; }
    public ForumThreadView? ThreadEntry { get; private set; }
    public IReadOnlyList<ForumPostView> Posts { get; private set; } = [];
    public bool CanModerate { get; private set; }
    public bool CanReply => User.Identity?.IsAuthenticated == true && ThreadEntry is { IsLocked: false, IsHidden: false };
    public int TotalPages { get; private set; }
    public int TotalVisiblePosts { get; private set; }
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public async Task<IActionResult> OnGetAsync(string sectionSlug, long threadId, CancellationToken cancellationToken)
    {
        bool loaded = await LoadThreadAsync(sectionSlug, threadId, cancellationToken);
        return loaded ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostReplyAsync(string sectionSlug, long threadId, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Challenge();

        if (!await LoadThreadAsync(sectionSlug, threadId, cancellationToken))
            return NotFound();

        if (ThreadEntry!.IsHidden)
            return NotFound();

        if (ThreadEntry.IsLocked)
        {
            ModelState.AddModelError(string.Empty, "This thread is locked.");
            return Page();
        }

        if (!ModelState.IsValid)
            return Page();

        ForumReplyResult result = await _forumReplyService.AddReplyAsync(
            threadId,
            Input.Content.Trim(),
            User,
            CanModerate,
            cancellationToken);

        return result.Status switch
        {
            ForumReplyStatus.MissingUser => Challenge(),
            ForumReplyStatus.ThreadNotFound => NotFound(),
            _ => RedirectToPage(new { sectionSlug, threadId, pageNumber = result.ReplyPage }),
        };
    }

    public async Task<IActionResult> OnPostToggleLockAsync(string sectionSlug, long threadId, int pageNumber, string? reason, CancellationToken cancellationToken)
    {
        if (!ForumAccessEvaluator.CanModerate(User))
            return Forbid();

        if (!TryNormalizeReason(reason, out string normalizedReason))
        {
            ErrorMessage = "A moderation reason is required.";
            return RedirectToPage(new { sectionSlug, threadId, pageNumber });
        }

        ForumModerationActionResult? result = await _forumModerationService.ToggleThreadLockAsync(
            threadId,
            normalizedReason,
            User,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        if (result == null)
            return NotFound();

        StatusMessage = result.StatusMessage;
        return RedirectToPage(new { sectionSlug, threadId, pageNumber });
    }

    public async Task<IActionResult> OnPostTogglePinAsync(string sectionSlug, long threadId, int pageNumber, string? reason, CancellationToken cancellationToken)
    {
        if (!ForumAccessEvaluator.CanModerate(User))
            return Forbid();

        if (!TryNormalizeReason(reason, out string normalizedReason))
        {
            ErrorMessage = "A moderation reason is required.";
            return RedirectToPage(new { sectionSlug, threadId, pageNumber });
        }

        ForumModerationActionResult? result = await _forumModerationService.ToggleThreadPinAsync(
            threadId,
            normalizedReason,
            User,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        if (result == null)
            return NotFound();

        StatusMessage = result.StatusMessage;
        return RedirectToPage(new { sectionSlug, threadId, pageNumber });
    }

    public async Task<IActionResult> OnPostToggleHiddenAsync(string sectionSlug, long threadId, int pageNumber, string? reason, CancellationToken cancellationToken)
    {
        if (!ForumAccessEvaluator.CanModerate(User))
            return Forbid();

        if (!TryNormalizeReason(reason, out string normalizedReason))
        {
            ErrorMessage = "A moderation reason is required.";
            return RedirectToPage(new { sectionSlug, threadId, pageNumber });
        }

        ForumModerationActionResult? result = await _forumModerationService.ToggleThreadHiddenAsync(
            threadId,
            normalizedReason,
            User,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        if (result == null)
            return NotFound();

        StatusMessage = result.StatusMessage;
        return RedirectToPage(new { sectionSlug, threadId, pageNumber });
    }

    public async Task<IActionResult> OnPostTogglePostHiddenAsync(string sectionSlug, long threadId, long postId, int pageNumber, string? reason, CancellationToken cancellationToken)
    {
        if (!ForumAccessEvaluator.CanModerate(User))
            return Forbid();

        if (!TryNormalizeReason(reason, out string normalizedReason))
        {
            ErrorMessage = "A moderation reason is required.";
            return RedirectToPage(new { sectionSlug, threadId, pageNumber });
        }

        ForumModerationActionResult? result = await _forumModerationService.TogglePostHiddenAsync(
            threadId,
            postId,
            normalizedReason,
            User,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        if (result == null)
            return NotFound();

        StatusMessage = result.StatusMessage;
        return RedirectToPage(new { sectionSlug, threadId, pageNumber });
    }

    private async Task<bool> LoadThreadAsync(string sectionSlug, long threadId, CancellationToken cancellationToken)
    {
        ForumThreadPageData? pageData = await _forumThreadQueryService.GetAsync(
            sectionSlug,
            threadId,
            PageNumber,
            User,
            cancellationToken);
        if (pageData == null)
            return false;

        Section = pageData.Section;
        ThreadEntry = pageData.Thread;
        Posts = pageData.Posts;
        CanModerate = pageData.CanModerate;
        PageNumber = pageData.PageNumber;
        TotalPages = pageData.TotalPages;
        TotalVisiblePosts = pageData.TotalVisiblePosts;
        return true;
    }

    private static bool TryNormalizeReason(string? reason, out string normalizedReason)
    {
        normalizedReason = string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
            return false;

        normalizedReason = reason.Trim();
        return normalizedReason.Length > 0;
    }

    public sealed class ReplyInputModel
    {
        [Required]
        [StringLength(20000, MinimumLength = 2)]
        public string Content { get; set; } = string.Empty;
    }
}
