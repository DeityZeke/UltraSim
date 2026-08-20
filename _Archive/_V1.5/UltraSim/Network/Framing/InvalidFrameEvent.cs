namespace UltraSim.Network
{
    public readonly struct InvalidFrameEvent
    {
        public readonly int FrameLength;
        public readonly int MaxFrameLength;
        public readonly int BufferedBytes;

        public InvalidFrameEvent(int frameLength, int maxFrameLength, int bufferedBytes)
        {
            FrameLength = frameLength;
            MaxFrameLength = maxFrameLength;
            BufferedBytes = bufferedBytes;
        }
    }
}
