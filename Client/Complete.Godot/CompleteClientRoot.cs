using UltraSim.Client.Bootstrap.Godot;
using UltraSim.Client.Local.Godot;
using UltraSim.Client.Network;

namespace UltraSim.Client.Complete.Godot;

public partial class CompleteClientRoot : LocalPlayClientRoot
{
    protected override ClientCompositionManifest Composition
    {
        get
        {
            _ = typeof(RemoteWorldStateClient);
            return ClientCompositionManifest.Complete;
        }
    }
}
