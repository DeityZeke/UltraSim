using Godot;

namespace UltraSim.Client.Godot.Mobiles;

[GlobalClass]
public partial class MobileCatalogNode : Node
{
    [Export] public MobileDefinitionCatalog? Catalog { get; set; }
    [Export] public MobileDefinition? DefaultDefinition { get; set; }

    public MobileDefinition? Resolve(string definitionId)
    {
        return Catalog?.Resolve(definitionId) ?? DefaultDefinition;
    }
}
