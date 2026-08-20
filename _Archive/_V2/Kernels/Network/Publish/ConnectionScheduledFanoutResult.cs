namespace UltraSim.Network;

public readonly record struct ConnectionScheduledFanoutResult(
    ConnectionScheduleResult Schedule,
    ScheduledWorkFanoutResult Fanout,
    long ScheduleElapsedTicks,
    long FanoutElapsedTicks);
