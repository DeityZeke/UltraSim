UltraSim Integrated S0 Design

Status: Proposed integration contract
Date: 2026-08-12
Baseline: ECS 3.4.1 persistence-hardening baseline
Purpose: Move V3 from individually proven subsystems into one small, real, authoritative runtime without rebuilding V2 Server wholesale or reopening generic kernel design without evidence.

0. How to use this document

This is a checkpointed design and implementation plan, not a single implementation task.

Each S0.x section is intended to be implementable, reviewed, benchmarked, and accepted independently. Do not begin a later slice merely because part of it is convenient while implementing an earlier slice. If a slice exposes a real design flaw in a lower layer, fix the flaw at the correct layer, add a regression, and then continue.

Checklist convention:

[ ] not started / not accepted

[x] implemented, independently verified, and accepted

A parent item is not complete until its listed acceptance gates are complete.

S0 completion summary

[x] S0.0 — Integration contract and project boundaries frozen

[x] S0.1 — Concrete ServerRuntime + atomic ServerGeneration root

[x] S0.2 — Internal and Headless hosts + minimal host capabilities

[x] S0.3 — UI-0 definition/compiler kernel

[x] S0.4 — Local client UI runtime + minimal Godot renderer

[x] S0.5 — Admission + optional minimal Accounting

[x] S0.6 — Real ECS + Accounting transactional persistence integration

[x] S0.7 — Game-owned ECS inventory vertical + generic Gump UI

[ ] S0.8 — Integrated end-to-end acceptance scenario and baselines

S0 is accepted only when S0.0 through S0.8 are all accepted.

1. S0 charter

1.1 Goal

Build the smallest real UltraSim runtime that proves the intended architecture under actual use:

Start one authoritative server runtime, compose one game, join it locally, optionally resolve an account/character, render a server-defined cached Gump, enter a persistent ECS world, manipulate a small real inventory, simulate, save, mutate, restore transactionally, and continue against the newly published generation.

This is the first integration milestone whose primary evidence is cross-layer behavior, not isolated kernel capability.

1.2 What S0 is not

S0 is not:

a port of _V2/Server;

ECS 3.5;

Network V3;

a complete client;

a complete UI framework;

a complete account system;

a complete inventory framework;

a mod loader;

a public server ecosystem;

a physics integration milestone.

S0 exists to prove the skeleton those systems can later attach to.

1.3 Primary architectural thesis

UltraSim is organized conceptually into four layers:

AUTHORING / MODDING
    game/server customization, definitions, values, safe hooks
                |
                v
GAME COMPOSITION
    the actual game: policy, systems, UI surfaces, admission, rules
                |
                v
RUNTIME / BRIDGES
    lifecycle, generation ownership, translation, routing, synchronization
                |
                v
KERNELS
    independent, engine-agnostic mechanisms

Hosts/renderers sit beside this stack and provide platform-specific execution or presentation.

Layer rule

A lower layer must not know the game-specific meaning of the data passing through it.

A bridge may:

translate identities;

translate state representations;

route commands/events;

synchronize lifecycles;

own bounded derived/cache state needed for translation.

A bridge must not:

become an independent gameplay database;

define game policy;

own domain business logic;

accumulate UI workflow;

become a general service locator;

silently duplicate another subsystem's authoritative state.

2. Frozen S0 architecture decisions

These decisions are considered part of S0's contract. Reopen them only if a reproduced integration failure demonstrates that the contract is wrong.

2.1 One authoritative server runtime

There is one concrete authoritative runtime concept:

ServerRuntime

Different execution environments instantiate the same runtime. They are not different server types.

InternalHost  ----\
HeadlessHost  -----+--> ServerRuntime
GodotHost later ---/

Do not introduce a SinglePlayerServer, DedicatedServer, GodotServer, or subclass hierarchy with divergent simulation behavior.

2.2 Generation is the atomic authority root

All mutually coherent authoritative roots are owned beneath one generation object:

ServerGeneration
    EcsWorld
    game-owned generation state
    optional Accounting state
    derived generation-owned indexes/services

Load publication is one root ownership transfer:

ActiveGeneration = CandidateGeneration

The host must not publish ECS, Accounting, Inventory, or other participant roots independently.

2.3 Kernels remain independent

A kernel may be referenced by a bridge or composition layer. Kernels do not reference each other merely because one game happens to use them together.

Removing Network must not make ECS or UI fail to compile. Removing UI must not change ECS. Removing a future Inventory kernel must not change Network.

2.4 Game composition becomes specific

Above Runtime/Bridges, default to game-specific implementation.

Do not build a generalized product framework merely because multiple hypothetical games could want different policies. Extract reusable mechanisms downward only after real repeated use or when a mechanism is unambiguously foundational.

2.5 Offline is semantic, not packet loopback

Single-player/local execution preserves authority and client boundaries but does not serialize and packetize data merely to cross the same process.

Remote: ServerRuntime -> Network -> ClientRuntime
Local:  ServerRuntime -> LocalSemanticLink -> ClientRuntime

Both paths eventually carry equivalent logical operations.

2.6 Host injection is typed and cold

Hosts may provide platform capabilities, but S0 must not introduce a general IServiceProvider/service-locator pattern.

Capabilities are:

explicit;

typed;

bound during startup;

absent if unused.

S0 only implements capabilities it actually consumes.

2.7 Persistence semantics remain authoritative

ECS 3.4.1 durable identity, saved shape, component schema/migration, reference remapping, staged reconstruction, and save-set transaction semantics remain the persistence truth.

A future alternate save profile may change physical representation/storage, but may not bypass those semantics.

S0 does not need to implement an alternate serialization format merely to prove this future seam.

2.8 Accounting is optional admission/domain state

A participant may enter a game without an account if the game's admission policy allows it.

Do not create fake accounts because a framework assumes an account always exists.

Accounting, when enabled, owns account-domain facts such as identity and character ownership. It does not duplicate authoritative ECS state such as current position, health, or inventory contents.

2.9 Inventory truth is not its viewer

For S0, authoritative item/container state lives in ECS. An inventory index may accelerate enumeration but is derived from ECS truth.

A UI list, future D2 grid, and future UO freeform viewer are projections over inventory truth, not separate inventory databases.

2.10 UI is a cached safe Gump descendant, not a second GUI engine

The generic UI path exists to let a server safely describe bounded renderer-independent interfaces, cache immutable definitions client-side, send model deltas, and receive actions.

It is not intended to recreate every Godot/Unity/Unreal UI widget in a serialized protocol.

Core UI principle:

Definitions describe views. Models carry state. Actions carry intent. Renderers own presentation. Servers own authority.

3. Target dependency shape

Exact folders/projects may evolve, but dependency direction must follow this shape.

Kernels/
    ECS/                 accepted 3.4.1 substrate
    UI/                  S0.3: small generic presentation protocol/compiler
    Network/             later; independent

