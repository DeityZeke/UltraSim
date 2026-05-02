# Server Script Compiler And Runtime Layout Plan

Purpose: define the RunUO-style server script compiler, exported server folder layout, and source-controlled runtime template before login, character flow, inventory viewers, crafting, vendors, and admin panels move into server-authored content.

This is server startup/plugin infrastructure. It is not part of the UI kernel, not part of the client patch stream, and not a way to send executable code to clients.

## Reference Shape

RunUO's useful pattern is the operational contract:

- the executable defines a base directory
- startup changes or resolves paths relative to that base directory
- scripts live under `Scripts/`
- generated script assemblies live under `Scripts/Output/`
- startup compiles all script files, reports diagnostics, then invokes script lifecycle hooks
- successful compile logs a clear `Scripts: Compiling ... - done` line
- cache hits skip compile and load the previous output assembly

UltraSim should keep that workflow, but modernize the implementation:

- use Roslyn instead of CodeDOM
- use content/reference/options hashes instead of timestamp-only hashes
- keep generated output outside the editable `scripts/` source tree by default
- support headless, console, and Godot server shells over the same runtime root contract
- preserve clear startup diagnostics and fail-fast behavior for dedicated hosting

## Layering

The compiler belongs above the engine-agnostic kernels and below gameplay scripts.

- `Kernels/*`
  - remain engine-agnostic and script-agnostic
  - do not reference script runtime services
- `Server/Core`
  - owns the application lifecycle and stable server API contracts exposed to scripts
  - may call into a script host abstraction during startup
- `Server/Hosting`
  - owns shared host-loop and runtime-root plumbing
  - is the natural home for base-directory resolution shared by headless, console, and Godot wrappers
- `Server/Scripting` or `Server/ScriptHost`
  - owns Roslyn compilation, cache manifests, diagnostics, assembly load, and script registration
  - should remain free of Godot, console UI, and transport-specific code
- `Server/_Headless`, `Server/_Console`, `Server/_Godot`
  - choose a runtime root and start the shared runtime
  - do not own script discovery, compile policy, or gameplay registration
- `scripts/`
  - operator/modder-authored source only
  - compiles against stable server/glue APIs

## Repository Layout

Do not put live mod source directly under `Server/` as if it were part of the core build.

Use a source-controlled runtime template:

```text
Server/
  RuntimeTemplate/
    README.md
    scripts/
      Accounts/
      Commands/
      ContextMenus/
      Customs/
      Engines/
      Items/
      Misc/
      Mobiles/
      Packages/
      Properties/
      Regions/
      Skills/
      Spells/
      Targets/
      UI/
    data/
    docs/
    web/
```

`Server/RuntimeTemplate/scripts` is example/default server content and modding scaffolding. The exported server package receives a copy of this folder. After export, the server operator owns that copy.

Large cohesive systems should live as packages, not as tentacles spread across every top-level script folder. A future Knives-style chat system should prefer a package shape:

```text
scripts/Packages/Chat/
  ChatPackageModule.cs
  Channels/
  Commands/
  UI/
  Rules/
  Data/
```

Top-level folders remain useful for loose content and small standalone scripts. `Packages` is for complete systems an operator should be able to copy, replace, disable, document, or eventually manifest as one unit.

Default server-authored platform flows can also be packages. The default
admission flow belongs in `scripts/Packages/Admission/`: it owns login,
character select/create panel definitions, stable admission event names, and the
step registration shape. The UI kernel must not contain admission-specific
panels; the kernel provides generic schema nodes and trusted-input contracts.
`Server/Admission` consumes script-registered steps and tracks active flow state
per connection. `Server/Network` only feeds it UI action documents and exposes a
panel gateway; admission progression, account validation, character selection,
and world-entry policy should not live in the transport project. Real account
validation plugs in through a credential-validator seam owned by the accounting
slice.

`Server/Accounting` is compiled server infrastructure, not script content. It
owns account records, password hash verification, account state checks, active
session conflict policy, login rate limiting, and audit records. Scripts may add
or replace admission panels/flow steps, but a script compile failure must never
remove the server's ability to reject invalid credentials.

