namespace UltraSim.ECS;

/// <summary>
/// Determines how several watched component columns qualify a changed query.
/// Every watched version is compared with one cursor baseline. A successful
/// observation advances that cursor even when no chunk or row qualifies.
/// </summary>
public enum ChangedMatchMode : byte
{
    /// <summary>At least one watched component changed after the cursor baseline.</summary>
    Any = 1,

    /// <summary>Every watched component changed after the same cursor baseline.</summary>
    All = 2,
}
