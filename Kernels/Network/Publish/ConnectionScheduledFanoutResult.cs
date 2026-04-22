namespace UltraSim.Network;

public readonly record struct ConnectionScheduledFanoutResult(
    ConnectionScheduleResult Schedule,
    ScheduledWorkFanoutResult Fanout);