Server/
    Runtime/             S0.1: ServerRuntime + ServerGeneration
    Persistence/         existing 3.4.1 save-set coordination
    Hosting/             S0.2 host-loop/shell plumbing if needed

Client/
    Core/                S0.4 engine-neutral client UI runtime/local link endpoint
    _Godot/              S0.4 renderer/host implementation

Game/                    logical game-composition boundary
    Contracts/           narrow cross-domain policy types; no UI/client dependency
    Composition/
    Simulation/
    Admission/
    Accounting/
    Inventory/
    UI/

Scripts/                 later authoring/modding boundary
Tools/UiDesigner/        later Gump Maker / exporter

Physical-boundary rule

Do not move accepted ECS/persistence code merely to make directories aesthetically match this diagram. This is first a dependency model, not a mandatory repository reshuffle.

4. Runtime ownership model

4.1 ServerRuntime

ServerRuntime is a concrete reusable authoritative machine. Its public surface should remain small.

Conceptual responsibilities:

Initialize / Start
Tick
Request or execute Save
Prepare/replace Generation
Stop / Dispose
Status necessary for hosts

It must not become V2 IServerApplication again.

Specifically, do not add account admin, session kicking, server browser, inventory mutation, UI rendering, command registration, discovery, or networking APIs directly onto ServerRuntime simply because wrappers may eventually want those features.

Those are exposed through game/runtime facets when they actually exist.

4.2 ServerGeneration

A generation owns all authoritative state that must change coherently on world replacement.

S0 expected members are conceptually:

EcsWorld
GameGenerationState
AccountingState?       // only in account-enabled composition
InventoryIndex?        // derived, generation-bound

Exact types are implementation decisions.

Accepted S0.6 implementation note: Runtime stores one opaque game-state object
inside `ServerGeneration`; composition may install it cold through
`ServerGenerationBuilder.SetGameState`. Typed game callbacks receive that state
paired with the same generation's ECS world. Runtime owns its lifetime but does
not inspect its game-domain meaning.

Generation lifetime invariants

A generation is constructed privately before publication.

No candidate generation root is externally visible before validation completes.

Publication is one host-owned root swap.

After publication succeeds, the old generation is no longer authoritative.

Old runtime handles are not translated into the new generation.

Old generation disposal occurs after it can no longer be observed as active.

Failure before publication leaves the active generation unchanged.

An unexpected failure after the publication boundary follows an explicit terminal/fatal policy rather than attempting an unprovable partial rollback.

4.3 Tick boundary

The host drives time; the ECS owns system scheduling.

S0 tick concept:

host elapsed/fixed step
    -> runtime input cutoff
    -> ECS pre-simulation structural boundary as required
    -> scheduled ECS graph
    -> ECS post-simulation structural boundary as required
    -> publication/observation available to downstream consumers
    -> sparse host housekeeping

ServerRuntime must not become another work scheduler.

5. UI-0 conceptual contract

UI is included in S0 because Admission and later Inventory/client integration will otherwise invent temporary presentation contracts that must be replaced later.

S0 implements only the minimum generic Gump substrate needed by the integrated scenario.

5.1 Two UI categories

Client-local application UI

Owned entirely by the client/renderer/application:

start menu;

server browser later;

local settings;

quit;

connection/session error shell;

other pre-server application workflow.

These are not server-defined Gumps.

Server-driven Gumps

Server/game-defined safe presentation:

character selection;

simple inventory panel;

messages/choices/forms;

future vendor/crafting/admin/custom-server panels.

5.2 Immutable definition vs dynamic instance model

Definition

Contains only immutable structure/contracts:

definition identity/key/hash/version;

layout topology;

elements;

style/asset roles;

model slot declarations;

action declarations;

capability requirements.

Model

Contains current per-instance values:

text;

list rows;

selected value;

toggle values;

current status/error text;

action availability state;

other dynamic values.

Action

Contains user intent:

panel/view instance identity;

definition identity as needed;

action ID;

context/row identity if needed;

input values;

client sequence if needed.

Hard rule

Changing any of the following must not change definition identity/hash:

label value;

character rows;

current selected row;

button enabled/disabled/unavailable state;

availability reason;

validation error/status;

health/quantity/current values.

5.3 Runtime IDs are numeric/prepared

Stable strings may exist in source metadata, compiler diagnostics, visual tooling, or serialized authoring files.

Normal prepared game/server code must not route UI by handwritten strings.

Compiler/generator resolves source names to prepared typed/numeric handles such as:

UiSlotId
UiActionId
UiDefinitionHandle

Normal code should conceptually look like:

model.Set(CharacterSelectUi.Title, ...)
model.ReplaceRows(CharacterSelectUi.Characters, ...)
request.Action == CharacterSelectUi.Play

not:

model.Set("charcter.naem", ...)

5.4 Initial renderer-neutral layout algebra

S0 supports a deliberately small layout set:

Canvas       exact/anchored/freeform placement
Vertical     ordered vertical flow
Horizontal   ordered horizontal flow
Overlay      shared bounds / layered children
Scroll       scrollable content host

Grid is reserved conceptually but is not required for S0 unless implementation evidence shows it is necessary for the two required Gumps.

Child sizing in flow layouts

The initial generic vocabulary should stay small, e.g.:

Fixed
Content
Fill

with a small alignment vocabulary:

Start
Center
End
Stretch

and explicit spacing/padding where needed.

Canvas placement

Canvas may support the renderer-neutral mathematical concepts required for conventional game UI:

logical x/y/width/height;

normalized anchors;

normalized pivot;

offsets;

z-order.

Do not reproduce every engine-specific RectTransform/Control quirk.

5.5 Initial element vocabulary

S0 should implement only what the integrated UI requires. Expected baseline:

Panel/background surface
Text
Image
Button
TextInput
Toggle
List/Repeater

A progress display may be added only if the S0 scenario genuinely uses it.

Do not add domain widgets such as:

InventoryGrid;

ContainerCanvas;

SkillTree;

MiniMap;

DialogueGraph;

CodeEditor;

property graph/editor.

Those are game/client semantic surfaces or renderer extensions, not generic kernel primitives.

5.6 Authoring style

The source API should preserve RunUO-like readability while allowing modern containment when useful.

Classic/freeform:

AddCanvas(root, canvas =>
{
    canvas.AddBackground(...);
    canvas.AddText(...);
    canvas.AddButton(...);
});

Modern containment:

AddVertical(root, column =>
{
    column.AddText(...);
    column.AddList(...);

    column.AddHorizontal(row =>
    {
        row.AddButton(...);
        row.AddButton(...);
    });
});

Nested lexical scopes exist to communicate containment only.

A call such as AddToggle(...) must not secretly mean all of:

declare static schema;

set current runtime value;

decide action availability;

build a network document;

hash the current model.

Definition compilation and instance model population remain separate phases.

5.7 Compiled representation

Authoring syntax is not the runtime representation.

The compiler may flatten definitions into compact arrays/tables of layout nodes, elements, slot bindings, action bindings, style roles, and asset references.

Do not preserve an object graph merely because the C# source used nested scopes.

