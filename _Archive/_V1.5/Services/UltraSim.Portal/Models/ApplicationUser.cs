using Microsoft.AspNetCore.Identity;

namespace UltraSim.Portal.Models;

public sealed class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public bool IsPrimaryOwner { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActiveUtc { get; set; } = DateTime.UtcNow;
}
