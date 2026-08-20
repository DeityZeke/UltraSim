namespace UltraSim.Server.Engines.Networking
{
    public readonly struct ServerNetworkTickResult
    {
        public readonly uint WorldTickBefore;
        public readonly uint WorldTickAfter;
        public readonly int DecodedDatagrams;
        public readonly int AppliedCommands;
        public readonly int PublishedReplicationCandidates;
        public readonly int SentReplicationPackets;

        public ServerNetworkTickResult(
            uint worldTickBefore,
            uint worldTickAfter,
            int decodedDatagrams,
            int appliedCommands)
            : this(
                worldTickBefore,
                worldTickAfter,
                decodedDatagrams,
                appliedCommands,
                publishedReplicationCandidates: 0,
                sentReplicationPackets: 0)
        {
        }

        public ServerNetworkTickResult(
            uint worldTickBefore,
            uint worldTickAfter,
            int decodedDatagrams,
            int appliedCommands,
            int publishedReplicationCandidates,
            int sentReplicationPackets)
        {
            WorldTickBefore = worldTickBefore;
            WorldTickAfter = worldTickAfter;
            DecodedDatagrams = decodedDatagrams;
            AppliedCommands = appliedCommands;
            PublishedReplicationCandidates = publishedReplicationCandidates;
            SentReplicationPackets = sentReplicationPackets;
        }
    }
}