5.8 Cold/hot/delta protocol semantics

Cold open:

Definition + Model
    -> validate
    -> cache definition
    -> create instance
    -> render

Hot open:

Definition identity/hash + Model
    -> exact cache hit
    -> create instance

Delta:

Instance + sequence + changed slots/rows/action state
    -> validate against retained compiled definition
    -> patch instance

A normal scalar delta must not require closing/reopening the Gump or rebuilding the full Godot control tree.

5.9 Renderer mapping

The renderer bridge hand-maps the deliberately finite generic vocabulary.

Example Godot mapping:

Text        -> Label
Button      -> Button
TextInput   -> LineEdit
Toggle      -> CheckBox
Canvas      -> Control
Vertical    -> VBoxContainer
Horizontal  -> HBoxContainer
Overlay     -> Control/overlay host
Scroll      -> ScrollContainer

Unity/Unreal renderers may later provide equivalent mappings.

Renderer equivalence is semantic, not pixel-identical across engines.

5.10 Future Gump Maker compatibility

S0 does not implement Gump Maker, but the compiled definition format and binding generation must not assume that C# is the only authoring source.

Future authoring frontends may include:

Standalone Gump Maker
Godot restricted-Control exporter
Unity editor exporter
C# definitions
mod/server data files

All should converge on the same compiled UI definition + generated/typed bindings.

6. Admission and Accounting contract

6.1 Admission owns entry progression

Admission is the path from an incoming/local participant to an authoritative world binding.

Accounting is optional state used by some admission policies.

S0 must prove two different game policies.

Profile A — no account

Join
 -> local/basic validation
 -> create or bind playable ECS entity
 -> enter world

No account or character-list abstraction is invented.

Profile B — account + character selection

Join
 -> trusted local identity
 -> resolve Account
 -> open CharacterSelect Gump
 -> choose CharacterRecord
 -> resolve PersistentEntityKey against current generation
 -> enter world

6.2 S0 admission extensibility

Do not build the final generalized admission plugin framework in S0.

S0 should make the policy sequencing explicit enough that the two required flows can share lower-level mechanisms without forcing all games through one rigid login flow.

If repeated step execution naturally emerges from the two profiles, extract only that proven mechanism.

Custom arbitrary pre/post hook systems, portal workflows, password workflows, UI-generated credentials, and runtime package injection are deferred.

6.3 Minimal Accounting data

S0 account-enabled composition needs only enough durable state to prove a real participant boundary.

Conceptually:

AccountId
CharacterId

AccountRecord
    AccountId
    minimal enabled/status metadata if needed

CharacterRecord
    CharacterId
    AccountId
    Slot
    DisplayName
    PrefabKey
    PersistentEntityKey

Do not persist duplicate authoritative ECS state such as current transform, health, needs, or inventory contents.

6.4 Authentication is not an S0 blocker

S0 uses a trusted local identity fixture/provider for the account-enabled flow.

Deferred:

password hashing;

PBKDF2/Argon2;

token exchange;

rate limiting;

lockout;

web portal;

cross-server account history;

moderation/ban federation.

7. Inventory S0 contract

7.1 Scope

S0 inventory is intentionally a game-owned ECS implementation, not a resurrection of the complete V2 Inventory kernel/store.

The slice exists to answer whether the actual game can represent item/container truth naturally on V3 ECS and expose it through the UI/runtime boundaries.

7.2 Authority

ECS owns authoritative item/container state.

Representative facts may include:

Item marker/type/provenance
Quantity
ContainedBy relationship
Placement/slot if required by S0
Equipped state if required by S0

Exact component shapes are decided during S0.7 based on the smallest implementation that supports the acceptance scenario.

7.3 Inventory index

Container enumeration must not require a full scan of all Item entities.

A generation-owned InventoryIndex or equivalent may map container identity to contained item identities.

It is:

derived;

bounded/accounted;

reconstructible from ECS truth;

not independently authoritative;

not independently persisted in S0.

7.4 S0 mutations

Required:

Move;

Split;

Equip.

Merge may be added if the Split/Move implementation proves it is required to keep semantics coherent.

Mutation rules:

validate before mutation;

accepted mutation applies atomically at the gameplay level;

rejected/no-op requests do not partially mutate;

no silent item duplication;

no silent item destruction;

no silent quantity clamping to a value other than the requested operation.

Deferred:

trade/escrow;

vendors;

auction;

mail;

guild bank;

viewer/session fanout;

corpse ownership policy;

sophisticated weight/grid/freeform solvers.

7.5 S0 inventory UI

Use the generic Gump system as a simple list/repeater view.

This is deliberately not the final semantic Inventory surface.

Required UI behavior:

Backpack
    Bread x5
    Iron Sword

actions:
    Split
    Equip

A successful mutation produces UI model deltas without Gump close/reopen.

The future D2/Skyrim/UO viewer system is deferred until real game inventory semantics exist to pressure it.

8. Host capability contract

8.1 S0 rule

Do not create interfaces for future platform facilities merely because they may someday exist.

S0 introduces only the host capabilities required to run the accepted slices.

Likely examples:

logging/output sink;

durable filesystem/save storage already required by persistence;

clock/timing only if runtime code cannot remain host-driven without one.

8.2 Future capability examples — not S0 deliverables

The architecture must leave room for future explicit capabilities such as:

Physics backend
alternate persistence profile/storage
platform authentication
platform telemetry

These do not belong in S0 unless a slice requires them.

8.3 Physics rule for later

When physics is eventually injected, a game declares its authoritative physics requirement/profile. Different authoritative hosts must provide semantically compatible implementations for that game.

Host injection must not imply that a simple internal solver and Godot/Jolt are interchangeable if gameplay outcomes depend on their collision/solver semantics.

9. Slice dependency graph

S0.0 Contract
   |
   v
S0.1 Runtime/Generation
   |
   v
S0.2 Hosts

S0.0
   |
   v
S0.3 UI Definition/Compiler
   |
   v
S0.4 Client UI + Godot + LocalSemanticLink

S0.2 + S0.4
       |
       v
S0.5 Admission + Accounting
       |
       v
S0.6 Transactional participant integration
       |
       v
S0.7 ECS Inventory vertical
       |
       v
S0.8 Full integrated acceptance

10. S0.0 — Integration contract and scaffolding

Objective

Freeze the responsibility/dependency rules before implementation begins.

Deliverables

Add/accept this design as the authoritative Integrated S0 plan.

Define project/folder ownership rules for Kernel, Runtime/Bridge, Game Composition, Client/Renderer, and future Authoring/Mods.

Record explicit dependency direction between new projects.

Record ServerRuntime as the only authoritative server runtime type concept.

Record ServerGeneration as the only active authoritative generation root.

Record LocalSemanticLink as the S0 client/server path; Network transport explicitly deferred.

Record UI definition/model/action separation and the small layout algebra.

Record Accounting as optional.

Record Inventory as game-owned ECS truth for S0.

