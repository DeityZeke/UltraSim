# Local Play Godot capability

`UltraSim.Client.Local.Godot` is the permanent presentation/composition consumer for the
Network-free local playable application. It is referenced by Single Player and Complete, but not
Online. F0.2 established its Cabin lifecycle; Concept Demo D0.0 now uses the same consumer for the
physical island world.

Concept Demo D0.1 replaces unconditional `StageAllChunks` with Client Core's bounded structural
residency controller. The 81-chunk local authored source remains available to the local application,
but the default client keeps 25 structural semantic chunks resident and realizes 9. The persisted
Render Distance setting supports radii 1–3; a live accepted change rebuilds only the client
cache/window/renderer around the same authoritative session. The HUD reports authored,
resident, rendered, peak, enter/exit, reset, and queue evidence. This is local preinstalled-source
residency, not CAS or remote structural acquisition.

```text
persistent ClientRoot
    +-- frozen resolved Cabin catalog
    +-- LocalPlayableSession authority-owner thread
    +-- Client Core world/Chat/UI state
    +-- Godot world/HUD presentation
```

`LocalPlayClientRoot` binds the capability into the shared F0.1 shell lifecycle.
`LocalCabinClient` owns the concrete Cabin semantic caches, renderer, controls, Inventory and Chat
consumers. Leaving the session synchronously stops the authority thread, detaches the catalog,
removes session presentation, and permits a fresh application/control/content generation on the
next entry.

Administrative relocation completion is not treated as immediate presentation convergence. The
F0.2 client continues pumping the bounded local semantic path until its controlled-player
projection equals the authoritative relocated position before a following facing/movement
acceptance step may derive an absolute world point. This closes the relocation/facing race without
giving Godot direct authority access or changing the accepted command format.

Physics P0.1d maps the resolved Space binding to the common edge-triggered Game `Jump` action for
the direct human-control profiles. OrbitCommand deliberately retains its UO-like no-jump grammar.
Godot presents the authoritative replicated Y position; it owns no body velocity, gravity,
grounding decision, or recovery teleport.

Human-play hardening keeps pointer semantics aligned with that structural world: Orbit destinations
accept the nearest valid Base/Top support, including detached terrain shells, and a primary-click
selection is available to both direct profiles before `Interact`. Orbit/third-person wheel zoom may
approach to 3 m. Collidable static tops are resolved as bounded physical support by Game
Physics, so landing on the authored rock returns to grounded locomotion rather than leaving a
blocker-only airborne state.

Normal Single Player entry explicitly selects the D0.0 Concept Demo fixture. Its bounded acceptance
journey walks through the MountainShell opening on the preserved base cave floor, jumps across real
void and lands on the detached Sky shell, exercises the Game-owned telepad with OrbitCommand, then
falls through unsupported air into Game-owned recovery. Client Core/Godot retain and realize the
three sparse shell fragments without converting terrain into ECS entities or per-cell nodes.

The unconfigured `Playable.Godot` wrapper and the F0.2 Default/Replace/Add lifecycle gates continue
to select the historical Cabin fixture. This is a deliberate compatibility split, not a runtime
feature flag: both paths use the same local semantic bridge and presentation consumer.

The normal user path resolves the frozen A0.1 `Default` catalog. Headless acceptance can inject
`Default`, `Replace`, or `Add` without adding a user-facing test-variant menu. Presentation resolves
verified PNG payloads by SHA-256 identity; it does not introduce cache, HTTP, or runtime model
import.

The historical X Bot and Y Bot are shared build-time calibration assets. F0.2 uses one as the
controlled-player body and freezes initial 1.6 m camera-pivot, 2.0 m nameplate, and 0.9 m effects
anchors. This is a concrete controlled-player calibration seam, not the generalized F0.4 actor
catalog.
