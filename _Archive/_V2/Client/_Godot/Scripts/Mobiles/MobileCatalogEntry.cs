using Godot;

namespace UltraSim.Client.Godot.Mobiles;

[GlobalClass]
public partial class MobileCatalogEntry : Resource
{
    [Export] public string DefinitionId { get; set; } = string.Empty;
    [Export] public MobileDefinition? Definition { get; set; }
}
