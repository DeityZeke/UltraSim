namespace UltraSim.Network
{
    public readonly struct PredictedStateSample<TState>
    {
        public readonly ushort Sequence;
        public readonly long ClientTick;
        public readonly TState State;

        public PredictedStateSample(ushort sequence, long clientTick, in TState state)
        {
            Sequence = sequence;
            ClientTick = clientTick;
            State = state;
        }
    }
}
