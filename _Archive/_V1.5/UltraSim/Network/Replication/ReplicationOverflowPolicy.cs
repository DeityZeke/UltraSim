namespace UltraSim.Network
{
    public readonly struct ReplicationOverflowPolicy
    {
        public readonly ReplicationOverflowAction Critical;
        public readonly ReplicationOverflowAction State;
        public readonly ReplicationOverflowAction Cosmetic;

        public ReplicationOverflowPolicy(
            ReplicationOverflowAction critical = ReplicationOverflowAction.Defer,
            ReplicationOverflowAction state = ReplicationOverflowAction.Defer,
            ReplicationOverflowAction cosmetic = ReplicationOverflowAction.Drop)
        {
            Critical = critical;
            State = state;
            Cosmetic = cosmetic;
        }

        public ReplicationOverflowAction GetAction(ReplicationLane lane) =>
            lane switch
            {
                ReplicationLane.Critical => Critical,
                ReplicationLane.State => State,
                _ => Cosmetic,
            };
    }
}
