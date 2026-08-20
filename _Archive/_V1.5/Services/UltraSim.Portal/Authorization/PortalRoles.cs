namespace UltraSim.Portal.Authorization;

public static class PortalRoles
{
    public const string PrimaryOwner = "PrimaryOwner";
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Developer = "Developer";
    public const string Moderator = "Moderator";
    public const string User = "User";

    public static readonly string[] All =
    [
        PrimaryOwner,
        Owner,
        Admin,
        Developer,
        Moderator,
    ];

    public static readonly string[] AdminPanelRoles =
    [
        PrimaryOwner,
        Owner,
        Admin,
    ];

    public static readonly string[] AdminAssignableRoles =
    [
        User,
        Moderator,
        Developer,
    ];

    public static readonly string[] OwnerAssignableRoles =
    [
        User,
        Moderator,
        Developer,
        Admin,
        Owner,
    ];

    public static string AdminPanelRolesCsv => string.Join(',', AdminPanelRoles);

    public static int GetTier(string? roleName)
    {
        return roleName switch
        {
            PrimaryOwner => 100,
            Owner => 95,
            Admin => 90,
            Developer => 80,
            Moderator => 40,
            _ => 0,
        };
    }

    public static string ResolveEffectiveRole(IEnumerable<string> roles, bool isPrimaryOwner)
    {
        if (isPrimaryOwner)
            return PrimaryOwner;

        string effective = User;
        int tier = 0;

        foreach (string role in roles)
        {
            int candidateTier = GetTier(role);
            if (candidateTier > tier)
            {
                tier = candidateTier;
                effective = role;
            }
        }

        return effective;
    }
}
