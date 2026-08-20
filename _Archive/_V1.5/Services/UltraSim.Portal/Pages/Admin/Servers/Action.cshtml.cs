using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;
using UltraSim.Portal.Services.RegistryApi;

namespace UltraSim.Portal.Pages.Admin.Servers;

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
    public string ServerId { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string ActionName { get; set; } = string.Empty;

    [BindProperty]
    public string Reason { get; set; } = string.Empty;

    public OfficialRegistryAdminServerRecord? Record { get; private set; }
    public string ActionLabel { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!TryResolveAction(ActionName, out _))
            return NotFound();

        Record = await _registryQueryClient.GetAdminServerAsync(ServerId, cancellationToken);
        if (Record == null)
            return NotFound();

        ActionLabel = ResolveActionLabel(ActionName);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!TryResolveAction(ActionName, out ServerListingLifecycleState? lifecycleState))
            return NotFound();

        Record = await _registryQueryClient.GetAdminServerAsync(ServerId, cancellationToken);
        if (Record == null)
            return NotFound();

        ActionLabel = ResolveActionLabel(ActionName);

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ModelState.AddModelError(nameof(Reason), "Reason is required.");
            return Page();
        }

        ApplicationUser? actor = await _userManager.GetUserAsync(User);
        if (actor == null)
            return Challenge();

        string actorRole = await PortalRoleResolver.GetEffectiveRoleAsync(_userManager, actor);
        string auditActionType = ResolveAuditActionType(ActionName);
        string? actorIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            if (string.Equals(ActionName, "delete", StringComparison.OrdinalIgnoreCase))
            {
                await _registryQueryClient.DeleteAdminServerAsync(ServerId, cancellationToken);
            }
            else if (string.Equals(ActionName, "host-ban", StringComparison.OrdinalIgnoreCase))
            {
                await _registryQueryClient.BanAdminServerHostAsync(
                    ServerId,
                    new OfficialRegistryAdminHostBanRequest(Reason),
                    cancellationToken);
            }
            else if (string.Equals(ActionName, "retention-reset", StringComparison.OrdinalIgnoreCase))
            {
                await _registryQueryClient.ResetAdminRetentionAsync(ServerId, cancellationToken);
            }
            else
            {
                await _registryQueryClient.SetAdminLifecycleAsync(
                    ServerId,
                    new OfficialRegistryLifecycleUpdateRequest(lifecycleState!.Value),
                    cancellationToken);
            }

            await _adminAuditService.WriteAsync(
                actor.Id,
                actor.Email,
                actorRole,
                auditActionType,
                "server",
                ServerId,
                Reason,
                "success",
                details: Record.Row.DisplayName,
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
                auditActionType,
                "server",
                ServerId,
                Reason,
                "failure",
                details: errorMessage,
                actorIpAddress: actorIpAddress,
                cancellationToken: cancellationToken);
            return Page();
        }
        catch
        {
            const string errorMessage = "An unexpected error occurred while applying the admin action.";
            ModelState.AddModelError(string.Empty, errorMessage);
            await _adminAuditService.WriteAsync(
                actor.Id,
                actor.Email,
                actorRole,
                auditActionType,
                "server",
                ServerId,
                Reason,
                "failure",
                details: errorMessage,
                actorIpAddress: actorIpAddress,
                cancellationToken: cancellationToken);
            return Page();
        }

        return RedirectToPage("/Admin/Servers/Index");
    }

    private static bool TryResolveAction(string actionName, out ServerListingLifecycleState? lifecycleState)
    {
        lifecycleState = actionName.ToLowerInvariant() switch
        {
            "disable" => ServerListingLifecycleState.Disabled,
            "restore" => ServerListingLifecycleState.Active,
            "delete" => null,
            "host-ban" => null,
            "retention-reset" => null,
            _ => null,
        };

        return string.Equals(actionName, "delete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(actionName, "host-ban", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(actionName, "retention-reset", StringComparison.OrdinalIgnoreCase) ||
               lifecycleState.HasValue;
    }

    private static string ResolveActionLabel(string actionName)
    {
        return actionName.ToLowerInvariant() switch
        {
            "disable" => "Disable",
            "restore" => "Restore",
            "delete" => "Delete",
            "host-ban" => "Ban Host",
            "retention-reset" => "Reset TTL",
            _ => actionName,
        };
    }

    private static string ResolveAuditActionType(string actionName)
    {
        return actionName.ToLowerInvariant() switch
        {
            "host-ban" => "listing.host_ban",
            "retention-reset" => "listing.delist_ttl_reset",
            "delete" => "listing.delete",
            "disable" => "listing.disable",
            "restore" => "listing.restore",
            _ => $"listing.{actionName}",
        };
    }
}
