# P9 spatial-space foundation — Game and Local acceptance

Status: **Game/Local foundation accepted; Online map transition not implemented; P9 remains ACTIVE.** This is not a P9 visual rating or PF6-readiness claim.

Starting `main` was `e9c84d6b122be8338be0c9927a2fa4a5b98536d3`; a concurrent AIOS documentation sync advanced it to `54226b02c0f010873b107769f44719380482a115` before the implementation commits. The implementation is `905c836a222c5a5b2b61dd89c57f11c915757634`; staged-load hardening is `25be672f3e36295d8f29cb5201987c5b5ae76471`. The canonical Release verifier passed uninterrupted on the latter commit. Host: Linux `7.2.6-1-cachyos` x86_64, .NET SDK `8.0.131`, Godot `4.7.1.stable.mono.official.a13da4feb`.

## Accepted boundary

- `WorldLocation` stores `SpatialSpaceId + WorldPosition` as one ECS component update. Game admits up to eight distinct prepared structural spaces per generation. An actor transition validates the destination root, support, collision and occupancy before changing its address; actor/public identity, participant, Inventory, Equipment and session remain owned by the same generation. Spatial index, public publications, physics, interactions, ground items and spatial Chat use the current space.
- `WorldPositionComponent` is now 24 bytes and persistence version 2. The pinned version-1 reader restores old addresses into `DefaultSpace`. A staged load rejects a restored nondefault space not admitted by its composition **before** replacing the live generation. The 100,000-entity Inventory fixture retains its workload but reserves 320 rather than 256 ECS chunks to accommodate the wider row; this is test capacity, not a product participant-cap change.
- The original two-room benchmark WorldPack remains a separate versioned artifact. Local Concept Demo admits its authored topology in Space 2 alongside Classic's Space 1. The existing Sky telepad remains same-space; a new Classic pad near `(22 m, 12 m)` and a return pad near `(16 m, 7 m)` use exact Game-owned destinations. No filename or scene name is a teleport command.
- Local switches its semantic cache, structural root and native world renderer, while retaining the actor, Inventory, application session and UI. Old-space subscriptions leave explicitly. The native canary performs `Classic → benchmark → Classic` twice using real semantic interaction commands, observes only destination-space residency, and checks the retired renderer has zero active chunks, nodes and resources.

## Verification

| Control | Result on implementation |
| --- | --- |
| Game World Release | 118/118, including different-space index/publication isolation and a real negative saved-space load. |
| Game Persistence Release | 15/15, including v1-to-DefaultSpace and v2 nondefault round trips. |
| Local Client Release | 25/25, including exact portal interaction, Inventory/actor retention and save/reload while in Space 2. |
| Game Chat / Inventory Release | 37/37 each; spatial speech cannot cross spaces, existing Inventory outcomes remain green. |
| S0 integration Release | 41/41; two participants occupy different spaces in one authority, with the moving actor retaining its public identity and backpack. |
| Godot Local `--spatial-space-smoke` | `SPATIAL SPACE LOCAL PASS trips=2 actor=retained world=retired session=clean`. |
| `./verify.sh --configuration=Release` | Passed uninterrupted on `25be672f`, including architecture, native/exports, SinglePlayer/Online/Complete, product dogfood, Direct Connect, Complete-hosted, impairment/accounting and final composition parity. |

During development, earlier runs exposed a missing local ASP.NET runtime selection, the wider-row test-capacity need, two native fixtures expecting one rather than two Local telepads, and one transient Server Hosting microbenchmark ratio failure. These were not silently treated as passes: the targeted Hosting rerun passed, the fixture assumptions were corrected, and two later full Release runs passed, the last on the exact hardened implementation commit. No protocol or product admission limit was raised.

## Explicitly incomplete

The server-side **Game authority** can hold Alice and Bob in different spaces concurrently; this acceptance does **not** show two network clients doing so. Production Online/Remote still advertises one authenticated structural manifest and uses a one-space owner-state/client structural lifecycle. The hosted profile therefore does not expose the new pads or admit Space 2. Enabling it without a space-aware advertisement, owner-state discontinuity, proof/debt retirement, client structural switch and independent two-client test would be incorrect. Local prepares both roots at generation start: the canary proves semantic/native unload and rebind, not cold network acquisition or measured warm structural-byte reuse. Remote multi-space and cold/warm acquisition are the next required pieces before the full product scenario or P9 can close.

For human Local inspection, launch the normal Single Player Concept Demo, select the new pad near `(22 m, 12 m)` and press `E`; use the pad near `(16 m, 7 m)` inside the two-room Cabin to return. No `--p9-two-room-benchmark` flag is needed for that journey. The old flag remains a separate visual-laboratory control.
