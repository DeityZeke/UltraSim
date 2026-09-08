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

## Actions and authority

Clients submit intent. Authoritative workflows re-resolve durable identity,
account status, ownership, generation affinity, and current game state at action
time. An open view is a presentation snapshot, not cached authorization.

Expected domain rejection is a result. An exception escaping a trusted Runtime
callback remains a terminal authority failure and must not be converted to a
normal UI rejection.

Once an authoritative mutation commits, presentation delivery cannot roll it
back. A failed open/update/close detaches or desynchronizes the session and
requires a fresh snapshot. Observer event failures are isolated from committed
authority.

## Local and remote paths

`LocalSemanticLink` is the engine-neutral offline bridge. It transfers the same
semantic definition/model/action operations without manufacturing packets or
sharing server authority.

The N0.4 remote path encodes the same bounded semantics through the accepted
secure `ProtocolLoopback`. `Game/Protocol` owns game message identity and the
server-side semantic port; `Client/Network` owns translation into
`UiClientRuntime`. Server view IDs are allocated before delivery and no
authoritative callback waits for a network round trip.

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

The renderer, exact cache identity, cold/hot semantics, Inventory v7 workflow, local/remote action
path, A0.6 content-backed `.usui` path, and F0.5 source convergence are accepted. One selected
standard Inventory artifact, reopened cached bytes, and content-backed exact bytes enter the same
`UiClientRuntime` and produce the same model, actions, Game handler, and Godot result. The standard
library remains small composition data; it does not make Client Core
depend on every Game definition, pull Game UI/Inventory authority assemblies into Online, or
substitute for required realm-content application proof.

I0.12 advances the passive definition to v7 with one item-details slot and Drop action. Game derives
rolled weapon text from persistent authoritative instance state and authorizes Drop only in an
owner-bound view. Godot still renders the generic compiled tree and returns a semantic action; it
does not choose item stats, ownership, or world placement.
