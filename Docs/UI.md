# UI, client projection, and authority

> Definitions describe views. Models carry state. Actions carry intent.
> Renderers own presentation. Servers own authority.

## Layers

```text
Kernels/UI
    neutral definitions, compiled bindings, models, deltas, actions, protocol

Game/UI and Game/Admission
    authoritative game workflows and semantic validation

Client/Core
    retained definitions/views, sequence validation, semantic link

Client/Network
    authenticated application-session bridge and remote action/apply translation

Client/Godot
    one Godot script assembly for shell, control construction, rendering,
    input forwarding, and edition-specific composition
```

`Game/UI` referencing Server Runtime is intentional: it implements server-side
game workflows. It does not make the UI kernel or client authoritative.

## Definitions and models

Authoring compiles bounded immutable definitions. Typed references are resolved
against the compiled definition; handwritten string lookups are not the normal
binding path. Models contain dynamic scalar/list state. Deltas carry monotonic
sequence numbers and update only the affected state where practical.

Row actions include source/list provenance and are accepted only when the row
exists in the current model. Display-only controls are noneditable. Definition
limits, text limits, row/cell limits, and cache-family limits are protocol
boundaries, not renderer suggestions.

## UO-inspired authoring and visual vocabulary

UltraSim's server-composed UI intentionally preserves the useful **authoring feel** of RunUO/UO
gumps without copying the old client packet vocabulary or binding the neutral UI kernel to Godot.

Historical UO/RunUO gumps exposed a small composable vocabulary including:

```text
background / resizepic
image / gumppic
tiled image / gumppictiled
item art / tilepic
button with normal + pressed art
checkbox with off + on art
radio groups
text / cropped text
text entry / limited text entry
scrollable HTML/text regions
page selection
alpha/transparency regions
tooltips
```

GumpStudio's classic `BackgroundElement` is effectively a nine-piece resizeable frame: nine
images form four corners, four tiled edges and a tiled center. Its `TiledElement` repeats one
image over a bounded rectangle. Buttons carry separate normal/pressed art; checkbox/radio controls
carry separate inactive/active art.

UltraSim should preserve the **semantic usefulness** of those primitives while allowing more
capable modern presentation. Do not require Game/server authors to manually construct Godot
`NinePatchRect`, `TextureButton`, theme overrides, atlas regions, or native nodes.

The target authoring experience should remain compact and familiar:

```csharp
canvas.AddBackground("frame", "ui.paper.frame", placement);
canvas.AddImage("portrait", "character.portrait", placement);
canvas.AddTiledImage("fill", "ui.paper.tile", placement);
canvas.AddButton("close", closeAction, closeLabel, visualRole: "ui.button.close");
```

Nested `Canvas`, `Vertical`, `Horizontal`, `Overlay`, and `Scroll` layout builders remain
valid and are more expressive than the historical flat/page-only gump language. Do not replace
them with a string-command scripting DSL merely to look like legacy UO.

### Current UI-0 vocabulary

The current neutral kernel already provides:

```text
layouts:
    Canvas
    Vertical
    Horizontal
    Overlay
    Scroll

elements:
    Panel
    Text
    Image
    Button
    TextInput
    Toggle
    List
```

Current visual realization is deliberately minimal:

- `Panel` exposes one `styleRole`;
- `Image` exposes one fixed `assetRole`;
- `Button` has semantic label/action state but no renderer-neutral image-state set;
- `Toggle` has boolean semantics but no authored off/on image set;
- Godot currently realizes Image as one `TextureRect`, Panel as one native `Panel`, and Button
  as one native `Button`.

Therefore UI-0 does **not** yet define nine-slice/nine-piece backgrounds, tiled images,
multi-image button states, radio/exclusive image choices, dynamic semantic image values, or
per-element image tint/source-region behavior.

### Visual roles, not Godot types

Prefer stable **visual roles** that resolve through presentation/content ownership.

For example:

