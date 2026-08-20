using Godot;
using UltraSim.Client.Runtime;

namespace Client.Godot.Prefabs.Mobiles
{
    [GlobalClass]
    public partial class MobileDefinitionCatalog : Resource
    {
        [Export] public MobileDefinition? DefaultDefinition { get; set; }
        [Export] public global::Godot.Collections.Array<MobileCatalogEntry> Entries { get; set; } = new();

        public MobileDefinition? Resolve(ClientActorDefinitionId definitionId)
        {
            if (definitionId.IsEmpty)
                return DefaultDefinition;

            for (int i = 0; i < Entries.Count; i++)
            {
                MobileCatalogEntry? entry = Entries[i];
                if (entry == null || entry.Definition == null)
                    continue;

                if (entry.ClientDefinitionId.Value == definitionId.Value)
                    return entry.Definition;
            }

            return DefaultDefinition;
        }
    }
}
