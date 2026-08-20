namespace UltraSim.Client.Engines.Networking
{
    public readonly struct ClientInboundCorrection<TCorrection>
    {
        public readonly ushort AuthoritativeSequence;
        public readonly long ServerTick;
        public readonly TCorrection Correction;

        public ClientInboundCorrection(
            ushort authoritativeSequence,
            long serverTick,
            in TCorrection correction)
        {
            AuthoritativeSequence = authoritativeSequence;
            ServerTick = serverTick;
            Correction = correction;
        }
    }
}
