# Local Play Godot capability

`UltraSim.Client.Local.Godot` is the permanent F0.2 presentation/composition consumer for the
Network-free local Cabin application. It is referenced by Single Player and Complete, but not
Online.

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

The normal user path resolves the frozen A0.1 `Default` catalog. Headless acceptance can inject
`Default`, `Replace`, or `Add` without adding a user-facing test-variant menu. Presentation resolves
verified PNG payloads by SHA-256 identity; it does not introduce cache, HTTP, or runtime model
import.

The historical X Bot and Y Bot are shared build-time calibration assets. F0.2 uses one as the
controlled-player body and freezes initial 1.6 m camera-pivot, 2.0 m nameplate, and 0.9 m effects
anchors. This is a concrete controlled-player calibration seam, not the generalized F0.4 actor
catalog.
