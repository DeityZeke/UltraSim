namespace UltraSim.Server.Hosting;

public readonly record struct ServerHostPumpResult(
    int CommandsHandled,
    int TicksProcessed,
    int DroppedCatchUpTicks);