```text
ui.paper.frame
ui.stats.background
ui.button.close
ui.button.tab
ui.checkbox.classic
ui.radio.classic
```

A visual role may resolve to one image, a nine-slice descriptor, a nine-piece frame, a tiled
texture, or a stateful control image set. Game/server code should select the semantic role; the
Godot adapter realizes it.

The existing `styleRole`/`assetRole` vocabulary may evolve into this richer resolver rather than
forcing every definition to repeat nine asset names or Godot theme details.

### Image presentation modes

The neutral image vocabulary should support at least:

```text
Natural
    draw at authored/native size

Fit
    preserve aspect inside bounds

Fill
    preserve aspect and crop to fill bounds

Stretch
    scale to exact bounds

Tile
    repeat image inside clipped bounds

NineSlice
    split one source image by fixed margins;
    corners remain fixed, edges/center use declared stretch/tile policy

NinePiece
    optional UO-style frame composed from nine semantic image roles;
    fixed corners with tiled/stretched edges and center
```

NineSlice and NinePiece are presentation contracts, not separate gameplay element kinds.

A frame descriptor should be bounded and deterministic, with explicit:

```text
left / top / right / bottom margins
edge mode = Stretch | Tile
center mode = Stretch | Tile
minimum realizable size
```

For a NinePiece frame, the descriptor may instead carry:

```text
TopLeft
Top
TopRight
Left
Center
Right
BottomLeft
Bottom
BottomRight
```

as semantic asset roles.

### Panels/backgrounds

Support both common forms:

```text
single-image authored panel
    e.g. one complete character/stat-sheet background

resizeable frame/panel
    e.g. parchment/stone/window frame using NineSlice or NinePiece
```

A panel/background is visual presentation. Text, controls and child layout remain independent
siblings/children in the compiled UI topology.

The authoring API should make common composition concise rather than requiring authors to hand-build
an Overlay for every background. A convenience `AddBackground` / image-backed panel form is
appropriate even if the compiled representation remains ordinary renderer-neutral elements.

### Stateful button images

Button interaction state is split between **model-owned semantic state** and **client-local
presentation state**.

Model/server-owned:

```text
Enabled / Disabled
Active / Inactive          # optional selected/toggled/tab-like semantic state
```

Client-local ephemeral state:

```text
Normal
Hover
Pressed                    # includes held-down
Released                   # optional short presentation transition
Focused
```

Hover/press/release/focus must never require a server round trip.

Prefer a reusable visual-role descriptor conceptually shaped as:

```text
UiButtonVisual
    Inactive:
        Normal      required
        Hover       optional
        Pressed     optional
        Released    optional

    Active:         optional complete/partial state set
        Normal
        Hover
        Pressed
        Released

    Disabled        optional
    FocusedOverlay  optional
```

Fallback is deterministic:

```text
missing Active state
    -> corresponding Inactive state

missing Hover
    -> current Normal/base

missing Pressed
    -> Hover, then Normal/base

missing Released
    -> Hover or Normal/base

missing Disabled
    -> current base plus adapter-defined disabled treatment
```

The optional Released image is presentation-only and may use a bounded client-side transition
duration owned by the visual descriptor/profile. Game authority never waits for it.

### Toggle / checkbox and radio/exclusive-choice imagery

The existing boolean `Toggle` semantics should gain image-backed visual roles so classic
off/on checkbox presentation does not require a bespoke widget.

A future exclusive-choice/radio semantic is also legitimate because it expresses a common
authoring concept directly:

```text
checkbox/toggle
    Off visual
    On visual

radio/exclusive option
    Unselected visual
    Selected visual
    group/selection identity
```

Hover/pressed/focus overlays may reuse the same local interaction-state resolver as buttons.

Do not treat a visual radio group as Game authority. The authoritative action still validates the
requested semantic choice.

### Dynamic semantic images and UO-style item art

Historical `AddItem` / `tilepic` displays item/static art. UltraSim should not make the neutral
UI kernel know what an Inventory Item, weapon, creature, or Godot texture is.

