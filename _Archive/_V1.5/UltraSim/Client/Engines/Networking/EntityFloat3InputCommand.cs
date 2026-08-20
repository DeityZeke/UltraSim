using System;
using UltraSim.Network;
using UltraSim.Shared.Engines.Networking;

namespace UltraSim.Client.Engines.Networking
{
    public readonly struct EntityFloat3InputCommand
    {
        public EntityFloat3InputCommand(short moveX, short moveZ, byte flags = 0)
        {
            MoveX = moveX;
            MoveZ = moveZ;
            Flags = flags;
        }

        public short MoveX { get; }
        public short MoveZ { get; }
        public byte Flags { get; }
    }

    public static class EntityFloat3InputCommandCodec
    {
        public static int Encode(in ClientInputSample<EntityFloat3InputCommand> sample, Span<byte> destination) =>
            InputFloat3Codec.TryEncode(
                destination,
                sample.Sequence,
                sample.Input.MoveX,
                sample.Input.MoveZ,
                sample.Input.Flags,
                sample.ClientTick,
                out int written)
                ? written
                : 0;
    }
}
