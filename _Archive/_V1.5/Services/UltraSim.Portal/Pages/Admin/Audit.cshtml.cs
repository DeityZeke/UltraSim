using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Pages.Admin;

[Authorize(Policy = PortalPolicies.AdminPanel)]
public sealed class AuditModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public AuditModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ActionType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Result { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TargetType { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ForumOnly { get; set; }

    public IReadOnlyList<AdminAuditRecord> Records { get; private set; } = [];
    public IReadOnlyList<string> AvailableActionTypes { get; private set; } = [];
    public IReadOnlyList<string> AvailableResults { get; private set; } = [];
    public IReadOnlyList<string> AvailableTargetTypes { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        AvailableActionTypes = await _db.AdminAuditRecords
            .Select(static x => x.ActionType)
            .Distinct()
            .OrderBy(static x => x)
            .ToArrayAsync(cancellationToken);

        AvailableResults = await _db.AdminAuditRecords
            .Select(static x => x.Result)
            .Distinct()
            .OrderBy(static x => x)
            .ToArrayAsync(cancellationToken);

        AvailableTargetTypes = await _db.AdminAuditRecords
            .Select(static x => x.TargetType)
            .Distinct()
            .OrderBy(static x => x)
            .ToArrayAsync(cancellationToken);

        IQueryable<AdminAuditRecord> query = _db.AdminAuditRecords;

        string? normalizedSearch = Normalize(Search);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(record =>
                (record.ActorEmail != null && record.ActorEmail.Contains(normalizedSearch)) ||
                record.ActorUserId.Contains(normalizedSearch) ||
                record.ActionType.Contains(normalizedSearch) ||
                record.TargetType.Contains(normalizedSearch) ||
                (record.TargetId != null && record.TargetId.Contains(normalizedSearch)) ||
                record.Reason.Contains(normalizedSearch) ||
                (record.Details != null && record.Details.Contains(normalizedSearch)));
        }

        string? normalizedActionType = Normalize(ActionType);
        if (!string.IsNullOrWhiteSpace(normalizedActionType))
            query = query.Where(record => record.ActionType == normalizedActionType);

        string? normalizedResult = Normalize(Result);
        if (!string.IsNullOrWhiteSpace(normalizedResult))
            query = query.Where(record => record.Result == normalizedResult);

        string? normalizedTargetType = Normalize(TargetType);
        if (!string.IsNullOrWhiteSpace(normalizedTargetType))
            query = query.Where(record => record.TargetType == normalizedTargetType);

        if (ForumOnly)
        {
            query = query.Where(record =>
                record.ActionType.StartsWith("forum.")
                || record.TargetType == "ForumThread"
                || record.TargetType == "ForumPost");
        }

        Records = await query
            // SQLite cannot order by DateTimeOffset directly; audit ids are append-only and monotonic.
            .OrderByDescending(static x => x.Id)
            .Take(200)
            .ToArrayAsync(cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