Record non-goals.

Validation

Existing ECS 3.4.1 and persistence suites remain unchanged/passing.

No new runtime feature is introduced merely to satisfy the design document.

Exit gate

A reviewer can assign any future file/type proposed during S0 to exactly one of:

Kernel
Runtime/Bridge
Game Composition
Client/Renderer/Host
Authoring/Tooling later

without ambiguous ownership.

11. S0.1 — ServerRuntime and ServerGeneration

Objective

Create the real authoritative runtime root without importing V2 Server architecture.

Required behavior

One concrete ServerRuntime can initialize, start, tick, stop, and dispose.

ServerRuntime owns exactly one current ServerGeneration reference while running.

A fresh generation owns a real V3 EcsWorld and can execute at least one scheduled simulation system.

Game composition/configuration is supplied above Runtime; Runtime does not hard-code medieval-game domain policy.

Runtime does not expose Account/Inventory/UI/Network admin APIs directly.

World replacement API is private/controlled enough that callers cannot publish participant roots independently.

Required tests

Fresh runtime lifecycle: create -> initialize -> start -> ticks -> stop -> dispose.

Invalid lifecycle transitions reject deterministically.

Repeated disposal is safe/idempotent where appropriate.

Generation replacement before publication cannot alter active generation.

Successful generation root swap changes all authoritative generation roots together.

Old ECS runtime handles do not resolve in the new world.

Old generation is disposed only after publication boundary.

Unexpected publication-boundary failure policy is explicit and tested.

Performance/accounting gate

Warmed runtime tick adds 0 managed allocation on the caller hot path.

Empty/minimal host wrapper overhead is measured against direct ECS execution.

A sustained unexplained runtime wrapper regression above roughly 2-5% is investigated before acceptance.

Non-goals

Network;

UI;

Accounts;

Inventory;

persistence participant composition beyond what is needed to define the future generation seam;

commands/admin/settings/discovery.

Exit gate

ServerRuntime can run a real ECS generation and replace the generation through a single authoritative root boundary without becoming a generalized V2 server facade.

Accepted S0.1 review clarifications

The scheduled-system requirement is an acceptance fixture, not a rule that every
valid game composition must register a system. The ECS scheduler is required;
an empty registered graph remains valid.

The host-facing Runtime API exposes lifecycle control and bounded status, not the
mutable generation root or an unconditional public replacement operation.
Replacement remains a controlled internal seam for later staged persistence.

`WorldClockSnapshot` establishes the persistable tick/world-time contract in
S0.1. Installation into a real durable multi-participant save set remains S0.6;
an in-memory snapshot round trip must not be described as durable persistence.

World-time rate defaults are mechanism-neutral. The 24x rate below is an explicit
test/game configuration, not Runtime's default game policy.

Authoritative simulation/world clock

Establish:

Host monotonic clock
Simulation tick
World-time accumulator
World-time advancement rate
Playback rate

with:

deterministic integer/fixed-point advancement,
persisted world time,
normal server playback fixed at 1×,
offline host allowed variable playback,
no calendar assumptions,
no day/night implementation,
no scheduler/kernel redesign.

And I'd give it one tiny test that says something like:

Configured:
normal world rate = 24×

Advance:
216,000 fixed 60Hz simulation steps
(60 minutes normal playback)

Expected:
WorldTime advanced exactly 24 hours

Save
Restore

Expected:
identical Tick and WorldTime

Run same simulation under 4× playback:
same resulting state after same number of simulation ticks

That last property is particularly important:

Playback speed changes how fast we reach tick N, not what tick N means.

If we preserve that, variable speed, pause, headless authoritative servers, deterministic testing, save/load, NPC schedules, and eventual world calendars all fit together without special cases.

12. S0.2 — Hosts and minimal platform capabilities

Objective

Prove that hosting is outside the authoritative server runtime.

Deliverables

InternalHost

Instantiates the same ServerRuntime used by all other hosts.

Drives it in-process without Network.

Exposes only the lifecycle/control hooks actually required by the local game/client bootstrap.

HeadlessHost

Instantiates the same ServerRuntime.

Provides console/headless process startup and graceful shutdown.

Provides basic diagnostics/log output.

Does not own simulation policy.

Host capabilities

Define explicit typed startup capabilities only for facilities actually consumed by S0.2.

No IServiceProvider, reflection-based service discovery, or arbitrary runtime capability lookup.

Absence of an unused future capability does not require a fake implementation.

Tests

Internal and Headless hosts produce identical canonical simulation state for an identical fixed-step deterministic fixture.

Both wrappers start/stop the same concrete runtime type.

Headless shutdown disposes current generation cleanly.

Host-specific logging/output does not alter simulation state.

Exit gate

A second renderer/host could theoretically instantiate ServerRuntime using documented startup hooks without subclassing or modifying the authoritative runtime.

Accepted S0.2 review clarifications

Optional diagnostics are best-effort and non-authoritative. An ordinary sink
failure cannot change simulation or lifecycle outcome; catastrophic resource
failure is not hidden.

Ordinary host lifecycle commands preserve `ServerRuntime` rejection semantics.
Headless `Shutdown` is a distinct cleanup operation and remains safe before
startup, after stop/fault, and when repeated.

Hosts preserve Runtime's single-owner thread contract. S0.2 deliberately does
not add concurrent lifecycle/tick access or locks around authoritative state.

`RunFixedSteps` is the deterministic S0.2 headless driver. It checks cancellation
between completed ticks and requires a running Runtime even when zero work was
requested. A future real-time process loop may drive `Tick(elapsed)` without
changing authoritative runtime semantics.

13. S0.3 — UI-0 kernel and definition compiler

Objective

Build the minimum V3 Gump substrate: immutable definitions, prepared typed IDs, dynamic models, actions, validation, and compilation.

Deliverables

Identity and compile model

Stable UiDefinitionKey/equivalent authoring identity.

Canonical definition hash/version identity.

Prepared numeric slot IDs.

Prepared numeric action IDs.

Immutable compiled definition representation.

Definition validation limits for untrusted/remote definitions.

Deterministic compilation: equivalent source definition -> byte/semantic identical compiled definition/hash.

Layout

Canvas.

Vertical.

Horizontal.

Overlay.

Scroll.

Minimal flow sizing/alignment vocabulary.

Canvas logical rect placement.

Anchors/pivot/z only to the degree required by the renderer-neutral contract.

Elements

Implement only the S0-required set:

Panel/background.

Text.

Image.

Button.

TextInput.

Toggle.

List/Repeater.

Model/action separation

Dynamic values are not embedded in definition hashing.

Dynamic action availability/reason is model/instance state, not definition state.

List rows are model state.

Action contracts are static; action invocation is transient intent.

Authoring

Support RunUO-like freeform Canvas declaration.

Support lexical nested AddVertical(...){...} / AddHorizontal(...){...} style through scoped child builders/lambdas.

Nesting communicates containment only.

Definition compilation is separate from model creation.

