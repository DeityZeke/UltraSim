using System.ComponentModel.DataAnnotations;
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
public sealed class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RegistryQueryClient _registryQueryClient;

    public DeleteModel(
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
    [Required]
    public string ConfirmationText { get; set; } = string.Empty;

    public string ServerName { get; private set; } = "Unknown Server";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ServerClaim? claim = await LoadOwnedClaimAsync(cancellationToken);
        if (claim == null)
            return NotFound();

        ServerName = claim.DisplayName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ApplicationUser? user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Challenge();

        ServerClaim? claim = await _db.ServerClaims
            .FirstOrDefaultAsync(x => x.Id == Id && x.OwnerUserId == user.Id, cancellationToken);

        if (claim == null)
            return NotFound();

        ServerName = claim.DisplayName;

        if (!StringComparer.Ordinal.Equals(ConfirmationText, "DELETE"))
        {
            ModelState.AddModelError(string.Empty, "Type DELETE exactly to confirm removal.");
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(claim.RequestedServerId))
        {
            try
            {
                await _registryQueryClient.DeleteOwnerClaimedServerAsync(
                    claim.RequestedServerId,
                    new OfficialRegistryOwnerDeleteRequest(user.Id),
                    cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                string msg = ex.StatusCode.HasValue
                    ? $"Registry returned an error (HTTP {(int)ex.StatusCode.Value}). Please try again later."
                    : "Could not reach the registry. Please try again later.";
                ModelState.AddModelError(string.Empty, msg);
                return Page();
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "An unexpected error occurred. Please try again later.");
                return Page();
            }
        }

        _db.ServerClaims.Remove(claim);
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
}
