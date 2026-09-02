using UltraSim.Client.Bootstrap.Godot;
using UltraSim.Client.Network;
using UltraSim.Game.Composition;
using UltraSim.Game.Replication;
using UltraSim.Server.Runtime;

namespace UltraSim.Client.Complete.Godot;

public partial class CompleteClientRoot : ClientCompositionBootstrap
{
    protected override ClientCompositionManifest Composition
    {
        get
        {
            _ = typeof(RemoteWorldStateClient);
            _ = typeof(UltraSimGameComposition);
            _ = typeof(GameProjectionService);
            _ = typeof(ServerRuntime);
            return ClientCompositionManifest.Complete;
        }
    }
}