Prepared game code has strongly typed/generated binding handles or an equivalent compile-time-safe API.

Normal game code does not require handwritten string slot/action IDs.

Required adversarial tests

Typo/unknown binding cannot silently compile into a working definition with dead runtime behavior.

Changing a model value does not change definition hash.

Changing action availability does not change definition hash.

Changing layout does change definition hash.

Duplicate slot/action declaration rejects.

Invalid containment rejects.

Excessive depth/count/text limits reject.

Unsupported layout/element kinds reject safely.

Deterministic source ordering produces canonical compiled representation.

Explicit non-goals

JSON production transport;

UI Network packets;

Godot rendering;

inventory-specific widgets;

context/radial menus;

rich tooltips;

Gump Maker;

arbitrary style/CSS system;

Grid unless proven necessary.

Exit gate

A UI definition can be authored readably, compiled once into an immutable renderer-neutral definition, and populated/updated through typed dynamic model/action state without stringly runtime routing.

Accepted S0.3 invariants

`UiDefinitionHandle` identifies an exact canonical compiled revision, not merely
the reusable authoring key. Layout/action revisions therefore cannot accept a
stale action request solely because their `UiDefinitionKey` is unchanged.

Flow-container child order is one shared ordinal across nested layouts and
elements. The compiled layout and element tables retain that ordinal so a
renderer can reconstruct interleaved authoring order without relying on table
or kind grouping.

Normal game authoring retains definition-local `UiSlotReference` and
`UiActionReference` values returned by declaration. These references are used
both by scoped element builders and to prepare compact runtime IDs after
compilation. Cold string binding remains available for freeform/tooling input
and diagnostics, but it is not the preferred game-code route.

Action contracts declare row-context and input-kind requirements. Definition,
instance, row-context, action availability, and input kind are validated before
transient intent is accepted. TextInput and Toggle may bind typed Text and
Boolean actions respectively; an omitted action represents display-only state.

Compilation independently revalidates layout options and containment rather
than relying on public constructors. Canonical floats normalize signed zero,
and invalid anchors, non-positive Canvas dimensions, duplicate/missing sibling
orders, unsupported kinds, and cross-definition authoring references reject.

14. S0.4 — Client UI runtime, LocalSemanticLink, and minimal Godot renderer

Objective

Prove the server-driven Gump architecture in an actual renderer without involving Network.

Client runtime deliverables

Definition cache keyed by exact server/composition identity + definition key/hash/protocol as appropriate.

Cold open: definition + model.

Hot open: definition identity + model.

Panel/view instance registry.

Sequence-aware model delta application.

Scalar slot patch.

List/repeater add/update/remove/replace behavior required by CharacterSelect.

Action-state patch.

Close instance.

Definition supersession behavior is explicit; stale definition never accepts incompatible updates.

LocalSemanticLink deliverables

Logical operations only, e.g.:

OpenView
UpdateView
CloseView
SubmitAction

No packet encode/decode.

No JSON serialization.

No socket.

No dependency on Kernels/Network.

Client/server authority boundary remains explicit.

Godot renderer deliverables

Map the S0 generic vocabulary to native Godot controls.

Text -> Label.

Button -> Button.

TextInput -> LineEdit.

Toggle -> CheckBox.

Canvas -> Control/freeform host.

Vertical -> VBoxContainer.

Horizontal -> HBoxContainer.

Overlay -> layered Control host.

Scroll -> ScrollContainer.

List/Repeater -> minimal native row/list implementation.

Renderer behavior gates

Open creates the native control tree once.

A scalar model delta patches the existing target control; it does not rebuild the whole Gump.

A list row delta changes only the affected list portion.

Local input becomes a typed UI action request.

Renderer does not mutate authoritative server state directly.

Renderer-specific controls/types do not leak into Kernels/UI.

Test Gumps

S0.4 should prove at least:

A. Freeform Canvas fixture

background/panel;

text;

two overlapping/z-ordered or anchored elements sufficient to prove Canvas semantics.

B. Modern flow fixture

Vertical root;

nested Horizontal action row;

list/repeater;

input or toggle.

Exit gate

The exact same compiled definition/model/action semantics operate locally through a generic client runtime and native Godot controls, with cold cache, hot cache, and live delta behavior.

Accepted S0.4 invariants

- Exact cache identity includes server/composition/protocol/key/version/hash;
  definition-family replacement is version-monotonic and same-version hash
  conflicts reject before mutation.
- View IDs are owner-thread-bound, monotonic identities. Closed IDs retire;
  only a proven newer revision of the same family may replace an open instance
  while retaining its ID.
- Open operations and deltas own immutable snapshots. Renderers receive a
  read-only model view, and scalar deltas share immutable list snapshots rather
  than cloning every row.
- Delta validation precedes candidate model mutation. Multiple distinct row
  changes to one list form one ordered atomic delta; duplicate row targets and
  replace-plus-row ambiguity reject.
- Renderer callbacks are valid only after Create/Apply/Close returns. Native
  renderer failure or synchronous callback reentry terminally faults the
  client runtime because external control state may already be partial.
- List row actions are explicit in the compiled definition, require row
  context, preserve stable row identity through native input, and respect
  dynamic action availability.
- The Godot acceptance scene instantiates the full S0 element vocabulary and
  verifies freeform Canvas placement, flow nesting, shared Overlay bounds,
  Scroll padding, in-place scalar/list updates, typed actions, and close.
- Permanent engine-neutral regression rows cover cold open, exact-cache hot
  open, scalar patch, and one-row list patch. These are bounded managed UI
  operations, not ECS zero-allocation hot paths.

Later remote transport must apply explicit artifact/message/value/count limits
before materializing untrusted UI definitions or dynamic payloads. S0.4 is a
trusted in-process semantic bridge and does not invent Network framing limits.

15. S0.5 — Admission and optional Accounting

Objective

Use the real runtime and real UI to prove that admission is game policy and Accounting is optional.

Profile A — accountless

Local participant joins.

Game policy performs minimal validation.

Game creates/binds a playable ECS entity.

Participant enters world.

No Account/CharacterRecord is created merely to satisfy framework assumptions.

Profile B — account + character select

Minimal Accounting

Stable AccountId.

Stable CharacterId.

Minimal AccountRecord.

CharacterRecord contains AccountId, slot, display name, PrefabKey, PersistentEntityKey.

Accounting does not duplicate current ECS transform/health/inventory state.

CharacterSelect Gump

Compiled definition is game-owned.

Character rows are dynamic model data.

Play/Create or Play-only actions are typed.

Action availability is dynamic without changing definition hash.

First open is cold.

Later open is hot when cache is present.

Entry

Trusted local identity resolves account.

Selected CharacterRecord resolves its PersistentEntityKey in the current generation.

The resolved entity becomes the controlled player binding.

Missing/stale durable entity reference rejects entry explicitly.

Deferred

passwords;

portal tokens;

rate limits;

character customization UI beyond the minimal fixture;

full extensible admission event/hook framework.

Exit gate