Accounting must support both local server accounts and future UltraSim
portal-backed login. In portal mode the server receives only an opaque
portal/session credential plus its own server instance id, asks the platform
service to validate it, and keeps only server-local instance data such as bans,
kicks, timeouts, character/world records, and audit outcomes. Portal passwords,
personal data, and platform account history are not passed to the game server.
Portal token validation is mandatory: production portal authenticators must
cryptographically verify the token or call the portal authority, not trust a
client-provided token string.

The template should not contain generated compiler output, saves, logs, backups, or cache files.

### Package Replacement Hooks

Script packages should be able to replace selected core providers through an
explicit single-owner registry. This is especially important for systems where
the compiled client/server core ships a small fallback but a package may provide
the real shard-specific experience.

Example:

```text
core startup:
  chat.surface = CoreChatFeedInput

script configure:
  chat.surface = RichChatPackageSurface
```

This is replacement, not accumulation. Registries for replaceable surfaces,
commands, panel providers, and system handlers should make the intended policy
explicit:

- `Set` or `Replace` when there can be only one active handler
- `Add` only for true fan-out/event cases
- duplicate owner conflicts fail loudly unless an explicit priority/override
  rule selects the winner
- diagnostics report the active owner and any suppressed/replaced owners

A future `scripts/Packages/Chat` package should use this path to supersede the
core chat surface without causing both chat panels to open. Removing or disabling
that package should either fall back to the core provider or honor an explicit
operator setting that disables chat UI entirely. Overhead/world chat and chat
window/feed visibility should be separate settings.

## Exported Runtime Root

The exported server folder is the runtime root. Headless, console, and Godot server shells should resolve relative paths from the same root.

```text
UltraSimServer/
  UltraSim.Server.Headless[.exe]
  UltraSim.Server.Console[.exe]
  UltraSim.Server.Godot[.exe]
  run-headless.bat
  run-console.bat
  run-godot-server.bat
  scripts/
  data/
  docs/
  web/
  saves/          generated if absent
  backups/        generated if absent
  logs/           generated if absent
  cache/
    scripts/      generated if absent
```

Default runtime root:

- use `--server-root=path` when supplied
- otherwise use `ULTRASIM_SERVER_ROOT` when supplied
- otherwise use `AppContext.BaseDirectory`

Tests and local developer tools may point `--server-root` at a disposable temp directory. Export/package tasks should copy `Server/RuntimeTemplate/*` into the runtime root when missing or when explicitly refreshing the template.

## Script Folder Semantics

Use `Mobiles`, not `Mobs`.

`Mobiles` matches the server-emulator concept better: players, NPCs, creatures, followers, mounts, vendors, pets, and any other mobile world actor.

Initial script categories:

- `Accounts`
  - account policy, admission hooks, auth/login glue that is safe to expose to scripts
- `Commands`
  - admin/player command registration
- `ContextMenus`
  - right-click/context/radial action providers and menu descriptors
- `Customs`
  - drop-in server operator custom packs
- `Engines`
  - larger systems: crafting, housing, auctions, vendors, parties, progression, inventory viewers
- `Items`
  - item definitions and item behavior
- `Misc`
  - small bootstrap helpers and glue only; avoid letting this become the default place for everything
- `Mobiles`
  - player/NPC/creature/follower/mount/vendor definitions and behavior
- `Packages`
  - complete bundled systems that should stay together, such as a future chat package with channels, commands, rules, settings panels, and package-local data
- `Properties`
  - property inspection/edit descriptors and admin reflection helpers
- `Regions`
  - world region rules, triggers, permissions, and environmental policy
- `Skills`
  - skill definitions, skill use handlers, progression hooks
- `Spells`
  - spell definitions and casting behavior
- `Targets`
  - target selection flows and target validation helpers
- `UI`
  - server-authored panels, gump-like builders, catalog-backed menus, admin pages

Avoid copying every RunUO root folder immediately. Add new top-level folders only when the distinction helps server operators find content. Otherwise use `Engines` or `Customs`.

## Human Mobile Shape

Human NPCs should use the same underlying actor definition shape as player characters.

A player is a controlled human mobile, not a separate species of runtime object. Human NPCs, companions, villagers, workers, guards, vendors, and player characters should be able to share:

