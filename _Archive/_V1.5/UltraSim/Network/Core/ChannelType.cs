namespace UltraSim.Network
{
    public enum ChannelType : byte
    {
        ReliableOrdered = 0,
        ReliableUnordered = 1,
        UnreliableSequenced = 2,
        Unreliable = 3
    }
}

