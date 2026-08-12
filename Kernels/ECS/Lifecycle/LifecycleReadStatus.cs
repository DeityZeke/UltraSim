namespace UltraSim.ECS;

/// <summary>Describes whether a lifecycle read delivered a complete retained prefix.</summary>
public enum LifecycleReadStatus : byte
{
    /// <summary>The cursor is current; no retained event is pending.</summary>
    NoData = 0,

    /// <summary>A retained prefix was copied and the cursor advanced by the delivered count.</summary>
    Success = 1,

    /// <summary>The cursor fell behind overwritten history and must be explicitly recaptured.</summary>
    ResyncRequired = 2,
}
