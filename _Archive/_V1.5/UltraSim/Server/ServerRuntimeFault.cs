using System;

namespace UltraSim.Server
{
    public sealed record ServerRuntimeFault(
        ServerRuntimeState State,
        string Operation,
        Exception Exception,
        DateTimeOffset Timestamp)
    {
        public string Message => Exception.Message;
    }
}
