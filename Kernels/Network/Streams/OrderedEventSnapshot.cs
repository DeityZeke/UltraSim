namespace UltraSim.Network;

public readonly record struct OrderedEventSnapshot(
    int Count,
    int Capacity,
    ulong OldestAvailableSequence,
    ulong NewestAvailableSequence);