The same ServerRuntime supports both an accountless instant-entry game policy and an account-backed character-selection policy without core runtime changes.

Accepted S0.5 invariants

- Runtime remains unaware of Admission and Accounting. Trusted game policy
  reaches the active generation through `ServerGameFacet`, a synchronous,
  owner-thread-bound callback capability. Hosts do not expose the facet and no
  game assembly receives `InternalsVisibleTo` access to Runtime internals.
- A game-facet callback exception terminally faults Runtime because active ECS
  mutation may already be partial. Expected policy rejection returns a value
  and does not throw across that boundary.
- Accountless admission does not construct or require `AccountingStore`, an
  Account, or a CharacterRecord. Its entity binder must be transactional on a
  false result and transfers one newly created live entity on success.
- Account-backed admission uses the same Runtime and a game-owned compiled
  CharacterSelect definition. Cold open, exact-cache hot reopen, dynamic Play
  availability, typed row intent, and selector close all use S0.4 semantics.
- Hot reopen applies only to a session still waiting for selection. It cannot
  demote an entered binding while retaining its controlled entity/character.
- Character selection resolves the selected account-owned
  `PersistentEntityKey` against the current active generation. Foreign-world,
  missing/stale, non-playable, unowned, and already-controlled entities reject
  explicitly.
- Admission sessions and Accounting are owner-thread-bound. Expected UI-open
  failures do not publish half-created sessions; duplicate view ownership and
  failed hot reopen cannot leave stale view routing.
- Accounting owns only account/character identity, status, ownership, display,
  prefab provenance, and durable entity linkage. Character IDs, per-account
  slots, and persistent entity links are unambiguous; transform, health, and
  inventory remain exclusively ECS truth.
- `LocalAdmissionUiPort` has explicit lifetime and detaches its S0.4 event
  forwarding when disposed. Admission disposal attempts every open view even
  when one close fails.

Here, "without core runtime changes" means neither admission policy changes
the Runtime lifecycle, generation model, or embeds game/account semantics in
Runtime. S0.5 adds only the generic game-facet capability anticipated by
section 4.1; both policies use the same concrete Runtime implementation.

16. S0.6 — Real multi-participant persistence integration

Objective

Install 3.4.1 persistence into the real runtime with a genuine non-ECS durable relationship.

Save participants

ECS participant remains authoritative for world entities/components.

Accounting becomes a real save-set participant.

Both are captured under one save-set sequence/common epoch contract.

Inventory is not a separate participant in S0 because its authoritative state is ECS.

Load pipeline

Prepare ECS privately.

Prepare Accounting privately.

Resolve Accounting PersistentEntityKey references against staged ECS identities.

Validate ECS participant.

Validate Accounting participant.

Assemble a complete CandidateGeneration privately.

Publish with exactly one active-generation root swap.

Dispose old generation only after publication.

Required failure tests

ECS prepare failure leaves active generation unchanged.

Accounting prepare failure leaves active generation unchanged.

Missing referenced persistent entity rejects candidate.

Duplicate/inconsistent character durable reference rejects candidate if forbidden by game policy.

Accounting validation failure leaves active generation unchanged.

Candidate generation construction failure leaves active generation unchanged.

Publication callback cannot be retried after an attempt.

Final publication primitive is one root transfer; there is no independently observable partial ECS/Accounting publication.

Old runtime ECS handles remain stale after successful load.

Failed load does not advance the live generation's durable allocator/state.

Canonical behavior

Save -> restore -> recapture preserves expected canonical ECS artifact semantics.

Accounting recapture is deterministic for equivalent semantic state.

Save sequence/generation identity agrees across participants/manifests as required by 3.4.1.

Exit gate

The persistence coordinator is no longer proven only against synthetic participants: a real Account -> Character -> PersistentEntityKey -> staged ECS entity graph survives save/load with one coherent generation publication.

17. S0.7 — Game-owned ECS inventory vertical

Objective

Add the first real game subsystem above the generic runtime and prove that it does not force a new generic kernel prematurely.

Fixture

Create a deterministic game fixture such as:

Alice
  Backpack
    Bread x5
    Iron Sword

ECS truth

Character/player entity exists through the normal prefab/content path.

Backpack/container is represented in ECS.

Bread item/stack is represented in ECS.

Sword item is represented in ECS.

Containment/equipment relationships survive structural changes and persistence.

Derived index

Container enumeration does not full-scan every item entity.

Inventory index is generation-owned and bounded/accounted.

Index can be rebuilt from ECS truth.

Index is not separately persisted.

Index is rebuilt/validated after staged restore before generation publication as appropriate.

Mutations

Move.

Split.

Equip.

Merge only if required by the chosen fixture/semantics.

Each mutation has regression tests for:

accepted atomic result;

rejected no-partial-mutation result;

stale/missing item;

stale/missing container;

invalid quantity;

duplicate/loss prevention.

Gump integration

Generic inventory list Gump uses the same UI kernel/runtime.

List rows are a projection of authoritative ECS inventory state.

Split/equip actions become game-domain inventory intents.

Accepted mutation produces row/scalar deltas.

Rejected mutation produces explicit status/model response.

No close/reopen refresh.

Persistence

Inventory state survives ECS save/load without an Inventory participant artifact.

Runtime entity references remap correctly.

Rebuilt InventoryIndex matches restored ECS truth.

UI reopened after restore shows saved inventory state.

Exit gate

A real game subsystem has successfully used ECS, Runtime, UI, Admission/Accounting, and Persistence without being promoted into a generic kernel merely because V2 had a generalized implementation.

Accepted S0.7 invariants

- Inventory is a narrow feature, not the game integration root. `Game/Composition`
  owns one generated `GameSchema`, concrete compiled content, and the aggregate
  `GameGenerationState(Accounting + Inventory + Content)`. `Game/Persistence`
  owns whole-game participant staging/publication and `Game/UI` owns the
  inventory workflow. Inventory itself depends only on ECS. A narrow
  `Game/Contracts` assembly carries admission policy types so Composition does
  not depend on the Admission/UI implementation or Client.Core.
- Inventory contributes generated component declarations but owns no numeric
  component IDs and no closed feature schema. Final IDs come from the one game
  schema alongside unrelated simulation/character components.
- Inventory truth is limited to game-owned ECS facts: container capacity,
  optional container ownership, item parent/equipment/quantity, and optional
  stack/equipment policies. Stable prefab provenance supplies item identity and
  presentation. Bread/Sword/Apple are game content, not an Inventory enum or
  switch; a third item requires no Inventory rule/index edit.
- Being a container and having an owner are independent. The same rules/index
  support both an owned Backpack and an ownerless World Chest. Creation and
  rebuild validate registered content provenance and owned/ownerless shape.
- `InventoryIndex` is generation-owned, bounded, and derived. Contained items
  occupy a global dense child-node pool consumed by actual rows; empty
  containers retain metadata only and do not reserve their theoretical gameplay
  capacity. Packed removal repairs the one moved donor node's links/location.
  A full ECS scan is reserved for transactional rebuild. Accounting reports
  known child-array bytes separately from explicitly excluded CLR dictionary
  overhead.
