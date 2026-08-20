namespace UltraSim.Server;

public sealed class ServerAccessRuleRegistry
{
    private readonly Dictionary<string, ServerAccessRuleDescriptor> _rules = new(StringComparer.Ordinal);

    public int Count => _rules.Count;

    public bool TryRegister(ServerAccessRuleDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (_rules.ContainsKey(descriptor.Key))
            return false;

        _rules.Add(descriptor.Key, descriptor);
        return true;
    }

    public void RegisterOrReplace(ServerAccessRuleDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _rules[descriptor.Key] = descriptor;
    }

    public bool Remove(string key)
    {
        string normalized = ServerAccessKey.NormalizeRuleKey(key);
        return _rules.Remove(normalized);
    }

    public bool TryGetRule(string key, out ServerAccessRuleDescriptor descriptor)
    {
        string normalized = ServerAccessKey.NormalizeRuleKey(key);
        return _rules.TryGetValue(normalized, out descriptor!);
    }

    public IReadOnlyList<ServerAccessRuleDescriptor> Snapshot()
    {
        return _rules.Values
            .OrderBy(static rule => rule.Key, StringComparer.Ordinal)
            .ToArray();
    }

    public ServerAccessEvaluationResult Evaluate(ServerAccessEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var missingRules = new List<string>();
        ServerAccessEvaluationResult? directDeny = MatchDirect(
            request.DirectDenies,
            request.PermissionKey,
            ServerAccessDecisionReason.DirectDeny,
            allowed: false,
            missingRules);
        if (directDeny is not null)
            return directDeny;

        ServerAccessEvaluationResult? ruleDeny = MatchRules(
            request.RuleKeys,
            request.PermissionKey,
            useDenies: true,
            missingRules);
        if (ruleDeny is not null)
            return ruleDeny;

        ServerAccessEvaluationResult? directGrant = MatchDirect(
            request.DirectGrants,
            request.PermissionKey,
            ServerAccessDecisionReason.DirectGrant,
            allowed: true,
            missingRules);
        if (directGrant is not null)
            return directGrant;

        ServerAccessEvaluationResult? ruleGrant = MatchRules(
            request.RuleKeys,
            request.PermissionKey,
            useDenies: false,
            missingRules);
        if (ruleGrant is not null)
            return ruleGrant;

        if (request.RequiredAccessLevel is int requiredAccessLevel
            && request.AccessLevel >= requiredAccessLevel)
        {
            return ServerAccessEvaluationResult.Allowed(
                ServerAccessDecisionReason.LegacyAccessLevel,
                request.PermissionKey,
                missingRuleKeys: missingRules.ToArray());
        }

        return ServerAccessEvaluationResult.Denied(missingRuleKeys: missingRules.ToArray());
    }

    private static ServerAccessEvaluationResult? MatchDirect(
        IReadOnlyList<string> patterns,
        string permissionKey,
        ServerAccessDecisionReason reason,
        bool allowed,
        List<string> missingRules)
    {
        for (int i = 0; i < patterns.Count; i++)
        {
            string pattern = patterns[i];
            if (!ServerAccessKey.MatchesPermission(pattern, permissionKey))
                continue;

            return allowed
                ? ServerAccessEvaluationResult.Allowed(reason, pattern, missingRuleKeys: missingRules.ToArray())
                : ServerAccessEvaluationResult.Denied(reason, pattern, missingRuleKeys: missingRules.ToArray());
        }

        return null;
    }

    private ServerAccessEvaluationResult? MatchRules(
        IReadOnlyList<string> ruleKeys,
        string permissionKey,
        bool useDenies,
        List<string> missingRules)
    {
        for (int i = 0; i < ruleKeys.Count; i++)
        {
            string ruleKey = ruleKeys[i];
            if (!_rules.TryGetValue(ruleKey, out ServerAccessRuleDescriptor? rule))
            {
                if (!missingRules.Contains(ruleKey, StringComparer.Ordinal))
                    missingRules.Add(ruleKey);
                continue;
            }

            IReadOnlyList<string> patterns = useDenies ? rule.Denies : rule.Grants;
            for (int patternIndex = 0; patternIndex < patterns.Count; patternIndex++)
            {
                string pattern = patterns[patternIndex];
                if (!ServerAccessKey.MatchesPermission(pattern, permissionKey))
                    continue;

                return useDenies
                    ? ServerAccessEvaluationResult.Denied(
                        ServerAccessDecisionReason.RuleDeny,
                        pattern,
                        rule.Key,
                        missingRules.ToArray())
                    : ServerAccessEvaluationResult.Allowed(
                        ServerAccessDecisionReason.RuleGrant,
                        pattern,
                        rule.Key,
                        missingRules.ToArray());
            }
        }

        return null;
    }
}
