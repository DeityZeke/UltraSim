# Authoritative Physics Architecture

Purpose: define the minimum authoritative physics shape the platform needs before terrain and other gameplay systems are built on top of it.

Status: planning/spec document. This is the physics companion to the current server/runtime, client/runtime, and terrain docs.

Related docs:
- `Docs/SERVER_RUNTIME_ARCHITECTURE_AND_ROADMAP.md`
- `Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`
- `Docs/CHUNK_TERRAIN_STATICS_IMPLEMENTATION_GUIDE.md`
- `Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`

---

## 1) Why This Exists

The current tracked server path now owns:

- transport
- session lifecycle
- player spawn/despawn
- player input
- authoritative movement tick
- replication
- AOI

That is enough to prove the runtime shape, but it is not enough to support terrain or a durable gameplay foundation.

The next missing platform seam is authoritative physics:

- gravity
- solid collision
- ground checks
- sweep/raycast/overlap queries
- server-authoritative character movement resolution

Without that seam:

- terrain becomes mostly render/data work, not a real gameplay system
- server authority over movement stops at simple position integration
- collision only exists in client scene data, not in server truth

This document defines the shape we want before terrain work begins.

---

## 2) Core Decision

Do not build a custom general-purpose physics engine in `UltraSim`.

Instead:

- define engine-agnostic physics contracts in core/shared code
- make the server runtime depend on those contracts
- provide the first backend with Godot/Jolt
- allow a future Unity backend to satisfy the same contracts later

This is the same pattern already used for:

- transport
- discovery publishers
- client renderers
- persistence providers

The core runtime stays backend-agnostic, not backend-free.

---

## 3) Scope We Actually Need First

We do **not** need full generalized physics before terrain.

We **do** need a minimum authoritative character/world collision layer.

### 3.1 Minimum pre-terrain physics scope

- gravity
- grounded detection
- capsule or character-body sweeps
- static-world collision resolution
- slide/block response
- raycast queries
- overlap queries
- trigger/area query support if cheap

### 3.2 Explicitly not required in the first slice

- complex rigidbody simulation sandbox
- joints/constraints
- ragdolls
- vehicles
- full deterministic cross-engine lockstep
- client-predicted authoritative physics parity

The first milestone is: the server can authoritatively answer “where can this player actually move?”

---

## 4) Architectural Shape

Recommended package layout:

```text
UltraSim/
- Physics/
  - Contracts/
  - Queries/
  - Bodies/
  - Runtime/

UltraSim/
- Server/
  - Physics/

Server.Godot/
- Physics/
```

### 4.1 Responsibilities

#### `UltraSim/Physics/`

Engine-agnostic contracts and data shapes:

- physics world interface
- collider/body descriptors
- movement/sweep/raycast/overlap requests
- hit/query results
- body handles / shape handles / layers / masks

#### `UltraSim/Server/Physics/`

Authoritative server-facing orchestration:

- server physics service
- ECS-facing movement resolution
- static-world registration/update hooks
- runtime-owned physics tick integration

#### `Server.Godot/Physics/`

Godot/Jolt adapter:

- actual collision world implementation
- static collider creation
- character sweep/move calls
- raycast/overlap adapter code

---

## 5) Runtime Ownership Model

`ServerRuntime` should eventually own:

- a physics service abstraction
- lifecycle for initializing and disposing the authoritative physics world
- physics-step ordering in the runtime tick

`ServerRuntime` should not own:

- Godot physics node types directly
- Jolt-specific API details in core runtime code

Recommended ownership split:

- `ServerRuntime`
  - owns `IServerPhysicsService`
- `ServerPlayerSessionWorld`
  - owns player movement intent/state
- `IServerPhysicsService`
  - resolves collision/gravity against the authoritative world

That means ECS stays the source of gameplay state and intent, while physics remains the collision/query backend.

---

## 6) Tick Ordering

The runtime tick should evolve toward this stable shape:

1. poll inbound transport
2. decode/control-route datagrams
3. apply inbound player input / gameplay commands
4. advance authoritative physics-backed movement
5. tick ECS world/gameplay systems
6. build replication
7. flush outbound transport

The key point is:

- movement resolution should no longer be “pure ECS math only”
- it should call into authoritative physics before final position/state is committed