Instead, add a neutral **semantic image/asset reference value** that can be bound dynamically:

```text
Ui image element
    fixed asset role
        OR
    model slot containing an admitted semantic presentation asset reference
```

Game-level convenience authoring may then offer an `AddItemImage`-like helper which resolves an
Item/Definition presentation facet into that neutral image reference.

This is important for:

```text
Inventory item icons
vendor/crafting rows
character portraits
skill/spell icons
quest/reward art
equipment paperdoll-like views
```

The server must not send arbitrary filesystem paths, Godot resources, or unvalidated URLs through
this mechanism. The reference must resolve through the existing authorized content/catalog/
presentation path.

### Tint, clipping, atlas/source regions

Useful image presentation metadata includes:

```text
optional tint/modulate
optional opacity
optional clip-to-bounds
optional source/atlas region where the content pipeline exposes one safely
```

Prefer semantic asset/catalog resolution to hard-coded atlas coordinates. Atlas packing remains a
presentation/content implementation strategy.

### Text and input semantics

Historical UO also had cropped text, limited text entries, scrollable HTML regions, and localization.

UltraSim already has bounded text values, TextInput, Scroll containers, and ordinary nested layout.
Future additions should be semantic rather than raw legacy HTML:

```text
Text overflow:
    Wrap
    Clip
    Ellipsis

TextInput:
    optional per-control maximum length

Rich/styled text:
    only a bounded renderer-neutral vocabulary if a real consumer earns it

Localization:
    stable localization identity/arguments rather than renderer-owned raw HTML
```

Do not introduce arbitrary HTML/JavaScript as a generic server UI payload.

### Tooltips

UO attached tooltips to individual gump entries. UltraSim already owns a separate bounded
Context/Tooltip presentation category.

Add a renderer-neutral per-element tooltip/help binding when earned so an element can contribute
authorized text/semantic help without owning a native tooltip implementation. The client remains
the presentation owner.

### Pages versus nested containers

Historical `Page` is useful precedent for switching visible groups, but UltraSim already has real
nested layout/container semantics.

Do not regress server authoring into flat page command streams.

If tabs/pages are needed, prefer a bounded semantic visibility/selection construct over the
existing compiled tree, for example:

```text
Stack / PageSet
    active page = bounded model value

Tab buttons
    ordinary semantic actions
    optional Active visual state
```

That keeps nesting, layout validation, exact compiled identity, and Local/Remote equivalence.

### Alpha/transparency regions

Historical `CheckerTrans` / `AddAlphaRegion` is presentation precedent, not a required element
kind. Normal panel/image opacity/tint should cover ordinary translucent backgrounds.

Only add a dedicated mask/transparency primitive if a real UI cannot be expressed with the
renderer-neutral image/panel vocabulary.

### Permanent authoring rule

> **The normal Game/server UI authoring path should be at least as convenient as the classic gump
> model for ordinary windows, while retaining UltraSim's typed definitions, nested layout,
> bounded models, semantic actions, exact content identity, and renderer neutrality.**

A first-party stat sheet, inventory, vendor, crafting panel, package-provided gump, and future
community-authored view should use substantially the same authoring contracts.

## Actions and authority

Clients submit intent. Authoritative workflows re-resolve durable identity,
account status, ownership, generation affinity, and current game state at action
time. An open view is a presentation snapshot, not cached authorization.

Expected domain rejection is a result. An exception escaping a trusted Runtime
callback remains a terminal authority failure and must not be converted to a
normal UI rejection.

Once an authoritative mutation commits, presentation delivery cannot roll it
back. Expected capacity/transport rejection before publication is retained or
reported for retry. A client rejection after delivery detaches or desynchronizes
the affected session and requires a fresh snapshot. Observer event failures are
isolated from committed authority.

## Failure, capacity, and callback boundaries

