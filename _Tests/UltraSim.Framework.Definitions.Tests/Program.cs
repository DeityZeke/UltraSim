using System.Text;
using System.Diagnostics;
using UltraSim.Framework.Definitions;
using UltraSim.Framework.Persistence;

var owner = new DefinitionPackageId("test.items");
var combatOwner = new DefinitionPackageId("test.combat.contracts");
var content = new StableDefinitionId("items.iron-sword");
var old = new StableDefinitionId("items.old-sword");
var combatCapability = new DefinitionCapabilityId("test.combat.runtime");
var itemFacet = new DefinitionFacetFamily<ItemMeaning>(new("items.meaning"), owner);
var combatFacet = new DefinitionFacetFamily<CombatMeaning>(new("combat.meaning"), combatOwner, combatCapability);
var swordSet = new DefinitionTagId("weapons.swords");

Check(!default(StableDefinitionId).IsValid && !default(DefinitionHandle).IsValid);
Throws<ArgumentException>(() => new StableDefinitionId("../bad"));
Throws<ArgumentException>(() => new StableDefinitionId("Items.Upper"));
Console.WriteLine("PASS P4 canonical stable identity and invalid/default rejection");

DefinitionCatalog active = Compose(selectedCombat: true);
Check(active.TryResolve(content, out DefinitionHandle activeSword) && activeSword.IsValid &&
    active.TryResolve(old, out DefinitionHandle remapped) && remapped == activeSword &&
    active.TryGetStableId(activeSword, out var authored) && authored == content);
var items = active.Bind(itemFacet);
var combats = active.Bind(combatFacet);
var swords = active.Bind(swordSet);
Check(items.TryGet(activeSword, out var item) && item.Name == "Iron Sword" &&
    combats.TryGet(activeSword, out var combat) && combat.MaximumDamage == 6 &&
    combats.RuntimeActive && swords.Contains(activeSword));
Check(active.Contributions.Count == 2 &&
    active.Contributions.Single(x => x.Facet == combatFacet.Id).Package == combatOwner);
Check(active.TagContributions.Single().Applied && active.AliasContributions.Single().OldId == old);
Check(!active.TryResolvePersistedDistinct([old, content], out _));
Check(active.TryResolvePersistedDistinct([old], out DefinitionHandle[] restored) &&
    restored.Length == 1 && restored[0] == activeSword);
Console.WriteLine("PASS P4 independent typed facet contributions, dense handles, tags and stable alias remap");

DefinitionCatalog dormant = Compose(selectedCombat: false);
Check(dormant.TryResolve(content, out DefinitionHandle dormantSword) &&
    dormantSword != activeSword && !combats.TryGet(dormantSword, out _));
var dormantCombat = dormant.Bind(combatFacet);
Check(dormantCombat.TryGet(dormantSword, out var dormantValue) &&
    dormantValue.MaximumDamage == 6 && !dormantCombat.RuntimeActive &&
    dormant.Contributions.Count == active.Contributions.Count);
Throws<InvalidOperationException>(() => Compose(selectedCombat: false, requiredCombat: true));
Console.WriteLine("PASS P4 Contracts meaning survives missing Core; explicit required capability fails closed");

var conflict = NewComposer();
conflict.Define(owner, content).With(owner, itemFacet, new("Iron Sword"));
Throws<InvalidOperationException>(() => conflict.Extend(content).With(combatOwner, itemFacet, new("Different")));
conflict.ContributeTag(owner, swordSet, content);
Throws<InvalidOperationException>(() => conflict.ContributeTag(combatOwner, swordSet, content));
conflict.Alias(owner, old, content);
Throws<InvalidOperationException>(() => conflict.Alias(combatOwner, old, content));
var chained = NewComposer();
chained.Define(owner, content);
chained.Alias(owner, old, content);
chained.Alias(owner, new("items.ancient-sword"), old);
Throws<InvalidOperationException>(() => chained.Freeze([]));
var optionalMembership = NewComposer();
optionalMembership.Define(owner, content);
optionalMembership.ContributeTag(combatOwner, swordSet, new("items.optional-absent"));
Check(!optionalMembership.Freeze([]).TagContributions.Single().Applied);
var sharedSet = NewComposer();
var second = new StableDefinitionId("items.steel-sword");
sharedSet.Define(owner, content);
sharedSet.Define(owner, second);
sharedSet.ContributeTag(owner, swordSet, content);
sharedSet.ContributeTag(combatOwner, swordSet, second);
DefinitionCatalog sharedCatalog = sharedSet.Freeze([]);
Check(sharedCatalog.TryResolve(content, out DefinitionHandle firstMember) &&
    sharedCatalog.TryResolve(second, out DefinitionHandle secondMember) &&
    sharedCatalog.Bind(swordSet).Contains(firstMember) && sharedCatalog.Bind(swordSet).Contains(secondMember) &&
    sharedCatalog.TagContributions.Count == 2 && sharedCatalog.TagContributions.All(row => row.Applied));
