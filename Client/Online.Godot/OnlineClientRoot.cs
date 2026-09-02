using UltraSim.Client.Bootstrap.Godot;
using UltraSim.Client.Network;

namespace UltraSim.Client.Online.Godot;

public partial class OnlineClientRoot : ClientRoot
{
    protected override ClientCompositionManifest Composition
    {
        get
        {
            _ = typeof(RemoteWorldStateClient);
            return ClientCompositionManifest.Online;
        }
    }
}