`IUiSemanticPort` has one explicit expected publication-failure family.
`UiSemanticPublicationException` means that a valid semantic presentation operation could not be
accepted at the current projection boundary: for example, retained-view capacity is exhausted or a
bounded local/remote delivery queue is temporarily full. Game-domain rejection remains a result;
unexpected exceptions remain implementation failures. Callers catch only this explicit family
outside authoritative Runtime callbacks, so presentation capacity cannot fault or roll back an
otherwise valid Game mutation.

Open/close ownership is transactional at the port boundary:

- an open view becomes controller-owned only after publication and action binding succeed;
- a close exception retains the controller's view identity and action endpoint for retry;
- an accepted close, or `false` when the port has already retired that projection, discharges the
  cleanup obligation;
- local terminal retirement closes authority-side projections without requiring a stopped client
  to consume more queued close operations;
- owner-thread shutdown attempts every independently owned resource and reports cleanup failure
  through session fault state rather than throwing out of the background authority thread.

The queued packet-free channel applies each operation at most once. If its application-proof queue
is full, the channel—not the disposable client bridge—retains the completed status and retries
proof publication; bridge replacement therefore cannot run the renderer/model operation again.
A replacement bridge must attach to the same `UiClientRuntime` projection. Replacing that retained
projection requires retiring the containing local session rather than silently acknowledging views
that belong to another runtime.

The client bridge pump is owner-thread-bound and non-reentrant. A renderer callback that attempts
to pump the bridge is rejected before it can observe or complete the outer operation. Bridge
disposal is rejection-atomic: action-sink detachment must succeed before the bridge marks itself
disposed or releases the session attachment, so a callback-time rejection leaves the installed
bridge operational and ordinarily replaceable later.

`UiClientRuntime` treats renderer callbacks as a sealed mutation scope. `Create`, `Apply`, and
`Close` callbacks cannot synchronously open/update/close views, alter the action sink, submit
intent, or dispose their owning runtime. A rejected callback faults the runtime without publishing
candidate model/cache state; disposal later closes any still-owned handle.

Definition-specific limits remain authoritative after wire admission. Action-unavailability text
is validated against the resolved compiled definition, not merely the wider wire default. Delta
decoding proves declared counts feasible from remaining bytes and configured slot/action/row
limits before allocating collections. The Godot adapter likewise preadmits every flow spacing and
padding value against its native integer range before constructing controls. Boolean `Unset`
always projects as an unpressed toggle, including a retained `true -> Unset` update.

Compiled definitions also own one finite outside-pointer policy. The default is modeless:
pointer input over a visible UI surface belongs to the UI, while input outside passes to gameplay
and leaves the view open. Definitions may instead declare a modal `Block` policy, or a `Close`
policy tied to exactly one ordinary no-input Game action. Click-outside closing therefore follows
the same validated semantic action path as the visible Close button; it is not a Godot-local view
deletion. Character Select is explicitly modal, Inventory and Server Monitor use the modeless
default, and informational signs use semantic click-outside Close. Local and Online interpret the
same compiled policy.

## F1.7 Godot workspace and native gumps

The Godot application owns one persistent presentation composition with this category order:

```text
Workspace < World < HUD < Menus/Gumps < Debug < Context/Tooltip
```

The shell-menu host and gameplay-gump host are distinct children of Menus. A session lease adds
only its World, HUD, and Context contributions and retires them together. `GodotUiRenderer` still
renders the exact compiled semantic definition/model, while `GodotGumpWindowManager` owns only the
native wrapper, bounded cascade, bring-to-front order, drag/application clamp, and native lifetime.
Inventory and Server Monitor may therefore coexist; closing either still follows its semantic Game
action and removes only its matching native window.

The selected Gameplay Output shape (1920×1080 by default) is maximum-fit in the application window.
Unused pixels are transitional dark workspace, frame-associated World overlays are clipped to the
result, and world pointer input outside it is rejected. This is an input/layout seam, not the F1.8
camera crop or F1.9 SubViewport.