- Creation, Move, Split, Equip, and destruction preflight expected failures and
  commit ECS plus index state as one game-domain mutation. Rejections preserve
  ECS liveness and index state; stale entities, missing containers/owners,
  invalid policy/quantities, content/capacity limits,
  occupied slots, and duplicate/loss paths are explicit outcomes. Raw ECS
  topology mutation is not a second supported Inventory write path.
- The inventory Gump uses the generic compiled UI definition, `UiModel`, and
  `UiModelDelta` path. One item list supplies row selection to separate
  Split/Equip controls. Sessions retain `PersistentEntityKey` and prove the
  persistent world before resolving compact row IDs, preventing cross-world
  durable-identity ABA. Accepted Split publishes `Update + Add`; accepted Equip
  publishes `Remove`; neither sends a full list `Replace` or close/reopen.
- ECS save/load is the only inventory persistence path. The save set contains
  `world` and `accounting` artifacts but no Inventory artifact. A staged restore
  resolves runtime entity references, rebuilds and validates `InventoryIndex`,
  combines it with prepared Accounting/content in `GameGenerationState`, and
  publishes the whole aggregate once;
  old ECS handles become stale and the restored inventory reopens through the
  hot client UI path.
- The scale gate creates 100,000 empty capacity-20 containers (2,000,000
  theoretical gameplay slots) with a 32-row child pool and zero contained rows.
  This locks the actual-use storage contract without pretending dictionary
  metadata is included in the 640 known child-array bytes.

18. S0.8 — Integrated acceptance scenario

Objective

Run one deterministic end-to-end scenario that exercises every accepted S0 boundary.

Scenario A — account-backed local flow

Fresh boot

InternalHost starts one ServerRuntime.

Game composition creates a fresh ServerGeneration.

ECS content/prefabs compile/seal through the normal V3 path.

Minimal simulation system(s) begin ticking.

World fixture

Create Account 1.

Create Character Alice record.

Create persistent Alice ECS entity.

Link CharacterRecord -> Alice by PersistentEntityKey.

Create Backpack, Bread x5, Iron Sword.

Client/admission

Local client enters account-backed Admission profile.

CharacterSelect definition is absent from cache.

Cold CharacterSelect open succeeds.

Godot renderer shows Alice.

Play action selects Alice.

Durable key resolves to current Alice runtime entity.

World entry succeeds.

Inventory

First Inventory Gump open succeeds.

Bread x5 and Iron Sword appear.

Split bread into the expected stacks.

Equip sword.

UI updates through deltas only.

Simulation

Run a fixed deterministic number of real ECS ticks.

At least one game/simulation component changes through the scheduler.

Save

ECS + Accounting capture one save generation.

Save completes through the real SaveSetCoordinator/current persistence contract.

Diverge live state

Mutate Alice/inventory after save so live state differs visibly from the saved generation.

Restore

Prepare staged ECS.

Prepare staged Accounting.

Resolve cross-participant durable references.

Validate all participants/candidate generation.

Publish one generation root.

Old ECS handles are stale.

Restored Alice is a new runtime entity identity representing the same durable entity.

Restored Backpack/Bread/Sword topology equals saved state.

Inventory index matches restored ECS truth.

UI after restore

CharacterSelect definition cache still has the exact definition when valid.

Reopen CharacterSelect through the hot path.

Reopen Inventory through the hot path if definition remains cached.

Models reflect restored saved state, not post-save divergent state.

Shutdown/restart smoke

Graceful shutdown saves or exits according to explicit test policy.

HeadlessHost can start from the same accepted save generation.

Canonical logical state matches InternalHost load.

Scenario B — accountless policy smoke

Start same runtime with accountless game admission composition.

Join directly into a playable entity.

No account participant is required for the running game's normal operation.

ECS save/load still works under the composition's declared participant set.

19. S0 performance and allocation gates

S0 is primarily architectural, but integration must not conceal obvious regressions.

19.1 Runtime hot tick

Warmed ServerRuntime tick with the minimal S0 simulation: zero timed managed allocation from the runtime wrapper path.

Measure direct ECS fixture vs runtime-hosted fixture.

Investigate sustained unexplained wrapper tax > approximately 2-5%.

19.2 UI

UI is not required to be ECS-style zero allocation everywhere. However:

Hot scalar delta must not rebuild the entire definition/model/control tree.

Hot definition cache lookup must not revalidate/recompile the full definition.

Repeated scalar updates should have bounded, measurable allocation behavior.

Capture cold-open build cost, hot-open cost, scalar patch cost, and list-row delta cost as permanent baseline rows.

19.3 Persistence

Do not reopen 3.4.1's known proportional-copy issue in S0 unless real integration violates an actual host budget.

Continue tracking:

authority pause;

capture allocation;

restore allocation;

peak process memory if practical;

canonical artifacts/digests.

19.4 Inventory

Permanent baseline candidates:

enumerate 10 / 100 / 1,000-item container through derived index;

accepted Move;

rejected Move;

Split;

Equip;

InventoryIndex rebuild at representative item count.

Do not optimize these before correctness unless a measured result is pathological.

20. Adversarial review gates

Every major S0 slice should receive at least one adversarial pass before being considered accepted.

Required review questions

Runtime/Generation

Can any participant/state become externally visible before generation publication?

Can publication partially succeed?

Can old generation handles accidentally resolve in the new world?

Can a host bypass runtime ownership and mutate roots independently?

UI

Can mutable model data affect definition hash?

Can a typo in a hand-written string silently create a dead binding?

Can an untrusted definition force unbounded allocation/depth/count?

Can stale definition/model/action IDs be accepted?

Can the renderer execute server-supplied code?

Can a local-only credential field leak into a generic action payload?

Admission/Accounting

Does accountless mode accidentally instantiate account state?

Can a character bind to the wrong persistent entity?

Can account state become a duplicate source of world truth?

Persistence

Can staged participants leak into active state?

Can failed load leave mixed generation state?

Can cross-participant refs resolve against the active world instead of staged world?

Inventory

Can a rejected mutation partially alter ECS/index state?

Can structural playback create duplicate/lost item membership?

Can index state diverge from ECS truth without detection/rebuild?

21. Explicit S0 non-goals / deferred backlog

These remain valid future ideas but must not block S0 acceptance.

Network / remote play

UDP transport;

protocol loopback;

AOI;

replication;

latest-state/reliable-event lanes;

baseline recovery;

secure session handshake.

Ecosystem

Gateway/master server;

LAN discovery;

public server list;

server polling;

web portal;

cross-server play history;

public/private hosting marketplace;

federated bans/reputation.

Accounting/security

password authentication;

Argon2/PBKDF2 production flow;

portal token validation;

lockout/rate limiting;

account moderation/admin UI;

cross-server identity history.

UI breadth

Gump Maker;

Godot UI export plugin;

