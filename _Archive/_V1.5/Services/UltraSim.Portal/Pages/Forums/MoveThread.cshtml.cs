using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services.Forums;

namespace UltraSim.Portal.Pages.Forums;

public sealed class MoveThreadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ForumAuditWriter _forumAuditWriter;

    public MoveThreadModel(
        ApplicationDbContext db,
        ForumAuditWriter forumAuditWriter)
    {
        _db = db;
        _forumAuditWriter = forumAuditWriter;
    }

    public MoveThreadModel(
        ApplicationDbContext db,
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager,
        UltraSim.Portal.Services.AdminAuditService adminAuditService)
        : this(db, new ForumAuditWriter(userManager, adminAuditService))
    {
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public ForumSectionView? CurrentSection { get; private set; }
    public ForumThreadView? ThreadEntry { get; private set; }
    public IReadOnlyList<SelectListItem> AvailableSections { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string sectionSlug, long threadId, CancellationToken cancellationToken)
    {
        if (!ForumAccessEvaluator.CanModerate(User))
            return Forbid();

        if (!await LoadAsync(sectionSlug, threadId, cancellationToken))
            return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string sectionSlug, long threadId, CancellationToken cancellationToken)
    {
        if (!ForumAccessEvaluator.CanModerate(User))
            return Forbid();

        if (!await LoadAsync(sectionSlug, threadId, cancellationToken))
            return NotFound();

        if (!ModelState.IsValid)
            return Page();

        ForumThread? thread = await _db.ForumThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (thread == null)
            return NotFound();

        if (thread.SectionId == Input.TargetSectionId)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.TargetSectionId)}", "Select a different target section.");
            return Page();
        }

        ForumSection? targetSection = await _db.ForumSections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Input.TargetSectionId, cancellationToken);
        if (targetSection == null)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.TargetSectionId)}", "Target section not found.");
            return Page();
        }

        thread.SectionId = targetSection.Id;
        thread.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _forumAuditWriter.WriteModerationAuditAsync(
            User,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            actionType: "forum.thread.move",
            targetType: "ForumThread",
            targetId: thread.Id.ToString(),
            reason: Input.Reason.Trim(),
            details: $"Moved '{thread.Title}' from '{CurrentSection!.Title}' to '{targetSection.Title}'.",
            cancellationToken);

        return RedirectToPage("/Forums/Thread", new { sectionSlug = targetSection.Slug, threadId = thread.Id });
    }

    private async Task<bool> LoadAsync(string sectionSlug, long threadId, CancellationToken cancellationToken)
    {
        ForumThread? thread = await _db.ForumThreads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (thread == null)
            return false;

        ForumSection? currentSection = await _db.ForumSections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == thread.SectionId && x.Slug == sectionSlug, cancellationToken);
        if (currentSection == null)
            return false;

        List<ForumSection> allSections = await _db.ForumSections
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

        CurrentSection = new ForumSectionView(currentSection.Slug, currentSection.Title);
        ThreadEntry = new ForumThreadView(thread.Id, thread.Title);
        AvailableSections = allSections
            .Select(x => new SelectListItem(x.Title, x.Id.ToString()))
            .ToArray();

        if (Input.TargetSectionId == 0)
            Input.TargetSectionId = currentSection.Id;

        return true;
    }

    public sealed class InputModel
    {
        [Range(1, int.MaxValue)]
        public int TargetSectionId { get; set; }

        [Required]
        [StringLength(500, MinimumLength = 3)]
        public string Reason { get; set; } = string.Empty;
    }

    public sealed record ForumSectionView(string Slug, string Title);
    public sealed record ForumThreadView(long Id, string Title);
}
