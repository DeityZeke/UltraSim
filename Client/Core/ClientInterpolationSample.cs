namespace UltraSim.Client;

public readonly record struct ClientInterpolationSample<TState>(
    ulong ServerTick,
    TState State);
