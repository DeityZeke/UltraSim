namespace UltraSim.Server;

public enum ServerAccessDecisionReason : byte
{
    Denied = 0,
    DirectDeny = 1,
    RuleDeny = 2,
    DirectGrant = 3,
    RuleGrant = 4,
    LegacyAccessLevel = 5,
}
