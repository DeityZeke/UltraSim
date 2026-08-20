namespace UltraSim.Server;

public sealed record ServerApplicationFault(
    ServerApplicationState State,
    string Operation,
    Exception Exception,
    DateTimeOffset TimestampUtc)
{
    public string Message => Exception.Message;
}
