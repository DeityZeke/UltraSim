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

Client/_Godot
    Godot control construction, rendering, and input forwarding
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

## Local and future remote paths

`LocalSemanticLink` is the engine-neutral offline bridge. It transfers the same
semantic definition/model/action operations without manufacturing packets or
sharing server authority. It is not a claim that Network V3 is implemented.

The remote path will encode the same bounded semantic protocol through Network
V3, with reconnect, ordering, duplication, loss, and compatibility behavior
owned by the network/client lifecycle.

`UiClientRuntime` owns retained client projection and renderer handles. Mature
network lifecycle must deterministically close/detach these resources on
disconnect or disposal.
