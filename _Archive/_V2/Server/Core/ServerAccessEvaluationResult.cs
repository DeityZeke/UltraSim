namespace UltraSim.Server;

public sealed record ServerAccessEvaluationResult(
    bool IsAllowed,
    ServerAccessDecisionReason Reason,
    string MatchedRuleKey,
    string MatchedPermissionKey,
    IReadOnlyList<string> MissingRuleKeys)
{
    public static ServerAccessEvaluationResult Allowed(
        ServerAccessDecisionReason reason,
        string matchedPermissionKey,
        string matchedRuleKey = "",
        IReadOnlyList<string>? missingRuleKeys = null)
    {
        return new ServerAccessEvaluationResult(
            true,
            reason,
            matchedRuleKey,
            matchedPermissionKey,
            missingRuleKeys ?? Array.Empty<string>());
    }

    public static ServerAccessEvaluationResult Denied(
        ServerAccessDecisionReason reason = ServerAccessDecisionReason.Denied,
        string matchedPermissionKey = "",
        string matchedRuleKey = "",
        IReadOnlyList<string>? missingRuleKeys = null)
    {
        return new ServerAccessEvaluationResult(
            false,
            reason,
            matchedRuleKey,
            matchedPermissionKey,
            missingRuleKeys ?? Array.Empty<string>());
    }
}
