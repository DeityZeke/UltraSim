namespace UltraSim.Client;

public readonly record struct ClientInputSample<TInput>(
    ushort Sequence,
    uint ClientTick,
    TInput Input);
