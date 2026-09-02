using UltraSim.Client.Bootstrap.Godot;
using UltraSim.Client.Local;

namespace UltraSim.Client.SinglePlayer.Godot;

public partial class SinglePlayerClientRoot : ClientRoot
{
    protected override ClientCompositionManifest Composition
    {
        get
        {
            _ = typeof(LocalPlayableSession);
            return ClientCompositionManifest.SinglePlayer;
        }
    }
}