- stat pools
- skills
- progression/leveling rules
- needs and simulation state
- inventory/equipment shape
- animation sets and presentation tags
- interaction verbs and social state
- character sheet/stat UI conventions

This is closer to Kenshi or The Sims than to a simple "mob" model. Scripts may still specialize behavior, control source, AI, permissions, and persistence policy, but the base data model should not make player-only assumptions for human-shaped actors. If a UI panel works for a player character sheet, it should usually be adaptable to an NPC/follower stat sheet by changing permissions and target binding, not by building a separate one-off contract.

## ECS Archetype And Prefab Rule

Most script-defined gameplay things should compile down to ECS prefab definitions on top of shared archetype shapes.

The ECS kernel already has prefab contracts such as `PrefabDefinition`, `PrefabCatalog`, `IPrefab<TSelf>`, `ITemplatePrefab<TSelf>`, and resolver-backed `World.CreatePrefab(...)` paths. Server scripts should use those concepts rather than inventing a parallel script-only entity model.

The current ECS prefab layer is intentionally low-level. It already supports the runtime truth:

```text
prefab key -> archetype signature -> ECS entity id -> component data
```

What the script compiler layer needs to add is an author-friendly facade over those primitives:

```text
ArchetypeShape authoring class -> ArchetypeSignature
Prefab<TShape> authoring class -> PrefabDefinition
script registration -> PrefabCatalog / runtime registries
```

That facade belongs in server/script API glue, not in the ECS kernel itself unless a kernel-level primitive is truly missing.

Authoring model:

- shared archetype shapes define the component layout and broad behavior category
- concrete scripts define specific prefab keys, display metadata, default values, and rule hooks
- archetype families compose by adding components and overriding/defaulting previously-declared component values, not by OOP inheritance

Example:

- `BaseItem > BaseWeapon > BaseMeleeWeapon > BaseBladedWeapon > iron_dagger` is a composition chain, not a class inheritance chain.
- `BaseItem` contributes common item components:
  - item identity
  - inventory placement
  - stack/quantity rules
  - weight/value
  - presentation/icon tags
- `BaseWeapon` adds weapon components:
  - equipment slot
  - damage profile
  - durability
  - interaction/action descriptors
- `BaseMeleeWeapon` adds or refines melee-specific components:
  - reach profile
  - swing timing
  - stamina/animation tags
- `BaseBladedWeapon` adds or refines bladed-weapon components:
  - damage type
  - parry/bleed/cut tags
  - repair material rules
- `iron_dagger` is the concrete prefab:
  - prefab key such as `items/weapons/blades/iron_dagger`
  - specific name, material, value, weight
  - concrete damage/range/speed values
  - durability and repair material defaults
  - icon/model/sound tags

Another example:

- `BaseWeapon` is a broad archetype/family shape:
  - item identity
  - equipment slot
  - damage profile
  - durability
  - inventory placement
  - presentation/icon tags
  - interaction/action descriptors
- `IronSword` is a concrete prefab:
  - prefab key such as `items/weapons/iron_sword`
  - name, material, value, weight
  - base damage/range/speed values
  - durability and repair material
  - default icon/model/sound tags

The same pattern should apply to mobiles, armor, resources, containers, crafting stations, housing pieces, projectiles, harvest nodes, and other entity-backed gameplay content.

Script authors may expose helper classes or fluent builders named like `BaseWeapon` for ergonomics, but those helpers should register component-set/archetype descriptors and prefab definitions. They should not create runtime OOP entity subclasses that bypass ECS identity, queries, persistence, replication, or admin inspection.

This keeps the runtime shape data-oriented and compatible with ECS queries, replication, persistence, admin inspection, and future control panels. Scripts can remain expressive, but their output should normally be registered prefab definitions, catalogs, and rule providers rather than bespoke object graphs that the ECS cannot inspect.

Exceptions are fine for non-entity systems such as commands, account policy, UI-only panels, pricing tables, recipes, and other pure data/rule registries. Entity-backed world things should prefer prefab/archetype registration.

### Timed Effects And Query Boundaries

Timed effects are a good example of when to add a component/archetype boundary.

