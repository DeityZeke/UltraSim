using Godot;
using UltraSim.Client.Runtime;

namespace Client.Godot.Prefabs.Mobiles
{
    [GlobalClass]
    public partial class MobileCatalogEntry : Resource
    {
        [Export] public int SharedActorDefinitionId { get; set; }
        [Export] public MobileDefinition? Definition { get; set; }

        public ClientActorDefinitionId ClientDefinitionId =>
            SharedActorDefinitionId <= 0
                ? default
                : new ClientActorDefinitionId((ushort)SharedActorDefinitionId);
    }
}
