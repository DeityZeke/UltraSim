namespace UltraSim.Network;

/// <summary>
/// Describes a full-state baseline snapshot for one client-side apply window.
/// This is the narrow seam that later delta/recovery work can build on.
/// </summary>
public readonly record struct StateBaselineDescriptor(
    uint BaselineId,
    ulong HighestLatestStateSequence,
    ulong LastOrderedEventSequence,
    ushort ItemCount);
