namespace UltraSim.Network
{
    public readonly struct SessionDisconnectReasonCount
    {
        public readonly SessionDisconnectReason Reason;
        public readonly long Count;

        public SessionDisconnectReasonCount(SessionDisconnectReason reason, long count)
        {
            Reason = reason;
            Count = count;
        }
    }
}
