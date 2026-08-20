using System;
using UltraSim.Network;

namespace UltraSim.Client.Engines.Networking
{
    public delegate int ClientInputEncoder<TInput>(
        in ClientInputSample<TInput> sample,
        Span<byte> destination);
}
