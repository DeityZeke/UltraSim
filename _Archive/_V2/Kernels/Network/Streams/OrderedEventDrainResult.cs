namespace UltraSim.Network;

public readonly record struct OrderedEventDrainResult(
    OrderedEventDrainStatus Status,
    int Count,
    OrderedEventCursor NextCursor,
    ulong OldestAvailableSequence,
    ulong NewestAvailableSequence);
