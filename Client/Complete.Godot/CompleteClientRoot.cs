using UltraSim.Client.Bootstrap.Godot;
using UltraSim.Client.Local;
using UltraSim.Client.Network;

namespace UltraSim.Client.Complete.Godot;

public partial class CompleteClientRoot : ClientCompositionBootstrap
{
    protected override ClientCompositionManifest Composition
    {
        get
        {
            _ = typeof(RemoteWorldStateClient);
            _ = typeof(LocalPlayableSession);
            return ClientCompositionManifest.Complete;
        }
    }
}
