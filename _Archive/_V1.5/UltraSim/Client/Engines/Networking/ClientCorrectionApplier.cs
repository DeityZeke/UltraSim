using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Client.Engines.Networking
{
    public delegate void ClientCorrectionApplier<TCorrection>(
        World world,
        in ReconciliationEntry<TCorrection> correction);
}
