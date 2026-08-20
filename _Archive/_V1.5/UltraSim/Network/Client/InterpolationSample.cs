namespace UltraSim.Network
{
    public readonly struct InterpolationSample<TState>
    {
        public readonly long ServerTick;
        public readonly TState State;

        public InterpolationSample(long serverTick, in TState state)
        {
            ServerTick = serverTick;
            State = state;
        }
    }
}
