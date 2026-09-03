using UltraSim.Client.Bootstrap.Godot;
using UltraSim.Client.Local.Godot;

namespace UltraSim.Client.SinglePlayer.Godot;

public partial class SinglePlayerClientRoot : LocalPlayClientRoot
{
    protected override ClientCompositionManifest Composition
    {
        get
        {
            return ClientCompositionManifest.SinglePlayer;
        }
    }
}
