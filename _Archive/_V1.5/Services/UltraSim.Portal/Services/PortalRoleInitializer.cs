using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Services;

public sealed class PortalRoleInitializer
{
    private readonly ApplicationDbContext _db;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PortalRoleInitializer> _logger;

    public PortalRoleInitializer(
        ApplicationDbContext db,
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ILogger<PortalRoleInitializer> logger)
    {
        _db = db;
        _roleManager = roleManager;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task EnsureRolesExistAsync()
    {
        foreach (string roleName in PortalRoles.All)
        {
            if (await _roleManager.RoleExistsAsync(roleName))
                continue;

            IdentityResult result = await _roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create role '{roleName}': {string.Join("; ", result.Errors.Select(static error => error.Description))}");
            }
        }
    }

    public async Task SeedPrimaryOwnerAsync(string identity, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identity))
            throw new InvalidOperationException("Primary owner identity is required.");

        string normalizedIdentity = identity.Trim();
        await EnsureRolesExistAsync();

        ApplicationUser? targetUser = await _userManager.FindByEmailAsync(normalizedIdentity);
        targetUser ??= await _userManager.FindByNameAsync(normalizedIdentity);

        if (targetUser == null)
            throw new InvalidOperationException($"No account matched '{identity}'.");

        ApplicationUser? existingPrimaryOwner = await _db.Users.FirstOrDefaultAsync(x => x.IsPrimaryOwner, cancellationToken);
        if (existingPrimaryOwner != null && existingPrimaryOwner.Id != targetUser.Id)
        {
            throw new InvalidOperationException(
                $"Primary owner is already assigned to '{existingPrimaryOwner.Email ?? existingPrimaryOwner.UserName ?? existingPrimaryOwner.Id}'.");
        }

        if (!targetUser.IsPrimaryOwner)
        {
            targetUser.IsPrimaryOwner = true;
            IdentityResult updateResult = await _userManager.UpdateAsync(targetUser);
            if (!updateResult.Succeeded)
                throw new InvalidOperationException(string.Join("; ", updateResult.Errors.Select(static error => error.Description)));
        }

        if (!await _userManager.IsInRoleAsync(targetUser, PortalRoles.PrimaryOwner))
        {
            IdentityResult addRoleResult = await _userManager.AddToRoleAsync(targetUser, PortalRoles.PrimaryOwner);
            if (!addRoleResult.Succeeded)
                throw new InvalidOperationException(string.Join("; ", addRoleResult.Errors.Select(static error => error.Description)));
        }

        _logger.LogInformation(
            "Primary owner seeded for account {Identity}.",
            targetUser.Email ?? targetUser.UserName ?? targetUser.Id);
    }
}
