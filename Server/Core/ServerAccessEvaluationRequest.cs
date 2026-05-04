namespace UltraSim.Server;

public sealed record ServerAccessEvaluationRequest
{
    public ServerAccessEvaluationRequest(
        string permissionKey,
        IEnumerable<string>? ruleKeys = null,
        IEnumerable<string>? directGrants = null,
        IEnumerable<string>? directDenies = null,
        int accessLevel = 0,
        int? requiredAccessLevel = null)
    {
        if (requiredAccessLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(requiredAccessLevel));

        PermissionKey = ServerAccessKey.NormalizePermissionKey(permissionKey);
        RuleKeys = ServerAccessKey.NormalizeRuleSet(ruleKeys);
        DirectGrants = ServerAccessKey.NormalizePermissionSet(directGrants);
        DirectDenies = ServerAccessKey.NormalizePermissionSet(directDenies);
        AccessLevel = accessLevel;
        RequiredAccessLevel = requiredAccessLevel;
    }

    public string PermissionKey { get; }

    public IReadOnlyList<string> RuleKeys { get; }

    public IReadOnlyList<string> DirectGrants { get; }

    public IReadOnlyList<string> DirectDenies { get; }

    public int AccessLevel { get; }

    public int? RequiredAccessLevel { get; }
}
