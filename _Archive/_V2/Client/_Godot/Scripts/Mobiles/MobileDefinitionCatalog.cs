using Godot;

namespace UltraSim.Client.Godot.Mobiles;

[GlobalClass]
public partial class MobileDefinitionCatalog : Resource
{
    [Export] public global::Godot.Collections.Array<MobileCatalogEntry> Entries { get; set; } = new();
    [Export] public MobileDefinition? DefaultDefinition { get; set; }

    public MobileDefinition? Resolve(string definitionId)
    {
        if (!string.IsNullOrWhiteSpace(definitionId))
        {
            foreach (MobileCatalogEntry entry in Entries)
            {
                if (entry.Definition is not null &&
                    string.Equals(entry.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Definition;
                }
            }
        }

        return DefaultDefinition;
    }
}
