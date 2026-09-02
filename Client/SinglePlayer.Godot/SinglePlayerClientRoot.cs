using UltraSim.Client.Bootstrap.Godot;
using UltraSim.Game.Composition;
using UltraSim.Game.Replication;
using UltraSim.Server.Runtime;

namespace UltraSim.Client.SinglePlayer.Godot;

public partial class SinglePlayerClientRoot : ClientCompositionBootstrap
{
    protected override ClientCompositionManifest Composition
    {
        get
        {
            _ = typeof(UltraSimGameComposition);
            _ = typeof(GameProjectionService);
            _ = typeof(ServerRuntime);
            return ClientCompositionManifest.SinglePlayer;
        }
    }
}
