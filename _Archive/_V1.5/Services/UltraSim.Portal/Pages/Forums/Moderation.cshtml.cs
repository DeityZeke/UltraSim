using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Services.Forums;

namespace UltraSim.Portal.Pages.Forums;

[Authorize(Policy = PortalPolicies.ForumModeration)]
public sealed class ModerationModel : PageModel
{
    private readonly ForumModerationDashboardQueryService _forumModerationDashboardQueryService;

    public ModerationModel(ForumModerationDashboardQueryService forumModerationDashboardQueryService)
    {
        _forumModerationDashboardQueryService = forumModerationDashboardQueryService;
    }

    [BindProperty(SupportsGet = true)]
    public string? SectionSlug { get; set; }

    public IReadOnlyList<ForumSectionOption> Sections { get; private set; } = [];
    public IReadOnlyList<ModerationThreadRow> Threads { get; private set; } = [];
    public IReadOnlyList<ModerationPostRow> HiddenPosts { get; private set; } = [];
    public IReadOnlyList<RecentAuditRow> RecentAudit { get; private set; } = [];
    public int HiddenThreadCount { get; private set; }
    public int LockedThreadCount { get; private set; }
    public int PinnedThreadCount { get; private set; }
    public int HiddenPostCount { get; private set; }
    public string CurrentFilterLabel { get; private set; } = "All Sections";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ForumModerationDashboardData? data = await _forumModerationDashboardQueryService.GetAsync(SectionSlug, cancellationToken);
        if (data == null)
            return NotFound();

        Sections = data.Sections;
        Threads = data.Threads;
        HiddenPosts = data.HiddenPosts;
        RecentAudit = data.RecentAudit;
        HiddenThreadCount = data.HiddenThreadCount;
        LockedThreadCount = data.LockedThreadCount;
        PinnedThreadCount = data.PinnedThreadCount;
        HiddenPostCount = data.HiddenPostCount;
        CurrentFilterLabel = data.CurrentFilterLabel;
        return Page();
    }
}
