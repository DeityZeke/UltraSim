namespace UltraSim.Client.Engines.Networking
{
    public readonly struct ClientInputSendResult
    {
        public readonly ushort Sequence;
        public readonly int PayloadBytes;

        public ClientInputSendResult(ushort sequence, int payloadBytes)
        {
            Sequence = sequence;
            PayloadBytes = payloadBytes;
        }
    }
}
