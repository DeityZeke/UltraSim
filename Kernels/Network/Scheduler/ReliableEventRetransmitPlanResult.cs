namespace UltraSim.Network;

public readonly record struct ReliableEventRetransmitPlanResult(
    int Count,
    bool RecoveryRequired,
    ulong NextSequence,
    ulong OldestAvailableSequence);
