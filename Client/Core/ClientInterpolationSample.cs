namespace UltraSim.Client;

public readonly record struct ClientInterpolationSample<TState>(
    uint ServerTick,
    TState State);
