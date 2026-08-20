using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using UltraSim.Discovery.Official;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services.RegistryApi;

namespace UltraSim.Portal.Pages.Account.Servers;

[Authorize]
public sealed class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RegistryQueryClient _registryQueryClient;

    public EditModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RegistryQueryClient registryQueryClient)
    {
        _db = db;
        _userManager = userManager;
        _registryQueryClient = registryQueryClient;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public OwnerServerFormModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ServerClaim? claim = await LoadOwnedClaimAsync(cancellationToken);
        if (claim == null)
            return NotFound();

        Input = new OwnerServerFormModel
        {
            DisplayName = claim.DisplayName,
            Description = claim.Description,
            TargetHost = claim.TargetHost,
            TargetPort = claim.TargetPort,
            WebsiteUrl = claim.WebsiteUrl,
            DiscordUrl = claim.DiscordUrl,
            OwnershipToken = string.Empty
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        ApplicationUser? user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Challenge();

        ServerClaim? claim = await _db.ServerClaims
            .FirstOrDefaultAsync(x => x.Id == Id && x.OwnerUserId == user.Id, cancellationToken);

        if (claim == null)
            return NotFound();

        bool ownershipTokenChanged = !string.IsNullOrWhiteSpace(Input.OwnershipToken?.Trim());

        claim.DisplayName = Input.DisplayName.Trim();
        claim.Description = Normalize(Input.Description);
        claim.TargetHost = Input.TargetHost.Trim();
        claim.TargetPort = Input.TargetPort;
        claim.WebsiteUrl = Normalize(Input.WebsiteUrl);
        claim.DiscordUrl = Normalize(Input.DiscordUrl);
        claim.OwnershipToken = null;
        claim.UpdatedUtc = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(claim.RequestedServerId))
        {
            try
            {
                await _registryQueryClient.UpdateOwnerMetadataAsync(
                    claim.RequestedServerId,
                    new OfficialRegistryOwnerMetadataUpdateRequest(
                        user.Id,
                        claim.DisplayName,
                        claim.TargetHost,
                        claim.TargetPort,
                        claim.Description,
                        claim.WebsiteUrl,
                        claim.DiscordUrl),
                    cancellationToken);

                if (ownershipTokenChanged)
                {
                    await _registryQueryClient.UpdateOwnerTokenAsync(
                        claim.RequestedServerId,
                        new OfficialRegistryOwnerTokenUpdateRequest(
                            user.Id,
                            Input.OwnershipToken!.Trim()),
                        cancellationToken);

                    claim.Status = ServerClaimStatus.PendingVerification;
                    claim.LastError = "Verification pending.";
                }
                else
                {
                    claim.LastError = claim.Status == ServerClaimStatus.Verified ? null : "Verification pending.";
                    if (claim.Status != ServerClaimStatus.Verified)
                        claim.Status = ServerClaimStatus.PendingVerification;
                }
            }
            catch (HttpRequestException ex)
            {
                claim.Status = ServerClaimStatus.Failed;
                claim.LastError = ex.StatusCode.HasValue
                    ? $"Registry returned an error (HTTP {(int)ex.StatusCode.Value}). Please try again later."
                    : "Could not reach the registry. Please try again later.";
            }
            catch
            {
                claim.Status = ServerClaimStatus.Failed;
                claim.LastError = "An unexpected error occurred. Please try again later.";
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("/Account/Servers/Index");
    }

    private async Task<ServerClaim?> LoadOwnedClaimAsync(CancellationToken cancellationToken)
    {
        ApplicationUser? user = await _userManager.GetUserAsync(User);
        if (user == null)
            return null;

        return await _db.ServerClaims
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Id && x.OwnerUserId == user.Id, cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
