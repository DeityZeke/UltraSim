namespace UltraSim.Client;

public readonly record struct ClientPumpProfileSnapshot(
    int InboundDatagramCount,
    long PollElapsedTicks,
    long PollPendingDrainElapsedTicks,
    long PollSocketReceiveElapsedTicks,
    long PollBundleUnpackElapsedTicks,
    long PollPayloadCopyElapsedTicks,
    long PollEnvelopeParseElapsedTicks,
    long PollAsyncReceiveElapsedTicks,
    int PollAsyncQueuedDatagramCount,
    long ControlElapsedTicks,
    long MovementElapsedTicks,
    long ObserverStateElapsedTicks,
    long ObserverStateDecodeElapsedTicks,
    long ObserverStateApplyElapsedTicks,
    long ObserverEventElapsedTicks);
