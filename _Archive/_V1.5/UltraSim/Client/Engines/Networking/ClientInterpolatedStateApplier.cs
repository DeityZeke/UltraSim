using UltraSim.ECS;

namespace UltraSim.Client.Engines.Networking
{
    public delegate void ClientInterpolatedStateApplier<TState>(
        World world,
        Entity entity,
        in TState state);
}
