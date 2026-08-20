namespace UltraSim.Server.Engines.Networking
{
    public readonly struct ServerReplicationTickResult
    {
        public readonly int PublishedCandidates;
        public readonly int SentPackets;

        public ServerReplicationTickResult(int publishedCandidates, int sentPackets)
        {
            PublishedCandidates = publishedCandidates;
            SentPackets = sentPackets;
        }
    }
}