var requiredMembership = NewComposer();
requiredMembership.Define(owner, content);
requiredMembership.ContributeTag(combatOwner, swordSet, new("items.required-absent"), requireDefinition: true);
Throws<InvalidOperationException>(() => requiredMembership.Freeze([]));
Console.WriteLine("PASS P4 facet/tag/alias conflicts and alias chains reject before publication");

var facetBound = new DefinitionComposer();
for (int i = 0; i < DefinitionComposer.MaximumFacetFamilies; i++)
    facetBound.RegisterFacet(new DefinitionFacetFamily<int>(new($"test.facet.{i}"), owner));
Throws<InvalidOperationException>(() => facetBound.RegisterFacet(
    new DefinitionFacetFamily<int>(new("test.facet.overflow"), owner)));
var definitionBound = new DefinitionComposer();
for (int i = 0; i < DefinitionComposer.MaximumDefinitions; i++)
    definitionBound.Define(owner, new StableDefinitionId($"test.definition.{i}"));
Throws<InvalidOperationException>(() => definitionBound.Define(owner,
    new StableDefinitionId("test.definition.overflow")));
Console.WriteLine("PASS P4 facet-family and Definition capacity enforce finite cold composition");

long[] allocations = new long[4];
int checksum = 0;
for (int i = 0; i < 20_000; i++)
    if (items.TryGet(activeSword, out var value) && swords.Contains(activeSword)) checksum += value.Name.Length;
for (int sample = 0; sample < allocations.Length; sample++)
{
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10_000; i++)
        if (items.TryGet(activeSword, out var value) && swords.Contains(activeSword)) checksum += value.Name.Length;
    allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
}
Check(checksum > 0 && allocations.Contains(0));
Console.WriteLine($"P4_HOT_LOOKUP allocations_10k=[{string.Join(',', allocations)}]");
const int iterations = 200_000;
long started = Stopwatch.GetTimestamp();
for (int i = 0; i < iterations; i++)
    if (active.TryResolve(content, out var value)) checksum += value.Index;
double resolveNs = (Stopwatch.GetTimestamp() - started) * 1_000_000_000.0 / Stopwatch.Frequency / iterations;
started = Stopwatch.GetTimestamp();
for (int i = 0; i < iterations; i++)
    if (items.TryGet(activeSword, out var value)) checksum += value.Name.Length;
double facetNs = (Stopwatch.GetTimestamp() - started) * 1_000_000_000.0 / Stopwatch.Frequency / iterations;
started = Stopwatch.GetTimestamp();
for (int i = 0; i < iterations; i++)
    if (swords.Contains(activeSword)) checksum++;
double tagNs = (Stopwatch.GetTimestamp() - started) * 1_000_000_000.0 / Stopwatch.Frequency / iterations;
Check(checksum > 0);
Console.WriteLine($"P4_LOOKUP_NS stable_to_handle={resolveNs:F1} typed_facet={facetNs:F1} dense_tag={tagNs:F1} iterations={iterations}");

