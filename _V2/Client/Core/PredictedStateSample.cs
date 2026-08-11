namespace UltraSim.Client;

public readonly record struct PredictedStateSample<TState>(
    ushort Sequence,
    uint ClientTick,
    TState State);