---

## 7) ECS / Physics Boundary

The ECS movement system should not become the physics engine.

Instead it should:

- read current body state and movement intent
- call physics queries/movement APIs
- receive a resolved movement result
- write the result back into ECS components

Example conceptual flow:

1. read player intent
2. build a character move request
3. call `MoveCharacter(...)`
4. receive:
   - final position
   - grounded state
   - hit normal
   - blocked/slid state
5. write back to ECS

This is the right seam for both Godot/Jolt and a future Unity backend.

---

## 8) Minimal Contract Direction

The exact type names are still flexible, but the core contract surface should cover:

- `IPhysicsWorld` or `IServerPhysicsWorld`
- `IPhysicsBodyHandle`
- `CharacterMoveRequest`
- `CharacterMoveResult`
- `RaycastRequest`
- `RaycastHit`
- `OverlapRequest`
- `OverlapResult`
- collision layers/masks

Minimum capabilities:

- `MoveCharacter(...)`
- `Raycast(...)`
- `OverlapCapsule(...)` or equivalent
- static collider registration/unregistration

The first implementation can be narrow and still be correct.

---

## 9) Static World / Terrain Relationship

Physics should come before terrain, but terrain will consume the same seam.

That means the authoritative physics service must eventually support:

- chunk-backed static collider registration
- frozen statics / baked structures as collision providers
- chunk load/unload registration updates

Terrain should be implemented on top of this, not beside it.

Practical implication:

- chunk content drives collider registration into the authoritative physics world
- the server uses the same collision truth for movement, LOS, and interaction queries

---

## 10) Client Relationship

The client can keep local collision/controller data for feel and presentation, but it is not authoritative.

Client-side physics should be treated as:

- local usability
- camera/controller feel
- optional prediction support later

Server-side authoritative physics remains the source of truth for:

- legal movement
- grounded state
- collision response
- interaction query correctness

---

## 11) Godot/Jolt First Backend

The first backend should use Godot's built-in physics stack with Jolt where available.

Reason:

- avoids building a custom engine
- integrates with the current host stack
- is fast enough for the current milestone
- gives a real authoritative backend before terrain

The Godot server backend should be allowed to:

- own an internal physics world representation
- create static colliders for server-authoritative geometry
- run character sweeps and queries
- translate results into engine-agnostic return types

Core rule:

- Godot/Jolt-specific code stays in `Server.Godot/Physics/`
- `UltraSim/Server` and `UltraSim/Physics` remain backend-agnostic

---

## 12) Unity / Other Engine Implication

If a future Unity server host exists, it should provide another backend implementation for the same contracts.

That means:

- the core runtime does not need a built-in physics engine
- it does need a required physics backend abstraction

This is acceptable.

The runtime already depends on injected backends for other concerns; physics is another such concern.

---

## 13) What This Enables

Once the authoritative physics seam exists, the platform can support:

- real server-authoritative gravity
- collision-correct movement
- terrain-backed collision
- structure/static collision
- authoritative interactable queries
- more credible replication because movement is constrained by real world geometry

This is the minimum stable base for terrain and later housing/flora systems.

---

## 14) Current Status

Implemented today:

- authoritative server movement tick exists
- authoritative player spawn/input/replication path exists
- client-side collision/controller data exists for presentation/local behavior

Not implemented yet:

- engine-agnostic authoritative physics contracts
- tracked server physics service
- Godot/Jolt-backed authoritative server physics adapter
- runtime-owned physics step integration
- static collider registration path for world content

---

## 15) Recommended Order

Recommended order relative to the broader platform roadmap:

1. finish the minimum client/server platform identity loop
   - server list
   - login/account
   - character select/create
   - persistence
2. add authoritative physics contracts and Godot/Jolt backend
3. integrate server-authoritative gravity/collision for player movement
4. only then begin terrain/chunk-content implementation

If forced to choose between terrain first and physics first:

- physics first

---

## 16) Out of Scope For This Doc

This doc does not define:

- full terrain data model
- navmesh/pathfinding architecture
- combat hit resolution
- client camera/controller polish
- ragdoll/vehicle/joint systems

Those can build later on top of this authoritative physics seam.
