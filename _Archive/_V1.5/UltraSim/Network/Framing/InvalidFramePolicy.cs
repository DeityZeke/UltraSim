namespace UltraSim.Network
{
    public enum InvalidFramePolicy : byte
    {
        Throw = 0,
        DropByte = 1,
        ResetBuffer = 2,
    }
}