Use wide components for normal, common shape data:

- `WeaponData`
- `WeaponState`
- `ItemData`
- `MobileStats`
- `NeedsState`

Use a separate component or sub-archetype when a system needs to efficiently query a subset:

- `TimedEffectState`
- `PoisonedWeaponState`
- `BurningState`
- `AlignmentState`
- `ConsecrationState`

Example:

```text
BaseItem
  + ItemIdentity
  + InventoryItem
  + ItemPresentation

BaseWeapon : BaseItem
  + WeaponData       // wide weapon definition/runtime data
  + WeaponState      // mutable weapon state
  + Durability

EffectWeapon : BaseWeapon
  + TimedEffectState // query boundary for effect tick systems

PoisonedWeapon : EffectWeapon
  + PoisonState      // query boundary if poison needs its own tick/query path
```

If poison is only inspected when a weapon is used, it can be a field on `WeaponState`. If poison has a ticking system that needs "all poisoned weapons" every tick, use a separate component/archetype boundary. The reason to add the boundary is simulation/query behavior, not taxonomy alone.

Applying or expiring an effect may structurally add/remove the relevant component. That moves the entity into or out of the effect archetype while preserving the rest of its item/weapon data.

### Authoring Facade Sketch

The final script-facing API can use C# inheritance for readable authoring, as long as the output remains ECS descriptors.

```csharp
public abstract class ArchetypeShape
{
    public abstract string Key { get; }

    public ArchetypeDescriptor Build()
    {
        var builder = new ArchetypeBuilder(Key);
        Configure(builder);
        return builder.Build();
    }

    protected virtual void Configure(ArchetypeBuilder builder)
    {
    }
}

public class BaseItem : ArchetypeShape
{
    public override string Key => "items/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        builder.Add<ItemIdentity>();
        builder.Add<InventoryItem>();
        builder.Add<ItemPresentation>();
    }
}

public class BaseWeapon : BaseItem
{
    public override string Key => "items/weapons/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        base.Configure(builder);
        builder.Add<WeaponData>();
        builder.Add<WeaponState>();
        builder.Add<Durability>();
    }
}

public class EffectWeapon : BaseWeapon
{
    public override string Key => "items/weapons/effect";

    protected override void Configure(ArchetypeBuilder builder)
    {
        base.Configure(builder);
        builder.Add<TimedEffectState>();
    }
}
```

Prefab authoring should bind to an archetype shape and set concrete defaults:

```csharp
public sealed class IronDagger : PrefabDefinition<BaseWeapon>
{
    protected override void Configure(PrefabBuilder builder)
    {
        builder.Set(new ItemIdentity("iron_dagger", "Iron Dagger"));
        builder.Set(new InventoryItem(width: 1, height: 2, weight: 1.2f, stackLimit: 1));
        builder.Set(new WeaponData(damageMin: 3, damageMax: 7, speed: 1.45f));
        builder.Set(new Durability(current: 35, max: 35));
        builder.SetRandom(random => new FireResistance(random.Next(1, 11)));
    }
}
```

Prefab keys default to the prefab type's short class name, so the example above registers as `IronDagger` and can be spawned with `/spawn IronDagger`. A script may still override `Key` when it wants a path-like stable key such as `items/weapons/blades/iron_dagger`.

Each prefab descriptor also carries a source name. For script-authored prefabs this is normally the CLR full type name, such as `Scripts.Items.Weapons.IronDagger`; for future data-authored prefabs it may be a URI-like source such as `data://prefabs/items/custom_dagger.json`. The key is the human/operator lookup id. The source name is for diagnostics, provenance, and duplicate-conflict messages.

Duplicate prefab keys are startup/configuration errors, not runtime ambiguity. Registration must fail loudly and list both source names:

```text
Duplicate prefab key 'IronDagger':
- Scripts.Items.Weapons.IronDagger
- Customs.MyShard.Weapons.IronDagger
Rename one prefab or override Key.
```

Prefab defaults may be constants or per-spawn factories. Randomized values must roll when an entity is spawned, not when the script registers the prefab. For example, `FireResistance = Random(1, 10)` should store a per-spawn value factory in the prefab descriptor so each spawned dagger can receive a different value.

