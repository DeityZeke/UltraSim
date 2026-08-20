using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Services.RegistryApi;

namespace UltraSim.Portal.Pages.Admin.Servers;

[Authorize(Policy = PortalPolicies.AdminPanel)]
public sealed class IndexModel : PageModel
{
    private readonly RegistryQueryClient _registryQueryClient;

    public IndexModel(RegistryQueryClient registryQueryClient)
    {
        _registryQueryClient = registryQueryClient;
    }

    [BindProperty(SupportsGet = true)]
    public ServerListingLifecycleState? Lifecycle { get; set; }

    [BindProperty(SupportsGet = true)]
    public ServerListingHealthState? Health { get; set; }

    public IReadOnlyList<OfficialRegistryAdminServerRecord> Records { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<OfficialRegistryAdminServerRecord> records = await _registryQueryClient.GetAdminServersAsync(cancellationToken);

            if (Lifecycle.HasValue)
                records = records.Where(x => x.Row.LifecycleState == Lifecycle.Value).ToArray();

            if (Health.HasValue)
                records = records.Where(x => x.Row.HealthState == Health.Value).ToArray();

            Records = records;
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = ex.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized =>
                    "Registry admin listing endpoint returned HTTP 401. Configure matching Registry:AdminApiToken values in the portal and registry settings.",
                _ when ex.StatusCode.HasValue =>
                    $"Registry admin listing endpoint returned HTTP {(int)ex.StatusCode.Value}.",
                _ => "Could not reach the registry admin listing endpoint.",
            };
        }
        catch
        {
            ErrorMessage = "An unexpected error occurred while loading admin server records.";
        }
    }
}