Unity/Unreal renderers;

Grid layout unless demanded;

rich tooltip system;

context menus;

radial menus;

HUD framework;

world-space UI;

semantic Inventory surface;

D2/UO/Skyrim inventory viewers;

arbitrary renderer extension packages.

Inventory breadth

transaction escrow;

trade;

vendors;

crafting inventory integration;

auction;

mail;

banks;

guild storage;

corpse/owner lifetime policy framework;

generalized permissions/range/weight/grid solver kernel.

Host/platform breadth

Godot server GUI;

Jolt physics injection;

Unity/Unreal hosts;

custom persistence representation/profile;

database persistence.

Authoring/modding

RunUO-style Scripts folder runtime;

hot reload;

content package mounting;

visual prefab editors;

visual Gump Maker.

22. Tasking discipline for Sol/Codex/other implementers

When assigning an implementation task derived from this document:

Name the exact S0 slice.

Quote its Objective, Deliverables, Required Tests, and Exit Gate.

Explicitly state the listed non-goals.

Do not ask the implementer to opportunistically prepare later slices unless required to keep the current slice correct.

Treat _V2 as behavioral/design reference, not code to port mechanically.

Do not change Kernels/ECS unless the current slice reproduces a concrete ECS deficiency.

Do not introduce a generic abstraction because a future feature might need it.

Add permanent regressions for every reproduced bug found during audit.

Capture benchmark evidence before and after any optimization motivated by integration.

End each slice with a clean worktree/diff check and a short acceptance report.

Suggested profile/branch naming:

integration-s0.1-runtime-generation
integration-s0.2-hosts
integration-s0.3-ui-definition
integration-s0.4-local-ui-godot
integration-s0.5-admission-accounting
integration-s0.6-persistence-participants
integration-s0.7-inventory-vertical
integration-s0.8-integrated-acceptance

23. Decision log

Frozen for S0

One concrete authoritative ServerRuntime concept.

Hosts instantiate; they do not subclass into different server semantics.

One active ServerGeneration root.

Generation publication is one ownership swap.

Kernels remain independent.

Bridge responsibilities are translation/routing/synchronization, not gameplay ownership.

Game Composition is allowed and expected to be game-specific.

Offline uses direct semantic links, not fake packet networking.

Accounting is optional.

S0 Accounting owns character ownership/durable linkage, not world-state copies.

S0 inventory truth lives in ECS.

Inventory index is derived.

UI is a cached server-driven Gump descendant.

UI definition/model/action are separate.

Prepared UI routing is typed/numeric; no normal handwritten string IDs.

UI supports both freeform Canvas and modern nested layout containers.

Neutral layout names: Vertical, Horizontal, not engine-specific VBox/HBox in the protocol.

Initial layout algebra remains deliberately small.

Nested UI authoring syntax expresses containment; it is not the runtime object representation.

Complex semantic game surfaces do not automatically become generic UI node types.

Future Gump Maker/exporters compile to the same definition representation.

Intentionally not frozen yet

These require evidence from S0 or later game work:

Exact public C# names/signatures for ServerRuntime, ServerGeneration, UI handles, and LocalSemanticLink.

Exact physical repository folders for every Game/Runtime project.

Whether UI Grid belongs in the guaranteed baseline.

Exact compiled UI memory layout / SoA vs compact AoS tables.

Exact binary/network UI wire format.

Final semantic Inventory surface contract.

Whether a standalone generic Inventory kernel survives V3 at all.

Final Admission plugin/hook DSL.

Final host physics capability contract.

Final alternate persistence profile contract.

24. Final S0 Definition of Done

Integrated S0 is accepted when all of the following are true:

One concrete ServerRuntime runs one authoritative ServerGeneration.

Internal and Headless wrappers host the same runtime.

Runtime wrapper hot path remains zero managed allocation and near direct-ECS cost.

UI definitions compile once, cache exactly, and keep dynamic model/action state out of definition identity.

A minimal Godot renderer maps the finite generic UI vocabulary to native controls.

Cold open, hot open, scalar delta, list-row delta, and action roundtrip work locally without Network.

No normal UI game code depends on handwritten string slot/action IDs.

Accountless Admission enters the world without fake Accounting.

Account-backed Admission selects a character through a real server-driven Gump.

Character ownership resolves by PersistentEntityKey into the active generation.

ECS + Accounting save/load through one real save-set transaction.

Candidate load is fully prepared/resolved/validated before one generation-root publication.

Failed load cannot expose mixed participant state.

Game-owned ECS inventory supports the S0 Move/Split/Equip fixture atomically.

InventoryIndex is derived/rebuildable and survives generation replacement correctly.

Inventory persists through ECS without a duplicate inventory artifact.

Inventory Gump updates through model deltas without close/reopen refresh.

Save -> mutate -> restore returns the saved semantic state and invalidates old runtime handles.

CharacterSelect/Inventory definitions can reopen through hot cache after restore when still compatible.

Final deterministic InternalHost and HeadlessHost state agrees for the acceptance fixture.

Every slice has permanent tests, baseline measurements appropriate to its scope, adversarial review, and a clean diff check.

At that point the project has crossed from independently excellent subsystems into a small real engine runtime. The next major integration pressure should come from Network V3 attaching to the already-proven LocalSemanticLink/client/runtime semantics, not from adding more speculative server framework breadth.

Appendix A — Canonical S0 scenario at a glance

INTERNAL HOST
    |
    v
SERVER RUNTIME
    |
    v
ACTIVE GENERATION
    |-- ECS WORLD
    |      Alice
    |        Backpack
    |          Bread x5
    |          Iron Sword
    |
    `-- ACCOUNTING
           Account 1
             Character Alice
                -> PersistentEntityKey(Alice)

LOCAL SEMANTIC LINK
    |
    v
CLIENT UI RUNTIME
    |-- definition cache
    |-- CharacterSelect instance
    `-- Inventory instance
    |
    v
GODOT RENDERER

FLOW
    join
    -> character select [cold definition]
    -> play Alice
    -> inventory [cold definition]
    -> split bread / equip sword [deltas]
    -> simulate
    -> save ECS + Accounting
    -> mutate live state
    -> prepare staged save
    -> resolve durable Character -> staged Alice
    -> validate
    -> one generation root swap
    -> old Entity handles stale
    -> reopen CharacterSelect/Inventory [hot definitions]
    -> saved state visible

Appendix B — S0 philosophy

Identity is a value.
Components are data.
Storage is layout-optimized.
Traversal is contiguous.
Mutation crosses a barrier.
Barrier work is batchable.
Runtime memory is bounded.
Normal use cannot corrupt invariants.

One server runtime.
One active generation.
Hosts provide platform facilities.
Bridges translate; they do not become applications.
Game policy lives in game composition.
Definitions describe views.
Models carry UI state.
Actions carry intent.
Renderers own presentation.
Persistence reconstructs generations.
Inventory truth is not its viewer.
Genericity is earned by mechanism or repeated use.
