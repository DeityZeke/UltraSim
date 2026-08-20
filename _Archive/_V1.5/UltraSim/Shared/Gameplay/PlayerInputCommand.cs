namespace UltraSim.Shared.Gameplay
{
    public readonly struct PlayerInputCommand
    {
        public PlayerInputCommand(ushort sequence, short moveX, short moveZ, long clientTick)
        {
            Sequence = sequence;
            MoveX = moveX;
            MoveZ = moveZ;
            ClientTick = clientTick;
        }

        public ushort Sequence { get; }
        public short MoveX { get; }
        public short MoveZ { get; }
        public long ClientTick { get; }
    }
}
