using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UltraSim.Discovery.Official;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Services.RegistryApi;

namespace UltraSim.Portal.Pages.Admin.Bans;

[Authorize(Policy = PortalPolicies.AdminPanel)]
public sealed class IndexModel : PageModel
{
    private readonly RegistryQueryClient _registryQueryClient;

    public IndexModel(RegistryQueryClient registryQueryClient)
    {
        _registryQueryClient = registryQueryClient;
    }

    public IReadOnlyList<OfficialRegistryAdminHostBanRecord> Records { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Records = await _registryQueryClient.GetAdminHostBansAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = ex.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized =>
                    "Registry bans endpoint returned HTTP 401. Configure matching Registry:AdminApiToken values in the portal and registry settings.",
                _ when ex.StatusCode.HasValue =>
                    $"Registry bans endpoint returned HTTP {(int)ex.StatusCode.Value}.",
                _ => "Could not reach the registry bans endpoint.",
            };
        }
        catch
        {
            ErrorMessage = "An unexpected error occurred while loading active host bans.";
        }
    }
}
