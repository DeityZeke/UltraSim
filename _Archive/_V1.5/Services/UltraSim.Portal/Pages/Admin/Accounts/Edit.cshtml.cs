using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;

namespace UltraSim.Portal.Pages.Admin.Accounts;

[Authorize(Policy = PortalPolicies.AdminPanel)]
public sealed class EditModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AdminAuditService _adminAuditService;

    public EditModel(
        UserManager<ApplicationUser> userManager,
        AdminAuditService adminAuditService)
    {
        _userManager = userManager;
        _adminAuditService = adminAuditService;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public string SelectedRole { get; set; } = PortalRoles.User;

    [BindProperty]
    public string Reason { get; set; } = string.Empty;

    public ApplicationUser? TargetUser { get; private set; }
    public string CurrentRole { get; private set; } = PortalRoles.User;
    public IReadOnlyList<string> AssignableRoles { get; private set; } = [];
    public string ActorRole { get; private set; } = PortalRoles.User;
    public bool CanEditTarget { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadStateAsync(cancellationToken))
            return NotFound();

        SelectedRole = CurrentRole;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!await LoadStateAsync(cancellationToken))
            return NotFound();

        if (!CanEditTarget)
        {
            ModelState.AddModelError(string.Empty, "You cannot edit that account through the normal admin UI.");
            return Page();
        }

        if (!AssignableRoles.Contains(SelectedRole, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(SelectedRole), "You cannot assign that role.");
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ModelState.AddModelError(nameof(Reason), "Reason is required.");
            return Page();
        }

        ApplicationUser? actor = await _userManager.GetUserAsync(User);
        if (actor == null)
            return Challenge();

        string? actorIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        IList<string> existingRoles = await _userManager.GetRolesAsync(TargetUser!);
        string priorRole = PortalRoles.ResolveEffectiveRole(existingRoles, TargetUser!.IsPrimaryOwner);

        for (int i = 0; i < PortalRoles.All.Length; i++)
        {
            string roleName = PortalRoles.All[i];
            if (!await _userManager.IsInRoleAsync(TargetUser, roleName))
                continue;

            IdentityResult removeResult = await _userManager.RemoveFromRoleAsync(TargetUser, roleName);
            if (!removeResult.Succeeded)
            {
                ModelState.AddModelError(string.Empty, string.Join("; ", removeResult.Errors.Select(static x => x.Description)));
                await _adminAuditService.WriteAsync(
                    actor.Id,
                    actor.Email,
                    ActorRole,
                    "accounts.role_assign",
                    "account",
                    TargetUser.Id,
                    Reason,
                    "failure",
                    details: string.Join("; ", removeResult.Errors.Select(static x => x.Description)),
                    actorIpAddress: actorIpAddress,
                    cancellationToken: cancellationToken);
                return Page();
            }
        }

        if (!string.Equals(SelectedRole, PortalRoles.User, StringComparison.Ordinal))
        {
            IdentityResult addResult = await _userManager.AddToRoleAsync(TargetUser, SelectedRole);
            if (!addResult.Succeeded)
            {
                ModelState.AddModelError(string.Empty, string.Join("; ", addResult.Errors.Select(static x => x.Description)));
                await _adminAuditService.WriteAsync(
                    actor.Id,
                    actor.Email,
                    ActorRole,
                    "accounts.role_assign",
                    "account",
                    TargetUser.Id,
                    Reason,
                    "failure",
                    details: string.Join("; ", addResult.Errors.Select(static x => x.Description)),
                    actorIpAddress: actorIpAddress,
                    cancellationToken: cancellationToken);
                return Page();
            }
        }

        await _adminAuditService.WriteAsync(
            actor.Id,
            actor.Email,
            ActorRole,
            "accounts.role_assign",
            "account",
            TargetUser.Id,
            Reason,
            "success",
            details: $"{priorRole} -> {SelectedRole}",
            actorIpAddress: actorIpAddress,
            cancellationToken: cancellationToken);

        return RedirectToPage("/Admin/Accounts/Index");
    }

    private async Task<bool> LoadStateAsync(CancellationToken cancellationToken)
    {
        ApplicationUser? actor = await _userManager.GetUserAsync(User);
        if (actor == null)
            return false;

        ActorRole = await PortalRoleResolver.GetEffectiveRoleAsync(_userManager, actor);
        AssignableRoles = PortalRoleResolver.GetAssignableRolesFor(ActorRole);

        TargetUser = await _userManager.FindByIdAsync(Id);
        if (TargetUser == null)
            return false;

        CurrentRole = await PortalRoleResolver.GetEffectiveRoleAsync(_userManager, TargetUser);
        CanEditTarget = PortalRoleResolver.CanManageTarget(ActorRole, CurrentRole, TargetUser.IsPrimaryOwner);
        return true;
    }
}