string saveRoot = Path.Combine(Path.GetTempPath(), "ultrasim-p4-definition-" + Guid.NewGuid().ToString("N"));
try
{
    // The saved participant captures only the stable authored ID. A new composition resolves it.
    var oldCatalog = NewComposer();
    oldCatalog.Define(owner, old);
    DefinitionCatalog original = oldCatalog.Freeze([]);
    var writer = new DefinitionReferenceParticipant(original, old);
    var coordinator = new SaveSetCoordinator(saveRoot);
    coordinator.Save([writer], new SaveSetContext(1, 100), action => action());
    var reader = new DefinitionReferenceParticipant(active, default);
    using (PreparedLoadGeneration prepared = coordinator.PrepareLoad([reader], new EmptyResolver()))
        prepared.Commit(parts => reader.Published = ((PreparedDefinitionReference)parts.Single()).Handle);
    Check(reader.Published == activeSword && writer.Published == default);
    var incompatible = new DefinitionReferenceParticipant(dormant, default);
    Check(dormant.TryResolve(old, out var dormantRemap) && dormantRemap != activeSword);
    using (PreparedLoadGeneration prepared = coordinator.PrepareLoad([incompatible], new EmptyResolver()))
        prepared.Commit(parts => incompatible.Published = ((PreparedDefinitionReference)parts.Single()).Handle);
    Check(incompatible.Published == dormantRemap);
    var missingComposer = NewComposer();
    missingComposer.Define(owner, new StableDefinitionId("items.other"));
    var missing = new DefinitionReferenceParticipant(missingComposer.Freeze([]), default);
    Throws<InvalidDataException>(() => coordinator.PrepareLoad([missing], new EmptyResolver()));
    Console.WriteLine("PASS P4 Framework save-set stores stable Definition ID; fresh dense handles and aliases resolve before publication");
}
finally { if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, recursive: true); }

Console.WriteLine("All P4 Definition controls passed.");

DefinitionComposer NewComposer()
{
    var composer = new DefinitionComposer();
    composer.RegisterFacet(itemFacet);
    composer.RegisterFacet(combatFacet);
    return composer;
}
DefinitionCatalog Compose(bool selectedCombat, bool requiredCombat = false)
{
    var composer = NewComposer();
    DefinitionBuilder builder = composer.Define(owner, content).With(owner, itemFacet, new("Iron Sword"));
    composer.Extend(content).With(combatOwner, combatFacet, new(2, 6));
    if (requiredCombat) builder.RequireRuntime(combatCapability);
    composer.ContributeTag(combatOwner, swordSet, content);
    composer.Alias(owner, old, content);
    return composer.Freeze(selectedCombat ? [combatCapability] : []);
}
static void Check(bool condition) { if (!condition) throw new Exception("P4 Definition assertion failed."); }
static void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }

readonly record struct ItemMeaning(string Name);
readonly record struct CombatMeaning(int MinimumDamage, int MaximumDamage);

sealed class DefinitionReferenceParticipant(DefinitionCatalog catalog, StableDefinitionId current) : ISaveSetParticipant
{
    public string Key => "definitions";
    public long MaximumArtifactBytes => 256;
    public DefinitionHandle Published;
    public ICapturedSaveSetParticipant Capture(in SaveSetContext context) =>
        new CapturedDefinitionReference(current);
    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context)
    {
        string text = new UTF8Encoding(false, true).GetString(artifact.Span);
        StableDefinitionId stable = new(text);
        if (!catalog.TryResolvePersistedDistinct([stable], out DefinitionHandle[] handles))
            throw new InvalidDataException($"Missing or ambiguous persisted Definition {stable}.");
        return new PreparedDefinitionReference(handles[0]);
    }
}
sealed class CapturedDefinitionReference(StableDefinitionId stable) : ICapturedSaveSetParticipant
{
    public string Key => "definitions";
    public void WriteArtifact(Stream destination) => destination.Write(Encoding.UTF8.GetBytes(stable.Value));
    public void Dispose() { }
}
sealed class PreparedDefinitionReference(DefinitionHandle handle) : IPreparedLoadParticipant
{
    public string Key => "definitions";
    public DefinitionHandle Handle => handle;
    public void Resolve(IPersistentReferenceResolver resolver) { }
    public void Validate() { if (!handle.IsValid) throw new InvalidDataException("Unresolved Definition handle."); }
    public void Dispose() { }
}
sealed class EmptyResolver : IExternalPersistentReferenceResolver
{
    public bool TryResolve(PersistentReferenceKey key, out object? value) { value = null; return false; }
}
