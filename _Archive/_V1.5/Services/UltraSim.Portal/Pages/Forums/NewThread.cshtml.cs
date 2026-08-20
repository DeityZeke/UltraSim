using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Pages.Forums;

[Authorize]
public sealed class NewThreadModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public NewThreadModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public ForumSectionView? Section { get; private set; }

    public async Task<IActionResult> OnGetAsync(string sectionSlug, CancellationToken cancellationToken)
    {
        if (!await LoadSectionAsync(sectionSlug, cancellationToken))
            return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string sectionSlug, CancellationToken cancellationToken)
    {
        if (!await LoadSectionAsync(sectionSlug, cancellationToken))
            return NotFound();

        if (Section!.IsReadOnly)
        {
            ModelState.AddModelError(string.Empty, "This section is read-only.");
            return Page();
        }

        if (!ModelState.IsValid)
            return Page();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        DateTime nowUtc = DateTime.UtcNow;
        ForumThread thread = new()
        {
            SectionId = Section.Id,
            AuthorUserId = userId,
            LastPostUserId = userId,
            Title = Input.Title.Trim(),
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastPostedUtc = nowUtc,
        };

        _db.ForumThreads.Add(thread);
        await _db.SaveChangesAsync(cancellationToken);

        _db.ForumPosts.Add(new ForumPost
        {
            ThreadId = thread.Id,
            AuthorUserId = userId,
            Content = Input.Content.Trim(),
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        });

        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("/Forums/Thread", new { sectionSlug = Section.Slug, threadId = thread.Id });
    }

    private async Task<bool> LoadSectionAsync(string sectionSlug, CancellationToken cancellationToken)
    {
        ForumSection? section = await _db.ForumSections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == sectionSlug, cancellationToken);

        if (section == null)
            return false;

        Section = new ForumSectionView(section.Id, section.Slug, section.Title, section.Description, section.IsReadOnly);
        return true;
    }

    public sealed class InputModel
    {
        [Required]
        [StringLength(200, MinimumLength = 4)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(20000, MinimumLength = 8)]
        public string Content { get; set; } = string.Empty;
    }

    public sealed record ForumSectionView(
        int Id,
        string Slug,
        string Title,
        string Description,
        bool IsReadOnly);
}
