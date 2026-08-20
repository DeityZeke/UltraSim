using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Pages.Admin.Accounts;

[Authorize(Policy = PortalPolicies.AdminPanel)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Role { get; set; }

    public IReadOnlyList<AccountEntry> Accounts { get; private set; } = [];
    public IReadOnlyList<string> AvailableRoles { get; } =
    [
        PortalRoles.User,
        PortalRoles.Moderator,
        PortalRoles.Developer,
        PortalRoles.Admin,
        PortalRoles.Owner,
        PortalRoles.PrimaryOwner,
    ];

    public async Task OnGetAsync()
    {
        List<ApplicationUser> users = await _db.Users.ToListAsync();

        var userRoleRows = await _db.UserRoles
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id,
                  (ur, r) => new { ur.UserId, RoleName = r.Name ?? string.Empty })
            .ToListAsync();

        Dictionary<string, List<string>> rolesByUserId = userRoleRows
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName).ToList());

        List<AccountEntry> entries = new(users.Count);
        for (int i = 0; i < users.Count; i++)
        {
            ApplicationUser user = users[i];
            rolesByUserId.TryGetValue(user.Id, out List<string>? roles);
            string effectiveRole = PortalRoles.ResolveEffectiveRole(roles ?? [], user.IsPrimaryOwner);
            entries.Add(new AccountEntry(
                user.Id,
                user.Email ?? user.UserName ?? user.Id,
                user.UserName,
                user.DisplayName,
                effectiveRole,
                user.IsPrimaryOwner));
        }

        IEnumerable<AccountEntry> query = entries;
        string? normalizedSearch = Normalize(Search);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(entry =>
                Contains(entry.DisplayNameOrEmail, normalizedSearch) ||
                Contains(entry.UserName, normalizedSearch) ||
                Contains(entry.DisplayName, normalizedSearch) ||
                Contains(entry.UserId, normalizedSearch));
        }

        string? normalizedRole = Normalize(Role);
        if (!string.IsNullOrWhiteSpace(normalizedRole))
        {
            query = query.Where(entry => string.Equals(entry.EffectiveRole, normalizedRole, StringComparison.Ordinal));
        }

        Accounts = query
            .OrderByDescending(static x => PortalRoles.GetTier(x.EffectiveRole))
            .ThenBy(static x => x.DisplayNameOrEmail, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static bool Contains(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    public sealed record AccountEntry(
        string UserId,
        string DisplayNameOrEmail,
        string? UserName,
        string? DisplayName,
        string EffectiveRole,
        bool IsPrimaryOwner);
}
