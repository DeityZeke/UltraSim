namespace UltraSim.Network
{
    public readonly struct ClientInputSample<TInput>
    {
        public readonly ushort Sequence;
        public readonly long ClientTick;
        public readonly TInput Input;

        public ClientInputSample(ushort sequence, long clientTick, in TInput input)
        {
            Sequence = sequence;
            ClientTick = clientTick;
            Input = input;
        }
    }
}
