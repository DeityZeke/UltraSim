using Godot;
using UltraSim.Client.Runtime;

namespace Client.Godot.Prefabs.Mobiles
{
    [GlobalClass]
    public partial class MobileCatalogNode : Node
    {
        [Export] public MobileDefinitionCatalog? Catalog { get; set; }

        public MobileDefinition? Resolve(ClientActorDefinitionId definitionId) =>
            Catalog?.Resolve(definitionId);
    }
}