Per-spawn factories should receive an explicit roll context supplied by the spawn/catalog bridge. Do not use `Random.Shared` or global static RNG directly inside prefab registration. The roll context can later carry deterministic seeds, spawn sequence, prefab key, region/world metadata, loot-quality context, or event modifiers without changing prefab authoring shape.

This API is illustrative, not locked. The implementation can choose exact names, but it should preserve the shape:

- class inheritance is for authoring readability
- composition output is component sets/archetype descriptors
- prefab output is registered `PrefabDefinition`
- spawned objects are ECS entities, not inherited runtime item classes
- randomized/defaulted component values are evaluated by the spawn path, not at registry time

The first implementation should keep this as a descriptor facade before spawning:

- `ArchetypeShape` and `ArchetypeBuilder` build `ArchetypeDescriptor` records from component types.
- `PrefabDefinition<TArchetype>` and `PrefabBuilder` build `PrefabDescriptor` records from a registered archetype shape and constant or per-spawn component defaults.
- `IUltraSimScriptContext.Definitions` owns archetype and prefab registration.
- prefab registration validates that the referenced archetype key was registered first and that the prefab key is globally unique.
- the facade may expose stable ECS contracts such as `IComponent<T>`, but not `World`, networking, persistence, or direct runtime mutation.

The catalog bridge converts those descriptors into the low-level ECS `PrefabCatalog` / `PrefabDefinition` path:

- register every archetype component type into an `EcsSchemaBuilder`
- build `ArchetypeSignature` values by archetype key
- translate each `PrefabDescriptor` into an ECS `PrefabDefinition`
- evaluate constant and per-spawn component values from a `PrefabRollContext` when the ECS prefab applies to a new entity

The movement/server world should build this catalog during startup when script definitions are present, attach it as the world's prefab resolver, and expose a narrow operator proof command such as `/spawn <prefab-key>`. That command should create a real ECS entity through `World.TryCreatePrefab`, not a script-side object, and tests should verify constant component values plus per-spawn factory values land on the entity.

## Live Data Prefab Authoring

Future admin/content tools should be able to build item, mobile, container, crafting, and other entity-backed definitions live in-game without generating C# by default.

Example target workflow:

1. A staff user opens an item builder panel.
2. They choose a model, texture/material, icon, display name, tags, inventory size, stack limit, attributes, components, and values.
3. They add semantic tags such as `craftable`, then use an embedded recipe builder to define required materials, skill requirements, station requirements, and output rules.
4. They click `Save`, `Build`, or `Generate`.
5. The server validates the definition, writes a data prefab record, registers it in the same prefab registry as script-authored content, and commands such as `/add my_new_item` work through the normal prefab path.

These definitions should be data-authored prefabs, not compiled script code:

```text
data/
  prefabs/
    items/
    mobiles/
    containers/
    crafting/
```

The exact file format can be JSON or another versioned data format. The important rule is that live-authored prefab data belongs in durable runtime data folders, not in `scripts/` and not only in disposable `cache/`. The runtime may still write normalized/binary prefab cache entries for faster startup, but the durable data file is the source of truth.

Data-authored prefabs should compile into the same runtime shape as C# prefab classes:

```text
data prefab file -> validated prefab descriptor -> PrefabCatalog / runtime registries -> ECS entity on spawn
```

The registry should carry origin metadata for every definition:

- built-in/core
- script-authored
- data-authored
- generated
- plugin/content-pack authored

That origin should also describe editability and reloadability. A script-authored `IronDagger` may require script recompilation or restart to change. A data-authored `my_new_item` should be live-addable, live-disableable, and live-erasable from admin tooling after validation.

Deleting or editing a data prefab should not silently mutate existing spawned entities unless an explicit migration/update command says to do so. Existing entities are normal ECS entities with component data. Prefab changes normally affect future spawns.

Prefab keys should be globally unique within the active runtime registry. If a new data prefab tries to use a key that already exists from built-in content, scripts, plugins, or another data prefab, saving should fail with a clear duplicate-key diagnostic. The author should rename it or place it under a different namespace. Do not silently override a definition from another origin.