Context/Tooltip owns hover presentation and one bounded right-click menu over already-disclosed
targets. The current finite actions are Select, Interact/Use, and Cancel. Outside click, target
invalidation, shell modal, and session retirement close it. Right-click no longer doubles as world
Stop; Escape and shell/modal gameplay-input loss remain the explicit cancellation paths. F11 owns
the finite Off → Client → Presentation → Content → Off local diagnostic cycle.

## Local and remote paths

`LocalSemanticLink` is the engine-neutral offline bridge. It transfers the same
semantic definition/model/action operations without manufacturing packets or
sharing server authority.

The N0.4 remote evidence path encodes the same bounded semantics through the accepted
secure `ProtocolLoopback` fixture, now physically owned by
`UltraSim.Network.Testing`. Production remote clients use the same protocol through
Hosted UDP without shipping that two-ended test topology. `Game/Protocol` owns
game message identity and the server-side semantic port; `Client/Network` owns
translation into `UiClientRuntime`. Server view IDs are allocated before delivery
and no authoritative callback waits for a network round trip.

Transport acknowledgement proves only that the remote protocol admitted a
message. A separate application apply result proves that the client UI state
machine applied, duplicated, or rejected a semantic operation. A rejected
post-commit update marks the server-side view desynchronized; it never rolls
authority back. Cold close plus exact hot reopen proves cache identity. Session
detach disposes the client runtime, closes renderer handles best-effort, and
clears definitions, so reconnect begins from a fresh coherent cache/session.

`UiClientRuntime` owns retained client projection and renderer handles and is
deterministically disposable. Accepted N0.5 retains this exact lifecycle while
replacing only the deterministic datagram topology with real UDP ownership.

Both Inventory and CharacterSelect retain the exact presented `UiModel` and run
`TryValidateAction` before entering any authority callback. Structurally valid
wire intent with an unknown action, wrong source/input contract, absent row, or
disabled action is contained as a semantic rejection; trusted implementation
exceptions after that boundary remain fatal rather than being disguised as user
rejection.

I0.8 consumes this path with the native Godot renderer. Admission supplies the
authorized actor to an owner-bound Inventory view; native list/button input sends
move, split, equip, and unequip intent through the generic remote UI protocol.
The open view resnapshots after save/load generation publication, while a fresh
authenticated application session reconstructs it after reconnect. Inventory
remains owner/private state and never enters public spatial replication.

D0.3 consumes the same semantic UI machinery for an authoritative informational sign. Public
presentation exposes only that the object is a sign. After ordinary Game interaction validates the
trusted target and range, Game supplies immutable title/body content and one compiled Close action.
The native Godot view is therefore a renderer of server-authored information, not the authority for
sign text, interaction distance, or view lifecycle.

## Current client closure

The renderer, exact cache identity, cold/hot semantics, Inventory v8 workflow, local/remote action
path, A0.6 content-backed `.usui` path, and F0.5 source convergence are accepted. One selected
standard Inventory artifact, reopened cached bytes, and content-backed exact bytes enter the same
`UiClientRuntime` and produce the same model, actions, Game handler, and Godot result. The standard
library remains small composition data; it does not make Client Core
depend on every Game definition, pull Game UI/Inventory authority assemblies into Online, or
substitute for required realm-content application proof.

I0.12 advanced the passive definition to v7 with one item-details slot and Drop action. Human-play
input hardening advances the passive artifact to v8 without moving authority into the client. I0.13
replaces the temporary preformatted weapon string with a typed bounded Inspection Property set.
Game determines disclosed facts from persistent authoritative instance state and authorizes Drop
only in an owner-bound view; an engine-neutral presentation profile determines labels, formatting,
and category/property ordering. The current Inventory model slot receives the result of that shared
formatter, while world hover consumes the same facts/profile. Godot still renders the generic
compiled tree and returns a semantic action; it does not choose item stats, ownership, or world
placement.
