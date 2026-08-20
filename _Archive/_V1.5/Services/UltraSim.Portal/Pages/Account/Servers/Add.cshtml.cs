using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services.RegistryApi;
using UltraSim.Discovery.Official;

namespace UltraSim.Portal.Pages.Account.Servers;

[Authorize]
public sealed class AddModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RegistryQueryClient _registryQueryClient;

    public AddModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RegistryQueryClient registryQueryClient)
    {
        _db = db;
        _userManager = userManager;
        _registryQueryClient = registryQueryClient;
    }

    [BindProperty]
    public OwnerServerFormModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        if (string.IsNullOrWhiteSpace(Input.OwnershipToken))
        {
            ModelState.AddModelError(nameof(Input.OwnershipToken), "Ownership token is required.");
            return Page();
        }

        ApplicationUser? user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Challenge();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ServerClaimStatus status = ServerClaimStatus.Draft;
        string? requestedServerId = null;
        string? lastError = null;

        try
        {
            OfficialRegistryOwnerClaimResponse claimResponse = await _registryQueryClient.SubmitOwnerClaimAsync(
                new OfficialRegistryOwnerClaimRequest(
                    OwnerAccountId: user.Id,
                    DisplayName: Input.DisplayName.Trim(),
                    TargetHost: Input.TargetHost.Trim(),
                    TargetPort: Input.TargetPort,
                    OwnershipToken: Input.OwnershipToken!.Trim(),
                    Description: Normalize(Input.Description),
                    WebsiteUrl: Normalize(Input.WebsiteUrl),
                    DiscordUrl: Normalize(Input.DiscordUrl)),
                cancellationToken);

            requestedServerId = claimResponse.ServerId;
            lastError = Normalize(claimResponse.Message);
            status = claimResponse.State == OfficialRegistryOwnerClaimState.PendingVerification
                ? ServerClaimStatus.PendingVerification
                : ServerClaimStatus.Failed;
        }
        catch (HttpRequestException ex)
        {
            status = ServerClaimStatus.Failed;
            lastError = ex.StatusCode.HasValue
                ? $"Registry returned an error (HTTP {(int)ex.StatusCode.Value}). Please try again later."
                : "Could not reach the registry. Please try again later.";
        }
        catch
        {
            status = ServerClaimStatus.Failed;
            lastError = "An unexpected error occurred. Please try again later.";
        }

        _db.ServerClaims.Add(new ServerClaim
        {
            OwnerUserId = user.Id,
            DisplayName = Input.DisplayName.Trim(),
            Description = Normalize(Input.Description),
            TargetHost = Input.TargetHost.Trim(),
            TargetPort = Input.TargetPort,
            WebsiteUrl = Normalize(Input.WebsiteUrl),
            DiscordUrl = Normalize(Input.DiscordUrl),
            OwnershipToken = null,
            RequestedServerId = requestedServerId,
            Status = status,
            LastError = lastError,
            SubmittedUtc = now,
            UpdatedUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("/Account/Servers/Index");
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
