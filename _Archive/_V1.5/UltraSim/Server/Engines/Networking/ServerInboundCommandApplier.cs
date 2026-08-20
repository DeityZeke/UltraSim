using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Engines.Networking
{
    public delegate void ServerInboundCommandApplier<TCommand>(
        World world,
        in QueuedInboundCommand<TCommand> command);
}