If the live builder cannot find an appropriate archetype shape, it may generate/register a new composed archetype descriptor from already-known component types and defaults:

```text
selected components + defaults -> generated ArchetypeSignature -> generated data prefab
```

This does not mean the UI can invent new CLR component types or new simulation behavior. New components, systems, rule providers, and executable behavior still require scripts/plugins/core code. The live builder can compose and configure existing registered component descriptors.

Generated archetype descriptors should reuse existing compatible signatures first. If no existing archetype fits, the builder may create a generated archetype descriptor from registered components. Maintenance/cleanup can purge generated archetypes that have no prefab references and no live entities.

The builder should only offer assets and component choices that are registered and available to both server and client. It should not support live-uploading a new model to one client or reserving placeholder assets that other clients cannot render. Asset descriptors may also declare compatible base archetypes or categories so a donkey model cannot be authored as a melee weapon unless a deliberate script/plugin adds that support.

Validation should happen before save and again before registration:

- component type is registered and allowed for live authoring
- required components are present
- field values match declared types/ranges
- placement/layout metadata matches container mode
- recipe/material/item references resolve
- asset/model/texture/icon tags resolve or are explicitly marked missing
- asset/model/texture/icon tags are compatible with the chosen archetype/category
- permission level is allowed to author that definition kind
- generated archetype signature is compatible with ECS/query/persistence requirements
- prefab key, revision, and hash are stable
- prefab key does not collide with any active definition from another origin
- referenced script-authored archetypes/components still exist and are compatible

Data prefab validity should be re-checked whenever the script assembly, plugin set, component descriptor registry, asset registry, or archetype registry changes. A prefab that references a removed/renamed archetype or component should move into an invalid/dangling-reference state and surface in diagnostics instead of disappearing or crashing registration.

Live editing should use an edit lease. When one staff user opens a prefab in edit mode, other users should see who is editing it and should not be able to save competing edits until the lease is closed, expires, or is forcibly released by an authorized operator. Save should still include the source revision/hash so stale writes can be rejected if a lease is recovered or force-released.

Save/register should be atomic:

1. write a temporary data file
2. validate the data file
3. build a new registry snapshot
4. swap the active registry snapshot
5. commit/rename the data file

If any step fails, the previous prefab definition and registry snapshot remain active.

Dependent systems should derive their rows from active registries rather than owning stale copies. If `special_dagger` is deleted, crafting menus, loot tables, vendors, and admin lists should naturally stop collecting that prefab on their next registry refresh/rebuild. Systems that store explicit references should report missing references through registry diagnostics and either drop the row or mark it invalid according to their own policy; they should not require the prefab registry to broadcast bespoke delete messages into every system.

Versioned data prefabs need a migration story. The minimum acceptable behavior is a schema/version field and clear validation errors when component data no longer matches the registered descriptor. Later versions can add registered migration hooks to rewrite older data prefab revisions into the current descriptor shape.

The live authoring path should be auditable:

- author/account/staff identity
- created/updated timestamps
- previous revision hash
- current revision hash
- validation diagnostics
- rollback target where possible

This feature is intentionally future work. The first script compiler slice should not implement it. The script compiler and prefab facade should simply avoid decisions that would make data-authored live prefabs second-class later.

## Cache Location

Default generated compiler output should live outside `scripts/`:

```text
cache/
  scripts/
    assemblies/
    manifests/
    diagnostics/
```

Reasons:

- `scripts/` stays source-only and mod-pack-friendly
- generated output is clearly disposable
- backups can include source without compiled artifacts
- cache wipe is one obvious operation: delete `cache/scripts`
- package/update tools do not confuse generated assemblies for editable content

RunUO's `Scripts/Output` pattern can be supported later as a compatibility option if needed, but it should not be the default.

## Compile Inputs And Cache Key

The cache manifest should include:

- script source file paths relative to runtime root
- content hash for every script source file
- referenced assembly identities and content hashes where available
- Roslyn/compiler package and runtime version
- compiler options
- define constants
- target framework/runtime identifier
- UltraSim server API version
- UI protocol version
- inventory/network/kernel contract versions where scripts can directly target them
- script host version
- debug/release flag

If any key input changes, the cache is invalid.

Cache hit behavior:

