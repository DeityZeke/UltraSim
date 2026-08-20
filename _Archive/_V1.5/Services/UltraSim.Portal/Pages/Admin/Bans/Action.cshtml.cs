using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;
using UltraSim.Portal.Services.RegistryApi;

namespace UltraSim.Portal.Pages.Admin.Bans;

[Authorize(Policy = PortalPolicies.AdminPanel)]
public sealed class ActionModel : PageModel
{
    private readonly RegistryQueryClient _registryQueryClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AdminAuditService _adminAuditService;

    public ActionModel(
        RegistryQueryClient registryQueryClient,
        UserManager<ApplicationUser> userManager,
        AdminAuditService adminAuditService)
    {
        _registryQueryClient = registryQueryClient;
        _userManager = userManager;
        _adminAuditService = adminAuditService;
    }

    [BindProperty(SupportsGet = true)]
    public string Host { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string ActionName { get; set; } = string.Empty;

    [BindProperty]
    public string Reason { get; set; } = string.Empty;

    public string ActionLabel { get; private set; } = string.Empty;

    public IActionResult OnGet()
    {
        if (!TryResolveAction(ActionName))
            return NotFound();

        ActionLabel = "Lift Host Ban";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!TryResolveAction(ActionName))
            return NotFound();

        ActionLabel = "Lift Host Ban";

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ModelState.AddModelError(nameof(Reason), "Reason is required.");
            return Page();
        }

        ApplicationUser? actor = await _userManager.GetUserAsync(User);
        if (actor == null)
            return Challenge();

        string actorRole = await PortalRoleResolver.GetEffectiveRoleAsync(_userManager, actor);
        string? actorIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            await _registryQueryClient.LiftAdminHostBanAsync(Host, cancellationToken);

            await _adminAuditService.WriteAsync(
                actor.Id,
                actor.Email,
                actorRole,
                "listing.host_unban",
                "host",
                Host,
                Reason,
                "success",
                details: Host,
                actorIpAddress: actorIpAddress,
                cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            string errorMessage = ex.StatusCode.HasValue
                ? $"Registry returned HTTP {(int)ex.StatusCode.Value}."
                : "Could not reach the registry.";

            ModelState.AddModelError(string.Empty, errorMessage);
            await _adminAuditService.WriteAsync(
                actor.Id,
                actor.Email,
                actorRole,
                "listing.host_unban",
                "host",
                Host,
                Reason,
                "failure",
                details: errorMessage,
                actorIpAddress: actorIpAddress,
                cancellationToken: cancellationToken);
            return Page();
        }
        catch
        {
            const string errorMessage = "An unexpected error occurred while lifting the host ban.";
            ModelState.AddModelError(string.Empty, errorMessage);
            await _adminAuditService.WriteAsync(
                actor.Id,
                actor.Email,
                actorRole,
                "listing.host_unban",
                "host",
                Host,
                Reason,
                "failure",
                details: errorMessage,
                actorIpAddress: actorIpAddress,
                cancellationToken: cancellationToken);
            return Page();
        }

        return RedirectToPage("/Admin/Bans/Index");
    }

    private static bool TryResolveAction(string actionName)
    {
        return string.Equals(actionName, "lift", StringComparison.OrdinalIgnoreCase);
    }
}
