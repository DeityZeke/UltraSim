namespace UltraSim.Client;

public readonly record struct ClientPumpProfileSnapshot(
    int InboundDatagramCount,
    long PollElapsedTicks,
    long ControlElapsedTicks,
    long MovementElapsedTicks,
    long ObserverStateElapsedTicks,
    long ObserverEventElapsedTicks);