- validate its manifest before loading
- validate API/runtime/compiler version gates before loading
- load the cached assembly only after validation succeeds
- log `Scripts: Compiling C# scripts... - done (cached)`

Cache miss behavior:

- compile into a new assembly under `cache/scripts/assemblies`
- write diagnostics under `cache/scripts/diagnostics`
- write manifest under `cache/scripts/manifests`
- log warning/error counts

Cache failure behavior:

- corrupt manifest, missing cached assembly, stale version gate, or cached assembly load failure should invalidate that cache entry
- after invalidating the cache entry, attempt one clean compile from source
- only fail startup if the clean compile fails
- log that the cache was ignored/rebuilt so operators know what happened

Compile failure behavior:

- report diagnostics grouped by relative script path
- include line/column and diagnostic id
- dedicated/headless startup fails fast by default
- interactive console/Godot wrappers may offer retry/reload controls later, but the core compiler should return a structured failure result

## Assembly Load And Future Reload

Do not design the first load path in a way that makes future reload structurally impossible.

The first implementation does not need live script reload. It should still load compiled scripts through a script assembly host abstraction rather than scattering direct `Assembly.LoadFrom` calls through startup code. The abstraction should own:

- compiled assembly path
- manifest identity
- loaded assembly handle
- discovered modules
- lifecycle invocation
- eventual unload/reload support

On modern .NET, unloadable reload likely means using a collectible `AssemblyLoadContext`. The first pass may choose a non-unloadable context and process-restart-only behavior, but call sites should talk to an `IScriptAssemblyHost`/`IScriptRuntime` style contract so the implementation can change later.

If script reload is added later, open server-driven UI instances need explicit supersession handling. A panel provider from an old script assembly may have open client instances when the provider reloads. The script/UI bridge should either:

- close/reopen affected panels, or
- bump schema revisions and trigger the existing UI supersession flow.

Do not silently keep accepting actions against stale provider instances after reload.

## References And API Surface

Scripts should compile against stable server APIs, not arbitrary project internals.

The first `IUltraSimScriptContext` should stay deliberately small:

- logging
- command registration
- UI panel provider registration
- definition registries only as they are intentionally introduced
- server/runtime metadata that is safe for scripts to read

Do not expose accounts, inventory, networking, persistence, or direct ECS world mutation in slice 1. Add each of those later through deliberate facades when a feature needs them.

Initial reference set should include:

- `UltraSim.Server.Core`
- server script API assembly, if split out later
- `UltraSim.UI` for schema contracts/builders that are explicitly safe for scripts
- server-side UI service/glue interfaces, exposed through a stable facade
- inventory/server inventory facades once gameplay inventory panels are scripted
- selected BCL assemblies

Avoid exposing:

- Godot assemblies
- wrapper projects
- test/benchmark assemblies
- internal transport implementation details
- direct mutable kernel internals unless intentionally surfaced through a server facade

## Lifecycle Hooks

Keep hooks explicit and deterministic.

The primary discovery path should be an explicit module contract, not arbitrary static method scanning.

```csharp
[UltraSimScriptModule]
public sealed class BasicUiModule : IUltraSimScriptModule
{
    public void Configure(IUltraSimScriptContext context)
    {
        context.UI.RegisterPanelProvider(new WelcomePanelProvider());
    }

    public void Initialize(IUltraSimScriptContext context)
    {
    }
}
```

This preserves the RunUO-like operator flow while giving modern diagnostics, tooling, and future dependency metadata. Static `Configure`/`Initialize` method scanning should not be added in the first pass unless a real compatibility need appears.

Initial lifecycle hooks:

- `Configure`
  - register services, commands, item definitions, mobile definitions, UI panel providers, rule providers
  - runs after scripts compile and before world/session load
- `Initialize`
  - perform startup tasks that require configured registries and loaded data
  - runs after configure and after core runtime data needed by scripts is available

Ordering rule:

- first pass may run modules in deterministic discovery order
- modules must not rely on discovery order for correctness
- dependency-ordered lifecycle is reserved for a later module dependency declaration mechanism
- when dependencies exist, `Configure` and `Initialize` must run in dependency order

Potential later hooks:

