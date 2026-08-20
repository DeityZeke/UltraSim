namespace UltraSim.Network
{
    public readonly struct FrameReaderStatsSnapshot
    {
        public readonly long AppendedBytes;
        public readonly long ConsumedFrames;
        public readonly long ConsumedBytes;
        public readonly long InvalidFrames;
        public readonly long DroppedBytes;

        public FrameReaderStatsSnapshot(
            long appendedBytes,
            long consumedFrames,
            long consumedBytes,
            long invalidFrames,
            long droppedBytes)
        {
            AppendedBytes = appendedBytes;
            ConsumedFrames = consumedFrames;
            ConsumedBytes = consumedBytes;
            InvalidFrames = invalidFrames;
            DroppedBytes = droppedBytes;
        }
    }
}