- `OnServerStarted`
- `OnServerStopping`
- `OnSave`
- `OnWorldLoaded`
- `OnScriptsReloaded`

## Custom Kernels And Plugins

Do not add a first-class custom-kernel compile tier in the first pass.

Most server customization should fit in `scripts/`:

- items
- mobiles
- commands
- UI panels
- crafting
- vendors
- inventory viewers
- combat/progression rules
- housing/auction/guild systems

If a server operator wants a VitaNex-style foundation package, it can live under `scripts/Engines/SomeFoundation/` and compile with the rest.

True custom kernels or deep runtime extensions should be a later plugin system:

```text
plugins/
  SomeCompiledExtension/
    SomeCompiledExtension.dll
    plugin.json
```

That future system needs capability registration, version compatibility, restart-only load rules, and a clearer trust boundary. It should not block the first script compiler.

## First Implementation Slice

Keep the first proof small.

1. Add runtime-root resolver shared by headless, console, and Godot server shells.
2. Add runtime template folders and README.
3. Add script compiler service with:
   - source discovery from `scripts/**/*.cs`
   - Roslyn compile
   - structured diagnostics
   - cache manifest
   - cache hit/miss result
4. Add minimal script API/context:
   - logging
   - command or startup registration
   - UI panel provider registration using `UiPanelBuilder`
   - no accounts/inventory/network/persistence/direct-world facades yet
5. Add one script-authored message/admin panel.
6. Add tests:
   - compile success
   - compile failure diagnostics
   - compile-failure startup/smoke proves the server fails fast with structured diagnostics
   - warning-as-error option defaults off but can fail compile when enabled
   - cache hit after clean second compile
   - cache miss after source change
   - corrupt cache or failed cached assembly load falls back to clean compile
   - generated output stays under `cache/scripts`, not `scripts`
   - script compile does not create or modify `saves`, `backups`, or runtime world files
   - script trying to reference Godot fails unless a future explicit server-side Godot reference mode is intentionally added
   - script registration discovery
   - script-authored UI panel opens through existing server UI service
7. Add a headless/console smoke:
   - disposable runtime root
   - template scripts copied in
   - compile succeeds
   - startup logs script compile phase

The first runtime binding should prove that registrations are not just counted at startup:

- script commands are registered into the live `ServerCommandRegistry` used by chat/console command dispatch
- script UI panel providers are openable through the existing `ServerUiService`
- the initial script-panel open command may be a small debug/operator command such as `/panel <provider-key>`
- opening a script panel must still use the normal server-driven UI packet path and normal per-connection UI scope
- provider build failures should reply with a structured error and must not crash the session host

This binding is still deliberately narrow. It does not expose inventory, accounts, persistence, or direct ECS world mutation to scripts.

## Security Boundary

Server scripts are trusted by the server operator and execute on the server only.

Client content patches may deliver:

- assets
- catalogs
- schemas
- manifests
- data tables

Client content patches may not deliver:

- executable C# scripts
- assemblies
- Godot scripts
- arbitrary native libraries

The client should only receive serialized data/contracts from server scripts, never script code.

## Diagnostics And Registries

Script compiler diagnostics and registration diagnostics are related, but they should not be the same subsystem.

The compiler/host should report:

- source files discovered
- compile cache hit/miss
- warnings and errors
- loaded assemblies/modules
- module configure/initialize failures

Runtime registries should report their own content state regardless of origin:

- archetype definitions by source/origin
- prefab definitions by source/origin
- item/mobile/container/crafting definition counts
- duplicate keys and superseded revisions
- disabled or invalid data-authored prefabs
- unresolved asset or recipe references
- live data prefab revisions and rollback targets

The future admin panel should probably include both views, but registration/content diagnostics must not be script-compiler-only. Built-in definitions, script-authored definitions, plugin definitions, and live data-authored prefab definitions should all surface through the same registry diagnostics model.

## Open Questions

- Should script reload be supported after initial startup, or deferred until the server runtime and save barriers are more mature?
- Should default scripts ship as editable source in exported packages, or as a starter pack separate from the minimal dedicated server package?
- Should package/update tooling preserve operator-edited scripts by default and only refresh missing template files?
